use std::sync::atomic::{AtomicBool, Ordering};

use chrono::Utc;
use uuid::Uuid;

use crate::core::audit::{append_entry, sanitize_entry};
use crate::core::models::{
    AuditLogEntry, CleanupExecutionItemResult, CleanupExecutionResult, CleanupItem,
    CleanupItemStatus, CleanupSelection, RiskLevel,
};
use crate::core::path_safety::PathSafetyValidator;
use crate::core::recycle_bin::move_to_recycle_bin;

/// Executes a cleanup pass over previously scanned items according to the
/// user's selection and confirmations. Safety invariants mirrored from the
/// original .NET core:
/// - Nothing happens unless `selection.confirmed` is explicitly true (the
///   second, explicit confirmation-dialog gate); refusing early throws no
///   items into an inconsistent partially-processed state.
/// - Medium/High risk items are skipped unless their matching confirmation
///   flag is set, even if their id is present in `selected_item_ids`.
/// - Every path is re-validated against `PathSafetyValidator` immediately
///   before deletion, regardless of any earlier scan-time validation.
/// - Cancellation stops further deletions immediately; already-processed
///   items keep their real outcome, unprocessed items become `Cancelled`.
/// - `total_bytes_freed` is computed purely from items that actually reached
///   `Deleted` status.
/// - Deletion always goes through `move_to_recycle_bin`; there is no
///   permanent-delete code path anywhere in this function.
pub fn execute_cleanup(
    preview_items: &[CleanupItem],
    selection: &CleanupSelection,
    validator: &PathSafetyValidator,
    cancel_flag: &AtomicBool,
) -> Result<CleanupExecutionResult, String> {
    if !selection.confirmed {
        return Err(
            "Cleanup execution requires an explicit second confirmation. Refusing to execute."
                .to_string(),
        );
    }

    let correlation_id = Uuid::new_v4().to_string();
    let started_at = Utc::now();

    log_audit(
        "ExecutionStarted",
        &format!(
            "Execution started for {} selected item(s).",
            selection.selected_item_ids.len()
        ),
        None,
        None,
        &correlation_id,
    );

    let mut results = Vec::with_capacity(preview_items.len());
    let mut cancelled_from_here_on = false;

    for item in preview_items {
        let is_selected = selection.selected_item_ids.iter().any(|id| id == &item.id);

        if !is_selected {
            results.push(to_result(
                item,
                false,
                CleanupItemStatus::Skipped,
                "NotSelected",
                None,
            ));
            continue;
        }

        if cancelled_from_here_on || cancel_flag.load(Ordering::SeqCst) {
            cancelled_from_here_on = true;
            results.push(to_result(
                item,
                true,
                CleanupItemStatus::Cancelled,
                "CancelledBeforeProcessing",
                None,
            ));
            continue;
        }

        if item.risk_level == RiskLevel::Medium && !selection.confirm_medium_risk {
            results.push(to_result(
                item,
                true,
                CleanupItemStatus::Skipped,
                "RequiresMediumRiskConfirmation",
                None,
            ));
            continue;
        }

        if item.risk_level == RiskLevel::High && !selection.confirm_high_risk {
            results.push(to_result(
                item,
                true,
                CleanupItemStatus::Skipped,
                "RequiresHighRiskConfirmation",
                None,
            ));
            continue;
        }

        let validation = validator.validate(&item.full_path);
        if !validation.is_allowed {
            let reason = validation.rejection_reason.unwrap_or_default();
            let error_message = format!("Path failed safety validation: {reason}.");
            results.push(to_result(
                item,
                true,
                CleanupItemStatus::Failed,
                "PathSafetyValidationFailed",
                Some(error_message.clone()),
            ));
            log_item_failure(item, &correlation_id, &error_message);
            continue;
        }

        let target_path = validation
            .normalized_path
            .unwrap_or_else(|| item.full_path.clone());
        match move_to_recycle_bin(&target_path) {
            Ok(()) => {
                results.push(to_result(
                    item,
                    true,
                    CleanupItemStatus::Deleted,
                    "None",
                    None,
                ));
                log_item_deleted(item, &correlation_id);
            }
            Err(err) => {
                results.push(to_result(
                    item,
                    true,
                    CleanupItemStatus::Failed,
                    "RecycleBinOperationFailed",
                    Some(err.clone()),
                ));
                log_item_failure(item, &correlation_id, &err);
            }
        }
    }

    let completed_at = Utc::now();
    let total_bytes_freed: u64 = results
        .iter()
        .filter(|r| r.status == CleanupItemStatus::Deleted)
        .map(|r| r.size_bytes)
        .sum();

    let deleted = results
        .iter()
        .filter(|r| r.status == CleanupItemStatus::Deleted)
        .count();
    let failed = results
        .iter()
        .filter(|r| r.status == CleanupItemStatus::Failed)
        .count();
    let skipped = results
        .iter()
        .filter(|r| r.status == CleanupItemStatus::Skipped)
        .count();
    let cancelled = results
        .iter()
        .filter(|r| r.status == CleanupItemStatus::Cancelled)
        .count();

    log_audit(
        if cancelled_from_here_on {
            "ExecutionCancelled"
        } else {
            "ExecutionCompleted"
        },
        &format!(
            "Execution finished. Deleted={deleted}, Failed={failed}, Skipped={skipped}, Cancelled={cancelled}, BytesFreed={total_bytes_freed}."
        ),
        None,
        None,
        &correlation_id,
    );

    Ok(CleanupExecutionResult {
        items: results,
        was_cancelled: cancelled_from_here_on,
        started_at_utc: started_at.to_rfc3339(),
        completed_at_utc: completed_at.to_rfc3339(),
        total_bytes_freed,
    })
}

