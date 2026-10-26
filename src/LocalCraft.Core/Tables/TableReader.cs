using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LocalCraft.Core.Tables;

/// <summary>
/// 读取 CSV / TSV / XLSX / XLSM。纯本地、只读，最多保留 20000 行。
/// </summary>
public sealed class TableReader
{
    private const int MaxRows = 20_000;
    private const int MaxColumns = 200;

    public TableScanResult Scan(string folder, bool includeSubfolders)
    {
        if (!Directory.Exists(folder))
        {
            return new TableScanResult { Error = "请选择存在的文件夹。" };
        }

        var files = new List<TableFileItem>();
        int skipped = 0;
        try
        {
            var search = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            foreach (string path in Directory.EnumerateFiles(folder, "*", search))
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext is not (".csv" or ".tsv" or ".xlsx" or ".xlsm")) continue;
                try
                {
                    TableData data = Read(path);
                    if (data.Headers.Count == 0) { skipped++; continue; }
                    files.Add(new TableFileItem
                    {
                        FilePath = Path.GetFullPath(path),
                        RowCount = data.Rows.Count,
                        Headers = data.Headers,
                        Data = data
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
            return new TableScanResult { Error = "扫描表格失败：" + ex.Message };
        }

        return new TableScanResult
        {
            Files = files.OrderBy(x => x.FileName, StringComparer.OrdinalIgnoreCase).ToList(),
            SkippedCount = skipped
        };
    }

    public static TableData Read(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".csv" => ReadDelimited(File.ReadAllText(path, DetectEncoding(path)), ','),
            ".tsv" => ReadDelimited(File.ReadAllText(path, DetectEncoding(path)), '\t'),
            ".xlsx" or ".xlsm" => ReadXlsx(path),
            _ => throw new NotSupportedException("不支持的表格格式。")
        };
    }

    private static Encoding DetectEncoding(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return new UTF8Encoding(true);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode;
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode;
        return new UTF8Encoding(false, false);
    }

    private static TableData ReadDelimited(string text, char delimiter)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            if (c == '"')
            {
                quoted = true;
            }
            else if (c == delimiter)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\r' || c == '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString());
                field.Clear();
                AddRow(rows, row);
                row = new List<string>();
            }
            else
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            AddRow(rows, row);
        }

        return BuildData(rows);
    }

    private static void AddRow(List<string[]> rows, List<string> row)
    {
        if (row.All(string.IsNullOrEmpty)) return;
        rows.Add(row.ToArray());
    }

    private static TableData BuildData(List<string[]> rows)
    {
        if (rows.Count == 0) return new TableData();
        var headers = rows[0].Select((value, index) =>
            string.IsNullOrWhiteSpace(value) ? $"列{index + 1}" : value.Trim()).ToArray();
        var dataRows = rows.Skip(1).Take(MaxRows)
            .Select(row => Normalize(row, headers.Length))
            .ToList();
        return new TableData { Headers = headers, Rows = dataRows };
    }

    private static string[] Normalize(string[] row, int length)
    {
        if (row.Length == length) return row;
        var result = new string[length];
        for (int i = 0; i < length; i++) result[i] = i < row.Length ? row[i] : string.Empty;
        return result;
    }

    private static TableData ReadXlsx(string path)
    {
        using ZipArchive archive = ZipFile.OpenRead(path);
        var sharedStrings = ReadSharedStrings(archive);
        ZipArchiveEntry? sheet = archive.GetEntry("xl/worksheets/sheet1.xml")
                                 ?? archive.Entries.FirstOrDefault(x =>
                                     x.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase)
                                     && x.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        if (sheet is null) return new TableData();

        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using Stream stream = sheet.Open();
        XDocument document = XDocument.Load(stream);
        var rows = new List<string[]>();
        foreach (XElement rowElement in document.Descendants(ns + "row"))
        {
            var values = new Dictionary<int, string>();
            int maxColumn = -1;
            foreach (XElement cell in rowElement.Elements(ns + "c"))
            {
                int column = GetColumnIndex((string?)cell.Attribute("r") ?? string.Empty);
                if (column < 0) column = maxColumn + 1;
                if (column >= MaxColumns) continue;
                string type = (string?)cell.Attribute("t") ?? string.Empty;
                string value = type switch
                {
                    "s" => GetSharedString(sharedStrings, cell.Element(ns + "v")),
                    "inlineStr" => string.Concat(cell.Descendants(ns + "t").Select(x => x.Value)),
                    _ => (string?)cell.Element(ns + "v") ?? string.Empty
                };
                values[column] = value;
                maxColumn = Math.Max(maxColumn, column);
            }

            if (values.Count == 0) continue;
            var row = new string[maxColumn + 1];
            for (int i = 0; i < row.Length; i++) row[i] = values.TryGetValue(i, out string? value) ? value : string.Empty;
            AddRow(rows, row.ToList());
            if (rows.Count > MaxRows) break;
        }

        return BuildData(rows);
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var result = new List<string>();
        ZipArchiveEntry? entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return result;
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using Stream stream = entry.Open();
        XDocument document = XDocument.Load(stream);
        foreach (XElement item in document.Descendants(ns + "si"))
        {
            result.Add(string.Concat(item.Descendants(ns + "t").Select(x => x.Value)));
        }

        return result;
    }

    private static string GetSharedString(List<string> values, XElement? valueElement)
    {
        if (valueElement is null || !int.TryParse(valueElement.Value, out int index)) return string.Empty;
        return index >= 0 && index < values.Count ? values[index] : string.Empty;
    }

    private static int GetColumnIndex(string reference)
    {
        var match = Regex.Match(reference, "^[A-Za-z]+");
        if (!match.Success) return -1;
        int result = 0;
        foreach (char c in match.Value.ToUpperInvariant())
        {
            result = result * 26 + (c - 'A' + 1);
        }

        return result - 1;
    }
}
