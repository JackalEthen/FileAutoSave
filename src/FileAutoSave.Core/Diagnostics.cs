using System;
using System.IO;
using FileAutoSave.Core.Storage;

namespace FileAutoSave.Core
{
    /// <summary>
    /// 极简诊断日志：追加到 %APPDATA%\FileAutoSave\diagnostics.log。
    /// 用于确认宿主是否加载了加载项、功能区回调是否被调用。
    /// </summary>
    public static class Diagnostics
    {
        private static readonly object Gate = new object();

        public static string LogPath => Path.Combine(AppPaths.Root, "diagnostics.log");

        public static void Log(string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(AppPaths.Root);
                    File.AppendAllText(LogPath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + Environment.NewLine);
                }
            }
            catch (Exception)
            {
            }
        }

        public static void LogException(string context, Exception exception) =>
            Log(context + " 抛异常: " + exception.GetType().Name + ": " + exception.Message);
    }
}
