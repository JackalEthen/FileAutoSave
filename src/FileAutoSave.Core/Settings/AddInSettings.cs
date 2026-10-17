using System;
using FileAutoSave.Core.Storage;

namespace FileAutoSave.Core.Settings
{
    /// <summary>
    /// 插件的用户配置。默认值即"开箱可用"。
    /// </summary>
    public sealed class AddInSettings
    {
        /// <summary>自动保存间隔，单位分钟，取值 1–60。</summary>
        public int AutoSaveIntervalMinutes { get; set; } = 5;

        /// <summary>本地快照库根目录。为空时使用 <see cref="AppPaths.DefaultSnapshotRoot"/>。</summary>
        public string SnapshotsRoot { get; set; } = string.Empty;

        /// <summary>每个文档保留的快照数量上限，超出后按时间删除最旧的。</summary>
        public int SnapshotRetention { get; set; } = 50;

        public CloudTargetSettings Cloud { get; set; } = new CloudTargetSettings();

        public bool NotifyOnCloudFailure { get; set; } = true;

        /// <summary>把间隔转成受约束的类型，越界时钳制到合法范围。</summary>
        public AutoSaveInterval GetInterval()
        {
            var minutes = Math.Min(60, Math.Max(1, AutoSaveIntervalMinutes));
            return AutoSaveInterval.FromMinutes(minutes);
        }

        public string GetSnapshotsRoot() =>
            string.IsNullOrWhiteSpace(SnapshotsRoot) ? AppPaths.DefaultSnapshotRoot : SnapshotsRoot;
    }
}
