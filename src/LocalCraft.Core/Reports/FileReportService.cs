using System.Security.Cryptography;
using System.Text;

namespace LocalCraft.Core.Reports;

public sealed class FileReportOptions
{
    public string Folder { get; set; } = string.Empty;
    public bool IncludeSubfolders { get; set; } = true;
    public bool IncludeHidden { get; set; }
    public bool IncludeHash { get; set; }
}

public sealed class FileReportRow
{
    public string RelativePath { get; init; } = string.Empty;
    public string Name => System.IO.Path.GetFileName(RelativePath);
    public string Extension => System.IO.Path.GetExtension(RelativePath);
    public long Size { get; init; }
    public DateTime Modified { get; init; }
    public string Hash { get; init; } = string.Empty;
    public string SizeText => FormatSize(Size);
    public string ModifiedText => Modified.ToString("yyyy-MM-dd HH:mm");

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = bytes;
        int index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }
        return $"{value:0.#} {units[index]}";
    }
}

public sealed class FileReportResult
{
    public IReadOnlyList<FileReportRow> Rows { get; init; } = Array.Empty<FileReportRow>();
    public int SkippedCount { get; init; }
    public string? Error { get; init; }
    public bool Success => string.IsNullOrEmpty(Error);
    public long TotalBytes => Rows.Sum(x => x.Size);
}

public static class FileReportService
{
    public static FileReportResult Scan(FileReportOptions options)
    {
        if (!Directory.Exists(options.Folder))
        {
            return new FileReportResult { Error = "请选择存在的文件夹。" };
        }

        var rows = new List<FileReportRow>();
        int skipped = 0;
        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = options.IncludeSubfolders,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = options.IncludeHidden ? 0 : FileAttributes.Hidden | FileAttributes.System
        };

        try
        {
            foreach (string path in Directory.EnumerateFiles(options.Folder, "*", enumeration))
            {
                try
                {
                    var info = new FileInfo(path);
                    rows.Add(new FileReportRow
                    {
                        RelativePath = Path.GetRelativePath(options.Folder, path),
                        Size = info.Length,
                        Modified = info.LastWriteTime,
                        Hash = options.IncludeHash ? ComputeHash(path) : string.Empty
                    });
                }
                catch
                {
                    skipped++;
                }
            }
        }
        catch (Exception ex)
        {
            return new FileReportResult { Error = "扫描失败：" + ex.Message };
        }

        return new FileReportResult { Rows = rows.OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase).ToList(), SkippedCount = skipped };
    }

    public static void WriteCsv(string path, IReadOnlyList<FileReportRow> rows)
    {
        var text = new StringBuilder();
        text.AppendLine("相对路径,文件名,扩展名,字节,大小,修改时间,SHA-256");
        foreach (var row in rows)
        {
            text.Append(Escape(row.RelativePath)).Append(',')
                .Append(Escape(row.Name)).Append(',')
                .Append(Escape(row.Extension)).Append(',')
                .Append(row.Size).Append(',')
                .Append(Escape(row.SizeText)).Append(',')
                .Append(Escape(row.ModifiedText)).Append(',')
                .Append(row.Hash).Append('\n');
        }

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(true));
    }

    public static void WriteHtml(string path, string folder, IReadOnlyList<FileReportRow> rows)
    {
        var text = new StringBuilder();
        text.Append("<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\"><title>LocalCraft 文件夹清单</title>")
            .Append("<style>body{font-family:'Microsoft YaHei UI',sans-serif;background:#f2f3f6;color:#14171c;margin:28px}")
            .Append("h1{font-size:22px} .meta{color:#5b6472;margin-bottom:18px} table{width:100%;border-collapse:collapse;background:#fff;border-radius:10px;overflow:hidden}")
            .Append("th,td{padding:8px 10px;border-bottom:1px solid #e4e7ec;text-align:left;font-size:12px} th{background:#f8f9fb;color:#5b6472}")
            .Append("td.num{text-align:right;font-variant-numeric:tabular-nums} .hash{font-family:Consolas,monospace;font-size:10px;color:#5b6472}")
            .Append("</style></head><body><h1>LocalCraft 文件夹清单</h1><div class=\"meta\">目录：")
            .Append(Html(folder)).Append(" · 文件数：").Append(rows.Count).Append(" · 总体积：")
            .Append(FormatTotal(rows.Sum(x => x.Size))).Append(" · 生成时间：").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
            .Append("</div><table><thead><tr><th>相对路径</th><th>大小</th><th>修改时间</th><th>SHA-256</th></tr></thead><tbody>");
        foreach (var row in rows)
        {
            text.Append("<tr><td>").Append(Html(row.RelativePath)).Append("</td><td class=\"num\">")
                .Append(Html(row.SizeText)).Append("</td><td>").Append(Html(row.ModifiedText))
                .Append("</td><td class=\"hash\">").Append(row.Hash).Append("</td></tr>");
        }

        text.Append("</tbody></table></body></html>");
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }

    public static string ComputeHash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    private static string Escape(string value)
    {
        return value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }

    private static string Html(string value) => System.Net.WebUtility.HtmlEncode(value);

    private static string FormatTotal(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = bytes;
        int index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }
        return $"{value:0.#} {units[index]}";
    }
}
