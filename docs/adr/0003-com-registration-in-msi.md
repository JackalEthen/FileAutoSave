# MSI 用 RegAsm 注册 COM，不用 WiX 的 `<Class>`

_Status: accepted_

打包方式定为单个 MSI（`installer/FileAutoSave.wxs`，WiX v4，`tools/build-msi.ps1` 生成）。

COM 注册没有用 WiX 的 `<Class>` 元素，因为它生成的是**原生** COM 注册：把 `HKCR\CLSID\{guid}\InprocServer32` 指向 **DLL 本身**。托管程序集需要的是 `InprocServer32 = mscoree.dll` 外加 `Assembly` / `Class` / `RuntimeVersion` / `CodeBase` 四个值——用 `<Class>` 装出来的加载项 Office 根本加载不了。这一点是实测确认的：先建了个探针 MSI，读它的 `Registry` 表，看到 `InprocServer32 = [#filWord]`（DLL 本体）而不是 `mscoree.dll`。

决定：MSI 负责装文件、并用原生注册表项写 Office 的 `Addins` 键（64 位与 Wow6432Node 两处）；COM 注册交给**延迟自定义动作**调用 `RegAsm.exe /codebase`，32 位与 64 位两套运行时各跑一次。自定义动作**直接以 `RegAsm.exe` 为目标**（不套一层 `cmd.exe`），这样安装过程不会弹出命令行窗口。

## Consequences

- MSI **不知道** RegAsm 写了哪些 COM 键，所以"修复"不会重建它们；这是可接受的代价。
- 安装依赖机器上有 .NET Framework 4.x 的 `RegAsm.exe`——对这个只支持 Windows + .NET Framework 的加载项来说成立。
- 卸载时自定义动作用 `Return="ignore"`（`/unregister` 失败不阻断卸载）。
- 自定义动作共 12 个（3 宿主 × 2 位宽 × 安装/卸载），是这套方案里唯一啰嗦的地方。

## Considered Options

- **手工把整套托管注册写成 `<RegistryValue>`** —— 可行，但 `Assembly` 强名称串与 `CodeBase` 文件 URL 极易写错，且 3 个类 × 2 个注册表视图要写一大坨，收益不抵风险。
- **WiX `<Class>`** —— 见上，服务模型就是错的。
