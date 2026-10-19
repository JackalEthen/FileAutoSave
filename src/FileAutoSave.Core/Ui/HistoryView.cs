using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using FileAutoSave.Core.Hosting;
using FileAutoSave.Core.Snapshots;

namespace FileAutoSave.Core.Ui
{
    /// <summary>
    /// 版本历史的可复用视图：文件名/计数头 + 快照列表 + 文本投影预览 + 行级差异 + 回滚 + 另存。
    /// 宿主外壳把它塞进文档侧边栏。布局按 380px 宽度调过，文件名单独一行、超长省略号。
    /// </summary>
    public sealed class HistoryView : UserControl
    {
        /// <summary>差异最多渲染多少行，超出就截断——2000 万字的中文文档也能立刻出结果。</summary>
        private const int MaxDiffLines = 4000;

        private readonly AddInRuntime _runtime;
        private readonly IHostAdapter _adapter;

        private readonly Panel _header = new Panel();
        private readonly Label _title = new Label();
        private readonly Label _subtitle = new Label();
        private readonly ListBox _list = new ListBox();
        private readonly RichTextBox _preview = new RichTextBox();
        private readonly CheckBox _diffToggle = new CheckBox { Text = "对比上一版", AutoSize = true };

        private IReadOnlyList<SnapshotInfo> _snapshots = Array.Empty<SnapshotInfo>();

