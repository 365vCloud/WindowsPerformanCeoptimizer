use std::collections::HashSet;
use std::fs;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, Ordering};
use std::time::{Duration, SystemTime};

use chrono::{DateTime, Utc};
use uuid::Uuid;

use crate::core::models::{CleanupItem, RiskLevel};
use crate::core::path_safety::PathSafetyValidator;

#[derive(Debug, Clone)]
pub struct ScanRoot {
    pub root: PathBuf,
    pub category: &'static str,
    pub risk_level: RiskLevel,
    pub detected_reason: &'static str,
}

pub struct ScanOptions {
    pub roots: Vec<ScanRoot>,
    pub minimum_file_age: Duration,
    pub maximum_candidate_count: usize,
}

impl Default for ScanOptions {
    fn default() -> Self {
        Self {
            roots: default_scan_roots(),
            minimum_file_age: Duration::from_secs(24 * 60 * 60),
            maximum_candidate_count: 1_000,
        }
    }
}

pub fn default_scan_roots() -> Vec<ScanRoot> {
    let mut roots = Vec::new();
    push_root(
        &mut roots,
        std::env::temp_dir(),
        "userTemporaryFiles",
        RiskLevel::Low,
        "当前用户 Temp 目录中的过期临时文件（仅预览）",
    );

    if let Some(windir) = env_path("WINDIR").or_else(|| env_path("SystemRoot")) {
        push_root(
            &mut roots,
            windir.join("Temp"),
            "systemTemporaryFiles",
            RiskLevel::Medium,
            "Windows Temp 目录中的过期系统临时文件（可能需要管理员权限）",
        );
        push_root(
            &mut roots,
            windir.join("SoftwareDistribution").join("Download"),
            "windowsUpdateDownloads",
            RiskLevel::Medium,
            "Windows 更新下载缓存/残留安装文件（可能需要管理员权限）",
        );
    }

    if let Some(program_data) = env_path("ProgramData") {
        push_root(
            &mut roots,
            program_data
                .join("Microsoft")
                .join("Windows")
                .join("DeliveryOptimization")
                .join("Cache"),
            "deliveryOptimizationCache",
            RiskLevel::Medium,
            "Windows 传递优化下载缓存（可能需要管理员权限）",
        );
        push_root(
            &mut roots,
            program_data
                .join("Microsoft")
                .join("Windows")
                .join("WER")
                .join("ReportArchive"),
            "systemErrorReports",
            RiskLevel::Medium,
            "系统级 Windows 错误报告归档（可能需要管理员权限）",
        );
        push_root(
            &mut roots,
            program_data
                .join("Microsoft")
                .join("Windows")
                .join("WER")
                .join("ReportQueue"),
            "systemErrorReports",
            RiskLevel::Medium,
            "系统级 Windows 错误报告队列（可能需要管理员权限）",
        );
    }

    if let Some(local_app_data) = env_path("LOCALAPPDATA") {
        push_root(
            &mut roots,
            local_app_data.join("Temp"),
            "userTemporaryFiles",
            RiskLevel::Low,
            "当前用户 LocalAppData Temp 目录中的过期临时文件（仅预览）",
        );
        push_root(
            &mut roots,
            local_app_data.join("CrashDumps"),
            "crashDumps",
            RiskLevel::Medium,
            "应用崩溃转储文件（调试用途，删除前请确认不再需要）",
        );
        push_root(
            &mut roots,
            local_app_data
                .join("Microsoft")
                .join("Windows")
                .join("WER")
                .join("ReportArchive"),
            "userErrorReports",
            RiskLevel::Low,
            "当前用户 Windows 错误报告归档",
        );
        push_root(
            &mut roots,
            local_app_data
                .join("Microsoft")
                .join("Windows")
                .join("WER")
                .join("ReportQueue"),
            "userErrorReports",
            RiskLevel::Low,
            "当前用户 Windows 错误报告队列",
        );
        push_root(
            &mut roots,
            local_app_data.join("D3DSCache"),
            "shaderCache",
            RiskLevel::Low,
            "Direct3D 着色器缓存，可由系统/应用重新生成",
        );
    }

    roots
}