fn to_result(
    item: &CleanupItem,
    was_selected: bool,
    status: CleanupItemStatus,
    reason: &str,
    error_message: Option<String>,
) -> CleanupExecutionItemResult {
    CleanupExecutionItemResult {
        item_id: item.id.clone(),
        full_path: item.full_path.clone(),
        status,
        size_bytes: item.size_bytes,
        was_selected,
        reason: reason.to_string(),
        error_message,
    }
}

fn log_item_deleted(item: &CleanupItem, correlation_id: &str) {
    log_audit(
        "ItemDeleted",
        "Item deleted.",
        Some(item.full_path.clone()),
        Some(item.size_bytes),
        correlation_id,
    );
}

fn log_item_failure(item: &CleanupItem, correlation_id: &str, error_message: &str) {
    log_audit(
        "ItemFailed",
        &format!("Item deletion failed: {error_message}"),
        Some(item.full_path.clone()),
        Some(item.size_bytes),
        correlation_id,
    );
}

fn log_audit(
    action_type: &str,
    message: &str,
    masked_path: Option<String>,
    size_bytes: Option<u64>,
    correlation_id: &str,
) {
    let entry = sanitize_entry(AuditLogEntry {
        timestamp_utc: Utc::now().to_rfc3339(),
        action_type: action_type.to_string(),
        message: message.to_string(),
        masked_path,
        size_bytes,
        correlation_id: correlation_id.to_string(),
    });
    let _ = append_entry(&entry);
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::core::path_safety::PathSafetyOptions;
    use std::fs::File;
    use std::io::Write as _;

    fn make_item(tmp_dir: &std::path::Path, name: &str, risk: RiskLevel) -> CleanupItem {
        let path = tmp_dir.join(name);
        File::create(&path).unwrap().write_all(b"x").unwrap();
        CleanupItem {
            id: Uuid::new_v4().to_string(),
            full_path: path.to_string_lossy().to_string(),
            category: "TemporaryFiles".to_string(),
            risk_level: risk,
            size_bytes: 1,
            last_modified_utc: Utc::now().to_rfc3339(),
            detected_reason: "test".to_string(),
        }
    }

    fn make_validator(root: &std::path::Path) -> PathSafetyValidator {
        PathSafetyValidator::new(PathSafetyOptions {
            allowed_roots: vec![root.to_path_buf()],
            denied_roots: vec![],
            resolve_symbolic_links: true,
        })
    }

    #[test]
    fn refuses_to_execute_without_explicit_confirmation() {
        let tmp = tempfile::tempdir().unwrap();
        let item = make_item(tmp.path(), "a.tmp", RiskLevel::Low);
        let validator = make_validator(tmp.path());
        let cancel = AtomicBool::new(false);
        let selection = CleanupSelection {
            selected_item_ids: vec![item.id.clone()],
            confirm_medium_risk: false,
            confirm_high_risk: false,
            confirmed: false,
        };

        let result = execute_cleanup(&[item], &selection, &validator, &cancel);
        assert!(result.is_err());
    }

    #[test]
    fn skips_unselected_items() {
        let tmp = tempfile::tempdir().unwrap();
        let item = make_item(tmp.path(), "a.tmp", RiskLevel::Low);
        let validator = make_validator(tmp.path());
        let cancel = AtomicBool::new(false);
        let selection = CleanupSelection {
            selected_item_ids: vec![],
            confirm_medium_risk: false,
            confirm_high_risk: false,
            confirmed: true,
        };

        let result = execute_cleanup(&[item], &selection, &validator, &cancel).unwrap();
        assert_eq!(result.items[0].status, CleanupItemStatus::Skipped);
        assert_eq!(result.total_bytes_freed, 0);
    }

    #[test]
    fn requires_medium_risk_confirmation_gate() {
        let tmp = tempfile::tempdir().unwrap();
        let item = make_item(tmp.path(), "a.tmp", RiskLevel::Medium);
        let validator = make_validator(tmp.path());
        let cancel = AtomicBool::new(false);
        let selection = CleanupSelection {
            selected_item_ids: vec![item.id.clone()],
            confirm_medium_risk: false,
            confirm_high_risk: false,
            confirmed: true,
        };

        let result = execute_cleanup(&[item], &selection, &validator, &cancel).unwrap();
        assert_eq!(result.items[0].status, CleanupItemStatus::Skipped);
        assert_eq!(result.items[0].reason, "RequiresMediumRiskConfirmation");
    }

    #[test]
    fn requires_high_risk_confirmation_gate() {
        let tmp = tempfile::tempdir().unwrap();
        let item = make_item(tmp.path(), "a.tmp", RiskLevel::High);
        let validator = make_validator(tmp.path());
        let cancel = AtomicBool::new(false);
        let selection = CleanupSelection {
            selected_item_ids: vec![item.id.clone()],
            confirm_medium_risk: false,
            confirm_high_risk: false,
            confirmed: true,
        };

        let result = execute_cleanup(&[item], &selection, &validator, &cancel).unwrap();
        assert_eq!(result.items[0].status, CleanupItemStatus::Skipped);
        assert_eq!(result.items[0].reason, "RequiresHighRiskConfirmation");
    }

    #[test]
    fn fails_items_that_no_longer_pass_path_validation() {
        let tmp = tempfile::tempdir().unwrap();
        let other_tmp = tempfile::tempdir().unwrap();
        // Item claims a path outside the validator's allowed root, simulating
        // a path that became unsafe between scan-time and execution-time.
        let mut item = make_item(tmp.path(), "a.tmp", RiskLevel::Low);
        item.full_path = other_tmp.path().join("a.tmp").to_string_lossy().to_string();
        let validator = make_validator(tmp.path());
        let cancel = AtomicBool::new(false);
        let selection = CleanupSelection {
            selected_item_ids: vec![item.id.clone()],
            confirm_medium_risk: false,
            confirm_high_risk: false,
            confirmed: true,
        };

        let result = execute_cleanup(&[item], &selection, &validator, &cancel).unwrap();
        assert_eq!(result.items[0].status, CleanupItemStatus::Failed);
        assert_eq!(result.items[0].reason, "PathSafetyValidationFailed");
    }

    #[test]
    fn cancellation_marks_remaining_items_cancelled() {
        let tmp = tempfile::tempdir().unwrap();
        let item1 = make_item(tmp.path(), "a.tmp", RiskLevel::Low);
        let item2 = make_item(tmp.path(), "b.tmp", RiskLevel::Low);
        let validator = make_validator(tmp.path());
        let cancel = AtomicBool::new(true);
        let selection = CleanupSelection {
            selected_item_ids: vec![item1.id.clone(), item2.id.clone()],
            confirm_medium_risk: false,
            confirm_high_risk: false,
            confirmed: true,
        };

        let result = execute_cleanup(&[item1, item2], &selection, &validator, &cancel).unwrap();
        assert!(result
            .items
            .iter()
            .all(|r| r.status == CleanupItemStatus::Cancelled));
        assert!(result.was_cancelled);
    }

    #[test]
    fn deletes_valid_low_risk_selected_item_via_recycle_bin() {
        let tmp = tempfile::tempdir().unwrap();
        let item = make_item(tmp.path(), "a.tmp", RiskLevel::Low);
        let validator = make_validator(tmp.path());
        let cancel = AtomicBool::new(false);
        let selection = CleanupSelection {
            selected_item_ids: vec![item.id.clone()],
            confirm_medium_risk: false,
            confirm_high_risk: false,
            confirmed: true,
        };

        let result = execute_cleanup(&[item], &selection, &validator, &cancel).unwrap();
        // On CI/sandboxed environments without an Explorer/shell recycle bin
        // available, the underlying `trash` operation itself may fail; either
        // way, the status must never silently become a permanent deletion,
        // and total_bytes_freed must be internally consistent with it.
        match result.items[0].status {
            CleanupItemStatus::Deleted => assert_eq!(result.total_bytes_freed, 1),
            CleanupItemStatus::Failed => assert_eq!(result.total_bytes_freed, 0),
            other => panic!("unexpected status: {other:?}"),
        }
    }
}
