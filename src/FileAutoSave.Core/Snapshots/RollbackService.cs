using System;
using System.IO;
using FileAutoSave.Core.Hosting;

namespace FileAutoSave.Core.Snapshots
{
    /// <summary>
    /// 回滚：先用当前状态存一份"回滚前快照"，再用目标快照覆盖文档。
    /// </summary>
    public static class RollbackService
    {
        public static void Rollback(IHostAdapter adapter, SnapshotStore store, SnapshotInfo target)
        {
            if (adapter == null)
            {
                throw new ArgumentNullException(nameof(adapter));
            }

            if (!adapter.IsSaved || string.IsNullOrEmpty(adapter.FilePath))
            {
                throw new InvalidOperationException("文档尚未保存，无法回滚。");
            }

            // 有未保存的改动时不能回滚：RollbackFrom 靠 Close(false) 丢内存内容，
            // 而"回滚前快照"存的是磁盘内容，压根不含这些改动 —— 回滚等于白丢。
            if (adapter.HasUnsavedChanges)
            {
                throw new InvalidOperationException(
                    "文档有未保存的改动，回滚会把它们丢掉（回滚前快照只保存磁盘上的内容）。\n请先 Ctrl+S 保存，再回滚。");
            }

            var sourcePath = adapter.FilePath!;
            var snapshotPath = store.ResolvePath(sourcePath, target);
            if (!File.Exists(snapshotPath))
            {
                throw new FileNotFoundException("快照文件已不存在。", snapshotPath);
            }

            // 存"回滚前快照"会触发保留策略：达到上限时最旧那份会被删掉，而那可能
            // 正是我们要用的目标快照。宿主那边的 RollbackFrom 是"先 Close 再复制"，
            // 真删掉了就是"文档已经关了，复制才报文件不存在"。所以先把目标拷一份到手。
            // 临时件放在快照所在目录（我们自己的地盘），finally 里删掉，不外泄用户内容。
            var workingCopy = Path.Combine(
                Path.GetDirectoryName(snapshotPath) ?? Path.GetTempPath(),
                Path.GetFileName(snapshotPath) + ".rollbacktmp");

            try
            {
                File.Copy(snapshotPath, workingCopy, overwrite: true);
                store.Save(sourcePath, adapter.ExtractTextProjection());
                adapter.RollbackFrom(workingCopy);
            }
            finally
            {
                TryDelete(workingCopy);
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
        }
    }
}
