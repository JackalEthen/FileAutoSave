using FileAutoSave.Core.Security;
using FileAutoSave.Core.Settings;

namespace FileAutoSave.Core.Cloud
{
    /// <summary>
    /// 根据配置与凭据构造云目标。未配置时返回 null。
    /// </summary>
    public static class CloudTargetFactory
    {
        public static ICloudTarget? Create(CloudTargetSettings settings, CloudCredential credential)
        {
            switch (settings.Kind)
            {
                case CloudTargetKind.WebDav:
                    return new WebDavTarget(settings.Endpoint, settings.RemoteRoot, settings.UserName, credential.Secret);

                case CloudTargetKind.S3:
                    return new S3Target(settings.Endpoint, settings.Bucket, settings.RemoteRoot, settings.UseSsl,
                        credential.AccessKeyId, credential.Secret);

                case CloudTargetKind.Nas:
                    return new NasTarget(settings.Endpoint, settings.RemoteRoot);

                default:
                    return null;
            }
        }
    }
}
