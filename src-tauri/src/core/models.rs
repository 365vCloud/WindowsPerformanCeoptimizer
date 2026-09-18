use serde::{Deserialize, Serialize};

/// Risk classification for a cleanup candidate. Higher risk items require
/// additional, explicit user confirmation before they can ever be deleted.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum RiskLevel {
    Low,
    Medium,
    High,
}

/// A single scanned cleanup candidate. Contains only filesystem metadata;
/// file contents are never read by the scanner.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CleanupItem {
    pub id: String,
    pub full_path: String,
    pub category: String,
    pub risk_level: RiskLevel,
    pub size_bytes: u64,
    /// RFC3339 timestamp (UTC) of the file's last modification time.
    pub last_modified_utc: String,
    pub detected_reason: String,
}

/// Options controlling a single scan invocation. All fields are optional on
/// the frontend and default to conservative values server-side.
#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ScanOptionsInput {
    /// Minimum file age in hours before a file becomes a candidate. Defaults to 24.
    pub minimum_age_hours: Option<u64>,
    /// Maximum number of candidates returned. Defaults to 1000, hard-capped at 5000.
    pub maximum_candidate_count: Option<usize>,
}

/// The user's explicit selection + confirmations for an execution pass.
/// Deletion is *always* "move to Recycle Bin" - there is no permanent
/// deletion mode in this application.
#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CleanupSelection {
    pub selected_item_ids: Vec<String>,
    /// Must be explicitly true for any Medium risk item to be processed.
    #[serde(default)]
    pub confirm_medium_risk: bool,
    /// Must be explicitly true for any High risk item to be processed.
    #[serde(default)]
    pub confirm_high_risk: bool,
    /// Second, explicit "yes" confirmation gate from the confirmation dialog.
    /// Required to be true or execution refuses to run at all.
    #[serde(default)]
    pub confirmed: bool,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum CleanupItemStatus {
    Deleted,
    Skipped,
    Failed,
    Cancelled,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CleanupExecutionItemResult {
    pub item_id: String,
    pub full_path: String,
    pub status: CleanupItemStatus,
    pub size_bytes: u64,
    pub was_selected: bool,
    pub reason: String,
    pub error_message: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CleanupExecutionResult {
    pub items: Vec<CleanupExecutionItemResult>,
    pub was_cancelled: bool,
    pub started_at_utc: String,
    pub completed_at_utc: String,
    /// Sum of `size_bytes` for items that actually reached `Deleted` status.
    pub total_bytes_freed: u64,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PathValidationOutcome {
    pub full_path: String,
    pub is_allowed: bool,
    pub normalized_path: Option<String>,
    pub rejection_reason: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AuditLogEntry {
    pub timestamp_utc: String,
    pub action_type: String,
    pub message: String,
    pub masked_path: Option<String>,
    pub size_bytes: Option<u64>,
    pub correlation_id: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SystemMetrics {
    pub cpu_percent: Option<f32>,
    pub memory_percent: Option<f32>,
    pub memory_used_bytes: Option<u64>,
    pub memory_total_bytes: Option<u64>,
    pub disks: Vec<DiskMetric>,
    pub collected_at_utc: String,
    pub unavailable_reason: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DiskMetric {
    pub name: String,
    pub mount_point: String,
    pub used_percent: Option<f32>,
    pub free_bytes: Option<u64>,
    pub total_bytes: Option<u64>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ProcessDiagnostic {
    pub pid: u32,
    pub name: String,
    pub cpu_percent: Option<f32>,
    pub memory_bytes: Option<u64>,
    pub memory_percent: Option<f32>,
    pub unavailable_reason: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct StartupItem {
    pub name: String,
    pub command: Option<String>,
    pub source: String,
    pub enabled: Option<bool>,
    pub unavailable_reason: Option<String>,
}
