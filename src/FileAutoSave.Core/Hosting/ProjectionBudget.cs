using System;

namespace FileAutoSave.Core.Hosting
{
    /// <summary>
    /// 文本投影的长度上限。
    /// 投影是在宿主 UI 线程上用 COM 遍历文档得到的：Excel 的 <c>UsedRange.Formula</c>
    /// 和 PowerPoint 的逐形状遍历在超大文档上会明显卡顿，超长 Word 文档的
    /// <c>Content.Text</c> 也会生成巨大的字符串。
    /// 超限就截断——投影**只用于显示差异**，不参与回滚，截断不影响任何恢复能力。
    /// </summary>
    public static class ProjectionBudget
    {
        /// <summary>约 20 万字符，够看清绝大多数文档的改动，又不至于拖垮 UI 线程。</summary>
        public const int MaxChars = 200_000;

        public static string Truncate(string text) =>
            string.IsNullOrEmpty(text) || text.Length <= MaxChars ? text : text.Substring(0, MaxChars);
    }
}