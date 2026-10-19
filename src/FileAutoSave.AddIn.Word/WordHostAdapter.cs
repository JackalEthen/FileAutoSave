using System;
using System.IO;
using FileAutoSave.Core.Hosting;
using NetOffice.WordApi;

namespace FileAutoSave.AddIn.Word
{
    /// <summary>
    /// Word 专属适配层：把它包装成一个具体的文档。
    /// </summary>
    internal sealed class WordHostAdapter : IHostAdapter
    {
        private readonly Document _document;

        public WordHostAdapter(Document document)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
        }

        public string HostName => "Word";

        public bool IsSaved => !string.IsNullOrEmpty(FilePath);

        public string? FilePath
        {
            get
            {
                try
                {
                    var directory = _document.Path;
                    return string.IsNullOrEmpty(directory) ? null : Path.Combine(directory, _document.Name);
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        public void Save() => _document.Save();

        public string ExtractTextProjection()
        {
            try
            {
                return ProjectionBudget.Truncate(_document.Content.Text ?? string.Empty);
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        public void RollbackFrom(string snapshotPath)
        {
            var target = FilePath ?? throw new InvalidOperationException("文档尚未保存，无法回滚。");
            var application = _document.Application;

            _document.Close(false);
            File.Copy(snapshotPath, target, overwrite: true);
            application.Documents.Open(target);
        }
    }
}
