using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FileAutoSave.Core;
using FileAutoSave.Core.Hosting;
using FileAutoSave.Core.Ui;
using NetOffice.OfficeApi;
using NetOffice.Tools;
using NetOffice.WordApi;
using NetOffice.WordApi.Tools;

namespace FileAutoSave.AddIn.Word
{
    [ComVisible(true)]
    [ProgId("FileAutoSave.AddIn.Word.Connect")]
    [Guid("6B1F2A40-0001-4E1B-9C3A-0F1E2D3C4B5A")]
    [COMAddin("FileAutoSave", "文件自动保存", LoadBehavior.LoadAtStartup)]
    [CustomPane(typeof(WordHistoryPane), "版本历史", false,
        PaneDockPosition.msoCTPDockPositionRight, PaneDockPositionRestrict.msoCTPDockPositionRestrictNoChange, 420, -1)]
    public class WordConnect : COMAddin
    {
        /// <summary>侧边栏需要拿到共享运行时；由本类在启动/卸载时维护。</summary>
        internal static AddInRuntime? SharedRuntime { get; private set; }

private readonly HashSet<string> _attached = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string? _lastActiveId;
        private readonly Timer _rescanTimer = new Timer { Interval = AddInRuntime.HeartbeatIntervalMs };

        private UiDispatcher? _dispatcher;
        private AddInRuntime? _runtime;
        private RibbonActions? _actions;
        private NetOffice.OfficeApi.Native.IRibbonUI? _ribbon;

