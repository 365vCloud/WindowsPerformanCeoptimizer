# Windows Performance Optimizer (v2 — Tauri)

一个包含系统仪表盘、卡顿诊断、启动项只读检查和垃圾文件清理的 Windows 性能工具。v2 版本已从 WPF 迁移到 **Rust + Tauri v2**：
前端是无构建依赖的静态 HTML/CSS/JS 仪表盘，后端是 Rust（Tauri commands）。所有删除操作默认且唯一走
Windows 回收站，绝不会自动执行永久删除。

## v2 架构

```
.
├─ src-tauri/            Tauri v2 后端（Rust）
│  ├─ Cargo.toml         crate 依赖（tauri、serde、chrono、uuid、regex、once_cell、trash、windows-sys）
│  ├─ tauri.conf.json    应用/窗口/CSP/打包配置（版本 2.0.0）
│  ├─ capabilities/      仅暴露自定义 command，不授予 shell/fs/process 插件权限
│  ├─ icons/             应用图标（占位图，见下方“已知限制”）
│  └─ src/
│     ├─ main.rs / lib.rs   Tauri Builder、panic hook、启动错误处理
│     ├─ crash.rs           崩溃日志写入（LocalAppData）+ Win32 MessageBox 中文错误提示
│     ├─ commands.rs        暴露给前端的 Tauri command（指标/进程/启动项/扫描/取消/复核/执行/审计/导出）
│     └─ core/
│        ├─ path_safety.rs  路径安全验证器（白名单/黑名单/遍历/驱动器根/重解析点解析）
│        ├─ scanner.rs      当前用户 Temp 垃圾文件只读扫描器（年龄过滤、数量上限、取消、单项隔离）
│        ├─ cleanup.rs      清理执行引擎（二次确认门控、风险确认门控、二次路径校验、取消）
│        ├─ recycle_bin.rs  基于 `trash` crate 的回收站移动（Windows Shell），无永久删除入口
│        ├─ audit.rs        本地审计日志（LocalAppData，JSON Lines，路径/密钥脱敏）
│        ├─ export.rs       清理结果导出（UTF-8 CSV/JSON，含 CSV 公式注入防护）
│        ├─ diagnostics.rs  CPU/内存/磁盘、进程和启动项只读采集（字段不可用时为 null）
│        └─ models.rs       跨前后端的强类型数据模型
├─ frontend/             静态前端（无打包/构建步骤，直接由 Tauri 加载）
│  ├─ index.html          中文标签页：仪表盘、卡顿诊断、垃圾文件、启动项、操作记录/结果
│  ├─ style.css
│  └─ main.js             调用 Tauri command；关闭/Esc/Enter 均不触发删除
├─ package.json           `@tauri-apps/cli` + `@tauri-apps/api`（仅用于跑 Tauri CLI，无前端打包器）
├─ legacy/WPO.App/        **已废弃** 的 WPF 壳（不再打包进 MSI，见 `legacy/WPO.App/LEGACY.md`）
├─ src/WPO.Core, src/WPO.Domain   原 .NET 安全逻辑与其单元测试，保留作参考/兼容旧测试，与 Tauri 应用无运行时依赖
├─ tests/WPO.Core.Tests/  仍然通过的原 .NET 单元测试（55 项）
└─ installer/WPO.Installer/   WiX v5 MSI 项目：打包 **Tauri 发布的 exe**（不再打包 WPO.App.exe）
```

## 关键安全设计（Rust 后端）

