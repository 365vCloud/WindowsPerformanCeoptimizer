pub mod commands;
pub mod core;
mod crash;

use commands::AppState;

pub fn install_panic_hook() {
    crash::install_panic_hook();
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    let result = tauri::Builder::default()
        .manage(AppState::default())
        .invoke_handler(tauri::generate_handler![
            commands::scan_temp_files,
            commands::scan_junk_files,
            commands::get_system_metrics,
            commands::scan_processes,
            commands::scan_startup_items,
            commands::cancel_scan,
            commands::cancel_execution,
            commands::revalidate_paths,
            commands::execute_cleanup,
            commands::get_audit_log,
            commands::clear_audit_log,
            commands::export_report,
            commands::export_result,
        ])
        .run(tauri::generate_context!());

    if let Err(error) = result {
        crash::handle_run_error(&error);
        std::process::exit(1);
    }
}
