using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
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
