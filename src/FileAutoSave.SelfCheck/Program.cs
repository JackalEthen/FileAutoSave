using System;
using System.IO;
using System.Linq;
using FileAutoSave.Core;
using FileAutoSave.Core.Settings;
using FileAutoSave.Core.Snapshots;

namespace FileAutoSave.SelfCheck
{
    /// <summary>
    /// 无框架自检：跑通核心逻辑，任一条断言失败即以非零退出码结束。
    /// </summary>
    internal static class Program
    {
        private static int _failures;

        private static int Main()
        {
            var workspace = Path.Combine(Path.GetTempPath(), "FileAutoSave_SelfCheck_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspace);

            try
            {
                CheckIntervalClamping();
                CheckSettingsRoundTrip(workspace);
                CheckSnapshotDedupAndRetention(workspace);
            }
            finally
            {
                try
                {
                    Directory.Delete(workspace, recursive: true);
                }
                catch (IOException)
                {
                }
            }

            Console.WriteLine(_failures == 0 ? "全部通过" : $"失败 {_failures} 项");
            return _failures == 0 ? 0 : 1;
        }

        private static void CheckIntervalClamping()
        {
            Check(new AddInSettings { AutoSaveIntervalMinutes = 0 }.GetInterval().Value == TimeSpan.FromMinutes(1), "间隔下限钳制到 1 分钟");
            Check(new AddInSettings { AutoSaveIntervalMinutes = 999 }.GetInterval().Value == TimeSpan.FromMinutes(60), "间隔上限钳制到 60 分钟");
            Check(new AddInSettings { AutoSaveIntervalMinutes = 30 }.GetInterval().Value == TimeSpan.FromMinutes(30), "合法间隔保持不变");
        }

        private static void CheckSettingsRoundTrip(string workspace)
        {
            var path = Path.Combine(workspace, "settings.json");
            var store = new SettingsStore(path);

            var settings = new AddInSettings
            {
                AutoSaveIntervalMinutes = 17,
                SnapshotRetention = 12,
                SnapshotsRoot = @"D:\snaps"
            };
            settings.Cloud.Kind = CloudTargetKind.WebDav;
            settings.Cloud.Endpoint = "https://dav.example.com/remote.php/dav/files/u/";
            store.Save(settings);

            var loaded = store.Load();
            Check(loaded.AutoSaveIntervalMinutes == 17, "设置往返：间隔");
            Check(loaded.SnapshotRetention == 12, "设置往返：保留数");
            Check(loaded.Cloud.Kind == CloudTargetKind.WebDav, "设置往返：云目标类型");
            Check(loaded.Cloud.Endpoint == "https://dav.example.com/remote.php/dav/files/u/", "设置往返：端点");

            var missing = new SettingsStore(Path.Combine(workspace, "nope.json")).Load();
            Check(missing.AutoSaveIntervalMinutes == 5, "缺失设置回退默认值");
        }

        private static void CheckSnapshotDedupAndRetention(string workspace)
        {
            var versions = Path.Combine(workspace, "versions");
            var source = Path.Combine(workspace, "book.docx");
            var store = new SnapshotStore(versions, retention: 3);

            File.WriteAllText(source, "v1");
            var first = store.Save(source, "v1");
            Check(first != null, "首次保存产生快照");

            var duplicate = store.Save(source, "v1");
            Check(duplicate == null, "内容未变时去重，不产生新快照");

            for (var i = 2; i <= 6; i++)
            {
                File.WriteAllText(source, "v" + i);
                store.Save(source, "v" + i);
            }

            var list = store.List(source);
            Check(list.Count == 3, $"保留策略生效（期望 3，实得 {list.Count}）");

            var newest = list[0];
            var newestPath = store.ResolvePath(source, newest);
            Check(File.Exists(newestPath), "最新快照文件存在");
            Check(File.ReadAllText(newestPath) == "v6", "最新快照是最新内容");

            var projectionPath = store.ResolveProjectionPath(source, newest);
            Check(File.Exists(projectionPath) && File.ReadAllText(projectionPath) == "v6", "文本投影已写入");

            Check(store.DirectoryName(source) == "book.docx", "快照目录以源文件名命名");

            var documentId = store.GetDocumentId(source);
            Check(!string.IsNullOrEmpty(documentId), "生成了文档标识");

            var documentDir = Path.Combine(versions, "book.docx");
            var files = Directory.GetFiles(documentDir);
            Check(files.Length == 3 * 2 + 1, $"目录里只剩保留的快照与投影加索引（实得 {files.Length}）");

            // 改名：原路径已消失 → 接上同一标识与同一批历史，目录跟着改名
            var renamed = Path.Combine(workspace, "renamed.docx");
            File.Move(source, renamed);
            var listAfterRename = store.List(renamed);
            Check(listAfterRename.Count == list.Count, "改名后历史条数不变（接上了）");
            Check(store.GetDocumentId(renamed) == documentId, "改名后文档标识不变");
            Check(store.DirectoryName(renamed) == "renamed.docx", "改名后目录跟着改名");
            Check(!Directory.Exists(documentDir), "旧目录已迁走");

            // 另存为副本：原路径还在 → 算另一个文件
            var copy = Path.Combine(workspace, "copy.docx");
            File.Copy(renamed, copy);
            var copySnapshot = store.Save(copy, "copy");
            Check(copySnapshot != null, "副本产生自己的快照");
            Check(store.GetDocumentId(copy) != documentId, "副本是不同的文档（标识不同）");

            // 同名不同路径：不共用目录
            var sub = Path.Combine(workspace, "sub");
            Directory.CreateDirectory(sub);
            var sameName = Path.Combine(sub, "copy.docx");
            File.WriteAllText(sameName, "other");
            store.Save(sameName, "other");
            Check(store.DirectoryName(sameName) != "copy.docx", "同名不同路径的文件不共用目录");
        }

        private static void Check(bool condition, string name)
        {
            if (condition)
            {
                Console.WriteLine("  OK  " + name);
                return;
            }

            _failures++;
            Console.WriteLine("FAIL  " + name);
        }
    }
}
