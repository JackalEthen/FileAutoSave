using System.IO;

namespace FileAutoSave.Core.Storage
{
    /// <summary>
    /// 原子写：先写同目录临时文件，再整体替换。
    /// </summary>
    /// <remarks>
    /// 直接 File.WriteAllText 不是原子的 —— 断电、磁盘满、Office 被强杀都会留下**截断的 JSON**。
    /// 而读失败在别处是被当成"文件不存在"的：快照本体还在磁盘上，UI 里却显示历史全没了，
    /// 那份 index.json 还会被空版本覆盖掉，目录里的快照就此变成再也找不回来的孤儿。
    /// 对一个"防丢文件"的产品，这条比多写几行代码重要得多。
    /// </remarks>
    internal static class AtomicFile
    {
        public static void WriteAllText(string path, string contents)
        {
            var temp = path + ".tmp";

            File.WriteAllText(temp, contents);

            if (File.Exists(path))
            {
                File.Replace(temp, path, null);
            }
            else
            {
                File.Move(temp, path);
            }
        }
    }
}
