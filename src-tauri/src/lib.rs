pub mod commands;
pub mod core;

use commands::AppState;

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .manage(AppState::default())
        .invoke_handler(tauri::generate_handler![
            commands::scan_temp_files,
            commands::cancel_scan,
            commands::cancel_execution,
            commands::revalidate_paths,
            commands::execute_cleanup,
            commands::get_audit_log,
            commands::clear_audit_log,
            commands::export_report,
        ])
        .run(tauri::generate_context!())
        .expect("error while running Windows Performance Optimizer");
}
