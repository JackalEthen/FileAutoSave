namespace FileAutoSave.Core.Snapshots
{
    /// <summary>
    /// 一个文档的**每文件**状态：唯一标识 + 两个互相独立的开关。
    /// 持久化在 ① 快照根目录的 documents.json（汇总）与 ② 该文档目录的 index.json（本文件）。
    /// </summary>
    public sealed class DocumentState
    {
        public string DocumentId { get; set; } = string.Empty;

        /// <summary>定时用当前内容覆盖原文件。</summary>
        public bool AutoSaveEnabled { get; set; }

        /// <summary>记录可回滚的快照（含每次手动保存）。</summary>
        public bool AutoRecoverEnabled { get; set; }
    }
}