        public WordConnect()
        {
            var location = typeof(WordConnect).Assembly.Location;
            OnStartupComplete += Addin_OnStartupComplete;
            OnDisconnection += Addin_OnDisconnection;
            // Word 没有"保存后"事件；另存为后文档才拿到路径，靠定时重扫兜底挂载。
            _rescanTimer.Tick += (_, __) =>
            {
                Sync();
                CheckDocumentNotice();
            };
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

/// <param name="ribbon">
        /// 必须是 <see cref="NetOffice.OfficeApi.Native.IRibbonUI"/>（接口）。
        /// 同名空间下还有一个 <c>NetOffice.OfficeApi.IRibbonUI</c>，但那是**包装类**不是接口，
        /// 参数用它 Office 会静默不调用 onLoad —— 于是 _ribbon 永远为 null，
        /// 按钮停在第一次 getPressed 读到的假 false 上再也刷不掉。
        /// </param>
        public void OnRibbonLoad(NetOffice.OfficeApi.Native.IRibbonUI ribbon)
        {
            _ribbon = ribbon;

            // Office 第一次调 getPressed 时 _actions 往往还没建好，返回的是
            // "加载中"的假 false。功能区一就绪就无条件重画一次，把磁盘上的真值刷上去；
            // 同时把 _lastActiveId 清掉，好让下一次 Sync 还能再刷一次。
            _lastActiveId = null;
            ribbon.InvalidateControl("btnAutoSave");
            ribbon.InvalidateControl("btnAutoRecover");
        }

public bool GetAutoSavePressed(IRibbonControl control)
        {
            return _actions?.GetAutoSaveEnabled(ActiveDocumentAdapter()) ?? false;
        }

        public bool GetAutoRecoverPressed(IRibbonControl control)
        {
            return _actions?.GetAutoRecoverEnabled(ActiveDocumentAdapter()) ?? false;
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

        /// <summary>当前活动文档的适配器（设置窗口要显示它的文档标识）。</summary>
        private WordHostAdapter? ActiveDocumentAdapter()
        {
            try
            {
                var document = Application.ActiveDocument;
                return document == null ? null : new WordHostAdapter(document);
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


                Application.DocumentOpenEvent += Application_DocumentOpenEvent;
                Application.NewDocumentEvent += Application_NewDocumentEvent;
                Application.DocumentBeforeCloseEvent += Application_DocumentBeforeCloseEvent;
                Application.DocumentBeforeSaveEvent += Application_DocumentBeforeSaveEvent;
                Application.WindowActivateEvent += Application_WindowActivateEvent;

                Sync();
                CheckDocumentNotice();
                _rescanTimer.Start();
            }
            catch (Exception ex)
            {
                Diagnostics.LogException("Word: OnStartupComplete", ex);
                Notify("加载项启动失败：" + ex.Message);
            }
        }

        private void Addin_OnDisconnection(ext_DisconnectMode removeMode, ref Array custom)
        {
            _rescanTimer.Stop();
            try
            {
                Application.DocumentOpenEvent -= Application_DocumentOpenEvent;
                Application.NewDocumentEvent -= Application_NewDocumentEvent;
                Application.DocumentBeforeCloseEvent -= Application_DocumentBeforeCloseEvent;
                Application.DocumentBeforeSaveEvent -= Application_DocumentBeforeSaveEvent;
                Application.WindowActivateEvent -= Application_WindowActivateEvent;
            }
            catch (Exception)
            {
            }

            _runtime?.Dispose();
            _runtime = null;
            SharedRuntime = null;
        }

        private void Application_DocumentOpenEvent(Document document)
        {
            Sync();
            CheckDocumentNotice();
        }

        private void Application_NewDocumentEvent(Document document)
        {
            Sync();
            }

        private void Application_WindowActivateEvent(Document document, Window window)
        {
            Sync();
            }

        private void Application_DocumentBeforeSaveEvent(Document document, ref bool saveAsUI, ref bool cancel)
        {
            Sync();
        }

        private void Application_DocumentBeforeCloseEvent(Document document, ref bool cancel)
        {
            var id = IdOf(document);
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
                foreach (Document document in Application.Documents)
                {
                    found++;
                    var id = IdOf(document);
                    if (id == null || !_attached.Add(id))
                    {
                        continue;
                    }

                    attached++;
                    _runtime.Attach(id, new WordHostAdapter(document));
                }

_runtime.Tick();
                RefreshToggles();
            }
            catch (Exception ex)
            {
                Diagnostics.LogException("Word: Sync", ex);
            }
        }

        /// <summary>
        /// 当前文档换了就把两个开关重画一遍，让它们显示的是**这个文档**存下来的状态。
        /// </summary>
        /// <remarks>
        /// Office 会缓存 getPressed 的结果。之前只在用户自己点开关时 Invalidate，
        /// 于是打开文档、切换文档、启动时恢复文档，功能区都还挂着上一个文档的状态——
        /// 这正是"状态不同步"的根源。
        /// 只在活动文档真的变了才重画，避免 15 秒心跳把功能区刷得一直闪。
        /// </remarks>
        private void RefreshToggles()
        {
            if (_ribbon == null)
            {
                return;
            }

            string? activeId;
            try
            {
                activeId = IdOf(Application.ActiveDocument);
            }
            catch (Exception)
            {
                return;
            }

            if (activeId == null || string.Equals(activeId, _lastActiveId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _lastActiveId = activeId;
            _ribbon.InvalidateControl("btnAutoSave");
            _ribbon.InvalidateControl("btnAutoRecover");
        }

/// <summary>打开/新建/切换文档时的提示：未保存→先另存为；已保存→可开启开关。</summary>
        /// <remarks>
        /// 新建与窗口切换**不在这里弹**：新建事件触发时文档还没建好，
        /// 提示会显得莫名其妙；切换窗口反复弹更吵。两者都交给 15 秒心跳，
        /// 那时文档一定已经建好。只有"打开已有文件"保持即时提示。
        /// </remarks>
        private void CheckDocumentNotice()
        {
            if (_actions == null || _runtime == null)
            {
                return;
            }

            try
            {
                var document = Application.ActiveDocument;
                if (document == null)
                {
                    return;
                }

                var adapter = new WordHostAdapter(document);
                if (adapter.IsSaved)
                {
                    // 用户已经就这个文档表过态（开或关都算）就不再打扰。
                    if (_runtime.GetState(adapter)?.SwitchesConfigured == true)
                    {
                        return;
                    }

                    _actions.NotifyDocumentOpened(adapter.FilePath!);
                }
                else
                {
                    _actions.NotifyUnsaved(document.Name);
                }
            }
            catch (Exception)
            {
            }
        }

        private static string? IdOf(Document document)
        {
            var path = new WordHostAdapter(document).FilePath;
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
