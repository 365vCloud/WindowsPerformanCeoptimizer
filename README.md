# Windows Performance Optimizer (v2 — Tauri)

一个包含系统仪表盘、卡顿诊断、启动项只读检查和垃圾文件清理的 Windows 性能工具。v2 版本已从 WPF 迁移到 **Rust + Tauri v2**：
前端是无构建依赖的静态 HTML/CSS/JS 仪表盘，后端是 Rust（Tauri commands）。所有删除操作默认且唯一走
Windows 回收站，绝不会自动执行永久删除。

## v2 架构

```
.
├─ src-tauri/            Tauri v2 后端（Rust）
│  ├─ Cargo.toml         crate 依赖（tauri、serde、chrono、uuid、regex、once_cell、trash）
│  ├─ tauri.conf.json    应用/窗口/CSP/打包配置（版本 2.0.0）
│  ├─ capabilities/      仅暴露自定义 command，不授予 shell/fs/process 插件权限
│  ├─ icons/             应用图标（占位图，见下方“已知限制”）
│  └─ src/
│     ├─ main.rs / lib.rs   Tauri Builder、command 注册
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

# 本仓库的 WiX v5 MSI（打包上面的 wpo-app.exe；必须先跑通 `npm run build`）
dotnet build installer\WPO.Installer\WPO.Installer.wixproj -c Release

# 不执行真实安装，静态检查 MSI 的文件/升级/安全表
powershell -ExecutionPolicy Bypass -File installer\WPO.Installer\Validate-Msi.ps1

# 可选：旧 .NET 核心逻辑的单元测试（迁移前的参考实现，仍然全部通过）
dotnet test tests\WPO.Core.Tests\WPO.Core.Tests.csproj
```

### 本次实际构建结果（在本机验证过）

- `cargo test`：Rust 单元测试 **20 项全部通过**（路径安全 6 项、扫描器 5 项、清理执行 6 项、审计脱敏 3 项）。
- `npx tauri build` / `npm run build`：**构建成功**，生成 `wpo-app.exe`、Tauri 自带 MSI 与 NSIS 安装包。
- `dotnet build installer\WPO.Installer\WPO.Installer.wixproj -c Release`：**构建成功**，生成
  `installer\WPO.Installer\bin\Release\WPO.Installer.msi`（Version 2.0.0.0，UpgradeCode 与 v1 保持一致）。
- `Validate-Msi.ps1`：**静态验证通过**（无 CustomAction、无 Registry 表、包含 `WindowsPerformanceOptimizer.exe`、
  含 MajorUpgrade 元数据、安装到 `WindowsPerformanceOptimizer` 目录）。
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
- 安装到 `C:\Program Files\WindowsPerformanceOptimizer\WindowsPerformanceOptimizer.exe`（即 Tauri 构建产出的
  `wpo-app.exe`，安装时改名为更具描述性的文件名）。
- 保留开始菜单快捷方式（非广告快捷方式，指向已安装的 exe，不需要写注册表）。
- 沿用 v1 的安全约束：无 `CustomAction`、无 `Registry` 表写入、标准 `MajorUpgrade`/安装/卸载/修复流程。
- **不再**打包 `WPO.App.exe`（旧 WPF 主程序）；`WPO.Installer.wixproj` 不再引用/发布 `legacy\WPO.App`。
- 目标机器需要已安装 WebView2 Runtime（Windows 10 2004+ / Windows 11 通常已预装；`tauri.conf.json` 中
  `webviewInstallMode` 设为 `downloadBootstrapper`，Tauri 自带的 NSIS/MSI 安装器可自动引导安装，但本仓库
  自带的 WiX MSI 不包含该引导逻辑，仅打包应用本体）。

## 已知限制 / 后续工作

- **签名未完成**：当前生成的 `wpo-app.exe`、Tauri MSI/NSIS、以及本仓库 WiX MSI 均**未签名**。发布流程应在
  受控 CI/发布环境中使用组织持有的代码签名证书对最终产物签名并加时间戳；证书、私钥、指纹和签名命令不得
  写入本仓库。签名后需重新运行 `Validate-Msi.ps1`。
- **应用图标为占位图**：`src-tauri/icons/` 下的图标由脚本临时生成（纯色背景 + "W" 字样），并非最终视觉设计，
  发布前应替换为正式图标资源。
- **仅覆盖当前用户 Temp 一个类别**：回收站已用空间、浏览器缓存、Windows 更新缓存、系统日志等清理类别仍未
  实现（与 v1 状态一致）。
- **前端未做浏览器兼容性测试**：`frontend/` 仅设计为在 Tauri 内置 WebView2 中运行，不追求独立浏览器兼容性。
- **本仓库的 WiX MSI 依赖手动预构建**：`WPO.Installer.wixproj` 只校验 `src-tauri/target/release/wpo-app.exe`
  是否存在，不会自动调用 `cargo`/`npm`（避免把工具链耦合进 MSBuild）；必须先手动运行 `npm run build`。
- **legacy WPF 代码保留但不维护**：`legacy/WPO.App` 与 `src/WPO.Core`、`src/WPO.Domain`、
  `tests/WPO.Core.Tests` 仅作为原安全逻辑的参考实现与回归测试保留，未来变更应只发生在 `src-tauri/`；
  两套实现之间没有共享代码或运行时依赖。

## 明确排除的行为（按需求）

本应用及其构建流程中**不包含**：注册表写入、进程终止、PowerShell 清理脚本、提权绕过、默认永久删除、
遥测/使用数据上传。
