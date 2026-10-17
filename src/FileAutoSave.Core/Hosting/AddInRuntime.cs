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
    /// ponytail: 快照根目录与保留数只在启动时生效，运行期改动需重启宿主。
    /// </remarks>
    public sealed class AddInRuntime : IDisposable
    {
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
                service.Start();
            }
        }

        public void Detach(string documentId)
        {
            lock (_gate)
            {
                if (_services.TryGetValue(documentId, out var service))
                {
                    service.Dispose();
                    _services.Remove(documentId);
                }
            }
        }

        /// <summary>读取该文档的每文件状态；没有已保存的文档时返回 null。</summary>
        public DocumentState? GetState(IHostAdapter? adapter)
        {
            var path = adapter?.FilePath;
            if (adapter == null || !adapter.IsSaved || string.IsNullOrEmpty(path))
            {
                return null;
            }

            lock (_gate)
            {
                if (_states.TryGetValue(path!, out var cached))
                {
                    return cached;
                }
            }

            var state = Store.EnsureDocument(path!);
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

        /// <summary>轻量心跳：让每个文档检查磁盘内容是否变化（捕捉手动保存）。</summary>
        public void Tick()
        {
            List<AutoSaveService> services;
            lock (_gate)
            {
                services = _services.Values.ToList();
            }

            foreach (var service in services)
            {
                service.CheckForChanges();
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
                    service.Dispose();
                }

                _services.Clear();
                _queue?.Dispose();
            }
        }
    }
}
