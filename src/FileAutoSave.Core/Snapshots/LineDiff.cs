using System;
using System.Collections.Generic;

namespace FileAutoSave.Core.Snapshots
{
    public enum DiffKind
    {
        Unchanged,
        Added,
        Removed
    }

    public struct DiffLine
    {
        public DiffLine(DiffKind kind, string text)
        {
            Kind = kind;
            Text = text;
        }

        public DiffKind Kind { get; }

        public string Text { get; }
    }

    /// <summary>
    /// 行级差异。刻意做成"先掐掉公共前后缀，中间块够小才跑 LCS"：
    /// 版本历史里相邻两个快照通常只差一处编辑，掐完前后缀后中间块往往很小，
    /// 结果既准确又快。真的整篇重写时中间块会很大，那就退化成"旧的整块标删、新的整块标增"，
    /// 不去算那个 O(n×m)。
    /// </summary>
    public static class LineDiff
    {
        /// <summary>中间块超过这个行数就不跑 LCS。</summary>
        private const int LcsLimit = 400;

        public static List<DiffLine> Compare(string? older, string? newer)
        {
            var a = Split(older);
            var b = Split(newer);
            var result = new List<DiffLine>();

            var head = 0;
            while (head < a.Count && head < b.Count && a[head] == b[head])
            {
                result.Add(new DiffLine(DiffKind.Unchanged, a[head]));
                head++;
            }

            var tailA = a.Count - 1;
            var tailB = b.Count - 1;
            var tail = new List<DiffLine>();
            while (tailA >= head && tailB >= head && a[tailA] == b[tailB])
            {
                tail.Insert(0, new DiffLine(DiffKind.Unchanged, a[tailA]));
                tailA--;
                tailB--;
            }

            var removed = new List<string>();
            for (var i = head; i <= tailA; i++)
            {
                removed.Add(a[i]);
            }

            var added = new List<string>();
            for (var i = head; i <= tailB; i++)
            {
                added.Add(b[i]);
            }

            AppendMiddle(result, removed, added);
            result.AddRange(tail);
            return result;
        }

        private static void AppendMiddle(List<DiffLine> result, List<string> removed, List<string> added)
        {
            if (removed.Count == 0 && added.Count == 0)
            {
                return;
            }

            if (removed.Count + added.Count > LcsLimit)
            {
                foreach (var line in removed)
                {
                    result.Add(new DiffLine(DiffKind.Removed, line));
                }

                foreach (var line in added)
                {
                    result.Add(new DiffLine(DiffKind.Added, line));
                }

                return;
            }

            var rows = removed.Count;
            var columns = added.Count;

            // table[i, j] = removed[i..] 与 added[j..] 的最长公共子序列长度
            var table = new int[rows + 1, columns + 1];
            for (var i = rows - 1; i >= 0; i--)
            {
                for (var j = columns - 1; j >= 0; j--)
                {
                    table[i, j] = removed[i] == added[j]
                        ? table[i + 1, j + 1] + 1
                        : Math.Max(table[i + 1, j], table[i, j + 1]);
                }
            }

            var r = 0;
            var c = 0;
            while (r < rows && c < columns)
            {
                if (removed[r] == added[c])
                {
                    result.Add(new DiffLine(DiffKind.Unchanged, removed[r]));
                    r++;
                    c++;
                }
                else if (table[r + 1, c] >= table[r, c + 1])
                {
                    result.Add(new DiffLine(DiffKind.Removed, removed[r]));
                    r++;
                }
                else
                {
                    result.Add(new DiffLine(DiffKind.Added, added[c]));
                    c++;
                }
            }

            while (r < rows)
            {
                result.Add(new DiffLine(DiffKind.Removed, removed[r++]));
            }

            while (c < columns)
            {
                result.Add(new DiffLine(DiffKind.Added, added[c++]));
            }
        }

        private static List<string> Split(string? text)
        {
            // net48 的 string.IsNullOrEmpty 没有可空标注，编译器不会自动收窄。
            return string.IsNullOrEmpty(text)
                ? new List<string>()
                : new List<string>(text!.Replace("\r\n", "\n").Replace('\r', '\n').Split(new[] { '\n' }, StringSplitOptions.None));
        }
    }
}