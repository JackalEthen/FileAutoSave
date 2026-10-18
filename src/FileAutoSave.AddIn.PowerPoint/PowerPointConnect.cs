using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FileAutoSave.Core;
using FileAutoSave.Core.Hosting;
using FileAutoSave.Core.Ui;
using NetOffice.OfficeApi;
using NetOffice.PowerPointApi;
using NetOffice.PowerPointApi.Tools;
using NetOffice.Tools;

namespace FileAutoSave.AddIn.PowerPoint
{
    [ComVisible(true)]
    [ProgId("FileAutoSave.AddIn.PowerPoint.Connect")]
    [Guid("6B1F2A40-0003-4E1B-9C3A-0F1E2D3C4B5A")]
    [COMAddin("FileAutoSave", "文件自动保存", LoadBehavior.LoadAtStartup)]
    [CustomPane(typeof(PowerPointHistoryPane), "版本历史", false,
        PaneDockPosition.msoCTPDockPositionRight, PaneDockPositionRestrict.msoCTPDockPositionRestrictNoChange, 420, -1)]
    public class PowerPointConnect : COMAddin
    {
        /// <summary>侧边栏需要拿到共享运行时；由本类在启动/卸载时维护。</summary>
        internal static AddInRuntime? SharedRuntime { get; private set; }

        private readonly HashSet<string> _attached = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Timer _rescanTimer = new Timer { Interval = 15000 };

        private UiDispatcher? _dispatcher;
        private AddInRuntime? _runtime;
        private RibbonActions? _actions;
        private IRibbonUI? _ribbon;

        public PowerPointConnect()
        {
            var location = typeof(PowerPointConnect).Assembly.Location;
            OnStartupComplete += Addin_OnStartupComplete;
            OnDisconnection += Addin_OnDisconnection;
            _rescanTimer.Tick += (_, __) => Sync();
        }

        public override string GetCustomUI(string RibbonID)
        {
            return RibbonXml;
        }

        public object? GetImage(string imageName)
        {
            var picture = PictureConverter.ToPictureDisp(RibbonIcons.Load(imageName));
            return picture;
        }

        public void OnRibbonLoad(IRibbonUI ribbon)
        {
            _ribbon = ribbon;
        }

        public bool GetAutoSavePressed(IRibbonControl control)
        {
            var pressed = _actions?.GetAutoSaveEnabled(ActiveDocumentAdapter()) ?? false;
            return pressed;
        }

        public bool GetAutoRecoverPressed(IRibbonControl control)
        {
            var pressed = _actions?.GetAutoRecoverEnabled(ActiveDocumentAdapter()) ?? false;
            return pressed;
        }

        public void OnToggleAutoSave(IRibbonControl control, bool pressed)
        {
            _actions?.SetAutoSave(ActiveDocumentAdapter(), pressed);
            _ribbon?.InvalidateControl("btnAutoSave");
        }

        public void OnToggleAutoRecover(IRibbonControl control, bool pressed)
        {
            _actions?.SetAutoRecover(ActiveDocumentAdapter(), pressed);
            _ribbon?.InvalidateControl("btnAutoRecover");
        }

        public void OnShowSettings(IRibbonControl control)
        {
            _actions?.OpenSettings(ActiveDocumentAdapter());
        }

        public void OnBackupToWeb(IRibbonControl control)
        {
            _actions?.BackupToWebNetdisk(ActiveDocumentAdapter());
        }

        /// <summary>当前活动演示文稿的适配器（设置窗口要显示它的文档标识）。</summary>
        private PowerPointHostAdapter? ActiveDocumentAdapter()
        {
            try
            {
                var presentation = Application.ActivePresentation;
                return presentation == null ? null : new PowerPointHostAdapter(presentation);
            }
            catch (Exception)
            {
                return null;
            }
        }

        

        public void OnShowHistoryPane(IRibbonControl control)
        {
            if (TaskPanes.Count == 0)
            {
                return;
            }

            TaskPanes[0].Visible = true;
        }

        

        private void Addin_OnStartupComplete(ref Array custom)
        {
            try
            {
                _dispatcher = new UiDispatcher();
                _runtime = new AddInRuntime(Notify);
                _actions = new RibbonActions(_runtime);
                SharedRuntime = _runtime;

                Application.PresentationOpenEvent += Application_PresentationOpenEvent;
                Application.NewPresentationEvent += Application_NewPresentationEvent;
                Application.PresentationBeforeCloseEvent += Application_PresentationBeforeCloseEvent;
                Application.PresentationBeforeSaveEvent += Application_PresentationBeforeSaveEvent;
                Application.WindowActivateEvent += Application_WindowActivateEvent;

                Sync();
                CheckDocumentNotice();
                _rescanTimer.Start();
            }
            catch (Exception ex)
            {
                Diagnostics.LogException("PowerPoint: OnStartupComplete", ex);
                Notify("加载项启动失败：" + ex.Message);
            }
        }

