using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using FileAutoSave.Core.Storage;
using Newtonsoft.Json;

namespace FileAutoSave.Core.Security
{
    /// <summary>
    /// 用 Windows DPAPI（绑当前用户）加密保存云凭据。
    /// </summary>
    public sealed class CredentialsStore
    {
        private readonly string _path;

        public CredentialsStore(string? path = null)
        {
            _path = path ?? AppPaths.CredentialsFile;
        }

        public CloudCredential Load()
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return new CloudCredential();
                }

                var protectedBytes = Convert.FromBase64String(File.ReadAllText(_path));
                var plain = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                return JsonConvert.DeserializeObject<CloudCredential>(Encoding.UTF8.GetString(plain))
                       ?? new CloudCredential();
            }
            catch (Exception)
            {
                // 换机器 / 换用户 / 文件损坏都会走到这里——当成没有凭据。
                return new CloudCredential();
            }
        }

        public void Save(CloudCredential credential)
        {
            if (credential == null)
            {
                throw new ArgumentNullException(nameof(credential));
            }

            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var plain = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(credential));
            var protectedBytes = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            File.WriteAllText(_path, Convert.ToBase64String(protectedBytes));
        }

        public void Clear()
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
    }
}
