using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace FileAutoSave.Core.Snapshots
{
    /// <summary>
    /// 快照库。**每个源文件一个同名目录**，便于人工辨认与维护：
    ///   {root}\{文件名}\index.json + 若干 时间戳.扩展名 + 同名 *.projection.txt
    ///   {root}\documents.json   ← 所有文档的标识/路径/最近内容哈希汇总
    ///
    /// 改名对齐：目录名和源文件名对不上时，用内容哈希去总注册表里找。
    ///   命中且原路径已消失 → 判定为"改名"，把旧目录迁到新名字、沿用原标识；
    ///   命中但原路径还在   → 判定为"另存为副本"，算新文档。
    /// 去重靠 SHA-256，体积控制靠保留计数。
    /// </summary>
    public sealed class SnapshotStore
    {
        private const string IndexFileName = "index.json";
        private const string ProjectionSuffix = ".projection.txt";

        private readonly string _root;
        private readonly int _retention;
        private readonly DocumentRegistry _registry;

        public SnapshotStore(string root, int retention)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new ArgumentException("快照根目录不能为空。", nameof(root));
            }

            _root = root;
            _retention = Math.Max(1, retention);
            Directory.CreateDirectory(_root);
            _registry = new DocumentRegistry(_root);
        }

        /// <summary>
        /// 为 <paramref name="sourceFile"/> 存一份快照。内容与最近一份相同时跳过并返回 null。
        /// </summary>
        public SnapshotInfo? Save(string sourceFile, string? textProjection)
        {
            if (!File.Exists(sourceFile))
            {
                return null;
            }

            var location = ResolveDocument(sourceFile);
            Directory.CreateDirectory(location.Directory);

            var sha = HashFile(sourceFile);
            var snapshots = location.Index.Snapshots;

            if (snapshots.Count > 0 && string.Equals(snapshots[0].Sha256, sha, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var timestamp = DateTime.UtcNow;
            var extension = Path.GetExtension(sourceFile);
            // 文件名用**本地时间**（人看得懂）；TimestampUtc 仍是 UTC，排序与显示以它为准。
            var stem = timestamp.ToLocalTime().ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture);
            var fileName = stem + extension;

            var suffix = 1;
            while (File.Exists(Path.Combine(location.Directory, fileName)))
            {
                fileName = stem + "-" + suffix.ToString(CultureInfo.InvariantCulture) + extension;
                suffix++;
            }

            var targetPath = Path.Combine(location.Directory, fileName);
            CopyShared(sourceFile, targetPath);

            var info = new SnapshotInfo
            {
                TimestampUtc = timestamp,
                FileName = fileName,
                Sha256 = sha,
                SizeBytes = new FileInfo(targetPath).Length,
                SourcePath = sourceFile
            };

            if (!string.IsNullOrEmpty(textProjection))
            {
                // 与快照文件同名（含防撞后缀），保证一一对应——否则同一毫秒内的多次保存会互相覆盖投影。
                var projectionName = Path.GetFileNameWithoutExtension(fileName) + ProjectionSuffix;
                var projectionPath = Path.Combine(location.Directory, projectionName);
                File.WriteAllText(projectionPath, textProjection);
                info.TextProjectionFileName = projectionName;
                info.TextProjectionSizeBytes = new FileInfo(projectionPath).Length;
            }

            snapshots.Insert(0, info);
            Prune(location.Directory, snapshots);

            location.Index.SourcePath = sourceFile;
            SaveIndex(location.Directory, location.Index);
            UpdateRegistry(location.Index, location.Directory, sourceFile, sha);

            return info;
        }

        /// <summary>
        /// 确保该文档有唯一标识与落盘的状态记录（① 注册表 + ② 该文档的 index.json），
        /// 并返回当前的每文件状态。**新文档的两个开关默认关闭。**
        /// </summary>
        public DocumentState EnsureDocument(string sourceFile)
        {
            var location = ResolveDocument(sourceFile);
            if (string.IsNullOrEmpty(location.Index.DocumentId))
            {
                location.Index.DocumentId = NewDocumentId();
            }

            location.Index.SourcePath = sourceFile;
            SaveIndex(location.Directory, location.Index);
            UpdateRegistry(location.Index, location.Directory, sourceFile, null);

            return ToState(location.Index);
        }

        /// <summary>写入该文档的每文件状态（① 与 ② 同时更新）。</summary>
        public void SaveState(string sourceFile, DocumentState state)
        {
            var location = ResolveDocument(sourceFile);
            location.Index.DocumentId = state.DocumentId;
            location.Index.SourcePath = sourceFile;
            location.Index.AutoSaveEnabled = state.AutoSaveEnabled;
            location.Index.AutoRecoverEnabled = state.AutoRecoverEnabled;

            SaveIndex(location.Directory, location.Index);
            UpdateRegistry(location.Index, location.Directory, sourceFile, null);
        }

        private static DocumentState ToState(DocumentIndex index) => new DocumentState
        {
            DocumentId = index.DocumentId,
            AutoSaveEnabled = index.AutoSaveEnabled,
            AutoRecoverEnabled = index.AutoRecoverEnabled
        };

        private void UpdateRegistry(DocumentIndex index, string directory, string sourceFile, string? fingerprint)
        {
            var entry = _registry.FindByDocumentId(index.DocumentId)
                        ?? new DocumentRegistry.Entry { DocumentId = index.DocumentId };

            entry.DirectoryName = Path.GetFileName(directory);
            entry.SourcePath = sourceFile;
            entry.AutoSaveEnabled = index.AutoSaveEnabled;
            entry.AutoRecoverEnabled = index.AutoRecoverEnabled;
            if (!string.IsNullOrEmpty(fingerprint))
            {
                entry.Fingerprint = fingerprint!;
            }

            _registry.Upsert(entry);
        }

        public IReadOnlyList<SnapshotInfo> List(string sourceFile) =>
            ResolveDocument(sourceFile).Index.Snapshots;

        public string ResolvePath(string sourceFile, SnapshotInfo info) =>
            Path.Combine(ResolveDocument(sourceFile).Directory, info.FileName);

        public string ResolveProjectionPath(string sourceFile, SnapshotInfo info) =>
            string.IsNullOrEmpty(info.TextProjectionFileName)
                ? string.Empty
                : Path.Combine(ResolveDocument(sourceFile).Directory, info.TextProjectionFileName);

        /// <summary>该文档在快照根下的目录名（用于云端相对路径）。</summary>
        public string DirectoryName(string sourceFile) =>
            Path.GetFileName(ResolveDocument(sourceFile).Directory);

        /// <summary>该文档的唯一标识；从未记录过则为 null。</summary>
        public string? GetDocumentId(string sourceFile)
        {
            var id = ResolveDocument(sourceFile).Index.DocumentId;
            return string.IsNullOrEmpty(id) ? null : id;
        }

        private sealed class DocumentLocation
        {
            public DocumentLocation(string directory, DocumentIndex index)
            {
                Directory = directory;
                Index = index;
            }

            public string Directory { get; }

            public DocumentIndex Index { get; }
        }

        private DocumentLocation ResolveDocument(string sourceFile)
        {
            var name = Sanitize(Path.GetFileName(sourceFile));
            if (string.IsNullOrEmpty(name))
            {
                name = "document";
            }

            // 1) 注册表按路径精确匹配（主索引）。
            var entry = _registry.FindBySourcePath(sourceFile);
            if (entry != null && !string.IsNullOrEmpty(entry.DirectoryName))
            {
                var knownDirectory = Path.Combine(_root, entry.DirectoryName);
                if (Directory.Exists(knownDirectory))
                {
                    var knownIndex = LoadIndex(knownDirectory) ?? NewIndex(sourceFile, entry.DocumentId);
                    knownIndex.SourcePath = sourceFile;
                    return new DocumentLocation(knownDirectory, knownIndex);
                }
            }

            // 2) 目录名直接匹配（注册表缺失/损坏时的兜底）。
            var directory = Path.Combine(_root, name);
            var index = LoadIndex(directory);
            if (index != null
                && (string.IsNullOrEmpty(index.SourcePath)
                    || string.Equals(index.SourcePath, sourceFile, StringComparison.OrdinalIgnoreCase)))
            {
                return new DocumentLocation(directory, index);
            }

            // 3) 目录名对不上：用内容哈希判断是不是改了名。
            if (File.Exists(sourceFile))
            {
                var fingerprint = HashFile(sourceFile);
                var renamed = _registry.FindByFingerprint(fingerprint);

                if (renamed != null
                    && !string.IsNullOrEmpty(renamed.DirectoryName)
                    && !string.Equals(renamed.SourcePath, sourceFile, StringComparison.OrdinalIgnoreCase)
                    && !File.Exists(renamed.SourcePath))
                {
                    var oldDirectory = Path.Combine(_root, renamed.DirectoryName);
                    if (Directory.Exists(oldDirectory))
                    {
                        if (!Directory.Exists(directory))
                        {
                            Directory.Move(oldDirectory, directory);
                        }
                        else
                        {
                            directory = oldDirectory;
                        }

                        var migrated = LoadIndex(directory) ?? NewIndex(sourceFile, renamed.DocumentId);
                        migrated.SourcePath = sourceFile;
                        SaveIndex(directory, migrated);

                        renamed.DirectoryName = Path.GetFileName(directory);
                        renamed.SourcePath = sourceFile;
                        _registry.Upsert(renamed);

                        return new DocumentLocation(directory, migrated);
                    }
                }
            }

            // 4) 新文档。若同名目录已被另一个**仍然存在**的文件占用，退化为带短哈希的名字，避免互相污染。
            if (index != null
                && !string.IsNullOrEmpty(index.SourcePath)
                && !string.Equals(index.SourcePath, sourceFile, StringComparison.OrdinalIgnoreCase)
                && File.Exists(index.SourcePath))
            {
                directory = Path.Combine(_root, name + "~" + ShortHash(sourceFile));
                index = LoadIndex(directory);
            }

            return new DocumentLocation(directory, index ?? NewIndex(sourceFile));
        }

        private static DocumentIndex NewIndex(string sourceFile, string? documentId = null) => new DocumentIndex
        {
            DocumentId = string.IsNullOrEmpty(documentId) ? NewDocumentId() : documentId!,
            SourcePath = sourceFile
        };

        private static string NewDocumentId() => Guid.NewGuid().ToString("N");

        private static string ShortHash(string text)
        {
            using (var sha1 = SHA1.Create())
            {
                var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(text.ToLowerInvariant()));
                return string.Concat(hash.Take(4).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
            }
        }

        private static string Sanitize(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(name.Length);
            foreach (var ch in name)
            {
                builder.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
            }

            return builder.ToString();
        }

        private void Prune(string directory, List<SnapshotInfo> snapshots)
        {
            while (snapshots.Count > _retention)
            {
                var oldest = snapshots[snapshots.Count - 1];
                snapshots.RemoveAt(snapshots.Count - 1);

                TryDelete(Path.Combine(directory, oldest.FileName));
                if (!string.IsNullOrEmpty(oldest.TextProjectionFileName))
                {
                    TryDelete(Path.Combine(directory, oldest.TextProjectionFileName));
                }
            }
        }

        private static DocumentIndex? LoadIndex(string directory)
        {
            var path = Path.Combine(directory, IndexFileName);
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<DocumentIndex>(File.ReadAllText(path));
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void SaveIndex(string directory, DocumentIndex index)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, IndexFileName),
                JsonConvert.SerializeObject(index, Formatting.Indented));
        }

        private static string HashFile(string path)
        {
            using (var stream = OpenSharedRead(path))
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(stream);
                return string.Concat(hash.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
            }
        }

        /// <summary>
        /// 以宽松共享模式打开：宿主（Word/Excel/PPT）通常持有目标文件的**写**句柄，
        /// 用默认的 <c>FileShare.Read</c> 会直接撞共享冲突。
        /// </summary>
        private static FileStream OpenSharedRead(string path) =>
            new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        private static void CopyShared(string sourcePath, string targetPath)
        {
            using (var source = OpenSharedRead(sourcePath))
            using (var target = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(target);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
