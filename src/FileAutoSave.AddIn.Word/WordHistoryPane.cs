using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FileAutoSave.Core;
using FileAutoSave.Core.Hosting;
using FileAutoSave.Core.Ui;
using NetOffice.OfficeApi;
using NetOffice.OfficeApi.Enums;
using NetOffice.WordApi;

namespace FileAutoSave.AddIn.Word
{
    /// <summary>
    /// 版本历史的文档侧边栏。宿主按类型全名当 ProgID 创建它，所以不要自定义 <c>[ProgId]</c>。
    /// </summary>
    /// <remarks>
    /// Office 在 CTPFactoryAvailable 阶段就创建这个控件，**早于 OnStartupComplete**，
    /// 那时共享运行时还没建立。所以内容必须**惰性创建**：等到可见/停靠变化时再建。
    /// </remarks>
    [ComVisible(true)]
    [Guid("6B1F2A40-1001-4E1B-9C3A-0F1E2D3C4B5A")]
    public class WordHistoryPane : UserControl, NetOffice.WordApi.Tools.ITaskPane
    {
        private NetOffice.WordApi.Application? _application;
        private HistoryView? _view;

        public void OnConnection(NetOffice.WordApi.Application application, _CustomTaskPane parentPane, object[] customArguments)
        {
            _application = application;
            EnsureView();
        }

        public void OnDisconnection()
        {
        }

        public void OnDockPositionChanged(MsoCTPDockPosition position) => EnsureView();

        public void OnVisibleStateChanged(bool visible)
        {
            if (visible)
            {
                EnsureView();
            }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible)
            {
                EnsureView();
            }
        }

        private void EnsureView()
        {
            if (_view != null)
            {
                return;
            }

            var runtime = WordConnect.SharedRuntime;
            if (runtime == null)
            {
                return;
            }

            Document? document = null;
            try
            {
                document = _application?.ActiveDocument;
            }
            catch (Exception ex)
            {
                Diagnostics.LogException("Word: 侧边栏取 ActiveDocument", ex);
            }

            if (document == null)
            {
                return;
            }

            try
            {
                _view = new HistoryView(runtime, new WordHostAdapter(document)) { Dock = DockStyle.Fill };
                Controls.Add(_view);
            }
            catch (Exception ex)
            {
                Diagnostics.LogException("Word: 侧边栏创建内容", ex);
            }
        }
    }
}
