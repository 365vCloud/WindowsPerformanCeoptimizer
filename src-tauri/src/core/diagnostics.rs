use crate::core::models::{DiskMetric, ProcessDiagnostic, StartupItem, SystemMetrics};
use chrono::Utc;

pub fn system_metrics() -> SystemMetrics {
    let mut system = sysinfo::System::new_all();
    system.refresh_all();
    let total = system.total_memory();
    let used = system.used_memory();
    let memory_percent = (total > 0).then(|| used as f32 * 100.0 / total as f32);
    let disks = sysinfo::Disks::new_with_refreshed_list()
        .iter()
        .map(|d| {
            let total_bytes = d.total_space();
            let free_bytes = d.available_space();
            DiskMetric {
                name: d.name().to_string_lossy().to_string(),
                mount_point: d.mount_point().to_string_lossy().to_string(),
                used_percent: (total_bytes > 0)
                    .then(|| (total_bytes - free_bytes) as f32 * 100.0 / total_bytes as f32),
                free_bytes: Some(free_bytes),
                total_bytes: Some(total_bytes),
            }
        })
        .collect();
    SystemMetrics {
        cpu_percent: Some(system.global_cpu_info().cpu_usage()),
        memory_percent,
        memory_used_bytes: Some(used),
        memory_total_bytes: Some(total),
        disks,
        collected_at_utc: Utc::now().to_rfc3339(),
        unavailable_reason: None,
    }
}

pub fn processes(limit: usize) -> Vec<ProcessDiagnostic> {
    let mut system = sysinfo::System::new_all();
    system.refresh_all();
    let total_memory = system.total_memory();
    let mut result: Vec<_> = system
        .processes()
        .values()
        .map(|p| ProcessDiagnostic {
            pid: p.pid().as_u32(),
            name: p.name().to_string(),
            cpu_percent: Some(p.cpu_usage()),
            memory_bytes: Some(p.memory()),
            memory_percent: (total_memory > 0)
                .then(|| p.memory() as f32 * 100.0 / total_memory as f32),
            unavailable_reason: None,
        })
        .collect();
    result.sort_by(|a, b| {
        b.cpu_percent
            .partial_cmp(&a.cpu_percent)
            .unwrap_or(std::cmp::Ordering::Equal)
    });
    result.truncate(limit.min(100));
    result
}

pub fn startup_items() -> Vec<StartupItem> {
    let mut out = Vec::new();
    let folders = [
        std::env::var_os("APPDATA").map(|v| {
            std::path::PathBuf::from(v).join(r"Microsoft\Windows\Start Menu\Programs\Startup")
        }),
        std::env::var_os("PROGRAMDATA").map(|v| {
            std::path::PathBuf::from(v).join(r"Microsoft\Windows\Start Menu\Programs\Startup")
        }),
    ];
    for (idx, folder) in folders.into_iter().flatten().enumerate() {
        match std::fs::read_dir(folder) {
            Ok(entries) => {
                for entry in entries.flatten() {
                    out.push(StartupItem {
                        name: entry.file_name().to_string_lossy().to_string(),
                        command: Some(entry.path().to_string_lossy().to_string()),
                        source: if idx == 0 {
                            "StartupFolderUser"
                        } else {
                            "StartupFolderCommon"
                        }
                        .into(),
                        enabled: Some(true),
                        unavailable_reason: None,
                    });
                }
            }
            Err(e) => out.push(StartupItem {
                name: "Startup folder".into(),
                command: None,
                source: if idx == 0 {
                    "StartupFolderUser"
                } else {
                    "StartupFolderCommon"
                }
                .into(),
                enabled: None,
                unavailable_reason: Some(e.to_string()),
            }),
        }
    }
    #[cfg(windows)]
    {
        use winreg::enums::{HKEY_CURRENT_USER, HKEY_LOCAL_MACHINE};
        for (root, path, source) in [
            (
                HKEY_CURRENT_USER,
                r"Software\Microsoft\Windows\CurrentVersion\Run",
                "RegistryUserRun",
            ),
            (
                HKEY_LOCAL_MACHINE,
                r"Software\Microsoft\Windows\CurrentVersion\Run",
                "RegistryMachineRun",
            ),
        ] {
            if let Ok(key) = winreg::RegKey::predef(root).open_subkey(path) {
                for value in key.enum_values().flatten() {
                    out.push(StartupItem {
                        name: value.0,
                        command: Some(value.1.to_string()),
                        source: source.into(),
                        enabled: Some(true),
                        unavailable_reason: None,
                    });
                }
            }
        }
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn process_limit_is_enforced() {
        assert!(processes(1).len() <= 1);
    }
    #[test]
    fn startup_reader_is_best_effort() {
        let _ = startup_items();
    }
}
