using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileAutoSave.Core.Storage;
using Newtonsoft.Json;

namespace FileAutoSave.Core.Snapshots
{
    /// <summary>
    /// 快照根目录下的总注册表 documents.json（存储点 ①）。
    /// 维护所有文档的 标识 ↔ 目录名 ↔ 源路径 ↔ 最近内容哈希，用于改名时把文件与版本目录对上号。
    /// </summary>
    public sealed class DocumentRegistry
    {
        public sealed class Entry
        {
            public string DocumentId { get; set; } = string.Empty;

            public string DirectoryName { get; set; } = string.Empty;

            public string SourcePath { get; set; } = string.Empty;

            /// <summary>最近一次快照的内容哈希（SHA-256）。</summary>
            public string Fingerprint { get; set; } = string.Empty;

            public bool AutoSaveEnabled { get; set; }

            public bool AutoRecoverEnabled { get; set; }

            public bool SwitchesConfigured { get; set; }

            public DateTime UpdatedUtc { get; set; }
        }

        private readonly string _path;
        private readonly List<Entry> _entries;

        public DocumentRegistry(string root)
        {
            _path = Path.Combine(root, "documents.json");
            _entries = Load();
        }

        public IReadOnlyList<Entry> Entries => _entries;

        /// <summary>按内容哈希查找已有文档。</summary>
        public Entry? FindByFingerprint(string fingerprint)
        {
            if (string.IsNullOrEmpty(fingerprint))
            {
                return null;
            }

            return _entries.FirstOrDefault(e => string.Equals(e.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase));
        }

        public Entry? FindByDocumentId(string documentId) =>
            _entries.FirstOrDefault(e => string.Equals(e.DocumentId, documentId, StringComparison.OrdinalIgnoreCase));

        /// <summary>按源文件完整路径查找（主索引：路径 → 文档）。</summary>
        public Entry? FindBySourcePath(string sourcePath) =>
            _entries.FirstOrDefault(e => string.Equals(e.SourcePath, sourcePath, StringComparison.OrdinalIgnoreCase));

        public void Upsert(Entry entry)
        {
            _entries.RemoveAll(e => string.Equals(e.DocumentId, entry.DocumentId, StringComparison.OrdinalIgnoreCase));
            entry.UpdatedUtc = DateTime.UtcNow;
            _entries.Add(entry);
            Save();
        }

        private List<Entry> Load()
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return new List<Entry>();
                }

                return JsonConvert.DeserializeObject<List<Entry>>(File.ReadAllText(_path)) ?? new List<Entry>();
            }
            catch (Exception ex)
            {
                // 丢的是"路径 ↔ 目录"的索引，各文档目录里的 index.json 还在，
                // 历史本身没丢（SnapshotStore 还有目录名兜底），但必须留痕。
                Diagnostics.LogException("注册表读取失败: " + _path, ex);
                return new List<Entry>();
            }
        }

        private void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                AtomicFile.WriteAllText(_path, JsonConvert.SerializeObject(_entries, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Diagnostics.LogException("注册表写入失败: " + _path, ex);
            }
        }
    }
}
