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

        /// <summary>
        /// 用户是否已经就这个文档**明确表过态**（开或关都算）。
        /// 没有它就分不清"默认关着"和"我特意关掉了"，
        /// 于是关掉之后每次打开还是弹窗引导，功能变成噪音。
        /// 老配置里没有这个字段，反序列化为 false，用户会再看到一次提示，之后不再打扰。
        /// </summary>
        public bool SwitchesConfigured { get; set; }
    }
}
