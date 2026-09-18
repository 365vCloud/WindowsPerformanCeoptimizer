use std::fs;
use std::path::Path;
use std::sync::atomic::{AtomicBool, Ordering};
use std::time::{Duration, SystemTime};

use chrono::{DateTime, Utc};
use uuid::Uuid;

use crate::core::models::{CleanupItem, RiskLevel};
use crate::core::path_safety::PathSafetyValidator;

pub struct ScanOptions {
    pub temp_root: std::path::PathBuf,
    pub minimum_file_age: Duration,
    pub maximum_candidate_count: usize,
}

impl Default for ScanOptions {
    fn default() -> Self {
        Self {
            temp_root: std::env::temp_dir(),
            minimum_file_age: Duration::from_secs(24 * 60 * 60),
            maximum_candidate_count: 1_000,
        }
    }
}

/// Signals that the caller requested cancellation while a scan was in progress.
#[derive(Debug)]
pub struct ScanCancelled;

/// Read-only scanner over the current user's Temp directory. Reads filesystem
/// metadata only (never file contents), never follows reparse points, and
/// re-validates every candidate against the path safety validator before it
/// is included in the result.
pub fn scan_temp_files(
    options: &ScanOptions,
    validator: &PathSafetyValidator,
    cancel_flag: &AtomicBool,
) -> Result<Vec<CleanupItem>, ScanCancelled> {
    let root = &options.temp_root;

    if !root.is_dir() || is_reparse_point(root) {
        return Ok(Vec::new());
    }

    let cutoff = SystemTime::now()
        .checked_sub(options.minimum_file_age)
        .unwrap_or(SystemTime::UNIX_EPOCH);

    let mut items = Vec::new();
    let _ = walk(
        root,
        root,
        cutoff,
        options.maximum_candidate_count,
        validator,
        cancel_flag,
        &mut items,
    );

    Ok(items)
}

fn walk(
    root: &Path,
    dir: &Path,
    cutoff: SystemTime,
    max_count: usize,
    validator: &PathSafetyValidator,
    cancel_flag: &AtomicBool,
    items: &mut Vec<CleanupItem>,
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
            walk(root, &path, cutoff, max_count, validator, cancel_flag, items)?;
            continue;
        }

        if !file_type.is_file() {
            continue;
        }

        if !is_under_root(&path, root) {
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

        let last_modified_utc: DateTime<Utc> = modified.into();

        items.push(CleanupItem {
            id: Uuid::new_v4().to_string(),
            full_path: validation.normalized_path.unwrap_or(full_path_str),
            category: "TemporaryFiles".to_string(),
            risk_level: RiskLevel::Low,
            size_bytes: metadata.len(),
            last_modified_utc: last_modified_utc.to_rfc3339(),
            detected_reason: "当前用户 Temp 目录中的过期临时文件（仅预览）".to_string(),
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

    fn make_validator(root: &Path) -> PathSafetyValidator {
        PathSafetyValidator::new(PathSafetyOptions {
            allowed_roots: vec![root.to_path_buf()],
            denied_roots: vec![],
            resolve_symbolic_links: true,
        })
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

        let validator = make_validator(tmp.path());
        let options = ScanOptions {
            temp_root: tmp.path().to_path_buf(),
            minimum_file_age: Duration::from_secs(24 * 3600),
            maximum_candidate_count: 1000,
        };
        let cancel = AtomicBool::new(false);
        let items = scan_temp_files(&options, &validator, &cancel).unwrap();

        assert_eq!(items.len(), 1);
        assert!(items[0].full_path.ends_with("old.tmp"));
    }

    #[test]
    fn respects_maximum_candidate_count() {
        let tmp = tempfile::tempdir().unwrap();
        for i in 0..10 {
            let f = tmp.path().join(format!("f{i}.tmp"));
            File::create(&f).unwrap().write_all(b"x").unwrap();
            set_old_mtime(&f, 48);
        }

        let validator = make_validator(tmp.path());
        let options = ScanOptions {
            temp_root: tmp.path().to_path_buf(),
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

        let validator = make_validator(tmp.path());
        let options = ScanOptions {
            temp_root: tmp.path().to_path_buf(),
            minimum_file_age: Duration::from_secs(24 * 3600),
            maximum_candidate_count: 1000,
        };
        let cancel = AtomicBool::new(true);
        let result = scan_temp_files(&options, &validator, &cancel).unwrap();
        assert!(result.is_empty(), "cancellation before any progress yields no candidates");
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

        let validator = make_validator(tmp.path());
        let options = ScanOptions {
            temp_root: tmp.path().to_path_buf(),
            minimum_file_age: Duration::from_secs(24 * 3600),
            maximum_candidate_count: 1000,
        };
        let cancel = AtomicBool::new(false);
        let items = scan_temp_files(&options, &validator, &cancel).unwrap();
        assert!(items.iter().all(|i| !i.full_path.contains("linked")));
    }
}


