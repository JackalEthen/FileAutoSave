using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileAutoSave.Core.Storage;
using Newtonsoft.Json;

namespace FileAutoSave.Core.Cloud
{
    /// <summary>
    /// 持久化的云端上传队列。失败按指数退避重试，进程重启后从磁盘恢复。
    /// </summary>
    public sealed class CloudUploadQueue : ICloudUploadSink, IDisposable
    {
        private sealed class Item
        {
            public string LocalPath { get; set; } = string.Empty;
            public string RemotePath { get; set; } = string.Empty;
            public int Attempts { get; set; }
            public DateTime NextAttemptUtc { get; set; }
        }

        private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(1);

        private readonly ICloudTarget _target;
        private readonly string _path;
        private readonly Action<string>? _onFailure;
        private readonly object _gate = new object();
        private readonly List<Item> _items = new List<Item>();
        private readonly Timer _sweeper;
        private int _flushing;
        private bool _disposed;

        public CloudUploadQueue(ICloudTarget target, Action<string>? onFailure = null, string? queuePath = null)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _path = queuePath ?? AppPaths.QueueFile;
            _onFailure = onFailure;

            lock (_gate)
            {
                _items.AddRange(Load());
            }

            _sweeper = new Timer(_ => Flush(), null, SweepInterval, SweepInterval);
        }

        public int PendingCount
        {
            get { lock (_gate) { return _items.Count; } }
        }

        public void Enqueue(string localSnapshotPath, string remoteRelativePath)
        {
            lock (_gate)
            {
                // 同一远端路径只保留最新的一个待传项。
                _items.RemoveAll(i => string.Equals(i.RemotePath, remoteRelativePath, StringComparison.OrdinalIgnoreCase));
                _items.Add(new Item
                {
                    LocalPath = localSnapshotPath,
                    RemotePath = remoteRelativePath,
                    NextAttemptUtc = DateTime.UtcNow
                });
                Persist();
            }

            Task.Run(() => Flush());
        }

        private void Flush()
        {
            if (_disposed || Interlocked.Exchange(ref _flushing, 1) == 1)
            {
                return;
            }

            try
            {
                while (!_disposed)
                {
                    Item? due;
                    lock (_gate)
                    {
                        due = _items.FirstOrDefault(i => i.NextAttemptUtc <= DateTime.UtcNow);
                    }

                    if (due == null)
                    {
                        return;
                    }

                    if (!File.Exists(due.LocalPath))
                    {
                        // 快照已被保留策略清掉，没什么可传的。
                        lock (_gate)
                        {
                            _items.Remove(due);
                            Persist();
                        }

                        continue;
                    }

                    try
                    {
                        _target.Upload(due.LocalPath, due.RemotePath);
                        lock (_gate)
                        {
                            _items.Remove(due);
                            Persist();
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (_gate)
                        {
                            due.Attempts++;
                            due.NextAttemptUtc = DateTime.UtcNow + Backoff(due.Attempts);
                            Persist();
                        }

                        _onFailure?.Invoke($"云端保存失败（第 {due.Attempts} 次，将自动重试）：{ex.Message}");
                        return; // 等下一轮扫描，避免忙循环。
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _flushing, 0);
            }
        }

        private static TimeSpan Backoff(int attempts)
        {
            var seconds = 30 * Math.Pow(2, Math.Min(attempts, 7));
            return TimeSpan.FromSeconds(Math.Min(seconds, MaxBackoff.TotalSeconds));
        }

        private List<Item> Load()
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return new List<Item>();
                }

                return JsonConvert.DeserializeObject<List<Item>>(File.ReadAllText(_path)) ?? new List<Item>();
            }
            catch (Exception)
            {
                return new List<Item>();
            }
        }

        private void Persist()
        {
            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(_path, JsonConvert.SerializeObject(_items, Formatting.Indented));
            }
            catch (Exception)
            {
                // 队列落盘失败不致命：内存中的队列照常工作。
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _sweeper.Dispose();

            if (_target is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
