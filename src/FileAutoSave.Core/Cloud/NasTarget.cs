using System.IO;

namespace FileAutoSave.Core.Cloud
{
    /// <summary>
    /// NAS 目标：把快照复制到 UNC 共享路径（\\nas\share\...）。
    /// </summary>
    public sealed class NasTarget : ICloudTarget
    {
        private readonly string _root;

        public NasTarget(string uncRoot, string subRoot)
        {
            var root = uncRoot ?? string.Empty;
            var sub = (subRoot ?? string.Empty).Trim('\\', '/');
            _root = string.IsNullOrEmpty(sub) ? root : Path.Combine(root, sub.Replace('/', '\\'));
        }

        public void Upload(string localPath, string remoteRelativePath)
        {
            var destination = Path.Combine(_root, remoteRelativePath.Replace('/', '\\'));
            var directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(localPath, destination, overwrite: true);
        }
    }
}
