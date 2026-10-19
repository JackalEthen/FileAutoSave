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
            // 端点没填就是"还没配好"，不是崩溃。S3 尤其凶：空端点会让 AWS SDK
            // 直接抛 No RegionEndpoint，异常从运行时构造函数冒出去，
            // 加载项当场死掉（定时器不启动、事件不订阅），而且每次启动都弹一次错。
            if (string.IsNullOrWhiteSpace(settings.Endpoint))
            {
                return null;
            }

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
