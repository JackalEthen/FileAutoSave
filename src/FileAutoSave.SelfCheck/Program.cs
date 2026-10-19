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
                CheckSwitchesConfiguredRoundTrip(workspace);
                CheckWebNetdiskLinks(workspace);
                CheckLineDiff();
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

        private static void CheckSwitchesConfiguredRoundTrip(string workspace)
        {
            var versions = Path.Combine(workspace, "configured-versions");
            var source = Path.Combine(workspace, "configured.docx");
            File.WriteAllText(source, "v1");

            var store = new SnapshotStore(versions, retention: 5);
            var fresh = store.EnsureDocument(source);
            var documentId = fresh.DocumentId;
            Check(!fresh.SwitchesConfigured, "新文档默认未被标记为已配置");

            fresh.AutoRecoverEnabled = true;
            fresh.SwitchesConfigured = true;
            store.SaveState(source, fresh);

            var reloaded = store.EnsureDocument(source);
            Check(reloaded.SwitchesConfigured, "已明确表态会被记住（不再弹引导）");
            Check(reloaded.AutoRecoverEnabled, "开关状态一并记住");

            // 另一份文档仍是"从未配置"，不该被上一个文档的设置污染
            var other = Path.Combine(workspace, "other.docx");
            File.WriteAllText(other, "v1");
            Check(!store.EnsureDocument(other).SwitchesConfigured, "未配置的文档不受影响");

            // 只读通道：显示路径靠它，必须认盘上的值，且绝不凭空造文档
            fresh.AutoRecoverEnabled = false;
            store.SaveState(source, fresh);
            Check(store.ReadState(source)!.AutoRecoverEnabled == false, "只读通道读到关");
            fresh.AutoSaveEnabled = true;
            store.SaveState(source, fresh);
            Check(store.ReadState(source)!.AutoSaveEnabled, "只读通道跟着写到开");
            Check(store.ReadState(source)!.DocumentId == documentId, "只读通道认得同一个文档标识");

            Check(store.ReadState(Path.Combine(workspace, "never-seen.docx")) == null, "只读通道对陌生文档返回 null 而不是造假数据");

            var before = File.GetLastWriteTimeUtc(Path.Combine(versions, "configured.docx", "index.json"));
            store.ReadState(source);
            Check(File.GetLastWriteTimeUtc(Path.Combine(versions, "configured.docx", "index.json")) == before, "只读通道不写盘");
        }

        private static void CheckWebNetdiskLinks(string workspace)
        {
            var defaults = WebNetdiskLink.Defaults();
            Check(defaults.Count == 5, "内置 5 个网页网盘入口");
            Check(defaults.All(l => l.IsUsable()), "内置入口全部可用");
            Check(defaults.Select(l => l.Url).Distinct().Count() == defaults.Count, "内置入口网址不重复");

            Check(!new WebNetdiskLink { Name = "坏网址", Url = "not a url" }.IsUsable(), "非法网址被判为不可用");
            Check(!new WebNetdiskLink { Name = "", Url = "https://a.com" }.IsUsable(), "空名称被判为不可用");
            Check(!new WebNetdiskLink { Name = "本地文件", Url = "file:///C:/x" }.IsUsable(), "非 http(s) 网址被判为不可用");
        }

        private static void CheckLineDiff()
        {
            // 中间改一行：公共前后缀掐掉后只剩 1 删 1 增（首尾两行未变仍要输出）
            var one = LineDiff.Compare("a\nb\nc", "a\nB\nc");
            Check(one.Count == 4, $"改一行 → 输出 4 行（实得 {one.Count}）");
            Check(one[0].Kind == DiffKind.Unchanged && one[0].Text == "a", "首行未变");
            Check(one[1].Kind == DiffKind.Removed && one[1].Text == "b", "中间标删旧行");
            Check(one[2].Kind == DiffKind.Added && one[2].Text == "B", "中间标增新行");
            Check(one[3].Kind == DiffKind.Unchanged && one[3].Text == "c", "尾行未变");

            Check(LineDiff.Compare("a", "a").Count == 1, "完全相同 → 全是未变");
            Check(LineDiff.Compare(null, "a").Any(l => l.Kind == DiffKind.Added), "旧版为空 → 全部新增");
            Check(LineDiff.Compare("a", null).Any(l => l.Kind == DiffKind.Removed), "新版为空 → 全部删除");

            // CRLF 与 LF 必须视为同一份内容
            Check(LineDiff.Compare("a\r\nb", "a\nb").All(l => l.Kind == DiffKind.Unchanged), "CRLF/LF 视为相同");

            // 中间块没超过上限时，LCS 会把改动的行对齐，中间不产生多余噪音
            var aligned = LineDiff.Compare("1\n2\n3\n4", "1\nX\n3\nY\n4");
            Check(aligned.Count(l => l.Kind == DiffKind.Unchanged) == 3, "LCS 对齐：3 行未变");
            Check(aligned.Count(l => l.Kind == DiffKind.Added) == 2, "LCS 对齐：2 行新增");
            Check(aligned.Count(l => l.Kind == DiffKind.Removed) == 1, "LCS 对齐：1 行删除");

            // 整篇重写：中间块超上限，退化成整块删+增，且不能把机器卡死
            var bigOld = string.Join("\n", Enumerable.Range(0, 900).Select(i => "old" + i));
            var bigNew = string.Join("\n", Enumerable.Range(0, 900).Select(i => "new" + i));
            var big = LineDiff.Compare(bigOld, bigNew);
            Check(big.Count == 1800, $"超限退化为整块删增（实得 {big.Count}）");
            Check(big.Count(l => l.Kind == DiffKind.Removed) == 900, "超限时旧块全标删");
            Check(big.Count(l => l.Kind == DiffKind.Added) == 900, "超限时新块全标增");
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
