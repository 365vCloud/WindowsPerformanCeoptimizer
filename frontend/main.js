"use strict";

const { invoke } = window.__TAURI__.core;

const state = {
  items: [],
  selected: new Set(),
  lastExecutionResult: null,
};

const el = (id) => document.getElementById(id);

function formatBytes(bytes) {
  if (bytes == null || !Number.isFinite(Number(bytes))) return "不可用";
  if (bytes < 1024) return `${bytes} B`;
  const units = ["KB", "MB", "GB", "TB"];
  let value = bytes / 1024;
  let unitIndex = 0;
  while (value >= 1024 && unitIndex < units.length - 1) {
    value /= 1024;
    unitIndex += 1;
  }
  return `${value.toFixed(1)} ${units[unitIndex]}`;
}

function riskLabel(risk) {
  if (risk === "low") return { text: "低", cls: "risk-low" };
  if (risk === "medium") return { text: "中", cls: "risk-medium" };
  return { text: "高", cls: "risk-high" };
}

function categoryLabel(category) {
  const labels = {
    userTemporaryFiles: "用户临时文件",
    systemTemporaryFiles: "系统临时文件",
    windowsUpdateDownloads: "Windows 更新残留",
    deliveryOptimizationCache: "传递优化缓存",
    systemErrorReports: "系统错误报告",
    userErrorReports: "用户错误报告",
    crashDumps: "崩溃转储",
    shaderCache: "着色器缓存",
  };
  return labels[category] || category || "其他";
}

function renderResults() {
  const body = el("results-body");
  body.innerHTML = "";
  for (const item of state.items) {
    const tr = document.createElement("tr");
    const risk = riskLabel(item.riskLevel);
    const disabled = risk.text === "高";

    const checkboxCell = document.createElement("td");
    const checkbox = document.createElement("input");
    checkbox.type = "checkbox";
    checkbox.checked = state.selected.has(item.id);
    checkbox.disabled = disabled;
    checkbox.addEventListener("change", () => {
      if (checkbox.checked) {
        state.selected.add(item.id);
      } else {
        state.selected.delete(item.id);
      }
      updateSelectionSummary();
    });
    checkboxCell.appendChild(checkbox);

    tr.appendChild(checkboxCell);
    tr.appendChild(td(categoryLabel(item.category)));
    tr.appendChild(td(item.fullPath));
    tr.appendChild(td(formatBytes(item.sizeBytes)));
    const riskCell = td(risk.text);
    riskCell.className = risk.cls;
    tr.appendChild(riskCell);
    tr.appendChild(td(new Date(item.lastModifiedUtc).toLocaleString()));
    tr.appendChild(td(item.detectedReason));
    body.appendChild(tr);
  }

  const hasItems = state.items.length > 0;
  el("btn-select-safe").disabled = !hasItems;
  el("btn-clear-selection").disabled = !hasItems;
  updateSelectionSummary();
}

function td(text) {
  const cell = document.createElement("td");
  cell.textContent = text;
  return cell;
}

function updateSelectionSummary() {
  const selectedItems = state.items.filter((i) => state.selected.has(i.id));
  const totalBytes = selectedItems.reduce((sum, i) => sum + i.sizeBytes, 0);
  el("selection-summary").textContent =
    `已选 ${selectedItems.length} 项，共 ${formatBytes(totalBytes)}`;
  el("btn-prepare-cleanup").disabled = selectedItems.length === 0;
}

async function scan() {
  const minAgeHours = Number(el("min-age-hours").value) || 0;
  el("btn-scan").disabled = true;
  el("btn-cancel-scan").disabled = false;
  el("scan-status").textContent = "扫描中…";
  try {
    const items = await invoke("scan_temp_files", {
      options: { minimumAgeHours: minAgeHours, maximumCandidateCount: 1000 },
    });
    state.items = items;
    state.selected.clear();
    renderResults();
    const categorySummary = Object.entries(
      items.reduce((acc, item) => {
        const label = categoryLabel(item.category);
        acc[label] = (acc[label] || 0) + 1;
        return acc;
      }, {})
    )
      .map(([category, count]) => `${category} ${count}`)
      .join("，");
    el("scan-status").textContent =
      `扫描完成，共 ${items.length} 项候选。${categorySummary ? `（${categorySummary}）` : ""}`;
  } catch (err) {
    el("scan-status").textContent = `扫描失败或已取消：${err}`;
  } finally {
    el("btn-scan").disabled = false;
    el("btn-cancel-scan").disabled = true;
  }

  async function refreshMetrics() {
    try {
      const m = await invoke("get_system_metrics");
      el("metric-cpu").textContent = m.cpuPercent == null ? "不可用" : `${m.cpuPercent.toFixed(1)}%`;
      el("metric-memory").textContent = m.memoryPercent == null ? "不可用" : `${m.memoryPercent.toFixed(1)}% (${formatBytes(m.memoryUsedBytes)}/${formatBytes(m.memoryTotalBytes)})`;
      const disk = (m.disks || []).find(d => d.usedPercent != null) || (m.disks || [])[0];
      el("metric-disk").textContent = disk?.usedPercent == null ? "不可用" : `${disk.usedPercent.toFixed(1)}%`;
      el("metrics-status").textContent = m.unavailableReason || `采集时间：${m.collectedAtUtc}`;
    } catch (err) { el("metrics-status").textContent = `指标不可用：${err}`; }
  }

  async function scanProcesses() {
    const body = el("process-body"); body.innerHTML = "";
    try {
      for (const p of await invoke("scan_processes", { limit: 20 })) {
        const tr = document.createElement("tr");
        tr.append(td(p.name)); tr.append(td(String(p.pid)));
        tr.append(td(p.cpuPercent == null ? "不可用" : `${p.cpuPercent.toFixed(1)}%`));
        tr.append(td(formatBytes(p.memoryBytes))); body.append(tr);
      }
    } catch (err) { body.append(td(`读取失败：${err}`)); }
  }

  async function scanStartup() {
    const body = el("startup-body"); body.innerHTML = "";
    try {
      for (const p of await invoke("scan_startup_items")) {
        const tr = document.createElement("tr"); tr.append(td(p.name)); tr.append(td(p.command || "不可用"));
        tr.append(td(p.source)); tr.append(td(p.enabled == null ? p.unavailableReason || "不可用" : "已发现")); body.append(tr);
      }
    } catch (err) { body.append(td(`读取失败：${err}`)); }
  }
}

