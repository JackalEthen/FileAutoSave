using System;
using System.IO;
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
    /// 必须在宿主的 UI 线程上使用：所有对 Office 对象的调用都发生在宿主主线程，避免跨线程访问 COM 崩溃。
    /// 本类**不持有定时器**——由宿主外壳的心跳（<see cref="Pump"/>）驱动，
    /// 20 个打开的文档也只有 1 个定时器。
    /// </remarks>
    public sealed class AutoSaveService : IDisposable
    {
        private readonly IHostAdapter _adapter;
        private AddInSettings _settings;
        private readonly SnapshotStore _store;
        private ICloudUploadSink? _cloud;
        private readonly Action<string>? _notify;

        private DocumentState _state = null!;
        private DateTime _lastSeenWriteUtc;

        /// <summary>下次该跑自动保存的时刻。按文档各自记录，到期时间天然错开。</summary>
        private DateTime _nextDueUtc;

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

            _nextDueUtc = DateTime.UtcNow + settings.GetInterval().Value;

            var path = adapter.FilePath;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                _lastSeenWriteUtc = File.GetLastWriteTimeUtc(path);
            }

        }

        public DocumentState State => _state;

        /// <summary>
        /// 宿主心跳（外壳那个 15 秒定时器）：到点就跑一次自动保存，顺带查一次磁盘变化。
        /// 早先这里是"每个文档一个定时器"，开十几个文档就十几个定时器同时醒；
        /// 改成共享心跳 + 每文档记住下次到期时间后，定时器回到 1 个，
        /// 而且到期时间依旧各自错开，不会齐步走。
        /// </summary>
        public void Pump()
        {
            if (_disposed)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if (now >= _nextDueUtc)
            {
                _nextDueUtc = now + _settings.GetInterval().Value;
                RunOnce();
            }

            CheckForChanges();
        }

        /// <summary>运行期改动全局设置或云目标时调用。</summary>
        public void Reconfigure(AddInSettings settings, ICloudUploadSink? cloud)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _cloud = cloud;

            // 按新间隔重新起算，否则从 60 分钟改到 1 分钟要干等一整轮。
            _nextDueUtc = DateTime.UtcNow + _settings.GetInterval().Value;
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
        }

        /// <summary>
        /// 关闭文档 / 卸载加载项前补最后一次快照。
        /// </summary>
        /// <remarks>
        /// 为什么必须有这一步：内容变化是靠 15 秒轮询发现的，而轮询只在文档还挂着时跑。
        /// 用户在轮询窗口内手动保存、然后立刻关闭文档，那次保存就永远不会被发现——
        /// 下次打开时基线时间已被重置成当前时间，改动凭空消失。
        /// 快照内容直接来自磁盘文件，所以这里不需要碰 COM，卸载时也安全（投影故意跳过）。
        /// 重复内容由 <see cref="SnapshotStore.Save"/> 的哈希去重挡掉。
        /// </remarks>
        public void SnapshotOnDetach()
        {
            try
            {
                if (_disposed || !_state.AutoRecoverEnabled)
                {
                    return;
                }

                Snapshot(withProjection: false);
            }
            catch (Exception ex)
            {
                Diagnostics.LogException($"{_adapter.HostName}: 关闭前补快照", ex);
            }
        }

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

        private SnapshotInfo? TrySnapshot() => Snapshot(withProjection: true);

        private SnapshotInfo? Snapshot(bool withProjection)
        {
            var path = _adapter.FilePath;
            if (!_adapter.IsSaved || string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return null;
            }

            _lastSeenWriteUtc = File.GetLastWriteTimeUtc(path);

            var projection = withProjection ? SafeProjection() : null;
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
