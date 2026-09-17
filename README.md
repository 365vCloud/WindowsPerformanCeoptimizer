# Windows Performance Optimizer

一个专注于**安全默认**的 Windows 磁盘/性能清理工具的第一版基础工程。本仓库当前提供的是可构建、可测试的核心骨架，
不包含任何危险的系统级修改逻辑；所有删除操作默认走回收站，绝不会自动执行永久删除。

## 解决方案结构

```
WindowsPerformanceOptimizer.sln
├─ src/
│  ├─ WPO.Domain/        领域模型与枚举（含可用性明确的系统指标、进程诊断模型），net8.0 类库
│  ├─ WPO.Core/           核心服务：只读性能诊断、路径安全验证器、清理预览/执行服务、审计与 DI，net8.0 类库
│  └─ WPO.App/            WPF 仪表盘：只读系统/进程诊断与当前用户 Temp 安全预览
├─ tests/
│  └─ WPO.Core.Tests/     xUnit 单元测试（net8.0），覆盖安全校验、清理执行、审计与性能扫描
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
  - 所有路径类字段在写入审计条目前先经过 `IAuditLogMasker` 脱敏（替换用户主目录前缀与用户名分段为 `<user>`）。
  - 支持导出为 CSV 与 JSON（`IAuditLogExporter`）。
- **当前用户临时文件预览**（`WPO.Core.Cleanup.SafeTemporaryFileScanner`）：
  - 只在 `Path.GetTempPath()` 对应的当前用户 Temp 根内递归枚举文件元数据，不读取文件正文。
  - 默认只包含最后修改时间超过 24 小时的文件，最大返回 1,000 项；每一项均调用 `IPathSafetyValidator`，并标记为
    `TemporaryFiles`、低风险及“当前用户 Temp 目录中的过期临时文件（仅预览）”。
  - 枚举器与候选项都会拒绝重解析点（符号链接、联结点等），不会沿此类路径递归；遇到单项访问、IO 或验证异常会跳过并继续。
- **只读性能诊断**（`WPO.Core.Diagnostics`）：
  - `ISystemMetricsService` 和 `IPerformanceScanService` 是可替换接口；Windows 实现仅使用标准 .NET 与 Windows
    系统 API 获取物理内存、系统盘可用空间与进程元数据，不需要管理员权限。
  - 进程扫描会短间隔异步采样单进程 CPU，单项异常隔离，并按内存占用降序返回最多 50 个进程。不会结束进程、修改优先级、
    使用 PowerShell、改注册表或更改任何系统设置。
  - 所有可能无法可靠取得的字段都使用 `MetricValue<T>` 表示；不可用时 `IsAvailable` 为 `false` 且 `Value` 为 `null`，
    不会以零或其他猜测值伪造。可执行路径、发布者和签名状态也允许为空。

## 预览使用

启动 `WPO.App` 后，点击“刷新诊断”可异步显示系统指标卡片和高内存进程表，扫描中可点击“取消诊断”。无法取得的数据将显示
“暂时无法获取”。“当前用户 Temp 文件安全预览”折叠区保留原有的异步预览与取消扫描功能。该界面不提供删除按钮，不会读取候选文件内容，
也不会执行删除、注册表、进程或系统配置操作。

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
  WPF 界面刻意没有删除、执行或自动清理入口，扫描结果不代表任何文件会被处理。
- 虽然扫描器会拒绝重解析点并限制在 Temp 白名单内，但文件系统在扫描后仍可能变化；任何未来的执行功能都必须重新进行
  路径安全验证，并要求显式的用户确认。
- `WindowsRecycleBinService` 依赖 `Microsoft.VisualBasic.FileIO.FileSystem`，仅在 Windows 上受支持（已加
  `[SupportedOSPlatform("windows")]` 标注）；单元测试全部通过 `IRecycleBinService` 的内存假实现验证，不会触发任何真实文件删除。
- WiX 安装器骨架仅打包了单个可执行文件组件，未包含发布配置（自包含/单文件发布）、图标、卸载清理等生产级细节。