fn env_path(name: &str) -> Option<PathBuf> {
    std::env::var_os(name)
        .filter(|v| !v.is_empty())
        .map(PathBuf::from)
}

fn push_root(
    roots: &mut Vec<ScanRoot>,
    root: PathBuf,
    category: &'static str,
    risk_level: RiskLevel,
    detected_reason: &'static str,
) {
    if root.as_os_str().is_empty() {
        return;
    }
    if roots.iter().any(|r| {
        r.root
            .to_string_lossy()
            .eq_ignore_ascii_case(&root.to_string_lossy())
    }) {
        return;
    }
    roots.push(ScanRoot {
        root,
        category,
        risk_level,
        detected_reason,
    });
}

/// Signals that the caller requested cancellation while a scan was in progress.
#[derive(Debug)]
pub struct ScanCancelled;

/// Read-only scanner over explicitly whitelisted junk-file locations. Reads
/// filesystem metadata only (never file contents), never follows reparse
/// points, and re-validates every candidate against the path safety validator
/// before it is included in the result.
pub fn scan_temp_files(
    options: &ScanOptions,
    validator: &PathSafetyValidator,
    cancel_flag: &AtomicBool,
) -> Result<Vec<CleanupItem>, ScanCancelled> {
    let cutoff = SystemTime::now()
        .checked_sub(options.minimum_file_age)
        .unwrap_or(SystemTime::UNIX_EPOCH);

    let mut items = Vec::new();
    let mut seen_paths = HashSet::new();

    for root in &options.roots {
        if cancel_flag.load(Ordering::SeqCst) {
            return Err(ScanCancelled);
        }
        if items.len() >= options.maximum_candidate_count {
            break;
        }
        if !root.root.is_dir() || is_reparse_point(&root.root) {
            continue;
        }

        let _ = walk(
            root,
            &root.root,
            cutoff,
            options.maximum_candidate_count,
            validator,
            cancel_flag,
            &mut items,
            &mut seen_paths,
        );
    }

    Ok(items)
}

fn walk(
    root: &ScanRoot,
    dir: &Path,
    cutoff: SystemTime,
    max_count: usize,
    validator: &PathSafetyValidator,
    cancel_flag: &AtomicBool,
    items: &mut Vec<CleanupItem>,
    seen_paths: &mut HashSet<String>,
) -> Result<(), ScanCancelled> {
    let read_dir = match fs::read_dir(dir) {
        Ok(rd) => rd,
        // Inaccessible directories are skipped, not fatal, for a safe partial preview.
        Err(_) => return Ok(()),
    };

    for entry in read_dir {
        if cancel_flag.load(Ordering::SeqCst) {
            return Err(ScanCancelled);
        }

        if items.len() >= max_count {
            return Ok(());
        }

        let entry = match entry {
            Ok(e) => e,
            Err(_) => continue,
        };
        let path = entry.path();

        // Never traverse into reparse points (symlinks/junctions): skip entirely.
        if is_reparse_point(&path) {
            continue;
        }

        let file_type = match entry.file_type() {
            Ok(ft) => ft,
            Err(_) => continue,
        };

        if file_type.is_dir() {
            walk(
                root,
                &path,
                cutoff,
                max_count,
                validator,
                cancel_flag,
                items,
                seen_paths,
            )?;
            continue;
        }

        if !file_type.is_file() {
            continue;
        }

        if !is_under_root(&path, &root.root) {
            continue;
        }

        let metadata = match entry.metadata() {
            Ok(m) => m,
            Err(_) => continue,
        };

        let modified = match metadata.modified() {
            Ok(m) => m,
            Err(_) => continue,
        };

        if modified > cutoff {
            continue;
        }

        let full_path_str = path.to_string_lossy().to_string();
        let validation = validator.validate(&full_path_str);
        if !validation.is_allowed {
            continue;
        }

        let full_path = validation.normalized_path.unwrap_or(full_path_str);
        if !seen_paths.insert(full_path.to_lowercase()) {
            continue;
        }

        let last_modified_utc: DateTime<Utc> = modified.into();

        items.push(CleanupItem {
            id: Uuid::new_v4().to_string(),
            full_path,
            category: root.category.to_string(),
            risk_level: root.risk_level,
            size_bytes: metadata.len(),
            last_modified_utc: last_modified_utc.to_rfc3339(),
            detected_reason: root.detected_reason.to_string(),
        });
    }

    Ok(())
}

