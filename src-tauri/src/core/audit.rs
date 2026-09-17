use std::fs::{self, OpenOptions};
use std::io::{BufRead, BufReader, Write};
use std::path::PathBuf;
use std::sync::Mutex;

use once_cell::sync::Lazy;
use regex::Regex;

use crate::core::models::AuditLogEntry;

static WRITE_LOCK: Lazy<Mutex<()>> = Lazy::new(|| Mutex::new(()));

static SECRET_PATTERN: Lazy<Regex> = Lazy::new(|| {
    Regex::new(r"(?i)\b(password|token|secret|api[-_]?key)\b\s*([:=])\s*\S+").unwrap()
});

static WINDOWS_PATH_PATTERN: Lazy<Regex> =
    Lazy::new(|| Regex::new(r"(?i)(?:[a-z]:[\\/]|\\\\)[^\s\r\n]+").unwrap());

/// Directory + filename mirroring the previous WPF app's location so any
/// existing on-disk audit history remains discoverable after the migration.
pub fn log_path() -> PathBuf {
    let local_app_data = std::env::var("LOCALAPPDATA").unwrap_or_else(|_| ".".to_string());
    PathBuf::from(local_app_data)
        .join("WindowsPerformanceOptimizer")
        .join("audit.jsonl")
}

/// Strips CR/LF (log injection defense), redacts obvious secret-looking
/// tokens, replaces Windows path-like substrings with `<path>`, then masks
/// the current username/profile directory with `<user>`.
pub(crate) fn sanitize_log_text(message: &str) -> String {
    let flattened = message.replace(['\r', '\n'], " ");
    let flattened = flattened.trim();
    let without_secrets = SECRET_PATTERN.replace_all(flattened, "$1$2<redacted>");
    let without_paths = WINDOWS_PATH_PATTERN.replace_all(&without_secrets, "<path>");
    mask_user(&without_paths)
}

fn sanitize_path(path: &str) -> String {
    mask_user(path)
}

fn mask_user(value: &str) -> String {
    let mut result = value.to_string();
    if let Ok(username) = std::env::var("USERNAME") {
        if !username.is_empty() {
            result = replace_case_insensitive(&result, &username, "<user>");
        }
    }
    if let Ok(profile) = std::env::var("USERPROFILE") {
        if !profile.is_empty() {
            result = replace_case_insensitive(&result, &profile, "<user>");
        }
    }
    result
}

fn replace_case_insensitive(haystack: &str, needle: &str, replacement: &str) -> String {
    if needle.is_empty() {
        return haystack.to_string();
    }
    let lower_haystack = haystack.to_lowercase();
    let lower_needle = needle.to_lowercase();
    let mut result = String::new();
    let mut last_end = 0;
    let mut start = 0;
    while let Some(pos) = lower_haystack[start..].find(&lower_needle) {
        let match_start = start + pos;
        let match_end = match_start + needle.len();
        result.push_str(&haystack[last_end..match_start]);
        result.push_str(replacement);
        last_end = match_end;
        start = match_end;
    }
    result.push_str(&haystack[last_end..]);
    result
}

pub fn sanitize_entry(mut entry: AuditLogEntry) -> AuditLogEntry {
    entry.message = sanitize_log_text(&entry.message);
    entry.masked_path = entry.masked_path.map(|p| sanitize_path(&p));
    entry
}

pub fn append_entry(entry: &AuditLogEntry) -> std::io::Result<()> {
    let _guard = WRITE_LOCK.lock().unwrap();
    let path = log_path();
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent)?;
    }
    let mut file = OpenOptions::new().create(true).append(true).open(&path)?;
    let line = serde_json::to_string(entry).unwrap_or_default();
    writeln!(file, "{line}")?;
    Ok(())
}

pub fn read_entries() -> std::io::Result<Vec<AuditLogEntry>> {
    let _guard = WRITE_LOCK.lock().unwrap();
    let path = log_path();
    if !path.exists() {
        return Ok(Vec::new());
    }
    let file = fs::File::open(&path)?;
    let reader = BufReader::new(file);
    let mut entries = Vec::new();
    for line in reader.lines() {
        let line = line?;
        if line.trim().is_empty() {
            continue;
        }
        if let Ok(entry) = serde_json::from_str::<AuditLogEntry>(&line) {
            entries.push(entry);
        }
    }
    Ok(entries)
}

/// Clearing the log requires an explicit confirmation flag from the caller;
/// there is no implicit/automatic clearing path.
pub fn clear_log(confirmed: bool) -> Result<(), String> {
    if !confirmed {
        return Err("Audit log clearing requires explicit confirmation.".to_string());
    }
    let _guard = WRITE_LOCK.lock().unwrap();
    let path = log_path();
    if path.exists() {
        fs::remove_file(&path).map_err(|e| e.to_string())?;
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn masks_secret_looking_tokens() {
        let msg = sanitize_log_text("login failed password=hunter2 for user");
        assert!(!msg.contains("hunter2"));
        assert!(msg.contains("<redacted>"));
    }

    #[test]
    fn masks_windows_paths() {
        let msg = sanitize_log_text(r"deleted file C:\Users\alice\AppData\Local\Temp\a.tmp");
        assert!(!msg.to_lowercase().contains("alice"));
        assert!(msg.contains("<path>"));
    }

    #[test]
    fn strips_newlines_to_prevent_log_injection() {
        let msg = sanitize_log_text("line1\nFAKE_ENTRY\r\nline2");
        assert!(!msg.contains('\n'));
        assert!(!msg.contains('\r'));
    }
}
