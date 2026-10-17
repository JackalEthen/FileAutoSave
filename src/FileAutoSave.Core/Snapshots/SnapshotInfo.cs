using System;

namespace FileAutoSave.Core.Snapshots
{
    /// <summary>
    /// 一个快照的描述。二进制本体 + 可选的文本投影（仅用于历史对比）。
    /// </summary>
    public sealed class SnapshotInfo
    {
        public DateTime TimestampUtc { get; set; }

        public string FileName { get; set; } = string.Empty;

        public string Sha256 { get; set; } = string.Empty;

        public long SizeBytes { get; set; }

        public string TextProjectionFileName { get; set; } = string.Empty;

        public long TextProjectionSizeBytes { get; set; }

        /// <summary>产生该快照的源文件完整路径，用于同名不同路径时的目录区分。</summary>
        public string SourcePath { get; set; } = string.Empty;
    }
}
