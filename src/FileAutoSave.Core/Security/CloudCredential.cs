namespace FileAutoSave.Core.Security
{
    /// <summary>
    /// 云目标的敏感凭据。整体经 DPAPI 加密后落盘，不进入 settings.json。
    /// </summary>
    public sealed class CloudCredential
    {
        /// <summary>S3 的 AccessKeyId；WebDAV/NAS 不使用。</summary>
        public string AccessKeyId { get; set; } = string.Empty;

        /// <summary>WebDAV 密码 / S3 的 SecretAccessKey。</summary>
        public string Secret { get; set; } = string.Empty;

        public bool IsEmpty => string.IsNullOrEmpty(AccessKeyId) && string.IsNullOrEmpty(Secret);
    }
}
