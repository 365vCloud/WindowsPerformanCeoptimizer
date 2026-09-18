use std::fs;
use std::io::Write;
use std::path::PathBuf;

use chrono::Utc;

use crate::core::models::CleanupExecutionResult;

fn reports_dir() -> PathBuf {
    let local_app_data = std::env::var("LOCALAPPDATA").unwrap_or_else(|_| ".".to_string());
    PathBuf::from(local_app_data)
        .join("WindowsPerformanceOptimizer")
        .join("reports")
}

/// Exports a completed cleanup execution report as UTF-8 (no BOM) CSV or
/// JSON under the current user's LocalAppData. Returns the written file's
/// full path. Export failures return an `Err` and never touch/replace any
/// previously written report.
pub fn export_report(result: &CleanupExecutionResult, format: &str) -> Result<String, String> {
    let dir = reports_dir();
    fs::create_dir_all(&dir).map_err(|e| e.to_string())?;

    let timestamp = Utc::now().format("%Y%m%d-%H%M%S");
    let (file_name, contents) = match format.to_lowercase().as_str() {
        "json" => (
            format!("cleanup-report-{timestamp}.json"),
            serde_json::to_string_pretty(result).map_err(|e| e.to_string())?,
        ),
        "csv" => (format!("cleanup-report-{timestamp}.csv"), to_csv(result)),
        other => return Err(format!("Unsupported export format: {other}")),
    };

    let path = dir.join(file_name);
    let mut file = fs::File::create(&path).map_err(|e| e.to_string())?;
    file.write_all(contents.as_bytes())
        .map_err(|e| e.to_string())?;

    Ok(path.to_string_lossy().to_string())
}

fn to_csv(result: &CleanupExecutionResult) -> String {
    let mut csv =
        String::from("ItemId,FullPath,Status,SizeBytes,WasSelected,Reason,ErrorMessage\n");
    for item in &result.items {
        csv.push_str(&format!(
            "{},{},{:?},{},{},{},{}\n",
            csv_escape(&item.item_id),
            csv_escape(&item.full_path),
            item.status,
            item.size_bytes,
            item.was_selected,
            csv_escape(&item.reason),
            csv_escape(item.error_message.as_deref().unwrap_or(""))
        ));
    }
    csv
}

fn csv_escape(value: &str) -> String {
    // CSV formula-injection guard: a leading =, +, -, or @ makes some
    // spreadsheet apps interpret the cell as a formula when opened.
    let guarded = if value.starts_with(['=', '+', '-', '@']) {
        format!("'{value}")
    } else {
        value.to_string()
    };

    if guarded.contains(',') || guarded.contains('"') || guarded.contains('\n') {
        format!("\"{}\"", guarded.replace('"', "\"\""))
    } else {
        guarded
    }
}