async function cancelScan() {
  await invoke("cancel_scan");
}

function selectSafeItems() {
  state.selected.clear();
  for (const item of state.items) {
    if (item.riskLevel === "low") {
      state.selected.add(item.id);
    }
  }
  renderResults();
}

function clearSelection() {
  state.selected.clear();
  renderResults();
}

function showOverlay(id) {
  el(id).classList.remove("hidden");
}
function hideOverlay(id) {
  el(id).classList.add("hidden");
}

let pendingSelectionIds = [];

async function prepareCleanup() {
  const selectedItems = state.items.filter((i) => state.selected.has(i.id));
  if (selectedItems.length === 0) return;

  // Re-validate every selected path immediately before showing the
  // confirmation dialog; drop anything that no longer validates.
  const paths = selectedItems.map((i) => i.fullPath);
  const validations = await invoke("revalidate_paths", { paths });
  const stillValid = new Set(
    validations.filter((v) => v.isAllowed).map((v) => v.fullPath)
  );

  const validItems = selectedItems.filter((i, idx) => {
    const v = validations[idx];
    return v && v.isAllowed;
  });

  if (validItems.length === 0) {
    el("scan-status").textContent = "所选项目在确认前已失效，请重新扫描。";
    return;
  }

  pendingSelectionIds = validItems.map((i) => i.id);
  const totalBytes = validItems.reduce((sum, i) => sum + i.sizeBytes, 0);
  el("confirm-count").textContent = String(validItems.length);
  el("confirm-size").textContent = formatBytes(totalBytes);

  const hasMedium = validItems.some((i) => i.riskLevel === "medium");
  el("medium-risk-ack-row").classList.toggle("hidden", !hasMedium);
  el("confirm-medium-risk").checked = false;

  showOverlay("confirm-overlay");
}

function closeConfirmDialog() {
  hideOverlay("confirm-overlay");
}

function onConfirmYesClicked() {
  const hasMedium = !el("medium-risk-ack-row").classList.contains("hidden");
  if (hasMedium && !el("confirm-medium-risk").checked) {
    return; // must explicitly acknowledge medium-risk items first
  }
  hideOverlay("confirm-overlay");
  showOverlay("second-confirm-overlay");
}

async function onSecondConfirmYes() {
  hideOverlay("second-confirm-overlay");
  showOverlay("progress-overlay");
  el("progress-status").textContent = "正在移动到回收站…";

  try {
    const confirmMediumRisk = el("confirm-medium-risk").checked;
    const result = await invoke("execute_cleanup", {
      selection: {
        selectedItemIds: pendingSelectionIds,
        confirmMediumRisk,
        confirmHighRisk: false,
        confirmed: true,
      },
    });
    state.lastExecutionResult = result;
    hideOverlay("progress-overlay");
    renderExecutionResult(result);
    showOverlay("result-overlay");
    // Refresh scan state: executed items are no longer valid candidates.
    await scan();
  } catch (err) {
    hideOverlay("progress-overlay");
    el("scan-status").textContent = `清理执行失败：${err}`;
  }
}

function onSecondConfirmNo() {
  hideOverlay("second-confirm-overlay");
}

async function onProgressCancel() {
  await invoke("cancel_execution");
}

