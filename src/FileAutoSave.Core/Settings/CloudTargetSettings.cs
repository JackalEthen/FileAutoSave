namespace FileAutoSave.Core.Settings
{
    public enum CloudTargetKind
    {
        None = 0,
        WebDav = 1,
        S3 = 2,
        Nas = 3
    }

    /// <summary>
    /// 云目标的非敏感配置。密码/密钥不在这里——见 <c>CredentialsStore</c>（DPAPI 加密）。
    /// </summary>
    public sealed class CloudTargetSettings
    {
        public CloudTargetKind Kind { get; set; } = CloudTargetKind.None;

        /// <summary>WebDAV 的根 URL、S3 的服务端点、NAS 的 UNC 路径。</summary>
        public string Endpoint { get; set; } = string.Empty;

        /// <summary>云端存放版本历史的根目录（相对 Endpoint）。</summary>
        public string RemoteRoot { get; set; } = "FileAutoSave";

        public string UserName { get; set; } = string.Empty;

        /// <summary>S3 兼容存储的 bucket 名；WebDAV/NAS 忽略。</summary>
        public string Bucket { get; set; } = string.Empty;

        public bool UseSsl { get; set; } = true;
    }
}
