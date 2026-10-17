# 采用纯 COM 加载项 + 共享核心 + 三外壳

_Status: accepted_

需求是跨 Word/Excel/PowerPoint 的插件，仅在 Windows 桌面使用，且要读写本地文件、贴近 Office 对象模型。可选的两条路：Office.js（一套代码覆盖三宿主，但进程外运行、写本地文件能力受限）与 COM 加载项（进程内、能力最强、仅 Windows）。选 COM。

COM 层不选 VSTO（锁定 .NET Framework、需 VSTO runtime、官方已列为 legacy），改用 **NetOffice** 框架实现 `IDTExtensibility2`——免 VSTO runtime、免 PIA 分发、版本无关。结构为**一套共享核心 + 每个宿主一个薄外壳**，与主流开源 Office 加载项（如 it235/office-ai-agent、blazzbyte/agent-office）一致。

## Consequences

COM 加载项在宿主进程内运行，**没有 per-add-in 崩溃隔离**：未处理异常会拖垮整个宿主，且 Office resiliency 会**自动禁用**崩溃过或启动慢的加载项。因此：所有入口（`OnConnection` 等）必须完整 `try/catch` 且不外泄异常；注册用 `LoadBehavior=3`（随宿主启动并连接）——不要用 16，它不含"连接"位，会出现"功能区渲染出来了但按钮点了没反应"的假象。

位宽：三个 shim 编译为 **AnyCPU**。纯托管程序集由 CLR 按宿主进程位宽加载，在 32/64 位 Office 中都正确；把位宽钉死（x86-only 或 x64-only）反而会在另一种 Office 上抛 `InvalidCastException`。因此要做的不是双构建，而是**注册时用 32 位与 64 位两套 `RegAsm` 各注册一次**，并在 `HKLM\Software\Microsoft\Office\...` 与 `HKLM\Software\Wow6432Node\Microsoft\Office\...` 两处都写 `Addins` 键——见 `installer/FileAutoSave.wxs`（MSI，注册细节见 ADR-0003）与 `tools/install.ps1`（脚本方式）。
