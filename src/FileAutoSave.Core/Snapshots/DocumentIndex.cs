using System.Collections.Generic;

namespace FileAutoSave.Core.Snapshots
{
    /// <summary>
    /// 一个文档目录内的 index.json 内容（存储点 ②）。
    /// 记录该文档的唯一标识、源文件路径、每文件开关，以及它的快照序列。
    /// </summary>
    public sealed class DocumentIndex
    {
        /// <summary>该文档的唯一标识（32 位十六进制）。</summary>
        public string DocumentId { get; set; } = string.Empty;

        /// <summary>最后一次已知的源文件完整路径；改名/移动时随之更新。</summary>
        public string SourcePath { get; set; } = string.Empty;

        /// <summary>每文件状态：自动保存。</summary>
        public bool AutoSaveEnabled { get; set; }

        /// <summary>每文件状态：自动恢复。</summary>
        public bool AutoRecoverEnabled { get; set; }

        /// <summary>每文件状态：用户是否已明确表过态（见 <see cref="DocumentState.SwitchesConfigured"/>）。</summary>
        public bool SwitchesConfigured { get; set; }

        public List<SnapshotInfo> Snapshots { get; set; } = new List<SnapshotInfo>();
    }
}