        public HistoryView(AddInRuntime runtime, IHostAdapter adapter)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));

            Dock = DockStyle.Fill;
            Font = new Font("Microsoft YaHei UI", 9f);

            _header.Dock = DockStyle.Top;
            _header.Height = 62;
            _header.Padding = new Padding(10, 8, 10, 4);

            _title.Dock = DockStyle.Top;
            _title.Height = 26;
            _title.AutoEllipsis = true;
            _title.TextAlign = ContentAlignment.MiddleLeft;
            _title.Font = new Font(Font, FontStyle.Bold);

            _subtitle.Dock = DockStyle.Top;
            _subtitle.Height = 20;
            _subtitle.AutoEllipsis = true;
            _subtitle.TextAlign = ContentAlignment.MiddleLeft;
            _subtitle.ForeColor = Color.DimGray;

            _header.Controls.Add(_subtitle);
            _header.Controls.Add(_title);

            _list.Dock = DockStyle.Top;
            _list.Height = 240;
            _list.IntegralHeight = false;
            _list.HorizontalScrollbar = true;
            _list.SelectedIndexChanged += (_, __) => ShowSelected();

            _preview.Dock = DockStyle.Fill;
            _preview.ReadOnly = true;
            _preview.WordWrap = false;
            _preview.ScrollBars = RichTextBoxScrollBars.Both;
            _preview.Font = new Font("Consolas", 9f);
            _preview.DetectUrls = false;

            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 56,
                Padding = new Padding(8, 12, 8, 12),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };

            var refresh = new Button
            {
                Text = "刷新",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(60, 32)
            };
            var export = new Button
            {
                Text = "另存为…",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(80, 32)
            };
            var restore = new Button
            {
                Text = "回滚到此版本",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(112, 32)
            };
            refresh.Click += (_, __) => Reload();
            export.Click += (_, __) => ExportSelected();
            restore.Click += (_, __) => RestoreSelected();
            _diffToggle.Anchor = AnchorStyles.Left;
            _diffToggle.CheckedChanged += (_, __) => ShowSelected();
            bar.Controls.Add(refresh);
            bar.Controls.Add(export);
            bar.Controls.Add(restore);
            bar.Controls.Add(_diffToggle);

            // 停靠顺序：Fill 先加（最后布局），其余按 上→下 的逆序加载。
            Controls.Add(_preview);
            Controls.Add(bar);
            Controls.Add(_list);
            Controls.Add(_header);

            Reload();
        }

        public void Reload()
        {
            _list.Items.Clear();

            if (!_adapter.IsSaved || string.IsNullOrEmpty(_adapter.FilePath))
            {
                _title.Text = "未保存的文档";
                _subtitle.Text = "没有版本历史";
                return;
            }

            _title.Text = Path.GetFileName(_adapter.FilePath);

            var snapshots = _runtime.Store.List(_adapter.FilePath!);
            _snapshots = snapshots;
            foreach (var info in snapshots)
            {
                _list.Items.Add(new SnapshotRow(info));
            }

            _subtitle.Text = snapshots.Count == 0
                ? "还没有快照（间隔到点后会自动记录）"
                : $"共 {snapshots.Count} 个快照";

            if (_list.Items.Count > 0)
            {
                _list.SelectedIndex = 0;
            }
            else
            {
                _preview.Clear();
            }
        }

        private void ShowSelected()
        {
            if (!(_list.SelectedItem is SnapshotRow row))
            {
                _preview.Clear();
                return;
            }

            // 列表按时间倒序：选中的下一项就是"上一版"。
            var index = _list.SelectedIndex;
            if (_diffToggle.Checked && index >= 0 && index + 1 < _snapshots.Count)
            {
                ShowDiff(_snapshots[index + 1], _snapshots[index]);
                return;
            }

            _preview.Clear();
            _preview.SelectionColor = Color.Black;

            var projection = ReadProjection(row.Info);
            _preview.Text = projection ?? "（该快照没有文本投影）";
        }

        /// <summary>渲染"从上一版到这一版"改了哪些行：绿色 + 是新增，红色 - 是删除。</summary>
        private void ShowDiff(SnapshotInfo older, SnapshotInfo newer)
        {
            var olderText = ReadProjection(older);
            var newerText = ReadProjection(newer);
            if (olderText == null || newerText == null)
            {
                _preview.Clear();
                _preview.SelectionColor = Color.Black;
                _preview.Text = "（相邻版本缺少文本投影，无法对比）";
                return;
            }

            _preview.Clear();
            _preview.SelectionColor = Color.Black;

            var lines = LineDiff.Compare(olderText, newerText);
            var shown = Math.Min(lines.Count, MaxDiffLines);
            for (var i = 0; i < shown; i++)
            {
                var line = lines[i];
                _preview.SelectionColor = line.Kind == DiffKind.Added
                    ? Color.DarkGreen
                    : line.Kind == DiffKind.Removed ? Color.DarkRed : Color.Gray;
                _preview.AppendText(
                    (line.Kind == DiffKind.Added ? "+ " : line.Kind == DiffKind.Removed ? "- " : "  ") +
                    line.Text + Environment.NewLine);
            }

            if (lines.Count > shown)
            {
                _preview.SelectionColor = Color.DimGray;
                _preview.AppendText($"…… 差异过长，仅显示前 {shown} 行（共 {lines.Count} 行）");
            }

            _preview.SelectionStart = 0;
            _preview.SelectionLength = 0;
            _preview.SelectionColor = Color.Black;
        }

        private string? ReadProjection(SnapshotInfo info)
        {
            var path = _runtime.Store.ResolveProjectionPath(_adapter.FilePath!, info);
            return !string.IsNullOrEmpty(path) && File.Exists(path) ? File.ReadAllText(path) : null;
        }

        /// <summary>
        /// 把选中的快照另存到任意位置，不碰原文件。
        /// 这是"文档已经坏了"的抢救手段——回滚是全有或全无，这个可以只取出单个版本。
        /// </summary>
        private void ExportSelected()
        {
            if (!(_list.SelectedItem is SnapshotRow row))
            {
                MessageBox.Show("请先选择一个版本。", "文件自动保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var source = _runtime.Store.ResolvePath(_adapter.FilePath!, row.Info);
            if (!File.Exists(source))
            {
                MessageBox.Show("该快照文件已不存在。", "文件自动保存", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var extension = Path.GetExtension(source);
            using (var dialog = new SaveFileDialog
            {
                Title = "另存快照为",
                FileName = Path.GetFileName(source),
                Filter = (string.IsNullOrEmpty(extension) ? "所有文件" : "原文档文件|" + extension) + "|所有文件|*.*"
            })
            {
                if (dialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    File.Copy(source, dialog.FileName, overwrite: true);
                    MessageBox.Show("已另存到：" + dialog.FileName, "文件自动保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("另存失败：" + ex.Message, "文件自动保存", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void RestoreSelected()
        {
            if (!(_list.SelectedItem is SnapshotRow row))
            {
                MessageBox.Show("请先选择一个版本。", "文件自动保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var when = row.Info.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
            var answer = MessageBox.Show(
                $"将用 {when} 的快照覆盖当前文档。\n覆盖前，磁盘上当前的内容会先自动存为一份快照。\n继续吗？",
                "回滚确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (answer != DialogResult.Yes)
            {
                return;
            }

            try
            {
                RollbackService.Rollback(_adapter, _runtime.Store, row.Info);
                MessageBox.Show("已回滚。", "文件自动保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("回滚失败：" + ex.Message, "文件自动保存", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private sealed class SnapshotRow
        {
            public SnapshotRow(SnapshotInfo info) => Info = info;

            public SnapshotInfo Info { get; }

            public override string ToString()
            {
                var when = Info.TimestampUtc.ToLocalTime().ToString("MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
                var size = (Info.SizeBytes / 1024.0).ToString("0.0", CultureInfo.CurrentCulture);
                return $"{when}   {size} KB";
            }
        }
    }
}
