using System.Text;
using System.Text.RegularExpressions;

namespace LocalCraft.Core.TextReplacement;

/// <summary>
/// 只读扫描文本文件并生成替换计划。二进制文件、超大文件和非文本扩展名会被跳过。
/// </summary>
public sealed class TextReplacePlanner
{
    private static readonly HashSet<string> DefaultTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".csv", ".tsv", ".json", ".xml", ".html", ".htm",
        ".css", ".js", ".ts", ".cs", ".xaml", ".py", ".java", ".sql", ".ini", ".log",
        ".yaml", ".yml", ".bat", ".cmd", ".ps1", ".config", ".tex", ".rtf"
    };

    public TextReplaceScanResult Scan(TextReplaceOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Folder) || !Directory.Exists(options.Folder))
        {
            return new TextReplaceScanResult { Error = "请选择存在的文件夹。" };
        }

        if (string.IsNullOrEmpty(options.FindText))
        {
            return new TextReplaceScanResult { Error = "请输入要查找的内容。" };
        }

        Regex regex;
        try
        {
            regex = BuildRegex(options);
        }
        catch (ArgumentException ex)
        {
            return new TextReplaceScanResult { Error = "查找表达式无效：" + ex.Message };
        }

        var include = ParseExtensions(options.IncludeExtensions);
        var exclude = ParseExtensions(options.ExcludeExtensions);
        long maxBytes = Math.Max(1, (long)(options.MaxFileSizeMb * 1024 * 1024));

        var plans = new List<TextReplaceFilePlan>();
        int skipped = 0;
        int total = 0;

        try
        {
            var search = options.IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            foreach (string path in Directory.EnumerateFiles(options.Folder, "*", search))
            {
                if (!ShouldConsider(path, options, include, exclude))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    var info = new FileInfo(path);
                    if (info.Length > maxBytes)
                    {
                        skipped++;
                        continue;
                    }

                    if (!TryReadText(path, out string text, out _))
                    {
                        skipped++;
                        continue;
                    }

                    var matches = regex.Matches(text);
                    if (matches.Count == 0) continue;

                    plans.Add(new TextReplaceFilePlan
                    {
                        FilePath = info.FullName,
                        MatchCount = matches.Count,
                        LastWriteTimeUtc = info.LastWriteTimeUtc,
                        Matches = BuildMatches(text, regex, options)
                    });
                    total += matches.Count;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    skipped++;
                }
            }
        }
        catch (Exception ex)
        {
            return new TextReplaceScanResult { Error = "扫描失败：" + ex.Message };
        }

        return new TextReplaceScanResult
        {
            Files = plans.OrderByDescending(x => x.MatchCount).ThenBy(x => x.FileName).ToList(),
            TotalMatches = total,
            SkippedFiles = skipped
        };
    }

    public static Regex BuildRegex(TextReplaceOptions options)
    {
        var regexOptions = RegexOptions.Multiline | RegexOptions.CultureInvariant;
        if (options.IgnoreCase) regexOptions |= RegexOptions.IgnoreCase;
        string pattern = options.UseRegex
            ? options.FindText
            : Regex.Escape(options.FindText);
        return new Regex(pattern, regexOptions, TimeSpan.FromSeconds(2));
    }

    public static bool TryReadText(string path, out string text, out Encoding encoding)
    {
        text = string.Empty;
        encoding = new UTF8Encoding(false);
        byte[] bytes = File.ReadAllBytes(path);
        if (LooksBinary(bytes)) return false;

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            encoding = new UTF8Encoding(true);
            text = encoding.GetString(bytes, 3, bytes.Length - 3);
            return true;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            encoding = Encoding.Unicode;
            text = encoding.GetString(bytes, 2, bytes.Length - 2);
            return true;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            encoding = Encoding.BigEndianUnicode;
            text = encoding.GetString(bytes, 2, bytes.Length - 2);
            return true;
        }

        try
        {
            var strictUtf8 = new UTF8Encoding(false, true);
            text = strictUtf8.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    public static string Apply(string text, TextReplaceOptions options)
    {
        Regex regex = BuildRegex(options);
        return regex.Replace(text, options.ReplaceText);
    }

    public static string ApplySelected(string text, TextReplaceOptions options, IReadOnlySet<int> selectedIndexes)
    {
        Regex regex = BuildRegex(options);
        var builder = new System.Text.StringBuilder(text.Length);
        int last = 0;
        foreach (Match match in regex.Matches(text))
        {
            if (!selectedIndexes.Contains(match.Index)) continue;
            builder.Append(text, last, match.Index - last);
            builder.Append(match.Result(options.ReplaceText));
            last = match.Index + match.Length;
        }
        builder.Append(text, last, text.Length - last);
        return builder.ToString();
    }

    private static IReadOnlyList<TextReplaceMatch> BuildMatches(string text, Regex regex, TextReplaceOptions options)
    {
        var matches = new List<TextReplaceMatch>();
        int line = 1;
        int cursor = 0;
        foreach (Match match in regex.Matches(text))
        {
            if (matches.Count >= 500) break;
            for (int i = cursor; i < match.Index; i++)
            {
                if (text[i] == '\n') line++;
            }
            cursor = match.Index;
            string replaced = regex.Replace(match.Value, options.ReplaceText);
            matches.Add(new TextReplaceMatch(line, Trim(match.Value), Trim(replaced), match.Index));
        }
        return matches;
    }

    private static string Trim(string value)
    {
        value = value.Trim();
        return value.Length <= 160 ? value : value[..157] + "…";
    }

    private static bool LooksBinary(byte[] bytes)
    {
        int limit = Math.Min(bytes.Length, 8192);
        int controls = 0;
        for (int i = 0; i < limit; i++)
        {
            byte value = bytes[i];
            if (value == 0) return true;
            if (value < 9 || (value > 13 && value < 32)) controls++;
        }

        return limit > 0 && controls > limit / 20;
    }

    private static bool ShouldConsider(string path, TextReplaceOptions options, HashSet<string> include, HashSet<string> exclude)
    {
        string extension = Path.GetExtension(path);
        if (exclude.Contains(extension)) return false;
        if (include.Count > 0) return include.Contains(extension);
        return options.AnyExtension || DefaultTextExtensions.Contains(extension);
    }

    private static HashSet<string> ParseExtensions(string value)
    {
        return value.Split(new[] { ',', ';', ' ', '，', '；' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Select(x => x.StartsWith('.') ? x : "." + x)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
