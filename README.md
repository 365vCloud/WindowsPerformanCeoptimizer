# Windows Performance Optimizer

一个专注于**安全默认**的 Windows 磁盘/性能清理工具的第一版基础工程。本仓库当前提供的是可构建、可测试的核心骨架，
不包含任何危险的系统级修改逻辑；所有删除操作默认走回收站，绝不会自动执行永久删除。

## 解决方案结构

```
WindowsPerformanceOptimizer.sln
├─ src/
│  ├─ WPO.Domain/        领域模型与枚举（含可用性明确的系统指标、进程诊断、启动项模型），net8.0 类库
│  ├─ WPO.Core/           核心服务：只读性能诊断、只读启动项检查、路径安全验证器、清理预览/执行服务、审计与 DI，net8.0 类库
│  └─ WPO.App/            WPF 仪表盘：只读系统/进程诊断、当前用户 Temp 安全预览＋清理审阅/二次确认 UI、只读启动项检查
├─ tests/
│  └─ WPO.Core.Tests/     xUnit 单元测试（net8.0），覆盖安全校验、清理执行、审计、性能扫描与启动项检查
└─ installer/
   └─ WPO.Installer/      WiX v5 安装器骨架（.wixproj + Product.wxs），仅打包 WPO.App 可执行文件
```

## 关键安全设计

