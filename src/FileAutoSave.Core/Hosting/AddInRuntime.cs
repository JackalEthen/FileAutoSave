using System;
using System.Collections.Generic;
using System.Linq;
using FileAutoSave.Core.Cloud;
using FileAutoSave.Core.Security;
using FileAutoSave.Core.Settings;
using FileAutoSave.Core.Snapshots;

namespace FileAutoSave.Core.Hosting
{
/// <summary>
        /// 三个宿主共用的运行时：持有全局设置、快照库、上传队列，
        /// 并为每个打开的文档挂一个 <see cref="AutoSaveService"/>，各自带**每文件**的开关状态。
        /// </summary>
        /// <remarks>
        /// 必须在宿主 UI 线程上构造与调用。
        /// 本类**不持有定时器**：节拍由宿主外壳的心跳提供（外壳每个应用一个定时器），
        /// 所以同时开多少文档都只有 1 个定时器。
        /// ponytail: 快照根目录与保留数只在启动时生效，运行期改动需重启宿主。
        /// </remarks>
        public sealed class AddInRuntime : IDisposable
        {
        /// <summary>
        /// 宿主外壳的心跳间隔（毫秒）。同时充当"扫描挂载"与"间隔到期"的节拍。
        /// </summary>
        public const int HeartbeatIntervalMs = 15000;

        private readonly Dictionary<string, AutoSaveService> _services =
            new Dictionary<string, AutoSaveService>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, DocumentState> _states =
            new Dictionary<string, DocumentState>(StringComparer.OrdinalIgnoreCase);

        private readonly object _gate = new object();
        private readonly SettingsStore _settingsStore = new SettingsStore();
        private readonly CredentialsStore _credentialsStore = new CredentialsStore();

        private AddInSettings _settings;
        private CloudUploadQueue? _queue;
        private bool _disposed;

        public AddInRuntime(Action<string>? notify = null)
        {
            Notify = notify;
            _settings = _settingsStore.Load();
            Store = new SnapshotStore(_settings.GetSnapshotsRoot(), _settings.SnapshotRetention);
            _queue = BuildQueue(_settings);
        }

        public Action<string>? Notify { get; }

        public AddInSettings Settings => _settings;

        public SnapshotStore Store { get; }

        public int ActiveDocumentCount
        {
            get { lock (_gate) { return _services.Count; } }
        }

        public int PendingUploads => _queue?.PendingCount ?? 0;

        public void Attach(string documentId, IHostAdapter adapter)
        {
            if (string.IsNullOrEmpty(documentId) || adapter == null)
            {
                return;
            }

            lock (_gate)
            {
                if (_disposed || _services.ContainsKey(documentId))
                {
                    return;
                }

                // 每文件状态：缺省两个开关都是关的，并立刻落盘以便下次沿用。
                var state = Store.EnsureDocument(adapter.FilePath!);
                _states[documentId] = state;

                var service = new AutoSaveService(adapter, state, _settings, Store, _queue, Notify);
                _services[documentId] = service;
            }
        }

        public void Detach(string documentId)
        {
            lock (_gate)
            {
                if (_services.TryGetValue(documentId, out var service))
                {
                    // 关闭前补一次快照：那次落在 15 秒轮询窗口里的手动保存不能丢。
                    service.SnapshotOnDetach();
                    service.Dispose();
                    _services.Remove(documentId);

                    // 顺手丢掉状态缓存：文档重开时要从磁盘重新读，
                    // 留着旧对象会让"关闭时的状态"和"下次打开读到的状态"脱节。
                    _states.Remove(documentId);
                }
            }
        }

        /// <summary>读取该文档的每文件状态；没有已保存的文档时返回 null。</summary>
        /// <remarks>
        /// 刻意**每次都从磁盘读**，不用 <c>_states</c> 缓存。
        /// 这个方法被功能区的 getPressed 和设置窗口调用，频率很高；
        /// 一旦它返回内存里的旧值，就会出现"明明存的是开，显示出来是关"。
        /// <c>_states</c> 只留给写入路径和运行中的 AutoSaveService 用。
        /// </remarks>
        public DocumentState? GetState(IHostAdapter? adapter)
        {
            var path = adapter?.FilePath;
            if (adapter == null || !adapter.IsSaved || string.IsNullOrEmpty(path))
            {
                return null;
            }

            var state = Store.ReadState(path!) ?? Store.EnsureDocument(path!);
            lock (_gate)
            {
                _states[path!] = state;
            }

            return state;
        }

        /// <summary>改写该文档的每文件状态：立即落盘，并同步到正在跑的服务。</summary>
        public DocumentState? SetState(IHostAdapter? adapter, bool? autoSave, bool? autoRecover)
        {
            var path = adapter?.FilePath;
            if (adapter == null || !adapter.IsSaved || string.IsNullOrEmpty(path))
            {
                return null;
            }

            var state = GetState(adapter) ?? Store.EnsureDocument(path!);
            if (autoSave.HasValue)
            {
                state.AutoSaveEnabled = autoSave.Value;
            }

            if (autoRecover.HasValue)
            {
                state.AutoRecoverEnabled = autoRecover.Value;
            }

            // 只要动过开关就记为"用户已表过态"，之后不再弹引导。
            state.SwitchesConfigured = true;

            Store.SaveState(path!, state);

            lock (_gate)
            {
                _states[path!] = state;
                if (_services.TryGetValue(path!, out var service))
                {
                    service.SetDocumentState(state);
                }
            }

            return state;
        }

        /// <summary>
        /// 心跳：由宿主外壳每 <see cref="HeartbeatIntervalMs"/> 毫秒调一次。
        /// 每个文档在这里检查"间隔是否到点"并顺手查一次磁盘变化。
        /// </summary>
        public void Tick()
        {
            List<AutoSaveService> services;
            lock (_gate)
            {
                services = _services.Values.ToList();
            }

            foreach (var service in services)
            {
                service.Pump();
            }
        }

        /// <summary>保存新的全局设置并即时应用到所有活跃文档。</summary>
        public void SaveSettings(AddInSettings settings)
        {
            _settingsStore.Save(settings);
            Reload();
        }

        public void SaveCredential(CloudCredential credential)
        {
            _credentialsStore.Save(credential);
            Reload();
        }

        public CloudCredential LoadCredential() => _credentialsStore.Load();

        /// <summary>重新读取设置与凭据，重建上传队列，并更新所有活跃文档。</summary>
        public void Reload()
        {
            lock (_gate)
            {
                _settings = _settingsStore.Load();

                _queue?.Dispose();
                _queue = BuildQueue(_settings);

                foreach (var service in _services.Values)
                {
                    service.Reconfigure(_settings, _queue);
                }
            }
        }

        private CloudUploadQueue? BuildQueue(AddInSettings settings)
        {
            var target = CloudTargetFactory.Create(settings.Cloud, _credentialsStore.Load());
            if (target == null)
            {
                return null;
            }

            Action<string>? onFailure = settings.NotifyOnCloudFailure ? Notify : null;
            return new CloudUploadQueue(target, onFailure);
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                foreach (var service in _services.Values)
                {
                    // 卸载时同样补一次：用户关掉 Office 直接退出也会走到这里。
                    service.SnapshotOnDetach();
                    service.Dispose();
                }

                _services.Clear();
                _queue?.Dispose();
            }
        }
    }
}
