use std::fs;
use std::path::{Path, PathBuf};

use crate::core::models::PathValidationOutcome;

/// Deny-by-default path safety validator. A candidate path is only ever
/// considered safe once it has survived, in order: empty/whitespace check,
/// traversal-segment rejection, normalization, drive-root rejection,
/// denied-root rejection, allow-list containment, and (if enabled)
/// reparse-point/symlink-escape resolution.
pub struct PathSafetyOptions {
    pub allowed_roots: Vec<PathBuf>,
    pub denied_roots: Vec<PathBuf>,
    pub resolve_symbolic_links: bool,
}

impl PathSafetyOptions {
    /// Conservative defaults: deny all well-known system/critical roots.
    pub fn with_defaults(allowed_roots: Vec<PathBuf>) -> Self {
        let mut denied_roots = Vec::new();
        for var in ["WINDIR", "SystemRoot"] {
            if let Ok(v) = std::env::var(var) {
                denied_roots.push(PathBuf::from(v));
            }
        }
        if let Ok(pf) = std::env::var("ProgramFiles") {
            denied_roots.push(PathBuf::from(pf));
        }
        if let Ok(pf86) = std::env::var("ProgramFiles(x86)") {
            denied_roots.push(PathBuf::from(pf86));
        }
        if let Ok(profile) = std::env::var("USERPROFILE") {
            denied_roots.push(PathBuf::from(profile));
        }

        Self {
            allowed_roots,
            denied_roots,
            resolve_symbolic_links: true,
        }
    }
}

pub struct PathSafetyValidator {
    options: PathSafetyOptions,
}

impl PathSafetyValidator {
    pub fn new(options: PathSafetyOptions) -> Self {
        Self { options }
    }

    pub fn validate(&self, candidate_path: &str) -> PathValidationOutcome {
        if candidate_path.trim().is_empty() {
            return reject("EmptyPath", None);
        }

        if contains_traversal_segment(candidate_path) {
            return reject("TraversalSegment", None);
        }

        let absolute = match std::path::absolute(candidate_path) {
            Ok(p) => p,
            Err(_) => return reject("InvalidPath", None),
        };

        // Checked against the untrimmed absolute path: trimming a trailing
        // separator from a bare drive root (e.g. "C:\") would otherwise turn
        // it into a drive-relative path ("C:") with no root component,
        // silently defeating this check.
        if is_drive_or_root_path(&absolute) {
            return reject("DriveRootDeletionForbidden", Some(&absolute));
        }

        let normalized = trim_trailing_separators(&absolute);

        if is_under_any_root(&normalized, &self.options.denied_roots)
            && !has_allowed_child_override(
                &normalized,
                &self.options.denied_roots,
                &self.options.allowed_roots,
            )
        {
            return reject("DeniedRoot", Some(&normalized));
        }

        if !is_under_any_root(&normalized, &self.options.allowed_roots) {
            return reject("NotInAllowList", Some(&normalized));
        }

        if self.options.resolve_symbolic_links {
            if let Some(resolved) = try_resolve_final_target(&normalized) {
                if !path_equals(&resolved, &normalized) {
                    let resolved_normalized = trim_trailing_separators(&resolved);
                    let resolved_path = PathBuf::from(&resolved_normalized);
                    if !is_under_any_root(&resolved_path, &self.options.allowed_roots)
                        || (is_under_any_root(&resolved_path, &self.options.denied_roots)
                            && !has_allowed_child_override(
                                &resolved_path,
                                &self.options.denied_roots,
                                &self.options.allowed_roots,
                            ))
                    {
                        return reject("SymlinkEscape", Some(&normalized));
                    }
                }
            }
        }

        PathValidationOutcome {
            full_path: candidate_path.to_string(),
            is_allowed: true,
            normalized_path: Some(path_to_string(&normalized)),
            rejection_reason: None,
        }
    }
}

fn reject(reason: &str, normalized: Option<&Path>) -> PathValidationOutcome {
    PathValidationOutcome {
        full_path: normalized.map(path_to_string).unwrap_or_default(),
        is_allowed: false,
        normalized_path: None,
        rejection_reason: Some(reason.to_string()),
    }
}

fn contains_traversal_segment(path: &str) -> bool {
    path.split(['\\', '/']).any(|s| s == "..")
}

