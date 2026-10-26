using System.Text;
using LocalCraft.Core.Journal;

namespace LocalCraft.Core.Tables;

public sealed class TableExportResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? OutputPath { get; init; }
    public OperationJournal? Journal { get; init; }
}

public sealed class TableExporter
{
    private readonly JournalRepository _repository;

    public TableExporter(JournalRepository repository)
    {
        _repository = repository;
    }

    public TableExportResult Export(
        TableData data,
        string outputPath,
        string sourcePath,
        string displayName = "表格处理")
    {
        if (data.Headers.Count == 0)
        {
            return new TableExportResult { Success = false, Message = "没有可导出的表格数据。" };
        }

        string fullOutput = Path.GetFullPath(outputPath);
        if (File.Exists(fullOutput))
        {
            return new TableExportResult { Success = false, Message = "输出文件已存在，请换一个文件名，避免覆盖原文件。" };
        }

        if (string.Equals(Path.GetFullPath(sourcePath), fullOutput, StringComparison.OrdinalIgnoreCase))
        {
            return new TableExportResult { Success = false, Message = "输出文件不能与输入文件相同。" };
        }

        var journal = new OperationJournal
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = "table",
            DisplayName = displayName,
            SourceDirectory = Path.GetDirectoryName(sourcePath) ?? string.Empty,
            CreatedAt = DateTimeOffset.Now,
            Status = JournalStatus.Prepared,
            Operations =
            {
                new JournalOperation
                {
                    Sequence = 1,
                    SourcePath = Path.GetFullPath(sourcePath),
                    TargetPath = fullOutput,
                    State = JournalOperationState.Completed
                }
            }
        };

        try
        {
            _repository.Save(journal);
            string? directory = Path.GetDirectoryName(fullOutput);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            string temp = fullOutput + ".tmp";
            WriteCsv(data, temp);
            File.Move(temp, fullOutput, overwrite: false);
            journal.Status = JournalStatus.Completed;
            journal.CompletedAt = DateTimeOffset.Now;
            _repository.Save(journal);
            return new TableExportResult
            {
                Success = true,
                Message = $"已写入 {data.Rows.Count} 行、{data.Headers.Count} 列到 {fullOutput}。",
                OutputPath = fullOutput,
                Journal = journal
            };
        }
        catch (Exception ex)
        {
            journal.Status = JournalStatus.Failed;
            journal.Error = ex.Message;
            try { _repository.Save(journal); } catch { }
            try
            {
                if (File.Exists(fullOutput + ".tmp")) File.Delete(fullOutput + ".tmp");
            }
            catch { }
            return new TableExportResult { Success = false, Message = "导出失败：" + ex.Message };
        }
    }

    private static void WriteCsv(TableData data, string path)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        writer.WriteLine(string.Join(",", data.Headers.Select(Escape)));
        foreach (string[] row in data.Rows)
        {
            writer.WriteLine(string.Join(",", row.Select(Escape)));
        }
    }

    private static string Escape(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}
