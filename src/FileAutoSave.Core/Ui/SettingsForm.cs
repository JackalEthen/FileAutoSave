using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using FileAutoSave.Core.Hosting;
using FileAutoSave.Core.Security;
using FileAutoSave.Core.Settings;

namespace FileAutoSave.Core.Ui
{
    /// <summary>
    /// 设置窗口。
    /// 布局刻意做成**单层** TableLayoutPanel（无嵌套 GroupBox / 无 AutoSize 套 AutoSize）——
    /// 那种嵌套在 WinForms 下极易塌陷，表现为文字和输入框互相遮挡。
    /// </summary>
    internal sealed class SettingsForm : Form
    {
        private const int TableWidth = 790;
        private const int LabelWidth = 170;
        private const int TrailingWidth = 110;
        private const int RowHeight = 50;
        private const int HeaderHeight = 46;

        private readonly AddInRuntime _runtime;

        private readonly NumericUpDown _interval = new NumericUpDown { AutoSize = false };

        private readonly TextBox _snapshotsRoot = new TextBox();
        private readonly NumericUpDown _retention = new NumericUpDown { AutoSize = false };

        private readonly ComboBox _cloudKind = new ComboBox { AutoSize = false };
        private readonly TextBox _endpoint = new TextBox();
        private readonly TextBox _remoteRoot = new TextBox();
        private readonly TextBox _userName = new TextBox();
        private readonly TextBox _webDavPassword = new TextBox { UseSystemPasswordChar = true };
        private readonly TextBox _accessKeyId = new TextBox();
        private readonly TextBox _s3Secret = new TextBox { UseSystemPasswordChar = true };
        private readonly TextBox _bucket = new TextBox();
        private readonly CheckBox _useSsl = new CheckBox { Text = "使用 HTTPS", AutoSize = true };
        private readonly Label _nasHint = new Label
        {
            AutoSize = false,
            ForeColor = Color.DimGray,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "NAS：快照会复制到该 UNC 路径，无需账号密码。"
        };

        private readonly CheckBox _notify = new CheckBox { Text = "云端保存失败时弹窗提醒", AutoSize = true };
        private readonly Label _summary = new Label { AutoSize = true, ForeColor = Color.DimGray };
        private readonly TextBox _documentId = new TextBox { ReadOnly = true };
        private readonly TextBox _documentAutoSave = new TextBox { ReadOnly = true };
        private readonly TextBox _documentAutoRecover = new TextBox { ReadOnly = true };

        private readonly ListBox _netdiskList = new ListBox { IntegralHeight = false, DisplayMember = "Name" };
        private readonly TextBox _netdiskName = new TextBox();
        private readonly TextBox _netdiskUrl = new TextBox();
        private readonly List<WebNetdiskLink> _links = new List<WebNetdiskLink>();
        private readonly Label _netdiskHint = new Label
        {
            AutoSize = false,
            ForeColor = Color.DimGray,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "点功能区「备份到网盘」：打开该网址，并在资源管理器里定位最新快照，你把文件拖进网页即可。"
        };

        private readonly IHostAdapter? _activeDocument;

        private readonly TableLayoutPanel _table = new TableLayoutPanel();
        private readonly System.Collections.Generic.List<int> _webDavRows = new System.Collections.Generic.List<int>();
        private readonly System.Collections.Generic.List<int> _s3Rows = new System.Collections.Generic.List<int>();
        private readonly System.Collections.Generic.List<int> _nasRows = new System.Collections.Generic.List<int>();

