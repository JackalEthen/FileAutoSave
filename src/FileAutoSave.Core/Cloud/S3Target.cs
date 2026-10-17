using System;
using Amazon.S3;
using Amazon.S3.Model;

namespace FileAutoSave.Core.Cloud
{
    /// <summary>
    /// S3 兼容目标（AWS S3 / MinIO / 阿里云 OSS 的 S3 接口等）。走 AWSSDK。
    /// </summary>
    public sealed class S3Target : ICloudTarget, IDisposable
    {
        private readonly IAmazonS3 _client;
        private readonly string _bucket;
        private readonly string _root;

        public S3Target(string endpoint, string bucket, string root, bool useSsl, string accessKeyId, string secret)
        {
            _bucket = bucket ?? string.Empty;
            _root = (root ?? string.Empty).Trim('/');

            var config = new AmazonS3Config
            {
                ServiceURL = endpoint,
                ForcePathStyle = true,
                UseHttp = !useSsl
            };

            _client = new AmazonS3Client(accessKeyId, secret, config);
        }

        public void Upload(string localPath, string remoteRelativePath)
        {
            var key = string.IsNullOrEmpty(_root)
                ? remoteRelativePath.Replace('\\', '/')
                : _root + "/" + remoteRelativePath.Replace('\\', '/');

            var request = new PutObjectRequest
            {
                BucketName = _bucket,
                Key = key,
                FilePath = localPath
            };

            _client.PutObjectAsync(request).GetAwaiter().GetResult();
        }

        public void Dispose() => _client.Dispose();
    }
}