        private void Addin_OnDisconnection(ext_DisconnectMode removeMode, ref Array custom)
        {
            _rescanTimer.Stop();
            try
            {
                Application.PresentationOpenEvent -= Application_PresentationOpenEvent;
                Application.NewPresentationEvent -= Application_NewPresentationEvent;
                Application.PresentationBeforeCloseEvent -= Application_PresentationBeforeCloseEvent;
                Application.PresentationBeforeSaveEvent -= Application_PresentationBeforeSaveEvent;
                Application.WindowActivateEvent -= Application_WindowActivateEvent;
            }
            catch (Exception)
            {
            }

            _runtime?.Dispose();
            _runtime = null;
            SharedRuntime = null;
        }

        private void Application_PresentationOpenEvent(Presentation presentation)
        {
            Sync();
            CheckDocumentNotice();
        }

        private void Application_NewPresentationEvent(Presentation presentation)
        {
            Sync();
            CheckDocumentNotice();
        }

        private void Application_WindowActivateEvent(Presentation presentation, DocumentWindow window)
        {
            Sync();
            CheckDocumentNotice();
        }

        private void Application_PresentationBeforeSaveEvent(Presentation presentation, ref bool cancel)
        {
            Sync();
        }

        private void Application_PresentationBeforeCloseEvent(Presentation presentation, ref bool cancel)
        {
            var id = IdOf(presentation);
            if (id == null)
            {
                return;
            }

            _attached.Remove(id);
            _runtime?.Detach(id);
        }

        private void Sync()
        {
            if (_runtime == null)
            {
                return;
            }

            try
            {
                var found = 0;
                var attached = 0;
                foreach (Presentation presentation in Application.Presentations)
                {
                    found++;
                    var id = IdOf(presentation);
                    if (id == null || !_attached.Add(id))
                    {
                        continue;
                    }

                    attached++;
                    _runtime.Attach(id, new PowerPointHostAdapter(presentation));
                }

                _runtime.Tick();
            }
            catch (Exception ex)
            {
                Diagnostics.LogException("PowerPoint: Sync", ex);
            }
        }

                /// <summary>打开/新建/切换文档时的提示：未保存→先另存为；已保存→可开启开关。</summary>
        private void CheckDocumentNotice()
        {
            if (_actions == null)
            {
                return;
            }

            try
            {
                var presentation = Application.ActivePresentation;
                if (presentation == null)
                {
                    return;
                }

                var adapter = new PowerPointHostAdapter(presentation);
                if (adapter.IsSaved)
                {
                    _actions.NotifyDocumentOpened(adapter.FilePath!);
                }
                else
                {
                    _actions.NotifyUnsaved(presentation.Name);
                }
            }
            catch (Exception)
            {
            }
        }

        private static string? IdOf(Presentation presentation)
        {
            var path = new PowerPointHostAdapter(presentation).FilePath;
            return string.IsNullOrEmpty(path) ? null : path;
        }

        private void Notify(string message) =>
            _dispatcher?.Post(() => MessageBox.Show(message, "文件自动保存", MessageBoxButtons.OK, MessageBoxIcon.Information));

        private const string RibbonXml = @"<customUI xmlns=""http://schemas.microsoft.com/office/2009/07/customui"" loadImage=""GetImage"" onLoad=""OnRibbonLoad"">
  <ribbon>
    <tabs>
      <tab idMso=""TabHome"">
        <group id=""FileAutoSaveGroup"" label=""文件自动保存"">
          <toggleButton id=""btnAutoSave"" label=""自动保存"" size=""large"" image=""save"" onAction=""OnToggleAutoSave"" getPressed=""GetAutoSavePressed"" screentip=""按设定间隔用当前内容覆盖原文件""/>
          <toggleButton id=""btnAutoRecover"" label=""自动恢复"" size=""large"" image=""rotate"" onAction=""OnToggleAutoRecover"" getPressed=""GetAutoRecoverPressed"" screentip=""按设定间隔记录可回滚的快照""/>
          <button id=""btnHistory"" label=""版本历史"" size=""large"" image=""history"" onAction=""OnShowHistoryPane"" screentip=""在文档右侧打开版本历史侧边栏""/>
          <button id=""btnBackupWeb"" label=""备份到网盘"" size=""large"" image=""cloud"" onAction=""OnBackupToWeb"" screentip=""打开网盘网页并定位最新快照，手动拖拽上传""/>
          <button id=""btnSettings"" label=""设置"" size=""large"" image=""settings"" onAction=""OnShowSettings"" screentip=""间隔、快照与云端设置""/>
        </group>
      </tab>
    </tabs>
  </ribbon>
</customUI>";
    }
}
