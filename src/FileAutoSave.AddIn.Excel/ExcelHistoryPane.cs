using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FileAutoSave.Core;
using FileAutoSave.Core.Hosting;
using FileAutoSave.Core.Ui;
using NetOffice.OfficeApi;
using NetOffice.OfficeApi.Enums;
using NetOffice.ExcelApi;

namespace FileAutoSave.AddIn.Excel
{
    /// <summary>版本历史的文档侧边栏。内容惰性创建——创建时机早于 OnStartupComplete。</summary>
    [ComVisible(true)]
    [Guid("6B1F2A40-1002-4E1B-9C3A-0F1E2D3C4B5A")]
    public class ExcelHistoryPane : UserControl, NetOffice.ExcelApi.Tools.ITaskPane
    {
        private NetOffice.ExcelApi.Application? _application;
        private HistoryView? _view;

        public void OnConnection(NetOffice.ExcelApi.Application application, _CustomTaskPane parentPane, object[] customArguments)
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

            var runtime = ExcelConnect.SharedRuntime;
            if (runtime == null)
            {
                return;
            }

            Workbook? workbook = null;
            try
            {
                workbook = _application?.ActiveWorkbook;
            }
            catch (Exception ex)
            {
                Diagnostics.LogException("Excel: 侧边栏取 ActiveWorkbook", ex);
            }

            if (workbook == null)
            {
                return;
            }

            try
            {
                _view = new HistoryView(runtime, new ExcelHostAdapter(workbook)) { Dock = DockStyle.Fill };
                Controls.Add(_view);
            }
            catch (Exception ex)
            {
                Diagnostics.LogException("Excel: 侧边栏创建内容", ex);
            }
        }
    }
}