- **路径安全验证器**（`WPO.Core.Security.PathSafetyValidator`）：
  - 白名单校验：候选路径必须位于配置的 `AllowedRoots` 之下（按路径分段比较，而非朴素字符串前缀），
    可防止“相似前缀”绕过（如允许根 `C:\Temp\Cache` 不会误放行同级目录 `C:\Temp\CacheOld`）。
  - 拒绝路径遍历（`..` 分段）。
  - 内置系统关键目录黑名单（Windows、System32、Program Files、用户主目录根），即使被误配置进白名单也会优先拒绝。
  - 拒绝对驱动器根目录（如 `C:\`）本身的操作。
  - 默认解析并复核符号链接/联结点的最终目标，防止符号链接逃逸白名单。
- **清理执行服务**（`WPO.Core.Cleanup.CleanupExecutionService`）：
  - 默认且唯一的自动删除方式是移动到回收站（`IRecycleBinService.MoveToRecycleBinAsync`）。
  - 永久删除（`DeletionMode.PermanentWithConfirmation`）必须同时满足显式请求该模式 **且** `ConfirmPermanentDeletion = true`，
    否则在执行前直接抛出异常，绝不会自动永久删除任何文件。
  - 中/高风险项（`RiskLevel.Medium` / `RiskLevel.High`）即使被选中，也必须携带对应的 `ConfirmMediumRisk` /
    `ConfirmHighRisk` 二次确认标志才会被处理，否则会被标记为 `Skipped`。
  - 支持取消：一旦检测到取消请求，立即停止后续删除，未处理项标记为 `Cancelled`。
  - 实际释放空间统计（`CleanupExecutionResult.TotalBytesFreed`）只累加真正 `Deleted` 状态的项，不包含失败/跳过/取消的项。
- **审计日志**（`WPO.Core.Audit`）：
  - `IAuditLogService` 的默认实现将 UTF-8 JSON Lines 日志写入当前用户 `%LocalAppData%\WindowsPerformanceOptimizer\audit.jsonl`；
    不写入文件内容、密码或令牌，路径仅保存脱敏后的最小标识（用户目录/用户名替换为 `<user>`）。
  - 写入前会移除 CR/LF，避免日志换行注入或 JSON Lines 结构破坏；“清除日志”服务调用要求显式确认。
  - `IAuditLogService`、`IExportService` 均可替换；WPF 仅通过这些接口读写日志和导出结果，不直接访问文件系统。
- **清理结果报告与导出**（`CleanupExecutionResult` / `WPO.Core.Export`）：
  - 报告从逐项最终状态派生扫描、选择、尝试、成功、跳过、失败、取消计数，分别提供预计字节（所有已选项）与实际释放字节
    （仅 `Deleted` 项），并以强类型原因标识未成功项目。
  - 执行结束后会打开独立结果窗口，显示最终状态和逐项原因；可导出 UTF-8（无 BOM）CSV/JSON、复制摘要、查看/确认清除
    本地审计日志。导出失败会显示原因，不会关闭或损坏当前结果。
- **当前用户临时文件预览**（`WPO.Core.Cleanup.SafeTemporaryFileScanner`）：
  - 只在 `Path.GetTempPath()` 对应的当前用户 Temp 根内递归枚举文件元数据，不读取文件正文。
  - 默认只包含最后修改时间超过 24 小时的文件，最大返回 1,000 项；每一项均调用 `IPathSafetyValidator`，并标记为
    `TemporaryFiles`、低风险及“当前用户 Temp 目录中的过期临时文件（仅预览）”。
  - 枚举器与候选项都会拒绝重解析点（符号链接、联结点等），不会沿此类路径递归；遇到单项访问、IO 或验证异常会跳过并继续。
- **清理审阅/确认 UI**（`WPO.App`：`MainWindow` + `ConfirmCleanupWindow` + `CleanupExecutionWindow`）：
  - 扫描结果以复选框表格展示：路径、大小、风险等级、验证状态（在展示前对每一项重新调用 `IPathSafetyValidator`，
    不只依赖扫描/预览阶段的历史结果）。
  - 高风险项目与验证失败的项目复选框始终禁用，无法被勾选；中风险项目默认不勾选，且即使被勾选，也必须先勾选窗口内
    “我已确认包含中风险清理项”后，“准备清理”才允许继续。
  - 提供“全选安全项目”（仅勾选当前有效的低风险项）与“清空选择”，并实时汇总已选数量与已选字节总数。
  - 点击“准备清理”只会打开独立的模态确认窗口（`ConfirmCleanupWindow`），展示数量、预计释放空间、固定的处理方式
    “移动到回收站（可还原，不会永久删除）”，以及风险说明；该窗口没有永久删除选项。取消、关闭窗口（含标题栏 ×）、
    Esc 均只会把对话框结果置为“未确认”；Enter 键在该窗口内被显式吞掉，永远不会触发确认，确认按钮也不设为默认按钮、
    默认不获得焦点（焦点默认停留在“取消”按钮上）。
  - 只有用户显式点击“确认清理”，并在随后弹出的二次确认对话框中显式选择“是”（默认值为“否”）后，才会调用既有的
    `ICleanupExecutionService.ExecuteAsync`；点击“准备清理”前后都会针对已选路径重新执行一次 `IPathSafetyValidator`
    校验，任何在此期间失效的项都会被自动取消勾选并阻止继续。
  - 执行阶段在独立的模态进度窗口（`CleanupExecutionWindow`）中异步运行，支持通过“取消”按钮或关闭窗口触发既有的
    `CancellationToken` 取消；单个项目失败不会中断其余项目的处理。执行结束或被取消后会展示每项的最终结果
    （已清理/已跳过/失败/已取消）与实际释放的字节数（`CleanupExecutionResult.TotalBytesFreed`，只统计真正删除成功的项）。
  - 该 UI 从不新增任何永久删除入口、不绕过 `IPathSafetyValidator`，也不在 `ICleanupExecutionService` 之外自行调用
    文件系统删除 API。
- **只读性能诊断**（`WPO.Core.Diagnostics`）：
  - `ISystemMetricsService` 和 `IPerformanceScanService` 是可替换接口；Windows 实现仅使用标准 .NET 与 Windows
    系统 API 获取物理内存、系统盘可用空间与进程元数据，不需要管理员权限。
  - 进程扫描会短间隔异步采样单进程 CPU，单项异常隔离，并按内存占用降序返回最多 50 个进程。不会结束进程、修改优先级、
    使用 PowerShell、改注册表或更改任何系统设置。
  - 所有可能无法可靠取得的字段都使用 `MetricValue<T>` 表示；不可用时 `IsAvailable` 为 `false` 且 `Value` 为 `null`，
    不会以零或其他猜测值伪造。可执行路径、发布者和签名状态也允许为空。
- **只读启动项检查**（`WPO.Core.Startup`）：
  - `IStartupItemService`（Windows 实现 `WindowsStartupItemService`）只读枚举最常见的自启动来源：当前用户与本机的
    `HKCU/HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` 注册表键，以及当前用户与所有用户的“启动”Shell 文件夹。
  - 绝不写注册表、不修改/删除快捷方式或文件、不结束进程、不调用 PowerShell 或任何外部进程；单个来源或单个条目读取失败
    （权限不足、键/文件不存在、格式异常等）都会被跳过，不会中断整体检查，也不会抛出未处理异常。
  - 结果按来源、名称排序并限制返回数量（`maximumItems`），支持 `CancellationToken` 取消。
  - `StartupItem.Publisher`、`IsSigned`、`IsEnabled`、`ExecutablePath` 均为可空：无法可靠判定时返回 `null`，而不是猜测值；
    发布者未知或签名缺失/无法验证**不会**被当作恶意软件的证据，`RiskLevel` 始终为 `Low`，仅在 `Notes` 中给出信息性说明。
  - 启用状态基于 `Explorer\StartupApproved\Run` 的近似启发式判断（无法确定时返回 `null`）；启动文件夹条目因缺乏禁用位，
    只要存在即视为“已启用”。签名状态使用 `X509Certificate.CreateFromSignedFile` 尝试验证 Authenticode 签名，异常时返回
    `false`（未检测到签名）或 `null`（无法确定，如访问被拒绝），两者都不等同于“已知恶意”。
  - 业务逻辑（排序/限制/取消/单项异常隔离）与实际的注册表/文件系统读取（`IStartupEntryReader` /
    `WindowsStartupEntryReader`）分离，便于用假实现覆盖单元测试，无需触碰真实注册表或启动项。

## 预览使用

启动 `WPO.App` 后，点击“刷新诊断”可异步显示系统指标卡片和高内存进程表，扫描中可点击“取消诊断”。无法取得的数据将显示
“暂时无法获取”。

“当前用户 Temp 文件安全预览与清理确认”折叠区点击“开始扫描”后，会以表格形式列出候选项（复选框、路径、大小、风险、
验证状态）。高风险与验证失败的项目无法勾选；中风险项目默认不勾选，勾选后仍需额外勾选“我已确认包含中风险清理项”才能继续。
可用“全选安全项目”一键勾选当前有效的低风险项，或用“清空选择”清空勾选；下方会实时显示已选数量与已选字节数。点击“准备清理”
只会打开一个独立的模态确认窗口，展示数量、预计释放空间、固定的“移动到回收站”处理方式与风险说明——取消、关闭、Esc、Enter
都不会执行任何清理，只有显式点击“确认清理”并在随后的二次确认中选择“是”，才会打开进度窗口并调用 `ICleanupExecutionService`
真正执行（默认仅移动到回收站，绝无永久删除入口）。执行过程中可随时点击“取消”请求 `CancellationToken` 取消，单项失败不会
中断其余项目；结束后会打开独立结果报告窗口，显示扫描/选择/尝试/成功/跳过/失败/取消、预计与实际释放字节、最终状态及每项原因；可导出 UTF-8
CSV/JSON、复制摘要，并查看或经明确确认后清除本地脱敏审计日志。导出失败不会影响当前报告。

“启动项检查（只读）”折叠区提供“检查启动项”/“取消检查”按钮与结果表格
（名称、可执行路径、来源、启用状态、签名状态），检查在后台异步执行、不会阻塞界面；若当前平台不支持或检查失败，会以
中文提示（如“启动项检查服务在当前平台不可用。”或“检查失败：<原因>”）展示，且不提供任何禁用/删除启动项的入口。

## 构建与测试

前置要求：.NET 8 SDK（`dotnet --version` 应输出 `8.0.x`）。WPF 项目与 WiX 安装器需要 Windows。

```powershell
# 还原 + 构建整个解决方案
dotnet build WindowsPerformanceOptimizer.sln

