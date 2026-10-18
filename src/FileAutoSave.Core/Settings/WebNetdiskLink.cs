using System;
using System.Collections.Generic;

namespace FileAutoSave.Core.Settings
{
    /// <summary>
    /// 一个"网页网盘"入口：只有名称和网址。
    /// 点击后用系统默认浏览器打开该网址，同时在资源管理器里定位当前文档的最新快照，
    /// 由用户自己拖拽上传。
    /// </summary>
    /// <remarks>
    /// 刻意**不**对接任何网盘的内部接口：夸克/迅雷/百度这类都没有公开 API，
    /// 模拟它们的私有接口既违反服务条款，又会随对方改版而失效。
    /// 只打开官方网页 + 定位本地快照，我们和它们的实现彻底解耦。
    /// </remarks>
    public sealed class WebNetdiskLink
    {
        public string Name { get; set; } = string.Empty;

        public string Url { get; set; } = string.Empty;

        /// <summary>名称非空且网址是绝对 URI 才算可用。</summary>
        public bool IsUsable() =>
            !string.IsNullOrWhiteSpace(Name) &&
            !string.IsNullOrWhiteSpace(Url) &&
            Uri.TryCreate(Url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        /// <summary>开箱即用的国内网盘入口，可在设置里增删改。</summary>
        public static List<WebNetdiskLink> Defaults() => new List<WebNetdiskLink>
        {
            new WebNetdiskLink { Name = "夸克网盘", Url = "https://pan.quark.cn" },
            new WebNetdiskLink { Name = "迅雷网盘", Url = "https://pan.xunlei.com" },
            new WebNetdiskLink { Name = "百度网盘", Url = "https://pan.baidu.com" },
            new WebNetdiskLink { Name = "天翼云盘", Url = "https://cloud.189.cn" },
            new WebNetdiskLink { Name = "阿里云盘", Url = "https://www.aliyundrive.com" }
        };
    }
}
