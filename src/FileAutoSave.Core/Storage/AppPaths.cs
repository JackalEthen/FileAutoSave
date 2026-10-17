using System;
using System.IO;

namespace FileAutoSave.Core.Storage
{
    /// <summary>
    /// 插件在本机的数据目录。所有本地状态（设置、凭证、待传队列、快照库）都放在这里。
    /// </summary>
    public static class AppPaths
    {
        public static string Root =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileAutoSave");

        public static string SettingsFile => Path.Combine(Root, "settings.json");

        public static string CredentialsFile => Path.Combine(Root, "credentials.dat");

        public static string QueueFile => Path.Combine(Root, "queue.json");

        public static string DefaultSnapshotRoot => Path.Combine(Root, "versions");

        public static void EnsureRoot()
        {
            Directory.CreateDirectory(Root);
        }
    }
}
