using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FileAutoSave.Core;
using FileAutoSave.Core.Hosting;
using FileAutoSave.Core.Ui;
using NetOffice.OfficeApi;
using NetOffice.OfficeApi.Enums;
using NetOffice.PowerPointApi;

namespace FileAutoSave.AddIn.PowerPoint
{
    /// <summary>版本历史的文档侧边栏。内容惰性创建——创建时机早于 OnStartupComplete。</summary>
    [ComVisible(true)]
    [Guid("6B1F2A40-1003-4E1B-9C3A-0F1E2D3C4B5A")]
    public class PowerPointHistoryPane : UserControl, NetOffice.PowerPointApi.Tools.ITaskPane
    {
        private NetOffice.PowerPointApi.Application? _application;
        private HistoryView? _view;

        public void OnConnection(NetOffice.PowerPointApi.Application application, _CustomTaskPane parentPane, object[] customArguments)
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

            var runtime = PowerPointConnect.SharedRuntime;
            if (runtime == null)
            {
                return;
            }

            Presentation? presentation = null;
            try
            {
                presentation = _application?.ActivePresentation;
            }
            catch (Exception ex)
            {
                Diagnostics.LogException("PowerPoint: 侧边栏取 ActivePresentation", ex);
            }

            if (presentation == null)
            {
                return;
            }

            try
            {
                _view = new HistoryView(runtime, new PowerPointHostAdapter(presentation)) { Dock = DockStyle.Fill };
                Controls.Add(_view);
            }
            catch (Exception ex)
            {
                Diagnostics.LogException("PowerPoint: 侧边栏创建内容", ex);
            }
        }
    }
}
