using System;
using System.Collections.Generic;
using FileAutoSave.Core.Storage;
using Newtonsoft.Json;

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

        /// <summary>
        /// 网页网盘入口（打开浏览器 + 定位最新快照，用户手动拖拽上传）。
        /// 与 <see cref="Cloud"/> 无关：那套是真上传，这套只是通往官网的快捷方式。
        /// </summary>
        /// <remarks>
        /// 必须显式 Replace：Json.NET 默认会**复用**上面这个已建好的列表并往里追加，
        /// 于是每次存读都把默认 5 条累加一遍（5 → 11 → 16 …）。
        /// Replace 之后语义才对：键缺失 → 保留默认值；键存在（含空数组）→ 整体覆盖。
        /// </remarks>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<WebNetdiskLink> WebNetdisks { get; set; } = WebNetdiskLink.Defaults();

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
