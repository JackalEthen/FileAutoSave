namespace FileAutoSave.Core.Hosting
{
    /// <summary>
    /// 宿主专属能力的抽象。每个宿主一份实现，Core 只依赖这个接口。
    /// </summary>
    public interface IHostAdapter
    {
        /// <summary>宿主名（Word / Excel / PowerPoint），用于提示文案。</summary>
        string HostName { get; }

        /// <summary>文档是否已保存过（有磁盘路径）。未命名文档为 false。</summary>
        bool IsSaved { get; }

        /// <summary>文档的磁盘路径；未保存时为 null。</summary>
        string? FilePath { get; }

        /// <summary>用当前内容覆盖原文件。</summary>
        void Save();

        /// <summary>提取用于历史对比的文本投影（Word→纯文本，Excel→TSV，PPT→纯文本）。</summary>
        string ExtractTextProjection();

        /// <summary>
        /// 用某个快照文件覆盖当前文档。实现方式为"关闭当前文档 → 覆盖原文件 → 重新打开"，
        /// 因此调用前必须已经为当前状态存过一份回滚前快照。
        /// </summary>
        void RollbackFrom(string snapshotPath);
    }
}