- **路径安全验证器**（`core::path_safety::PathSafetyValidator`）：拒绝空路径、`..` 路径遍历段、驱动器根路径
  （如 `C:\`）、系统关键目录（Windows、System32、Program Files、用户主目录）；白名单按路径**分段**匹配，
  防止“相似前缀”绕过（`C:\Temp\Cache` 不会误放行 `C:\Temp\CacheOld`）；默认解析符号链接/联结点的最终目标并
  重新校验，防止符号链接逃逸。
- **垃圾文件扫描器**（`core::scanner::scan_temp_files`）：只在当前用户 `%TEMP%` 内递归枚举文件**元数据**
  （不读取文件内容）；默认只包含最后修改时间超过 24 小时的文件；默认最多返回 1000 项（硬上限 5000）；
  从不遍历重解析点（符号链接/联结点），单项访问异常会被跳过而不中断整体扫描；支持通过 `cancel_scan`
  command 随时取消。
- **清理执行引擎**（`core::cleanup::execute_cleanup`）：
  - 必须显式携带 `confirmed: true`（对应前端“最终确认”弹窗的“是”）才会执行任何操作，否则直接返回错误、
    不触碰任何文件。
  - 中/高风险项即使在选中列表中，也必须携带对应的确认标志才会被处理，否则标记为“已跳过”。
  - 每一项在真正删除前都会**重新调用** `PathSafetyValidator`，不依赖扫描时的历史校验结果。
  - 支持取消：一旦检测到取消请求，后续未处理项立即标记为“已取消”，已处理项保留真实结果。
  - 唯一的删除方式是移动到回收站（`core::recycle_bin::move_to_recycle_bin`，基于 `trash` crate 调用
    Windows Shell）；**代码中不存在任何永久删除路径**。
  - 释放空间统计只累加真正达到“已删除”状态的项。
- **审计日志**（`core::audit`）：UTF-8 JSON Lines 写入当前用户
  `%LocalAppData%\WindowsPerformanceOptimizer\audit.jsonl`；写入前剥离 CR/LF（防日志注入）、脱敏形如
  `password=`/`token=`/`secret=`/`api-key=` 的疑似密钥、脱敏 Windows 风格路径、并将当前用户名/用户目录替换
  为 `<user>`。清除日志需要显式 `confirmed: true`。
- **导出**（`core::export::export_report`）：UTF-8（无 BOM）CSV/JSON 写入
  `%LocalAppData%\WindowsPerformanceOptimizer\reports\`；CSV 导出包含公式注入防护（`=`/`+`/`-`/`@` 开头的
  单元格会被加前缀 `'`）。
- **启动失败可见化**（`main.rs` / `lib.rs` / `crash.rs`）：应用启动前安装 `std::panic::set_hook`；
  所有 panic 以及 `tauri::Builder::run(...)` 返回的错误都会写入
  `%LocalAppData%\WindowsPerformanceOptimizer\logs\crash-*.log`，仅记录错误类型、消息和源码位置，并通过
  Win32 `MessageBoxW` 弹出中文提示，不再出现“安装后双击没有任何反应”的静默退出。

## 前端交互与“不自动清理”约束

- 高风险项目的复选框始终禁用；中风险项目默认不勾选，且必须在确认弹窗中额外勾选“我已确认包含中风险清理项”
  才能继续。
- 点击“准备清理”前会对所有已选路径重新调用 `revalidate_paths` command；确认弹窗展示数量、预计释放空间、
  固定处理方式“移动到回收站（可还原，不会永久删除）”，没有永久删除选项。
- **关闭弹窗、点击遮罩、Esc、Enter 均只会取消/关闭对话框，绝不会触发清理**；确认按钮不是默认按钮，默认
  焦点停留在“取消”/“否”按钮上（见 `frontend/main.js` 的按键与遮罩点击处理）。
- 只有依次经过“准备清理”→ 确认弹窗“确认清理”→ 二次确认弹窗显式选择“是”，才会调用 `execute_cleanup`。
- 执行阶段显示独立的进度弹窗，可随时点击“取消”调用 `cancel_execution`；结束后展示逐项最终状态、原因、
  实际释放字节，并提供 CSV/JSON 导出与本地审计日志查看/清除入口。

## 构建

### 前置要求

- Rust stable（含 MSVC 工具链：`rustup`、Visual Studio Build Tools 的 “使用 C++ 的桌面开发” 工作负载，
  提供 `link.exe`）
- Node.js（仅用于运行 `@tauri-apps/cli`；前端本身无构建步骤）
- .NET 8 SDK（仅用于构建 WiX MSI 安装器、以及可选的 legacy WPF/测试项目）
- Windows（Tauri 打包、WiX、回收站集成均为 Windows 专用）
- Microsoft Edge WebView2 Runtime：Windows 11 与多数 Windows 10 机器通常已自带；若缺失，Tauri 自带安装器
  现使用 `embedBootstrapper`，而本仓库的自定义 WiX MSI 仍要求目标机预装 Runtime（失败时会看到中文错误弹窗
  和 crash log 路径）。

本仓库已提交 `src-tauri/Cargo.lock` 与 `package-lock.json`，可直接复现依赖版本。

### 构建与测试命令

