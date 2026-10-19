# FileAutoSave

> Auto-save with a **customizable interval**, **local snapshot rollback**, and **cloud version history** for Word, Excel and PowerPoint.

![version](https://img.shields.io/badge/version-0.2.1-2ea44f?style=flat-square)
![license](https://img.shields.io/badge/license-AGPL--3.0-blue?style=flat-square)
![platform](https://img.shields.io/badge/platform-Windows-0078D6?style=flat-square)
![.NET](https://img.shields.io/badge/.NET%20Framework-4.8-512BD4?style=flat-square)
![hosts](https://img.shields.io/badge/hosts-Word%20%7C%20Excel%20%7C%20PowerPoint-2B579A?style=flat-square)
![cloud](https://img.shields.io/badge/cloud-WebDAV%20%7C%20S3%20%7C%20NAS-F38020?style=flat-square)
![data](https://img.shields.io/badge/data-100%25%20local-success?style=flat-square)
![telemetry](https://img.shields.io/badge/telemetry-none-brightgreen?style=flat-square)

English | [简体中文](README-zh.md)

---

## Why

Office's built-in AutoSave only works for files stored on OneDrive / SharePoint: it **requires sign-in**, its **interval is not configurable**, and local files get nothing at all.

FileAutoSave fills that gap — a COM add-in running inside the Office process that gives you timed saving and version rollback on your own terms.

## Features

| Feature | Description |
|---|---|
| **Auto-save** | Overwrites the original file with the current content on your interval (1–60 min) |
| **Auto-recover** | Records a rollbackable snapshot whenever the content changes — **including every manual save** |
| **Version history pane** | A document-side panel listing snapshots with a text-projection preview and one-click rollback |
| **Rollback** | Always stores a "pre-rollback snapshot" first, so a mis-click is recoverable |
| **Cloud version history** | WebDAV (Nextcloud / Synology / Jianguoyun), S3-compatible (AWS / MinIO / Alibaba OSS), NAS (UNC share) |
| **Backup to web netdisk** | No cloud account needed: opens Quark / Xunlei / Baidu / 189 / Aliyun Drive in your browser and locates the latest snapshot so you drag it up |
| **Retry queue** | Failed cloud uploads go to a persisted queue with exponential backoff |
| **Per-file switches** | Auto-save and auto-recover are **independent** and their state is remembered per document |
| **Document identity** | History stays attached after a rename or move |

**Privacy**: snapshots, settings and queues live locally in `%APPDATA%\FileAutoSave`; cloud credentials are encrypted with Windows DPAPI; no telemetry, no network access unless you configure a cloud target.

## Install

1. Download the MSI for your language: `FileAutoSave-zh-CN.msi` (简体中文) or `FileAutoSave-en-US.msi` (English)
2. Run it (wizard: welcome → license → install folder → install)
3. Open Word / Excel / PowerPoint → **Home** tab → the **FileAutoSave** group

Uninstall: Settings → Apps → `FileAutoSave` (user data is kept by default; use `tools\uninstall.ps1 -RemoveUserData` to wipe it)

> Requires Windows + .NET Framework 4.8 + Office 2010 or later. Both 32-bit and 64-bit Office are supported.

## Usage

Five buttons on the ribbon:

- **Auto-save** (toggle) — overwrite the original file on the interval
- **Auto-recover** (toggle) — record rollbackable snapshots
- **Version history** — open the side pane
- **Backup to netdisk** — open the netdisk site and locate the latest snapshot, then drag it up
- **Settings** — interval / snapshot root / retention / cloud

Both switches **default to off** and their state is **remembered per document**:

| Auto-save | Auto-recover | Result |
|:---:|:---:|---|
| off | off | nothing happens (default) |
| on | off | the file is overwritten on the interval; **no** version history |
| off | on | the file is never overwritten, but every manual save records a snapshot |
| on | on | timed overwrite **and** timed snapshots |

## No cloud account? Three ways to back up

Auto-recover and version history are **already fully local** — they work with no cloud configured at all. For off-site backup, pick one:

| Your situation | Use | Cost |
|---|---|---|
| You have a NAS or a LAN share | Settings → Cloud target → **NAS (UNC path)** | just fill in `\\PC\share\FileAutoSave` — real upload, cloud version history included |
| You have an account, or can register | Settings → Cloud target → **WebDAV**: Jianguoyun at `https://dav.jianguoyun.com/dav/` (password = the *app password* under Account info → Security options) — or **S3-compatible**: Cloudflare R2, Backblaze B2, MinIO | **zero code**, configure and go. R2 and B2 each have 10 GB free; Jianguoyun's free tier caps *upload* traffic at 1 GB/month |
| Neither | Ribbon → **Backup to netdisk** | opens the site and locates the snapshot — **you drag it up yourself** |

**About "Backup to netdisk"**: it is only a shortcut to the official site. Quark / Xunlei / Baidu **publish no API**, and emulating their private endpoints both violates their terms and breaks whenever they change them — so we don't. The cost is that it is **off-site backup only**: with no file listing available there is no cloud version history and no cloud rollback. History always stays local.

Five entries (Quark, Xunlei, Baidu, 189, Aliyun Drive) are **built in** — one click on the ribbon and pick one; there is nothing to configure.

## Snapshot layout

```
%APPDATA%\FileAutoSave\versions\
├─ documents.json              ← registry: every document's id / path / content hash / switches
└─ test.docx\                  ← one folder per source file, named after it
   ├─ index.json               ← this document: id + state + snapshot list
   ├─ 20261017-211541123.docx           ← the snapshot itself (used for rollback)
   ├─ 20261017-211541123.projection.txt ← text projection (preview only, never used for rollback)
   └─ ...
```

**Why two files per snapshot**: Word→txt and Excel→csv lose formatting, formulas, multi-sheet structure and images — **rolling back from those would destroy your document**. So the binary snapshot is the recovery source, and the text projection only exists to show what changed.

## Build from source

```powershell
# requires the .NET SDK and WiX 5
dotnet tool install --global wix --version 5.*
wix extension add -g WixToolset.UI.wixext/5.0.2

# build + self-check
dotnet build FileAutoSave.slnx -c Release
dotnet run --project src\FileAutoSave.SelfCheck -c Release

# produces the two localized MSIs (ICE-validated)
powershell -ExecutionPolicy Bypass -File tools\build-msi.ps1

# or install without the MSI
powershell -ExecutionPolicy Bypass -File tools\install.ps1   # run as administrator
```

## Architecture

```
src/
├─ FileAutoSave.Core/            shared core (never touches Office objects)
│  ├─ Hosting/                   runtime, per-document service, ribbon actions, host abstraction
│  ├─ Snapshots/                 snapshot store, document identity + registry, rollback
│  ├─ Cloud/                     WebDAV / S3 / NAS, upload queue
│  ├─ Security/                  DPAPI credential storage
│  └─ Ui/                        settings dialog, history view, ribbon icons
├─ FileAutoSave.AddIn.Word/      three thin shims: Word
├─ FileAutoSave.AddIn.Excel/                       Excel
├─ FileAutoSave.AddIn.PowerPoint/                  PowerPoint
└─ FileAutoSave.SelfCheck/       framework-free self-check (44 assertions)
```

Design decisions live in [`docs/adr/`](docs/adr/); the domain glossary is [`CONTEXT.md`](CONTEXT.md).

## Known limitations

- **No WPS support** (Microsoft Office only).
- **Renames re-attach, copies start fresh**: a rename (the old path is gone) adopts the same history and renames its folder; Save As / a file copy (the old path still exists) counts as a new file. A copy is **byte-identical** to its original, so no content hash can tell them apart — "is the old path still there" is the only reliable discriminator.
- **In-place replacement is not detected**: overwriting a path with a completely different document is indistinguishable on disk from "the same file edited elsewhere and put back", and is treated as the same document.
- **Auto-recover with auto-save off can lag up to ~15 s**: Word has no "after save" event, so manual saves are picked up by content-change detection. Closing a document and unloading the add-in both flush one last snapshot, so a manual save inside the polling window is not lost.
- **Text projection is truncated on very large documents**: the projection is produced by walking the document over COM on the UI thread and is cut off past 200,000 characters (Excel / PowerPoint stop walking early). The projection only feeds the diff display — never rollback — so truncating it costs no recovery capability.
- **Not code-signed**: SmartScreen will warn on install.
- Snapshot root and retention take effect **at startup**; restart the host after changing them.

## License

[AGPL-3.0](https://www.gnu.org/licenses/agpl-3.0.html)
