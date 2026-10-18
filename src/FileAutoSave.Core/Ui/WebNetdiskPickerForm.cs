using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using FileAutoSave.Core.Settings;

namespace FileAutoSave.Core.Ui
{
    /// <summary>
    /// 「备份到网盘」的选择窗口：挑一个网页网盘，然后由 <see cref="Hosting.RibbonActions"/>
    /// 打开浏览器并定位快照。本窗口只做选择，不碰网盘，也不做上传。
    /// </summary>
    internal sealed class WebNetdiskPickerForm : Form
    {
        private readonly ListBox _list = new ListBox { IntegralHeight = false, DisplayMember = "Name" };

        /// <summary>用户选中的入口；取消时为 null。</summary>
        public WebNetdiskLink? Selected => _list.SelectedItem as WebNetdiskLink;

        public WebNetdiskPickerForm(IReadOnlyList<WebNetdiskLink> links, string snapshotFileName)
        {
            Text = "备份到网页网盘";
            Font = new Font("Microsoft YaHei UI", 10f);
            AutoScaleMode = AutoScaleMode.Font;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(520, 340);

            foreach (var link in links)
            {
                _list.Items.Add(link);
            }

            _list.SelectedIndex = 0;
            _list.MouseDoubleClick += (_, __) => { DialogResult = DialogResult.OK; Close(); };
            _list.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    DialogResult = DialogResult.OK;
                    Close();
                }
            };

            var title = new Label
            {
                Dock = DockStyle.Top,
                Height = 62,
                Padding = new Padding(14, 12, 14, 0),
                Text = "选一个网盘。确认后会打开它的网页，并在资源管理器里定位最新快照：" +
                       Environment.NewLine + snapshotFileName
            };

            var hint = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 56,
                Padding = new Padding(14, 6, 14, 0),
                ForeColor = Color.DimGray,
                Text = "把资源管理器里的文件拖进网页的上传区即可完成备份。" +
                       Environment.NewLine + "我们不替你上传——网盘账号和登录状态都在你自己的浏览器里。"
            };

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 56,
                Padding = new Padding(10, 8, 12, 8),
                WrapContents = false
            };

            var open = new Button { Text = "打开并定位", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(120, 32) };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(90, 32) };
            buttons.Controls.Add(open);
            buttons.Controls.Add(cancel);

            _list.Dock = DockStyle.Fill;

            Controls.Add(_list);
            Controls.Add(hint);
            Controls.Add(buttons);
            Controls.Add(title);

            AcceptButton = open;
            CancelButton = cancel;
        }
    }
}