```powershell
# 安装 Node 依赖（仅 @tauri-apps/cli + @tauri-apps/api）
npm install

# Rust 单元测试（路径遍历/相似前缀/年龄过滤/重解析点/风险确认门控/取消 等）
cd src-tauri
cargo test
cd ..

# 完整 Tauri 发布构建（生成 exe，以及 Tauri 自带的 MSI/NSIS 安装包，可选）
npm run build
# 等价于: npx tauri build
# 产物：
#   src-tauri\target\release\wpo-app.exe
#   src-tauri\target\release\bundle\msi\Windows Performance Optimizer_2.0.0_x64_en-US.msi
#   src-tauri\target\release\bundle\nsis\Windows Performance Optimizer_2.0.0_x64-setup.exe

# 本仓库的 WiX v5 MSI（默认重新构建 Tauri exe，再打包）
dotnet build installer\WPO.Installer\WPO.Installer.wixproj -c Release

# 开发静态检查；会明确显示 UNSIGNED DEVELOPMENT ARTIFACT — NOT FOR DISTRIBUTION
powershell -ExecutionPolicy Bypass -File installer\WPO.Installer\Validate-Msi.ps1

# 可选：旧 .NET 核心逻辑的单元测试（迁移前的参考实现，仍然全部通过）
dotnet test tests\WPO.Core.Tests\WPO.Core.Tests.csproj
```

### 开发构建说明

- `cargo build --release`：**构建成功**，生成更新后的 `src-tauri\target\release\wpo-app.exe`。
- `cargo test`：Rust 单元测试 **22 项全部通过**（原路径安全/扫描/清理/审计测试 + 启动诊断相关编译回归覆盖）。
- `npx tauri build` / `npm run build`：**构建成功**，生成 `wpo-app.exe`、Tauri 自带 MSI 与 NSIS 安装包；当前
  `tauri.conf.json` 使用 `embedBootstrapper`，安装包会携带 WebView2 引导程序而不是在安装时再在线下载。
- `dotnet build installer\WPO.Installer\WPO.Installer.wixproj -c Release`：**构建成功**，生成
  `installer\WPO.Installer\bin\Release\WPO.Installer.msi`（Version 2.0.0.0，UpgradeCode 与 v1 保持一致，
  同版本重建包也允许通过 `MajorUpgrade AllowSameVersionUpgrades="yes"` 替换旧安装）。
- `Validate-Msi.ps1`：开发静态验证会检查无 CustomAction、无 Registry 表、包含
  `WindowsPerformanceOptimizer.exe`、MajorUpgrade 元数据和目标目录；它不是签名证明，并会输出醒目的
  `UNSIGNED DEVELOPMENT ARTIFACT — NOT FOR DISTRIBUTION` 警告。
- **实际安装/启动/卸载验证通过**：
  1. 先静默安装修复前 MSI，再静默安装修复后、版本号仍为 `2.0.0.0` 的 MSI；
  2. 安装目录中的 `WindowsPerformanceOptimizer.exe` 哈希从
     `D39A1648E35B63B167E7931F12887536D88BA4B8E40D8C53DFA526B83A356FE4`
     变为
     `B47E9E8518F45E91F1AFC85765DD9F3850AEC41F60EC1D067FEA1636C98959F1`，
     证明同版本重建包确实替换了旧 exe，而不是保留 stale 文件；
  3. 启动安装后的 exe，PowerShell 读到 `MainWindowTitle = Windows Performance Optimizer`，确认窗口真正出现；
  4. 静默卸载后 `C:\Program Files\WindowsPerformanceOptimizer\WindowsPerformanceOptimizer.exe` 与安装目录均已清理。
- `dotnet test tests\WPO.Core.Tests\WPO.Core.Tests.csproj`：旧 .NET 核心测试 **55 项全部通过**（作为参考实现
  保留，未随本次迁移改动其逻辑）。
- `dotnet build legacy\WPO.App\WPO.App.csproj -c Release`：legacy WPF 项目本身仍可独立构建（仅证明未被破坏），
  但**不再是本仓库的构建/发布流程的一部分**，也不打包进 v2 MSI。

首次构建 Rust 部分前，本机没有 Rust/Node/MSVC 工具链，已通过 `winget` 安装：
`Rustlang.Rustup`（stable-x86_64-pc-windows-msvc）、`OpenJS.NodeJS.LTS`、
`Microsoft.VisualStudio.2022.BuildTools`（C++ 桌面开发工作负载，提供 `link.exe`）。若在新机器上构建，
需要先完成以上安装（或等效的 Visual Studio 安装）。

## MSI 安装器（v2）

- `installer\WPO.Installer\Product.wxs` 的 `Package` 版本号为 `2.0.0.0`；`UpgradeCode` 与 v1 保持不变
  （`6f2b6f1e-6f6b-4a1a-9c1b-8b1a2f2e9d10`），确保旧版本可以被 `MajorUpgrade` 正常升级/覆盖安装。