/// Lexical normalization equivalent to .NET's `Path.GetFullPath` for our
/// purposes: traversal segments are already rejected before this runs, so we
/// only need an absolute, separator-normalized path - no filesystem access.
fn normalize(candidate: &str) -> Option<PathBuf> {
    let absolute = std::path::absolute(candidate).ok()?;
    Some(PathBuf::from(trim_trailing_separators(&absolute)))
}

fn trim_trailing_separators(path: &Path) -> PathBuf {
    let s = path.to_string_lossy();
    let trimmed = s.trim_end_matches(['\\', '/']);
    PathBuf::from(trimmed)
}

fn path_to_string(path: &Path) -> String {
    path.to_string_lossy().to_string()
}

fn path_equals(a: &Path, b: &Path) -> bool {
    a.to_string_lossy().to_lowercase() == b.to_string_lossy().to_lowercase()
}

fn is_drive_or_root_path(normalized: &Path) -> bool {
    let mut components = normalized.components();
    match components.next() {
        Some(std::path::Component::Prefix(_)) => {}
        _ => return false,
    }
    match components.next() {
        Some(std::path::Component::RootDir) => {}
        _ => return false,
    }
    components.next().is_none()
}

fn is_under_any_root(candidate: &Path, roots: &[PathBuf]) -> bool {
    roots.iter().any(|root| {
        if root.as_os_str().is_empty() {
            return false;
        }
        let normalized_root = match normalize(&root.to_string_lossy()) {
            Some(p) => p,
            None => return false,
        };
        is_under(candidate, &normalized_root)
    })
}

fn has_allowed_child_override(
    candidate: &Path,
    denied_roots: &[PathBuf],
    allowed_roots: &[PathBuf],
) -> bool {
    denied_roots.iter().any(|denied| {
        let Some(denied_root) = normalize(&denied.to_string_lossy()) else {
            return false;
        };

        is_under(candidate, &denied_root)
            && allowed_roots.iter().any(|allowed| {
                let Some(allowed_root) = normalize(&allowed.to_string_lossy()) else {
                    return false;
                };

                !path_equals(&allowed_root, &denied_root)
                    && is_under(candidate, &allowed_root)
                    && is_under(&allowed_root, &denied_root)
            })
    })
}

/// Segment-aware "candidate is at or below root" check. Comparing against
/// `root + separator` (never a raw string prefix) is what prevents
/// "similar prefix" bypasses, e.g. an allowed root of `C:\Temp\Cache` must
/// not match the sibling folder `C:\Temp\CacheOld`.
fn is_under(candidate: &Path, root: &Path) -> bool {
    if path_equals(candidate, root) {
        return true;
    }

    let root_str = root.to_string_lossy().to_lowercase();
    let candidate_str = candidate.to_string_lossy().to_lowercase();
    let root_with_sep = if root_str.ends_with('\\') {
        root_str
    } else {
        format!("{}\\", root_str)
    };

    candidate_str.starts_with(&root_with_sep)
}

/// Best-effort reparse point / symlink resolution. Returns `None` when the
/// path does not exist or is not a reparse point, meaning no re-validation
/// against the resolved target is needed.
fn try_resolve_final_target(path: &Path) -> Option<PathBuf> {
    let metadata = fs::symlink_metadata(path).ok()?;
    if !metadata.file_type().is_symlink() {
        return None;
    }

    match fs::canonicalize(path) {
        Ok(resolved) => Some(strip_extended_prefix(resolved)),
        Err(_) => None,
    }
}