# 仅运行单元测试
dotnet test tests\WPO.Core.Tests\WPO.Core.Tests.csproj

# 单独构建 WiX 安装器（首次构建会自动还原 WiX v5 工具链）
dotnet build installer\WPO.Installer\WPO.Installer.wixproj
```

`dotnet build` 会自动构建 `WPO.App`（WPF, net8.0-windows）；`WPO.Installer` 会引用其构建输出打包为 `WPO.Installer.msi`。

## 已知限制 / 后续工作

- 当前只接入当前用户 Temp 的只读预览扫描器；回收站、浏览器缓存、Windows 更新缓存、日志等类别仍未实现。
- WPF 界面现提供“审阅并确认后清理”的完整流程（勾选 → 独立模态确认 → 二次确认 → 独立模态执行进度），但仍然刻意不提供
  永久删除入口，也不会在任何按钮的默认行为（Cancel/关闭/Esc/Enter）下自动执行清理。
- 虽然扫描器会拒绝重解析点并限制在 Temp 白名单内，但文件系统在扫描后仍可能变化；UI 在展示候选项时以及点击“准备清理”前
  都会重新调用 `IPathSafetyValidator`，执行服务本身在删除前也会再次校验，任何一层发现路径不再安全都会拒绝删除该项。
- `WindowsRecycleBinService` 依赖 `Microsoft.VisualBasic.FileIO.FileSystem`，仅在 Windows 上受支持（已加
  `[SupportedOSPlatform("windows")]` 标注）；单元测试全部通过 `IRecycleBinService` 的内存假实现验证，不会触发任何真实文件删除。
- WiX 安装器骨架仅打包了单个可执行文件组件，未包含发布配置（自包含/单文件发布）、图标、卸载清理等生产级细节。
- 启动项检查目前只覆盖 Run 注册表键与启动文件夹；服务、计划任务、WMI 事件订阅等其他自启动机制尚未实现，也不解析
  “启动”文件夹中 `.lnk` 快捷方式指向的真实目标路径（返回的是快捷方式文件自身路径）。
