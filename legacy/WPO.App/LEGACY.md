# Legacy WPF Shell (superseded by Tauri v2)

This WPF project (`WPO.App`) is retained **only** for historical reference and is
**no longer the shipped desktop application**. Windows Performance Optimizer v2
is implemented as a Rust/Tauri v2 application; see `src-tauri/` and `frontend/`
at the repository root, and the top-level `README.md` for the current
architecture, build instructions, and MSI packaging.

`WPO.App.exe` is **not** included in the v2 MSI installer. It is excluded from
`installer/WPO.Installer` and is not built as part of the default v2 release
pipeline. `WPO.Core`, `WPO.Domain`, and their tests remain in `src/`/`tests/`
purely as a reference implementation of the original safety logic that the
Rust backend re-implements; they are not linked into, or required by, the
Tauri application.
