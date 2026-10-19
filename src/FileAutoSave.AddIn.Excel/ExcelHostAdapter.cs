using System;
using System.Globalization;
using System.IO;
using System.Text;
using FileAutoSave.Core.Hosting;
using NetOffice.ExcelApi;

namespace FileAutoSave.AddIn.Excel
{
    /// <summary>
    /// Excel 专属适配层：包装成一个具体的工作簿。文本投影为逐工作表的 TSV（保留公式）。
    /// </summary>
    internal sealed class ExcelHostAdapter : IHostAdapter
    {
        private readonly Workbook _workbook;

        public ExcelHostAdapter(Workbook workbook)
        {
            _workbook = workbook ?? throw new ArgumentNullException(nameof(workbook));
        }

        public string HostName => "Excel";

        public bool IsSaved => !string.IsNullOrEmpty(FilePath);

        public string? FilePath
        {
            get
            {
                try
                {
                    var directory = _workbook.Path;
                    return string.IsNullOrEmpty(directory) ? null : Path.Combine(directory, _workbook.Name);
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        public void Save() => _workbook.Save();

        public string ExtractTextProjection()
        {
            var builder = new StringBuilder();

            try
            {
                foreach (Worksheet sheet in _workbook.Worksheets)
                {
                    if (builder.Length >= ProjectionBudget.MaxChars)
                    {
                        break;
                    }

                    builder.Append("### ").AppendLine(sheet.Name);

                    object? formula;
                    try
                    {
                        formula = sheet.UsedRange.Formula;
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    if (formula is object[,] cells)
                    {
                        AppendCells(builder, cells);
                    }
                }
            }
            catch (Exception)
            {
            }

            return ProjectionBudget.Truncate(builder.ToString());
        }

        private static void AppendCells(StringBuilder builder, object[,] cells)
        {
            var firstRow = cells.GetLowerBound(0);
            var lastRow = cells.GetUpperBound(0);
            var firstColumn = cells.GetLowerBound(1);
            var lastColumn = cells.GetUpperBound(1);

            for (var row = firstRow; row <= lastRow; row++)
            {
                // 超大 UsedRange 会让这一趟跑很久；超预算就停，投影只用于显示差异。
                if (builder.Length >= ProjectionBudget.MaxChars)
                {
                    return;
                }

                for (var column = firstColumn; column <= lastColumn; column++)
                {
                    if (column > firstColumn)
                    {
                        builder.Append('\t');
                    }

                    builder.Append(Convert.ToString(cells[row, column], CultureInfo.InvariantCulture));
                }

                builder.AppendLine();
            }
        }

        public void RollbackFrom(string snapshotPath)
        {
            var target = FilePath ?? throw new InvalidOperationException("工作簿尚未保存，无法回滚。");
            var application = _workbook.Application;

            _workbook.Close(false);
            File.Copy(snapshotPath, target, overwrite: true);
            application.Workbooks.Open(target);
        }
    }
}