fn is_under_root(candidate: &Path, root: &Path) -> bool {
    let candidate_str = candidate.to_string_lossy().to_lowercase();
    let root_str = root.to_string_lossy().to_lowercase();
    let root_with_sep = if root_str.ends_with('\\') {
        root_str
    } else {
        format!("{}\\", root_str)
    };
    candidate_str.starts_with(&root_with_sep)
}

fn is_reparse_point(path: &Path) -> bool {
    match fs::symlink_metadata(path) {
        Ok(metadata) => metadata.file_type().is_symlink(),
        // Cannot determine: treat conservatively as a reparse point so it is skipped.
        Err(_) => true,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::core::path_safety::{PathSafetyOptions, PathSafetyValidator};
    use std::fs::{self as stdfs, File};
    use std::io::Write;
    use std::time::Duration;

    fn make_validator(roots: Vec<PathBuf>) -> PathSafetyValidator {
        PathSafetyValidator::new(PathSafetyOptions {
            allowed_roots: roots,
            denied_roots: vec![],
            resolve_symbolic_links: true,
        })
    }

    fn make_root(root: &Path, category: &'static str, risk_level: RiskLevel) -> ScanRoot {
        ScanRoot {
            root: root.to_path_buf(),
            category,
            risk_level,
            detected_reason: "test reason",
        }
    }

    fn set_old_mtime(path: &Path, hours_ago: u64) {
        let old = SystemTime::now() - Duration::from_secs(hours_ago * 3600);
        let file_time = filetime::FileTime::from_system_time(old);
        filetime::set_file_mtime(path, file_time).unwrap();
    }

    #[test]
    fn filters_by_minimum_age() {
        let tmp = tempfile::tempdir().unwrap();
        let old_file = tmp.path().join("old.tmp");
        let new_file = tmp.path().join("new.tmp");
        File::create(&old_file).unwrap().write_all(b"x").unwrap();
        File::create(&new_file).unwrap().write_all(b"x").unwrap();
        set_old_mtime(&old_file, 48);

        let validator = make_validator(vec![tmp.path().to_path_buf()]);
        let options = ScanOptions {
            roots: vec![make_root(tmp.path(), "userTemporaryFiles", RiskLevel::Low)],
            minimum_file_age: Duration::from_secs(24 * 3600),
            maximum_candidate_count: 1000,
        };
        let cancel = AtomicBool::new(false);
        let items = scan_temp_files(&options, &validator, &cancel).unwrap();

        assert_eq!(items.len(), 1);
        assert!(items[0].full_path.ends_with("old.tmp"));
        assert_eq!(items[0].category, "userTemporaryFiles");
    }

    #[test]
    fn scans_multiple_categories_and_preserves_risk() {
        let tmp = tempfile::tempdir().unwrap();
        let user_temp = tmp.path().join("user-temp");
        let updates = tmp
            .path()
            .join("Windows")
            .join("SoftwareDistribution")
            .join("Download");
        stdfs::create_dir_all(&user_temp).unwrap();
        stdfs::create_dir_all(&updates).unwrap();
        let temp_file = user_temp.join("a.tmp");
        let update_file = updates.join("cab.tmp");
        File::create(&temp_file).unwrap().write_all(b"x").unwrap();
        File::create(&update_file).unwrap().write_all(b"x").unwrap();
        set_old_mtime(&temp_file, 48);
        set_old_mtime(&update_file, 48);

        let validator = make_validator(vec![user_temp.clone(), updates.clone()]);
        let options = ScanOptions {
            roots: vec![
                make_root(&user_temp, "userTemporaryFiles", RiskLevel::Low),
                make_root(&updates, "windowsUpdateDownloads", RiskLevel::Medium),
            ],
            minimum_file_age: Duration::from_secs(24 * 3600),
            maximum_candidate_count: 1000,
        };
        let cancel = AtomicBool::new(false);
        let items = scan_temp_files(&options, &validator, &cancel).unwrap();

        assert_eq!(items.len(), 2);
        assert!(items
            .iter()
            .any(|i| i.category == "userTemporaryFiles" && i.risk_level == RiskLevel::Low));
        assert!(items
            .iter()
            .any(|i| i.category == "windowsUpdateDownloads" && i.risk_level == RiskLevel::Medium));
    }

    #[test]
    fn respects_maximum_candidate_count_across_roots() {
        let tmp = tempfile::tempdir().unwrap();
        let a = tmp.path().join("a");
        let b = tmp.path().join("b");
        stdfs::create_dir_all(&a).unwrap();
        stdfs::create_dir_all(&b).unwrap();
        for i in 0..10 {
            let root = if i % 2 == 0 { &a } else { &b };
            let f = root.join(format!("f{i}.tmp"));
            File::create(&f).unwrap().write_all(b"x").unwrap();
            set_old_mtime(&f, 48);
        }

        let validator = make_validator(vec![a.clone(), b.clone()]);
        let options = ScanOptions {
            roots: vec![
                make_root(&a, "userTemporaryFiles", RiskLevel::Low),
                make_root(&b, "userErrorReports", RiskLevel::Low),
            ],
            minimum_file_age: Duration::from_secs(24 * 3600),
            maximum_candidate_count: 3,
        };
        let cancel = AtomicBool::new(false);
        let items = scan_temp_files(&options, &validator, &cancel).unwrap();
        assert_eq!(items.len(), 3);
    }

    #[test]
    fn cancellation_stops_scan_early() {
        let tmp = tempfile::tempdir().unwrap();
        for i in 0..50 {
            let sub = tmp.path().join(format!("d{i}"));
            stdfs::create_dir_all(&sub).unwrap();
            let f = sub.join("f.tmp");
            File::create(&f).unwrap().write_all(b"x").unwrap();
            set_old_mtime(&f, 48);
        }

        let validator = make_validator(vec![tmp.path().to_path_buf()]);
        let options = ScanOptions {
            roots: vec![make_root(tmp.path(), "userTemporaryFiles", RiskLevel::Low)],
            minimum_file_age: Duration::from_secs(24 * 3600),
            maximum_candidate_count: 1000,
        };
        let cancel = AtomicBool::new(true);
        let result = scan_temp_files(&options, &validator, &cancel).unwrap_err();
        let _ = result;
    }

    #[cfg(windows)]
    #[test]
    fn skips_reparse_points_entirely() {
        let tmp = tempfile::tempdir().unwrap();
        let real_dir = tmp.path().join("real");
        let linked_dir = tmp.path().join("linked");
        stdfs::create_dir_all(&real_dir).unwrap();
        let inner_file = real_dir.join("secret.tmp");
        File::create(&inner_file).unwrap().write_all(b"x").unwrap();
        set_old_mtime(&inner_file, 48);

        if std::os::windows::fs::symlink_dir(&real_dir, &linked_dir).is_err() {
            return; // requires dev mode/elevation on some runners
        }

        let validator = make_validator(vec![tmp.path().to_path_buf()]);
        let options = ScanOptions {
            roots: vec![make_root(tmp.path(), "userTemporaryFiles", RiskLevel::Low)],
            minimum_file_age: Duration::from_secs(24 * 3600),
            maximum_candidate_count: 1000,
        };
        let cancel = AtomicBool::new(false);
        let items = scan_temp_files(&options, &validator, &cancel).unwrap();
        assert!(items.iter().all(|i| !i.full_path.contains("linked")));
    }
}