function renderExecutionResult(result) {
  const deleted = result.items.filter((i) => i.status === "deleted").length;
  const failed = result.items.filter((i) => i.status === "failed").length;
  const skipped = result.items.filter((i) => i.status === "skipped").length;
  const cancelled = result.items.filter((i) => i.status === "cancelled").length;

  el("result-summary").textContent =
    `已删除 ${deleted}，失败 ${failed}，已跳过 ${skipped}，已取消 ${cancelled}，` +
    `实际释放 ${formatBytes(result.totalBytesFreed)}${result.wasCancelled ? "（已取消）" : ""}`;

  const body = el("result-body");
  body.innerHTML = "";
  const statusText = { deleted: "已清理", skipped: "已跳过", failed: "失败", cancelled: "已取消" };
  for (const item of result.items) {
    const tr = document.createElement("tr");
    tr.appendChild(td(item.fullPath));
    tr.appendChild(td(statusText[item.status] || item.status));
    tr.appendChild(td(item.errorMessage || item.reason));
    tr.appendChild(td(formatBytes(item.sizeBytes)));
    body.appendChild(tr);
  }
}

async function exportReport(format) {
  if (!state.lastExecutionResult) return;
  try {
    const path = await invoke("export_report", {
      result: state.lastExecutionResult,
      format,
    });
    el("result-summary").textContent += ` | 已导出：${path}`;
  } catch (err) {
    el("result-summary").textContent += ` | 导出失败：${err}`;
  }
}

async function refreshAuditLog() {
  const entries = await invoke("get_audit_log");
  const body = el("audit-body");
  body.innerHTML = "";
  for (const entry of entries.slice(-200).reverse()) {
    const tr = document.createElement("tr");
    tr.appendChild(td(entry.timestampUtc));
    tr.appendChild(td(entry.actionType));
    tr.appendChild(td(entry.message));
    tr.appendChild(td(entry.maskedPath || ""));
    body.appendChild(tr);
  }
}

async function clearAuditLog() {
  const confirmed = window.confirm(
    "确定要清除本地审计日志吗？此操作不可撤销。"
  );
  if (!confirmed) return;
  try {
    await invoke("clear_audit_log", { confirmed: true });
    await refreshAuditLog();
  } catch (err) {
    alert(`清除失败：${err}`);
  }
}

function wireEvents() {
  document.querySelectorAll(".tab").forEach(tab => tab.addEventListener("click", () => {
    document.querySelectorAll(".tab").forEach(t => t.classList.toggle("active", t === tab));
    document.querySelectorAll(".tab-panel").forEach(p => p.classList.toggle("hidden", p.id !== tab.dataset.panel));
  }));
  el("btn-refresh-metrics").addEventListener("click", refreshMetrics);
  el("btn-scan-processes").addEventListener("click", scanProcesses);
  el("btn-scan-startup").addEventListener("click", scanStartup);
  el("btn-scan").addEventListener("click", scan);
  el("btn-cancel-scan").addEventListener("click", cancelScan);
  el("btn-select-safe").addEventListener("click", selectSafeItems);
  el("btn-clear-selection").addEventListener("click", clearSelection);
  el("btn-prepare-cleanup").addEventListener("click", prepareCleanup);

  el("btn-confirm-cancel").addEventListener("click", closeConfirmDialog);
  el("btn-confirm-yes").addEventListener("click", onConfirmYesClicked);

  el("btn-second-no").addEventListener("click", onSecondConfirmNo);
  el("btn-second-yes").addEventListener("click", onSecondConfirmYes);

  el("btn-progress-cancel").addEventListener("click", onProgressCancel);

  el("btn-result-close").addEventListener("click", () => hideOverlay("result-overlay"));
  el("btn-export-csv").addEventListener("click", () => exportReport("csv"));
  el("btn-export-json").addEventListener("click", () => exportReport("json"));

  el("btn-refresh-audit").addEventListener("click", refreshAuditLog);
  el("btn-clear-audit").addEventListener("click", clearAuditLog);

  // Safety: Escape closes any open dialog without confirming anything; Enter
  // never triggers a destructive default action while a dialog is open.
  document.addEventListener("keydown", (e) => {
    const openOverlays = [
      "confirm-overlay",
      "second-confirm-overlay",
      "progress-overlay",
    ].filter((id) => !el(id).classList.contains("hidden"));

    if (openOverlays.length === 0) return;

    if (e.key === "Escape") {
      if (openOverlays.includes("confirm-overlay")) closeConfirmDialog();
      if (openOverlays.includes("second-confirm-overlay")) onSecondConfirmNo();
      e.preventDefault();
    }

    if (e.key === "Enter") {
      // Never let Enter submit a destructive dialog implicitly.
      e.preventDefault();
    }
  });

  // Overlay click-outside also only ever cancels, never confirms.
  el("confirm-overlay").addEventListener("click", (e) => {
    if (e.target.id === "confirm-overlay") closeConfirmDialog();
  });
  el("second-confirm-overlay").addEventListener("click", (e) => {
    if (e.target.id === "second-confirm-overlay") onSecondConfirmNo();
  });
}

wireEvents();
refreshAuditLog();
refreshMetrics();