        public SettingsForm(AddInRuntime runtime, IHostAdapter? activeDocument)
        {
            _runtime = runtime;
            _activeDocument = activeDocument;

            Text = "文件自动保存 - 设置";
            Font = new Font("Microsoft YaHei UI", 10f);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(TableWidth + 40, 880);
            MinimumSize = new Size(TableWidth + 56, 560);
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;

            _table.Dock = DockStyle.Top;
            _table.Width = TableWidth;
            _table.AutoSize = true;
            _table.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _table.ColumnCount = 3;
            _table.Padding = new Padding(0);
            _table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LabelWidth));
            _table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, TableWidth - LabelWidth - TrailingWidth));
            _table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, TrailingWidth));

            BuildRows();

            var container = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(16), BackColor = SystemColors.Control };
            container.Controls.Add(_table);

            Controls.Add(container);
            Controls.Add(BuildButtonBar());

            LoadFromSettings();
            UpdateCloudFieldVisibility();
        }

        private void BuildRows()
        {
            _interval.Minimum = 1;
            _interval.Maximum = 60;

            _retention.Minimum = 1;
            _retention.Maximum = 500;

            var browse = new Button
            {
                Text = "浏览…",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(90, 34)
            };
            browse.Click += (_, __) => BrowseSnapshotsRoot();

            _cloudKind.DropDownStyle = ComboBoxStyle.DropDownList;
            _cloudKind.Items.AddRange(new object[]
            {
                "不使用云端",
                "WebDAV（坚果云 / Nextcloud / 群晖）",
                "S3 兼容（AWS / MinIO / 阿里云 OSS）",
                "NAS（UNC 共享路径）"
            });
            _cloudKind.SelectedIndexChanged += (_, __) => UpdateCloudFieldVisibility();

            AddHeader("自动保存（全局）");
            AddRow("间隔（分钟）", _interval);

            AddHeader("本地快照（全局）");
            AddRow("快照目录", _snapshotsRoot, browse);
            AddRow("每个文档保留数", _retention);

            AddHeader("云端保存（全局，同一时刻只有一个生效）");
            AddRow("云目标类型", _cloudKind);
            AddRow("地址 / 端点", _endpoint);
            AddRow("云端根目录", _remoteRoot);
            _webDavRows.Add(AddRow("用户名", _userName));
            _webDavRows.Add(AddRow("密码", _webDavPassword));
            _s3Rows.Add(AddRow("AccessKeyId", _accessKeyId));
            _s3Rows.Add(AddRow("SecretAccessKey", _s3Secret));
            _s3Rows.Add(AddRow("Bucket", _bucket));
            _s3Rows.Add(AddRow(string.Empty, _useSsl));
            _nasRows.Add(AddRow(string.Empty, _nasHint));

            AddHeader("网页网盘（手动上传：打开官网 + 定位最新快照）");
            AddRow("已配置", _netdiskList, BuildNetdiskButtons(), 156);
            AddRow("名称", _netdiskName);
            AddRow("网址", _netdiskUrl);
            AddRow(string.Empty, _netdiskHint);

            AddHeader("提醒");
            AddRow(string.Empty, _notify);

            AddHeader("当前文档（每文件，用功能区里的开关修改）");
            AddRow("文档标识", _documentId);
            AddRow("自动保存", _documentAutoSave);
            AddRow("自动恢复", _documentAutoRecover);
        }

        private void AddHeader(string text)
        {
            var row = _table.RowCount++;
            _table.RowStyles.Add(new RowStyle(SizeType.Absolute, HeaderHeight));
            _table.Controls.Add(new Label
            {
                Text = text,
                Font = new Font(Font, FontStyle.Bold),
                TextAlign = ContentAlignment.BottomLeft,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 6, 0, 0)
            }, 0, row);
            _table.SetColumnSpan(_table.GetControlFromPosition(0, row)!, 3);
        }

        private int AddRow(string label, Control control, Control? trailing = null, int height = RowHeight)
        {
            var row = _table.RowCount++;
            _table.RowStyles.Add(new RowStyle(SizeType.Absolute, height));

            if (!string.IsNullOrEmpty(label))
            {
                _table.Controls.Add(new Label
                {
                    Text = label,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Dock = DockStyle.Fill,
                    AutoSize = false
                }, 0, row);
            }

            // 按控件类型决定布局，避免"固定尺寸在字体缩放下被裁切"：
            //   长文本框 / 下拉框 → 拉满整格
            //   数字框           → 固定宽、垂直居中
            //   复选框 / 说明    → 自适应，垂直居中
            switch (control)
            {
                case NumericUpDown numeric:
                    numeric.Anchor = AnchorStyles.Left;
                    numeric.Width = 110;
                    break;
                case CheckBox checkBox:
                    checkBox.Anchor = AnchorStyles.Left;
                    break;
                case ComboBox combo:
                    combo.Dock = DockStyle.Fill;
                    break;
                default:
                    control.Dock = DockStyle.Fill;
                    break;
            }

            control.Margin = new Padding(0, 6, 8, 6);
            _table.Controls.Add(control, 1, row);

            if (trailing != null)
            {
                trailing.Anchor = AnchorStyles.Left;
                _table.Controls.Add(trailing, 2, row);
            }

            return row;
        }

        /// <summary>网页网盘那一行的添加/更新与删除按钮。</summary>
        private Control BuildNetdiskButtons()
        {
            var upsert = new Button { Text = "添加/更新", AutoSize = true, MinimumSize = new Size(96, 30) };
            var remove = new Button { Text = "删除", AutoSize = true, MinimumSize = new Size(96, 30) };
            upsert.Click += (_, __) => UpsertNetdisk();
            remove.Click += (_, __) => RemoveNetdisk();

            _netdiskList.SelectedIndexChanged += (_, __) =>
            {
                if (_netdiskList.SelectedItem is WebNetdiskLink link)
                {
                    _netdiskName.Text = link.Name;
                    _netdiskUrl.Text = link.Url;
                }
            };

            var panel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = false,
                Width = TrailingWidth
            };
            panel.Controls.Add(upsert);
            panel.Controls.Add(remove);
            return panel;
        }

        private void RefreshNetdisks()
        {
            _netdiskList.Items.Clear();
            foreach (var link in _links)
            {
                _netdiskList.Items.Add(link);
            }
        }

        private void UpsertNetdisk()
        {
            var candidate = new WebNetdiskLink { Name = _netdiskName.Text.Trim(), Url = _netdiskUrl.Text.Trim() };
            if (!candidate.IsUsable())
            {
                MessageBox.Show(this,
                    "请填写名称，且网址必须是 http:// 或 https:// 开头的完整网址。",
                    "文件自动保存", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var index = _links.FindIndex(l => l.Name == candidate.Name);
            if (index >= 0)
            {
                _links[index] = candidate;
            }
            else
            {
                _links.Add(candidate);
            }

            RefreshNetdisks();
            _netdiskList.SelectedIndex = index >= 0 ? index : _links.Count - 1;
        }

        private void RemoveNetdisk()
        {
            if (!(_netdiskList.SelectedItem is WebNetdiskLink link))
            {
                return;
            }

            _links.Remove(link);
            _netdiskName.Clear();
            _netdiskUrl.Clear();
            RefreshNetdisks();
        }

        private FlowLayoutPanel BuildButtonBar()        {
            var save = new Button
            {
                Text = "保存",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(100, 36)
            };
            var cancel = new Button
            {
                Text = "取消",
                DialogResult = DialogResult.Cancel,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(100, 36)
            };
            save.Click += (_, __) => SaveAndClose();

            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 68,
                Padding = new Padding(16, 16, 24, 16),
                WrapContents = false,
                AutoSize = false
            };
            bar.Controls.Add(save);
            bar.Controls.Add(cancel);
            _summary.Margin = new Padding(0, 8, 24, 0);
            bar.Controls.Add(_summary);

            CancelButton = cancel;
            return bar;
        }

        private void UpdateCloudFieldVisibility()
        {
            var kind = (CloudTargetKind)_cloudKind.SelectedIndex;
            SetRowsVisible(_webDavRows, kind == CloudTargetKind.WebDav);
            SetRowsVisible(_s3Rows, kind == CloudTargetKind.S3);
            SetRowsVisible(_nasRows, kind == CloudTargetKind.Nas);
        }

        private void SetRowsVisible(System.Collections.Generic.List<int> rows, bool visible)
        {
            foreach (var row in rows)
            {
                _table.RowStyles[row].Height = visible ? RowHeight : 0;
                for (var column = 0; column < _table.ColumnCount; column++)
                {
                    var control = _table.GetControlFromPosition(column, row);
                    if (control != null)
                    {
                        control.Visible = visible;
                    }
                }
            }
        }

        private void LoadFromSettings()
        {
            var settings = _runtime.Settings;
            var credential = _runtime.LoadCredential();

            _interval.Value = Math.Min(_interval.Maximum, Math.Max(_interval.Minimum, settings.AutoSaveIntervalMinutes));
            _snapshotsRoot.Text = settings.GetSnapshotsRoot();
            _retention.Value = Math.Min(_retention.Maximum, Math.Max(_retention.Minimum, settings.SnapshotRetention));
            _cloudKind.SelectedIndex = (int)settings.Cloud.Kind;
            _endpoint.Text = settings.Cloud.Endpoint;
            _remoteRoot.Text = settings.Cloud.RemoteRoot;
            _userName.Text = settings.Cloud.UserName;
            _bucket.Text = settings.Cloud.Bucket;
            _useSsl.Checked = settings.Cloud.UseSsl;
            _notify.Checked = settings.NotifyOnCloudFailure;
            _accessKeyId.Text = credential.AccessKeyId;
            _webDavPassword.Text = credential.Secret;
            _s3Secret.Text = credential.Secret;

            _links.Clear();
            _links.AddRange(settings.WebNetdisks ?? WebNetdiskLink.Defaults());
            RefreshNetdisks();

            _summary.Text = $"当前：{_runtime.ActiveDocumentCount} 个活跃文档，{_runtime.PendingUploads} 个待上传";

            RefreshDocumentInfo();
        }

        /// <summary>显示当前文档的唯一标识与两个每文件开关。</summary>
        private void RefreshDocumentInfo()
        {
            try
            {
                if (_activeDocument == null || !_activeDocument.IsSaved || string.IsNullOrEmpty(_activeDocument.FilePath))
                {
                    _documentId.Text = "（当前没有已保存的文档）";
                    _documentAutoSave.Text = "-";
                    _documentAutoRecover.Text = "-";
                    return;
                }

                var state = _runtime.GetState(_activeDocument);
                _documentId.Text = state?.DocumentId ?? "（尚未记录）";
                _documentAutoSave.Text = state?.AutoSaveEnabled == true ? "开" : "关";
                _documentAutoRecover.Text = state?.AutoRecoverEnabled == true ? "开" : "关";
            }
            catch (Exception)
            {
                _documentId.Text = "（读取失败）";
                _documentAutoSave.Text = "?";
                _documentAutoRecover.Text = "?";
            }
        }

        private void SaveAndClose()
        {
            var settings = _runtime.Settings;
            settings.AutoSaveIntervalMinutes = (int)_interval.Value;
            settings.SnapshotRetention = (int)_retention.Value;
            settings.SnapshotsRoot = _snapshotsRoot.Text.Trim();
            settings.NotifyOnCloudFailure = _notify.Checked;
            settings.Cloud.Kind = (CloudTargetKind)_cloudKind.SelectedIndex;
            settings.Cloud.Endpoint = _endpoint.Text.Trim();
            settings.Cloud.RemoteRoot = _remoteRoot.Text.Trim();
            settings.Cloud.UserName = _userName.Text.Trim();
            settings.Cloud.Bucket = _bucket.Text.Trim();
            settings.Cloud.UseSsl = _useSsl.Checked;
            settings.WebNetdisks = new List<WebNetdiskLink>(_links);

            _runtime.SaveSettings(settings);
            _runtime.SaveCredential(new CloudCredential
            {
                AccessKeyId = _accessKeyId.Text.Trim(),
                Secret = settings.Cloud.Kind == CloudTargetKind.S3 ? _s3Secret.Text : _webDavPassword.Text
            });

            DialogResult = DialogResult.OK;
            Close();
        }

        private void BrowseSnapshotsRoot()
        {
            using (var dialog = new FolderBrowserDialog { SelectedPath = _snapshotsRoot.Text })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _snapshotsRoot.Text = dialog.SelectedPath;
                }
            }
        }
    }
}
