using System;
using System.IO;
using System.Text;
using FileAutoSave.Core.Hosting;
using NetOffice.OfficeApi.Enums;
using NetOffice.PowerPointApi;

namespace FileAutoSave.AddIn.PowerPoint
{
    /// <summary>
    /// PowerPoint 专属适配层：包装成一个具体的演示文稿。文本投影为逐页的纯文本。
    /// </summary>
    internal sealed class PowerPointHostAdapter : IHostAdapter
    {
        private readonly Presentation _presentation;

        public PowerPointHostAdapter(Presentation presentation)
        {
            _presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
        }

        public string HostName => "PowerPoint";

        public bool IsSaved => !string.IsNullOrEmpty(FilePath);

        public string? FilePath
        {
            get
            {
                try
                {
                    var directory = _presentation.Path;
                    return string.IsNullOrEmpty(directory) ? null : Path.Combine(directory, _presentation.Name);
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        public void Save() => _presentation.Save();

        public string ExtractTextProjection()
        {
            var builder = new StringBuilder();

            try
            {
                foreach (Slide slide in _presentation.Slides)
                {
                    if (builder.Length >= ProjectionBudget.MaxChars)
                    {
                        break;
                    }

                    builder.Append("### 幻灯片 ").AppendLine(slide.SlideIndex.ToString());

                    foreach (Shape shape in slide.Shapes)
                    {
                        if (!AppendShapeText(builder, shape))
                        {
                            break;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            return ProjectionBudget.Truncate(builder.ToString());
        }

        private static bool AppendShapeText(StringBuilder builder, Shape shape)
        {
            try
            {
                if (shape.HasTextFrame == MsoTriState.msoTrue
                    && shape.TextFrame.HasText == MsoTriState.msoTrue)
                {
                    builder.AppendLine(shape.TextFrame.TextRange.Text);
                }
            }
            catch (Exception)
            {
            }

            // 逐形状遍历在超大演示文稿上很慢，超预算就让外层循环收工。
            return builder.Length < ProjectionBudget.MaxChars;
        }

        public void RollbackFrom(string snapshotPath)
        {
            var target = FilePath ?? throw new InvalidOperationException("演示文稿尚未保存，无法回滚。");
            var application = _presentation.Application;

            _presentation.Close();
            File.Copy(snapshotPath, target, overwrite: true);
            application.Presentations.Open(target, MsoTriState.msoFalse, MsoTriState.msoFalse, MsoTriState.msoTrue);
        }
    }
}
