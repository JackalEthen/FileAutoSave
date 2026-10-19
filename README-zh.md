# FileAutoSave

> 给 Word / Excel / PowerPoint 补上**可自定义间隔**的自动保存、**本地快照回滚**与**云端版本历史**。

![version](https://img.shields.io/badge/version-0.2.2-2ea44f?style=flat-square)
![license](https://img.shields.io/badge/license-AGPL--3.0-blue?style=flat-square)
![platform](https://img.shields.io/badge/platform-Windows-0078D6?style=flat-square)
![.NET](https://img.shields.io/badge/.NET%20Framework-4.8-512BD4?style=flat-square)
![hosts](https://img.shields.io/badge/hosts-Word%20%7C%20Excel%20%7C%20PowerPoint-2B579A?style=flat-square)
![cloud](https://img.shields.io/badge/cloud-WebDAV%20%7C%20S3%20%7C%20NAS-F38020?style=flat-square)
![data](https://img.shields.io/badge/data-100%25%20local-success?style=flat-square)
![telemetry](https://img.shields.io/badge/telemetry-none-brightgreen?style=flat-square)

[English](README.md) | 简体中文

---

## 为什么做这个

Office 自带的 AutoSave 只在文件存到 OneDrive / SharePoint 时可用：**需要登录**、**间隔不可调**，本地文件更是完全不触发。

FileAutoSave 填的就是这个缺口——一个跑在 Office 进程内的 COM 加载项，把"定时存盘 + 版本回滚"做成你自己能掌控的东西。

## 功能

| 功能 | 说明 |
|---|---|
| **自动保存** | 按你设定的间隔（1–60 分钟）用当前内容覆盖原文件 |
| **自动恢复** | 内容一变化就记一份可回滚的快照，**每次手动保存也算** |
| **版本历史侧边栏** | 文档右侧面板列出快照 + 文本投影预览 + 一键回滚 |
| **回滚** | 覆盖文档前先自动存一份"回滚前快照"，手滑也能救回来 |
| **云端版本历史** | WebDAV（坚果云 / Nextcloud / 群晖）、S3 兼容（AWS / MinIO / 阿里云 OSS）、NAS（UNC 共享） |
| **备份到网页网盘** | 没云盘也能用：打开夸克 / 迅雷 / 百度 / 天翼 / 阿里云盘的官网，并定位最新快照，你手动拖拽上传 |
| **失败重试** | 云端上传失败进持久化队列，按指数退避自动重试 |
| **每文件开关** | 自动保存、自动恢复**互相独立**，都按文件记住状态 |
| **文档标识** | 改名 / 移动后仍能对齐到同一份版本历史 |

**隐私**：快照、设置、队列全部在本地 `%APPDATA%\FileAutoSave`；云凭据用 Windows DPAPI 加密；无遥测、无联网（除非你自己配了云端）。

## 安装

1. 下载对应语言的 MSI：`FileAutoSave-zh-CN.msi`（简体中文）或 `FileAutoSave-en-US.msi`（English）
2. 双击安装（向导：欢迎 → 许可 → 选目录 → 安装）
3. 打开 Word / Excel / PowerPoint → **「开始」选项卡** → 「**文件自动保存**」组

卸载：设置 → 应用 → `FileAutoSave`（用户数据默认保留；要清掉用 `tools\uninstall.ps1 -RemoveUserData`）

> 需要 Windows + .NET Framework 4.8 + Office 2010 以上。32 位与 64 位 Office 都支持。

## 用法

功能区有五个按钮：

- **自动保存**（开关）——定时覆盖原文件
- **自动恢复**（开关）——记录可回滚的快照
- **版本历史**——打开右侧边栏
- **备份到网盘**——打开网盘官网 + 定位最新快照，手动拖拽上传
- **设置**——间隔 / 快照目录 / 保留数 / 云端

两个开关**默认关闭**，且**每个文件独立记住状态**：

| 自动保存 | 自动恢复 | 效果 |
|:---:|:---:|---|
| 关 | 关 | 什么都不做（默认） |
| 开 | 关 | 定时覆盖原文件，**不产生**版本历史 |
| 关 | 开 | 不覆盖文件，但每次手动保存后记一份快照 |
| 开 | 开 | 定时覆盖 + 定时记快照 |

## 没有云盘怎么备份

自动恢复和版本历史**本来就是纯本地的**，不配任何云也能用。异地备份按下面三档选，越往下越省事：

| 你的情况 | 用什么 | 代价 |
|---|---|---|
| 有 NAS / 局域网共享 | 设置 → 云目标 → **NAS（UNC 共享路径）** | 填个 `\\PC\share\FileAutoSave`，真上传，有云端版本历史 |
| 有账号 / 愿意注册 | 设置 → 云目标 → **WebDAV**：坚果云 `https://dav.jianguoyun.com/dav/`，密码填网页版「账户信息 → 安全选项 → 应用密码」<br>或 **S3 兼容**：Cloudflare R2、Backblaze B2、MinIO | **零代码**，配置即用。R2 / B2 各有 10GB 免费；坚果云免费版每月上传流量 1GB |
| 都不想折腾 | 功能区 → **备份到网盘** | 打开网盘官网 + 定位快照，**手动拖一下** |

**关于「备份到网盘」**：它只是通往官网的快捷方式。夸克 / 迅雷 / 百度这类**都没有公开 API**，我们去模拟它们的私有接口既违反服务条款，又会随对方改版而失效——所以不模拟。代价是它**只做异地备份**：拿不到云端文件列表，因此没有云端版本历史、也没有云端回滚（版本历史始终在本地）。

夸克 / 迅雷 / 百度 / 天翼 / 阿里云盘五个入口**内置**，功能区点一下就选，没有需要配的地方。

## 快照长什么样

```
%APPDATA%\FileAutoSave\versions\
├─ documents.json              ← 汇总：所有文档的标识 / 路径 / 内容哈希 / 每文件开关
└─ test.docx\                  ← 每个源文件一个同名目录
   ├─ index.json               ← 该文档：标识 + 状态 + 快照序列
   ├─ 20261017-211541123.docx           ← 快照本体（回滚用）
   ├─ 20261017-211541123.projection.txt ← 文本投影（只用于预览对比，不参与回滚）
   └─ ...
```

**为什么要两份**：Word→txt、Excel→csv 会丢格式 / 公式 / 多表 / 图片，**拿它回滚等于毁文档**。所以二进制快照当恢复源，文本投影只用于"看改了啥"。

## 从源码构建

```powershell
# 需要 .NET SDK + WiX 5
dotnet tool install --global wix --version 5.*
wix extension add -g WixToolset.UI.wixext/5.0.2

# 编译 + 跑自检
dotnet build FileAutoSave.slnx -c Release
dotnet run --project src\FileAutoSave.SelfCheck -c Release

# 产出中英两份 MSI（都过 ICE 校验）
powershell -ExecutionPolicy Bypass -File tools\build-msi.ps1

# 或者用脚本方式安装（免安装包）
powershell -ExecutionPolicy Bypass -File tools\install.ps1   # 需管理员
```

## 架构

```
src/
├─ FileAutoSave.Core/            共享核心（不碰 Office 对象）
│  ├─ Hosting/                   运行时、每文档服务、功能区动作、宿主抽象
│  ├─ Snapshots/                 快照库、文档标识与注册表、回滚
│  ├─ Cloud/                     WebDAV / S3 / NAS、上传队列
│  ├─ Security/                  DPAPI 凭据
│  └─ Ui/                        设置窗口、版本历史视图、功能区图标
├─ FileAutoSave.AddIn.Word/      三个薄外壳：Word
├─ FileAutoSave.AddIn.Excel/                  Excel
├─ FileAutoSave.AddIn.PowerPoint/             PowerPoint
└─ FileAutoSave.SelfCheck/       无框架自检（54 项）
```

设计决策记录在 [`docs/adr/`](docs/adr/)，领域术语在 [`CONTEXT.md`](CONTEXT.md)。

## 已知限制

- **WPS 不支持**（只做 Microsoft Office）。
- **改名能接上，副本算新文件**：改名（原路径消失）会接上同一份历史并同步改名目录；另存为 / 复制副本（原路径还在）算新文件。副本与原文件**字节相同**，光靠哈希分不开，"原路径是否还在"是唯一可靠的判据。
- **同路径整体替换无法识别**：有人把另一个文档覆盖到同一路径，和"该文件在别处编辑过再放回"在磁盘上不可区分，按同一文档处理。
- **自动恢复（自动保存关闭时）最多约 15 秒延迟**：Word 没有"保存后"事件，靠内容变化检测。关闭文档与卸载加载项时会补一次快照，所以轮询窗口内的那次手动保存不会丢。
- **超大文档的文本投影会被截断**：投影在 UI 线程上用 COM 遍历文档，超过 20 万字符就截断（Excel / PowerPoint 会提前收工）。投影只用于显示差异，不参与回滚，截断不影响任何恢复能力。
- **无代码签名**：安装时会有 SmartScreen 提示。
- 快照目录与保留数**在启动时生效**，改了要重启宿主。

## 许可证

[AGPL-3.0](https://www.gnu.org/licenses/agpl-3.0.html)
