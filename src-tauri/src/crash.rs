use std::any::type_name_of_val;
use std::fs::{self, OpenOptions};
use std::io::Write;
use std::panic::PanicHookInfo;
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, Ordering};

use chrono::Local;

use crate::core::audit::sanitize_log_text;

static DIALOG_VISIBLE: AtomicBool = AtomicBool::new(false);

pub fn install_panic_hook() {
    let previous_hook = std::panic::take_hook();
    std::panic::set_hook(Box::new(move |panic_info| {
        handle_panic(panic_info);
        previous_hook(panic_info);
    }));
}

pub fn handle_run_error(error: &dyn std::error::Error) {
    let summary = sanitize_log_text(&error.to_string());
    let log_path = append_crash_log("tauri_run_error", "tauri::Error", &summary, None);
    show_startup_error_dialog(
        "Windows Performance Optimizer 启动失败。",
        &summary,
        log_path.as_deref(),
    );
}

fn handle_panic(panic_info: &PanicHookInfo<'_>) {
    let payload = panic_info.payload();
    let (payload_type, message): (String, String) =
        if let Some(value) = payload.downcast_ref::<&str>() {
            ("&str".to_string(), sanitize_log_text(value))
        } else if let Some(value) = payload.downcast_ref::<String>() {
            ("String".to_string(), sanitize_log_text(value))
        } else {
            (
                sanitize_log_text(type_name_of_val(payload)),
                "未提供 panic 文本。".to_string(),
            )
        };

    let location = panic_info.location().map(|location| {
        format!(
            "{}:{}:{}",
            location.file(),
            location.line(),
            location.column()
        )
    });
    let log_path = append_crash_log("panic", &payload_type, &message, location.as_deref());

    let summary = match location {
        Some(location) => format!("{message}\n位置：{location}"),
        None => message.to_string(),
    };
    show_startup_error_dialog(
        "Windows Performance Optimizer 发生严重错误并已退出。",
        &summary,
        log_path.as_deref(),
    );
}

fn append_crash_log(
    crash_kind: &str,
    detail_type: &str,
    message: &str,
    location: Option<&str>,
) -> Option<String> {
    let path = crash_log_path();
    let parent = path.parent()?.to_path_buf();
    fs::create_dir_all(parent).ok()?;

    let mut file = OpenOptions::new()
        .create(true)
        .append(true)
        .open(&path)
        .ok()?;

    let timestamp = Local::now().to_rfc3339();
    writeln!(file, "timestamp={timestamp}").ok()?;
    writeln!(file, "kind={}", sanitize_log_text(crash_kind)).ok()?;
    writeln!(file, "detail_type={}", sanitize_log_text(detail_type)).ok()?;
    writeln!(file, "message={}", sanitize_log_text(message)).ok()?;
    if let Some(location) = location {
        writeln!(file, "location={}", sanitize_log_text(location)).ok()?;
    }

    Some(path.display().to_string())
}

fn crash_log_path() -> PathBuf {
    let local_app_data = std::env::var("LOCALAPPDATA").unwrap_or_else(|_| ".".to_string());
    let file_name = format!(
        "crash-{}-{}.log",
        Local::now().format("%Y%m%d-%H%M%S"),
        std::process::id()
    );

    PathBuf::from(local_app_data)
        .join("WindowsPerformanceOptimizer")
        .join("logs")
        .join(file_name)
}

fn show_startup_error_dialog(title: &str, summary: &str, log_path: Option<&str>) {
    if DIALOG_VISIBLE.swap(true, Ordering::SeqCst) {
        return;
    }

    let mut body = format!(
        "{title}\n\n错误摘要：{summary}\n\n常见原因：Microsoft Edge WebView2 Runtime 缺失或安装不完整。"
    );
    if let Some(log_path) = log_path {
        body.push_str("\n诊断日志：");
        body.push_str(log_path);
    }
    body.push_str("\n\n请安装/修复 WebView2 Runtime 后重试。");

    show_message_box("Windows Performance Optimizer", &body);
    DIALOG_VISIBLE.store(false, Ordering::SeqCst);
}

#[cfg(target_os = "windows")]
fn show_message_box(title: &str, body: &str) {
    use windows_sys::Win32::UI::WindowsAndMessaging::{MessageBoxW, MB_ICONERROR, MB_OK};

    let title_utf16: Vec<u16> = title.encode_utf16().chain([0]).collect();
    let body_utf16: Vec<u16> = body.encode_utf16().chain([0]).collect();

    unsafe {
        MessageBoxW(
            std::ptr::null_mut(),
            body_utf16.as_ptr(),
            title_utf16.as_ptr(),
            MB_ICONERROR | MB_OK,
        );
    }
}

#[cfg(not(target_os = "windows"))]
fn show_message_box(_title: &str, _body: &str) {}
