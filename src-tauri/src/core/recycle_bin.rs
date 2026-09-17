use std::path::Path;

/// Moves a file or directory to the Windows Recycle Bin using the `trash`
/// crate, which delegates to the Windows Shell (IFileOperation) under the
/// hood. This module intentionally exposes no permanent-delete function:
/// permanent deletion is out of scope for this application by design.
pub fn move_to_recycle_bin(path: &str) -> Result<(), String> {
    let p = Path::new(path);
    if !p.exists() {
        return Err("Path no longer exists.".to_string());
    }

    trash::delete(p).map_err(|e| e.to_string())
}
