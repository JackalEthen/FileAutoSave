using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using FileAutoSave.Core.Hosting;
using FileAutoSave.Core.Snapshots;

namespace FileAutoSave.Core.Ui
{
    /// <summary>
    /// 版本历史的可复用视图：文件名/计数头 + 快照列表 + 文本投影预览 + 回滚。
    /// 宿主外壳把它塞进文档侧边栏。布局按 380px 宽度调过，文件名单独一行、超长省略号。
    /// </summary>
    public sealed class HistoryView : UserControl
    {
        private readonly AddInRuntime _runtime;
        private readonly IHostAdapter _adapter;

        private readonly Panel _header = new Panel();
        private readonly Label _title = new Label();
        private readonly Label _subtitle = new Label();
        private readonly ListBox _list = new ListBox();
        private readonly TextBox _preview = new TextBox();

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
            _preview.Multiline = true;
            _preview.ReadOnly = true;
            _preview.WordWrap = false;
            _preview.ScrollBars = ScrollBars.Both;
            _preview.Font = new Font("Consolas", 9f);

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
                MinimumSize = new Size(72, 32)
            };
            var restore = new Button
            {
                Text = "回滚到此版本",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(124, 32)
            };
            refresh.Click += (_, __) => Reload();
            restore.Click += (_, __) => RestoreSelected();
            bar.Controls.Add(refresh);
            bar.Controls.Add(restore);

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
                _preview.Text = string.Empty;
            }
        }

        private void ShowSelected()
        {
            if (!(_list.SelectedItem is SnapshotRow row))
            {
                _preview.Text = string.Empty;
                return;
            }

            var projectionPath = _runtime.Store.ResolveProjectionPath(_adapter.FilePath!, row.Info);
            _preview.Text = !string.IsNullOrEmpty(projectionPath) && File.Exists(projectionPath)
                ? File.ReadAllText(projectionPath)
                : "（该快照没有文本投影）";
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
                $"将用 {when} 的快照覆盖当前文档。\n当前状态会先自动存为一份快照。继续吗？",
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