- `MajorUpgrade` 显式启用了 `AllowSameVersionUpgrades="yes"`：当支持/测试场景需要“同一版本号、重新打包的新 MSI”
  去替换旧安装时，Windows Installer 会先卸载旧 ProductCode 再安装新包，避免保留 stale exe。
- 安装到 `C:\Program Files\WindowsPerformanceOptimizer\WindowsPerformanceOptimizer.exe`（即 Tauri 构建产出的
  `wpo-app.exe`，安装时改名为更具描述性的文件名）。
- 保留开始菜单快捷方式（非广告快捷方式，指向已安装的 exe，不需要写注册表）。
- 沿用 v1 的安全约束：无 `CustomAction`、无 `Registry` 表写入、标准 `MajorUpgrade`/安装/卸载/修复流程。
- **不再**打包 `WPO.App.exe`（旧 WPF 主程序）；`WPO.Installer.wixproj` 不再引用/发布 `legacy\WPO.App`。
- Tauri 自带 MSI/NSIS 安装器的 `webviewInstallMode` 已改为 `embedBootstrapper`：
  - **优点**：目标机缺少 WebView2 时，不必依赖安装当下再联网下载；
  - **代价**：安装包体积会比 `downloadBootstrapper` 更大；
  - **已有 Runtime 的机器**：会直接复用现有 WebView2，不会重复安装。
- 本仓库自定义 WiX MSI 仍只打包应用本体，不内嵌 WebView2 安装逻辑；因此离线环境若使用该 MSI，建议先确认
  WebView2 Runtime 已存在。若缺失，应用现在会弹出中文错误框，并把诊断写入
  `%LocalAppData%\WindowsPerformanceOptimizer\logs\crash-*.log`。

### 开发包与正式签名发布

`dotnet build installer\WPO.Installer\WPO.Installer.wixproj -c Release` 默认以
`BuildTauriBeforeMsi=true` 执行本地锁定的 `node_modules\.bin\tauri.cmd build --no-bundle`，清理旧 Release 输出，并生成
`src-tauri\target\release\wpo-app.build-manifest.json`。它产出的 MSI/EXE 是**未签名开发产物，不可分发**。

正式发布只允许使用 `installer\WPO.Installer\Build-SignedRelease.ps1`。脚本先执行干净 Tauri Release
构建，签署并验证 EXE，再以其 SHA-256 固定值构建 WiX MSI，签署并严格验证两个文件的签名、证书身份和
RFC3161 时间戳。成功目录只在签名后计算并写入 `SHA256SUMS.txt` 与不含秘密的
`release-manifest.json`。不要将 PFX、私钥、密码或证书导入仓库。

```powershell
# 密码仅存在受保护的 CI secret 环境变量；PFX 路径和时间戳服务为示例占位值。
$env:WPO_CODESIGN_PFX_PASSWORD = '<CI secret>'
powershell -ExecutionPolicy Bypass -File installer\WPO.Installer\Build-SignedRelease.ps1 `
  -PfxPath 'C:\secure\organization-codesigning.pfx' `
  -PfxPasswordEnvironmentVariable 'WPO_CODESIGN_PFX_PASSWORD' `
  -TimestampUrl 'https://timestamp.example.org/rfc3161'

# 或使用已安装到证书存储的私钥证书（不传密码）。
powershell -ExecutionPolicy Bypass -File installer\WPO.Installer\Build-SignedRelease.ps1 `
  -CertificateThumbprint 'REPLACE_WITH_ORGANIZATION_THUMBPRINT' `
  -TimestampUrl 'https://timestamp.example.org/rfc3161'
```

脚本在正式模式下绝不会生成测试/自签名证书；没有组织代码签名证书时，正式命令必须失败。发布验收使用：

```powershell
powershell -ExecutionPolicy Bypass -File installer\WPO.Installer\Validate-Msi.ps1 `
  -RequireSignature -ExpectedSignerThumbprint 'REPLACE_WITH_ORGANIZATION_THUMBPRINT' `
  -ExpectedSignerSubject 'CN=Example Organization'
