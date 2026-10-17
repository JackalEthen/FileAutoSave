using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace FileAutoSave.Core.Cloud
{
    /// <summary>
    /// WebDAV 目标（坚果云、Nextcloud、群晖等）。PUT 上传，缺目录时用 MKCOL 逐级创建。
    /// </summary>
    public sealed class WebDavTarget : ICloudTarget, IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _root;

        public WebDavTarget(string endpoint, string root, string userName, string password)
        {
            _root = (root ?? string.Empty).Trim('/');
            _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userName}:{password}")));
            Endpoint = (endpoint ?? string.Empty).TrimEnd('/');
        }

        public string Endpoint { get; }

        public void Upload(string localPath, string remoteRelativePath)
        {
            var relative = Combine(_root, remoteRelativePath);
            var uri = new Uri(Endpoint + "/" + relative);
            EnsureCollections(uri);

            using (var content = new StreamContent(File.OpenRead(localPath)))
            {
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                using (var request = new HttpRequestMessage(HttpMethod.Put, uri) { Content = content })
                using (var response = _http.SendAsync(request).GetAwaiter().GetResult())
                {
                    response.EnsureSuccessStatusCode();
                }
            }
        }

        private void EnsureCollections(Uri fileUri)
        {
            var segments = fileUri.AbsolutePath.Trim('/').Split('/');
            var builder = new StringBuilder();
            for (var i = 0; i < segments.Length - 1; i++)
            {
                builder.Append('/').Append(segments[i]);
                var collection = new Uri(fileUri.GetLeftPart(UriPartial.Authority) + builder + "/");
                using (var request = new HttpRequestMessage(new HttpMethod("MKCOL"), collection))
                using (var response = _http.SendAsync(request).GetAwaiter().GetResult())
                {
                    // 201 创建成功；405 已存在。其余错误留给后面的 PUT 去暴露。
                }
            }
        }

        // ponytail: 文档键是十六进制、文件名是时间戳+扩展名，路径里不含需转义的字符，故不额外做 URL 编码。
        private static string Combine(string left, string right)
        {
            var a = (left ?? string.Empty).Trim('/');
            var b = (right ?? string.Empty).Trim('/').Replace('\\', '/');
            return string.IsNullOrEmpty(a) ? b : a + "/" + b;
        }

        public void Dispose() => _http.Dispose();
    }
}
