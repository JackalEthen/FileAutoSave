using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using FileAutoSave.Core.Settings;
using FileAutoSave.Core.Ui;

namespace FileAutoSave.Core.Hosting
{
    /// <summary>
    /// 功能区按钮背后的动作。宿主外壳的回调统一委托到这里。
    /// 自动保存 / 自动恢复是**每文件**状态，所以每个动作都需要当前文档的适配器。
    /// </summary>
    public sealed class RibbonActions
    {
        private readonly AddInRuntime _runtime;
        private readonly HashSet<string> _notified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public RibbonActions(AddInRuntime runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public bool GetAutoSaveEnabled(IHostAdapter? activeDocument) =>
            _runtime.GetState(activeDocument)?.AutoSaveEnabled ?? false;

        public bool GetAutoRecoverEnabled(IHostAdapter? activeDocument) =>
            _runtime.GetState(activeDocument)?.AutoRecoverEnabled ?? false;

        public void SetAutoSave(IHostAdapter? activeDocument, bool enabled)
        {
            if (!RequireSaved(activeDocument))
            {
                return;
            }

            _runtime.SetState(activeDocument, enabled, null);
        }

        public void SetAutoRecover(IHostAdapter? activeDocument, bool enabled)
        {
            if (!RequireSaved(activeDocument))
            {
                return;
            }

            _runtime.SetState(activeDocument, null, enabled);
        }

        /// <summary>已保存文档打开时的提示（每份文件一次）。</summary>
        public void NotifyDocumentOpened(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !_notified.Add("saved:" + filePath))
            {
                return;
            }

            MessageBox.Show(
                $"已打开「{System.IO.Path.GetFileName(filePath)}」。\n可在「文件自动保存」组里开启自动保存与自动恢复（两者独立，默认关闭）。",
                "文件自动保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>未保存文档的即时提醒（每份文件一次）。</summary>
        public void NotifyUnsaved(string fileName)
        {
            if (!_notified.Add("unsaved:" + fileName))
            {
                return;
            }

            MessageBox.Show(
                $"当前文档「{fileName}」尚未保存。\n请先执行一次「另存为」，再开启自动保存和自动恢复。",
                "文件自动保存", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public void OpenSettings(IHostAdapter? activeDocument)
        {
            using (var form = new SettingsForm(_runtime, activeDocument))
            {
                form.ShowDialog();
            }
        }

        /// <summary>
        /// 备份到网页网盘：打开对方官网，同时在资源管理器里定位当前文档的最新快照，
        /// 用户自己拖拽上传。我们不碰它的接口——既不违规也不会随对方改版而失效。
        /// </summary>
        public void BackupToWebNetdisk(IHostAdapter? activeDocument)
        {
            // 固定的五个入口，没有可配置项——用户要加网盘就改这里。
            var links = WebNetdiskLink.Defaults().Where(l => l.IsUsable()).ToList();
            if (links.Count == 0)
            {
                MessageBox.Show(
                    "没有可用的网页网盘入口。\n这是内置列表，请检查安装是否完整。",
                    "文件自动保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!RequireSaved(activeDocument))
            {
                return;
            }

            var path = activeDocument!.FilePath!;

            // Store.List 按时间倒序，第一个就是最新快照。
            var newest = _runtime.Store.List(path).FirstOrDefault();
            if (newest == null)
            {
                MessageBox.Show(
                    "当前文档还没有快照可供备份。\n请先开启「自动恢复」，或等一次自动保存/手动保存生成快照。",
                    "文件自动保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var snapshotPath = _runtime.Store.ResolvePath(path, newest);

            using (var picker = new WebNetdiskPickerForm(links, Path.GetFileName(snapshotPath)))
            {
                if (picker.ShowDialog() != DialogResult.OK || picker.Selected == null)
                {
                    return;
                }

                OpenNetdisk(picker.Selected, snapshotPath);
            }

            if (_notified.Add("webnetdisk-hint"))
            {
                MessageBox.Show(
                    "已打开网盘网页，并在资源管理器里定位了最新快照。\n把文件拖进网页的上传区就完成了。",
                    "文件自动保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        /// <summary>用系统默认浏览器打开网盘，并在资源管理器里选中快照文件。</summary>
        private static void OpenNetdisk(WebNetdiskLink link, string snapshotPath)
        {
            try
            {
                Process.Start(new ProcessStartInfo(link.Url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Diagnostics.LogException("WebNetdisk: 打开浏览器", ex);
            }

            if (!File.Exists(snapshotPath))
            {
                return;
            }

            try
            {
                // ponytail: 只定位文件，不额外打开文件夹窗口——少一个窗口少一份干扰。
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + snapshotPath + "\"")
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Diagnostics.LogException("WebNetdisk: 定位快照", ex);
            }
        }

        public void ShowStatus(IHostAdapter? activeDocument)
        {
            var settings = _runtime.Settings;
            var state = _runtime.GetState(activeDocument);
            var name = activeDocument?.FilePath != null ? System.IO.Path.GetFileName(activeDocument.FilePath) : "（无活动文档）";

            var text = new StringBuilder()
                .AppendLine($"当前文档：{name}")
                .AppendLine($"  文档标识：{state?.DocumentId ?? "（未记录）"}")
                .AppendLine($"  自动保存：{(state?.AutoSaveEnabled == true ? "开" : "关")}")
                .AppendLine($"  自动恢复：{(state?.AutoRecoverEnabled == true ? "开" : "关")}")
                .AppendLine()
                .AppendLine($"间隔：{settings.AutoSaveIntervalMinutes} 分钟")
                .AppendLine($"活跃文档：{_runtime.ActiveDocumentCount}")
                .AppendLine($"待上传：{_runtime.PendingUploads}")
                .AppendLine($"快照目录：{settings.GetSnapshotsRoot()}")
                .ToString();

            MessageBox.Show(text, "文件自动保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static bool RequireSaved(IHostAdapter? activeDocument)
        {
            if (activeDocument != null && activeDocument.IsSaved)
            {
                return true;
            }

            MessageBox.Show(
                "当前文档尚未保存。\n请先执行一次「另存为」，自动保存与自动恢复才会对它生效。",
                "文件自动保存", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }
}
