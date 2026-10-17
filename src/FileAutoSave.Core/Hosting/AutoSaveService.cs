using System;
using System.IO;
using System.Windows.Forms;
using FileAutoSave.Core.Cloud;
using FileAutoSave.Core.Settings;
using FileAutoSave.Core.Snapshots;

namespace FileAutoSave.Core.Hosting
{
    /// <summary>
    /// 单个文档的自动保存 / 自动恢复编排。
    /// 两个开关是**每文件状态**（<see cref="DocumentState"/>）、互相独立、默认关闭：
    ///   - 自动保存：间隔到点用当前内容覆盖原文件；
    ///   - 自动恢复：内容发生变化就记一份快照（含每次手动保存）。
    /// </summary>
    /// <remarks>
    /// 必须在宿主的 UI 线程上构造：内部用 WinForms 定时器，Tick 在创建它的线程上触发，
    /// 从而保证所有对 Office 对象的调用都发生在宿主主线程，避免跨线程访问 COM 崩溃。
    /// </remarks>
    public sealed class AutoSaveService : IDisposable
    {
        private readonly IHostAdapter _adapter;
        private AddInSettings _settings;
        private readonly SnapshotStore _store;
        private ICloudUploadSink? _cloud;
        private readonly Action<string>? _notify;
        private readonly Timer _timer;

        private DocumentState _state = null!;
        private DateTime _lastSeenWriteUtc;
        private bool _disposed;

        public AutoSaveService(
            IHostAdapter adapter,
            DocumentState state,
            AddInSettings settings,
            SnapshotStore store,
            ICloudUploadSink? cloud = null,
            Action<string>? notify = null)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _cloud = cloud;
            _notify = notify;

            _timer = new Timer { Interval = (int)_settings.GetInterval().Value.TotalMilliseconds };
            _timer.Tick += OnTick;

            var path = adapter.FilePath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                _lastSeenWriteUtc = File.GetLastWriteTimeUtc(path);
            }

        }

        public DocumentState State => _state;

        public void Start()
        {
            if (!_disposed)
            {
                _timer.Start();
            }
        }

        public void Stop() => _timer.Stop();

        public void SetInterval(AutoSaveInterval interval) =>
            _timer.Interval = (int)interval.Value.TotalMilliseconds;

        /// <summary>运行期改动全局设置或云目标时调用。</summary>
        public void Reconfigure(AddInSettings settings, ICloudUploadSink? cloud)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _cloud = cloud;
            SetInterval(settings.GetInterval());
        }

        /// <summary>外部（功能区开关）改动该文档状态时调用。</summary>
        public void SetDocumentState(DocumentState state)
        {
            var recoverJustEnabled = !_state.AutoRecoverEnabled && state.AutoRecoverEnabled;
            _state = state ?? throw new ArgumentNullException(nameof(state));

            if (recoverJustEnabled)
            {
                // 刚打开自动恢复：立刻给一个起点快照，用户马上能看到历史。
                TrySnapshot();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer.Stop();
            _timer.Tick -= OnTick;
            _timer.Dispose();
        }

        private void OnTick(object? sender, EventArgs e) => RunOnce();

        /// <summary>间隔到点：自动保存覆盖原文件；自动恢复记快照。</summary>
        public void RunOnce()
        {
            try
            {
                RunOnceCore();
            }
            catch (Exception ex)
            {
                Diagnostics.LogException($"{_adapter.HostName}: RunOnce", ex);
                _notify?.Invoke($"{_adapter.HostName} 自动保存出错：{ex.Message}");
            }
        }

        /// <summary>
        /// 轻量检查：内容在磁盘上变了就记快照。宿主侧 15 秒重扫时调用，
        /// 用来捕捉"手动保存"（自动保存关闭时历史也不会漏）。
        /// </summary>
        public void CheckForChanges()
        {
            try
            {
                if (_disposed || !_state.AutoRecoverEnabled)
                {
                    return;
                }

                var path = _adapter.FilePath;
                if (!_adapter.IsSaved || string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    return;
                }

                if (File.GetLastWriteTimeUtc(path) == _lastSeenWriteUtc)
                {
                    return;
                }

                TrySnapshot();
            }
            catch (Exception ex)
            {
                Diagnostics.LogException($"{_adapter.HostName}: CheckForChanges", ex);
            }
        }

        private void RunOnceCore()
        {
            if (!_state.AutoSaveEnabled && !_state.AutoRecoverEnabled)
            {
                return;
            }

            if (!_adapter.IsSaved || string.IsNullOrEmpty(_adapter.FilePath))
            {
                return;
            }

                        var path = _adapter.FilePath!;

            if (_state.AutoSaveEnabled)
            {
                try
                {
                    _adapter.Save();
                }
                catch (Exception ex)
                {
                    Diagnostics.LogException($"{_adapter.HostName}: 覆盖原文件 {path}", ex);
                    _notify?.Invoke($"原文件无法写入（{ex.Message}），本次未覆盖原文件。");
                }
            }

            if (_state.AutoRecoverEnabled)
            {
                TrySnapshot();
            }
        }

        private SnapshotInfo? TrySnapshot()
        {
            var path = _adapter.FilePath;
            if (!_adapter.IsSaved || string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return null;
            }

            _lastSeenWriteUtc = File.GetLastWriteTimeUtc(path);

            var projection = SafeProjection();
            var snapshot = _store.Save(path!, projection);

            if (snapshot == null)
            {
                return null;
            }


            if (_cloud != null)
            {
                _cloud.Enqueue(
                    _store.ResolvePath(path!, snapshot),
                    _store.DirectoryName(path!) + "/" + snapshot.FileName);
            }

            return snapshot;
        }

        private string? SafeProjection()
        {
            try
            {
                return _adapter.ExtractTextProjection();
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
