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

            var sourcePath = adapter.FilePath!;
            var snapshotPath = store.ResolvePath(sourcePath, target);
            if (!File.Exists(snapshotPath))
            {
                throw new FileNotFoundException("快照文件已不存在。", snapshotPath);
            }

            store.Save(sourcePath, adapter.ExtractTextProjection());
            adapter.RollbackFrom(snapshotPath);
        }
    }
}