/// Windows `canonicalize` returns `\\?\`-prefixed paths; strip the prefix so
/// downstream string comparisons behave the same as ordinary paths.
fn strip_extended_prefix(path: PathBuf) -> PathBuf {
    let s = path.to_string_lossy();
    if let Some(stripped) = s.strip_prefix(r"\\?\") {
        PathBuf::from(stripped)
    } else {
        path
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::fs::{self as stdfs, File};

    fn validator(allowed: Vec<PathBuf>) -> PathSafetyValidator {
        PathSafetyValidator::new(PathSafetyOptions {
            allowed_roots: allowed,
            denied_roots: vec![],
            resolve_symbolic_links: true,
        })
    }

    #[test]
    fn rejects_traversal_segments() {
        let tmp = tempfile::tempdir().unwrap();
        let v = validator(vec![tmp.path().to_path_buf()]);
        let candidate = tmp.path().join("..").join("evil");
        let result = v.validate(candidate.to_str().unwrap());
        assert!(!result.is_allowed);
        assert_eq!(result.rejection_reason.as_deref(), Some("TraversalSegment"));
    }

    #[test]
    fn rejects_similar_prefix_sibling_directory() {
        let tmp = tempfile::tempdir().unwrap();
        let allowed_root = tmp.path().join("Cache");
        stdfs::create_dir_all(&allowed_root).unwrap();
        let sibling = tmp.path().join("CacheOld");
        stdfs::create_dir_all(&sibling).unwrap();
        let candidate_file = sibling.join("file.tmp");
        File::create(&candidate_file).unwrap();

        let v = validator(vec![allowed_root]);
        let result = v.validate(candidate_file.to_str().unwrap());
        assert!(!result.is_allowed);
        assert_eq!(result.rejection_reason.as_deref(), Some("NotInAllowList"));
    }

    #[test]
    fn allows_path_within_allowed_root() {
        let tmp = tempfile::tempdir().unwrap();
        let file_path = tmp.path().join("file.tmp");
        File::create(&file_path).unwrap();

        let v = validator(vec![tmp.path().to_path_buf()]);
        let result = v.validate(file_path.to_str().unwrap());
        assert!(result.is_allowed);
        assert!(result.normalized_path.is_some());
    }

    #[test]
    fn rejects_drive_root() {
        let v = validator(vec![PathBuf::from("C:\\")]);
        let result = v.validate("C:\\");
        assert!(!result.is_allowed);
        assert_eq!(
            result.rejection_reason.as_deref(),
            Some("DriveRootDeletionForbidden")
        );
    }

    #[test]
    fn rejects_denied_root_even_if_under_allowed_root() {
        let tmp = tempfile::tempdir().unwrap();
        let denied = tmp.path().join("denied");
        stdfs::create_dir_all(&denied).unwrap();
        let file_path = denied.join("file.tmp");
        File::create(&file_path).unwrap();

        let v = PathSafetyValidator::new(PathSafetyOptions {
            allowed_roots: vec![tmp.path().to_path_buf()],
            denied_roots: vec![denied],
            resolve_symbolic_links: true,
        });
        let result = v.validate(file_path.to_str().unwrap());
        assert!(!result.is_allowed);
        assert_eq!(result.rejection_reason.as_deref(), Some("DeniedRoot"));
    }

    #[test]
    fn allows_explicit_safe_child_inside_denied_parent() {
        let tmp = tempfile::tempdir().unwrap();
        let safe_child = tmp.path().join("safe-temp");
        let sibling = tmp.path().join("documents");
        stdfs::create_dir_all(&safe_child).unwrap();
        stdfs::create_dir_all(&sibling).unwrap();
        let safe_file = safe_child.join("file.tmp");
        let sibling_file = sibling.join("private.txt");
        File::create(&safe_file).unwrap();
        File::create(&sibling_file).unwrap();

        let v = PathSafetyValidator::new(PathSafetyOptions {
            allowed_roots: vec![safe_child],
            denied_roots: vec![tmp.path().to_path_buf()],
            resolve_symbolic_links: true,
        });

        assert!(v.validate(safe_file.to_str().unwrap()).is_allowed);
        let rejected = v.validate(sibling_file.to_str().unwrap());
        assert!(!rejected.is_allowed);
        assert_eq!(rejected.rejection_reason.as_deref(), Some("DeniedRoot"));
    }

    #[cfg(windows)]
    #[test]
    fn rejects_symlink_escape() {
        let tmp = tempfile::tempdir().unwrap();
        let allowed_root = tmp.path().join("allowed");
        let outside_root = tmp.path().join("outside");
        stdfs::create_dir_all(&allowed_root).unwrap();
        stdfs::create_dir_all(&outside_root).unwrap();
        let real_file = outside_root.join("secret.txt");
        File::create(&real_file).unwrap();

        let link_path = allowed_root.join("link.txt");
        if std::os::windows::fs::symlink_file(&real_file, &link_path).is_err() {
            // Symlink creation requires elevated privileges/dev mode on some
            // CI runners; skip rather than fail spuriously in that case.
            return;
        }

        let v = validator(vec![allowed_root]);
        let result = v.validate(link_path.to_str().unwrap());
        assert!(!result.is_allowed);
        assert_eq!(result.rejection_reason.as_deref(), Some("SymlinkEscape"));
    }
}
