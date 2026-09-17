use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Mutex;
use std::time::Duration;

use tauri::State;

use crate::core::audit::{clear_log, read_entries};
use crate::core::cleanup::execute_cleanup as run_cleanup;
use crate::core::models::{
    AuditLogEntry, CleanupExecutionResult, CleanupItem, CleanupSelection, PathValidationOutcome,
    ScanOptionsInput,
};
use crate::core::path_safety::{PathSafetyOptions, PathSafetyValidator};
use crate::core::scanner::{scan_temp_files as run_scan, ScanOptions};
use crate::core::models::{ProcessDiagnostic, StartupItem, SystemMetrics};

const MAX_CANDIDATE_HARD_CAP: usize = 5_000;

/// Shared, in-memory application state for a single window session. Holding
/// the last scan's results server-side (rather than round-tripping the full
/// list through the frontend on every call) keeps execution authoritative:
/// the frontend can only ever act on paths this process itself discovered.
pub struct AppState {
    pub last_scan: Mutex<Vec<CleanupItem>>,
    pub scan_cancel: AtomicBool,
    pub exec_cancel: AtomicBool,
}

impl Default for AppState {
    fn default() -> Self {
        Self {
            last_scan: Mutex::new(Vec::new()),
            scan_cancel: AtomicBool::new(false),
            exec_cancel: AtomicBool::new(false),
        }
    }
}

fn build_validator() -> PathSafetyValidator {
    let temp_root = std::env::temp_dir();
    PathSafetyValidator::new(PathSafetyOptions::with_defaults(vec![temp_root]))
}

#[tauri::command]
pub fn scan_temp_files(
    state: State<AppState>,
    options: Option<ScanOptionsInput>,
) -> Result<Vec<CleanupItem>, String> {
    state.scan_cancel.store(false, Ordering::SeqCst);

    let mut scan_options = ScanOptions::default();
    if let Some(opts) = options {
        if let Some(hours) = opts.minimum_age_hours {
            scan_options.minimum_file_age = Duration::from_secs(hours * 3600);
        }

        if let Some(count) = opts.maximum_candidate_count {
            scan_options.maximum_candidate_count = count.min(MAX_CANDIDATE_HARD_CAP);
        }

    }

    let validator = build_validator();
    let items = run_scan(&scan_options, &validator, &state.scan_cancel)
        .map_err(|_| "扫描已取消。".to_string())?;

    let mut last_scan = state.last_scan.lock().map_err(|_| "内部状态异常。".to_string())?;
    *last_scan = items.clone();

    Ok(items)
}

#[tauri::command]
pub fn get_system_metrics() -> SystemMetrics { crate::core::diagnostics::system_metrics() }

#[tauri::command]
pub fn scan_processes(limit: Option<usize>) -> Vec<ProcessDiagnostic> {
    crate::core::diagnostics::processes(limit.unwrap_or(20))
}

#[tauri::command]
pub fn scan_startup_items() -> Vec<StartupItem> { crate::core::diagnostics::startup_items() }

#[tauri::command]
pub fn scan_junk_files(
    state: State<AppState>, options: Option<ScanOptionsInput>,
) -> Result<Vec<CleanupItem>, String> { scan_temp_files(state, options) }

#[tauri::command]
pub fn cancel_scan(state: State<AppState>) {
    state.scan_cancel.store(true, Ordering::SeqCst);
}

#[tauri::command]
pub fn cancel_execution(state: State<AppState>) {
    state.exec_cancel.store(true, Ordering::SeqCst);
}

/// Re-validates a set of paths right before showing the confirmation dialog.
/// The filesystem can change between scan and confirmation; the frontend
/// must call this immediately before allowing the user to proceed, and drop
/// any item that comes back `is_allowed: false`.
#[tauri::command]
pub fn revalidate_paths(paths: Vec<String>) -> Vec<PathValidationOutcome> {
    let validator = build_validator();
    paths.iter().map(|p| validator.validate(p)).collect()
}

#[tauri::command]
pub fn execute_cleanup(
    state: State<AppState>,
    selection: CleanupSelection,
) -> Result<CleanupExecutionResult, String> {
    state.exec_cancel.store(false, Ordering::SeqCst);
    let validator = build_validator();
    let items = state
        .last_scan
        .lock()
        .map_err(|_| "内部状态异常。".to_string())?
        .clone();

    run_cleanup(&items, &selection, &validator, &state.exec_cancel)
}

#[tauri::command]
pub fn get_audit_log() -> Result<Vec<AuditLogEntry>, String> {
    read_entries().map_err(|e| e.to_string())
}

#[tauri::command]
pub fn clear_audit_log(confirmed: bool) -> Result<(), String> {
    clear_log(confirmed)
}

#[tauri::command]
pub fn export_report(result: CleanupExecutionResult, format: String) -> Result<String, String> {
    crate::core::export::export_report(&result, &format)
}

#[tauri::command]
pub fn export_result(result: CleanupExecutionResult, format: String) -> Result<String, String> {
    export_report(result, format)
}