```

#### GitHub Actions 正式签名发布

`.github/workflows/signed-release.yml`（手动触发 `workflow_dispatch`）在 `windows-latest` 上执行完整的
`Build-SignedRelease.ps1` 正式流程。需要在仓库 Secrets 中配置：

| Secret | 内容 |
| --- | --- |
| `CODE_SIGNING_PFX_BASE64` | 组织代码签名证书（.pfx）的 Base64 编码 |
| `CODE_SIGNING_PFX_PASSWORD` | PFX 密码 |

缺少任一 Secret 时工作流直接失败，不会上传未签名产物。PFX 只写入 runner 临时目录、以不可导出方式临时导入，
签名结束后立即删除；成功产物（已签名 EXE/MSI、`SHA256SUMS.txt`、`release-manifest.json`）作为
`WindowsPerformanceOptimizer-signed-release` artifact 上传。

#### 本地签名流水线自检（非正式、不可分发）

在没有组织证书的开发机上，可用 `-LocalTestSigning` 端到端验证签名流水线本身（signtool、RFC3161 时间戳、
EXE 哈希绑定、签名校验）：

```powershell
powershell -ExecutionPolicy Bypass -File installer\WPO.Installer\Build-SignedRelease.ps1 -LocalTestSigning
```

该模式会临时生成主题为 `CN=WPO LOCAL TEST SIGNING - NOT FOR DISTRIBUTION`、有效期 3 天、**不可导出**的
自签名证书，签名完成后立刻从证书存储删除（私钥随之销毁）。输出固定写入
`installer\WPO.Installer\bin\TestSignedRelease\`（禁止写入 `SignedRelease`），附带
`TEST-SIGNED-NOT-FOR-DISTRIBUTION.txt`，`release-manifest.json` 中 `releaseType` 为
`test-signed-not-for-distribution`。Windows 不信任这些签名（`Get-AuthenticodeSignature` 状态为
`UnknownError`/不受信任的根），`Validate-Msi.ps1 -RequireSignature` 在没有 `-AllowUntrustedTestSigner`
时会明确拒绝该证书，`Test-ReleasePipeline.ps1` 对这一拒绝行为有回归测试。

### 重建 MSI 的建议流程

1. **正式发布优先递增版本号**（`tauri.conf.json` / `Product.wxs`），让标准 MajorUpgrade 路径处理升级。
2. 若是支持/热修复场景，必须保持版本号不变时，可直接重建 MSI；仓库内的 WiX 配置已允许同版本替换旧安装。
3. 开发构建使用 `dotnet build installer\WPO.Installer\WPO.Installer.wixproj -c Release`；正式发布必须运行
   `Build-SignedRelease.ps1`，不能手工分拆签名步骤。
4. 若用户反馈自定义 WiX MSI 安装后无法启动，优先检查：
   - 目标机是否安装 WebView2 Runtime；
   - `%LocalAppData%\WindowsPerformanceOptimizer\logs\crash-*.log` 中的错误类型/消息/位置；
   - 当前安装包是否真的是最新重建产物。

## 已知限制 / 后续工作

- **正式签名待接入组织证书**：签名流水线（本地脚本 + GitHub Actions 工作流）已可端到端运行，并已用
  `-LocalTestSigning` 自检模式验证 EXE/MSI 均能被 signtool 签名、加时间戳并通过校验；但仓库不包含证书、私钥
  或密码，**可分发的受信任签名**仍需在受控 CI/发布环境中配置组织代码签名证书后运行 `Build-SignedRelease.ps1`
  或 `signed-release` 工作流。`bin\TestSignedRelease` 中的产物不可分发。
- **应用图标为占位图**：`src-tauri/icons/` 下的图标由脚本临时生成（纯色背景 + "W" 字样），并非最终视觉设计，
  发布前应替换为正式图标资源。
- **仅覆盖当前用户 Temp 一个类别**：回收站已用空间、浏览器缓存、Windows 更新缓存、系统日志等清理类别仍未
  实现（与 v1 状态一致）。
- **前端未做浏览器兼容性测试**：`frontend/` 仅设计为在 Tauri 内置 WebView2 中运行，不追求独立浏览器兼容性。
- **WiX 依赖 Tauri 工具链**：WiX 项目默认构建新鲜的 Tauri Release 输出；仅在受控发布脚本的第二个 MSI 构建阶段
  会设为 `BuildTauriBeforeMsi=false`，并以已签名 EXE 的 SHA-256 强制校验，防止打包 stale exe。
- **legacy WPF 代码保留但不维护**：`legacy/WPO.App` 与 `src/WPO.Core`、`src/WPO.Domain`、
  `tests/WPO.Core.Tests` 仅作为原安全逻辑的参考实现与回归测试保留，未来变更应只发生在 `src-tauri/`；
  两套实现之间没有共享代码或运行时依赖。

## 明确排除的行为（按需求）

本应用及其构建流程中**不包含**：注册表写入、进程终止、PowerShell 清理脚本、提权绕过、默认永久删除、
遥测/使用数据上传。
