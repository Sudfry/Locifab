using System.Text.RegularExpressions;

namespace LocalCraft.Core.Search;

public sealed class FileSearchOptions
{
    public string Folder { get; set; } = string.Empty;

    public string Pattern { get; set; } = string.Empty;

    public bool UseRegex { get; set; }

    public bool IncludeSubfolders { get; set; } = true;

    public bool IncludeHidden { get; set; }

    public string Extensions { get; set; } = string.Empty;

    public double MinSizeMb { get; set; }

    public double MaxSizeMb { get; set; }

    public int MaxResults { get; set; } = 5000;
}

public sealed class FileSearchItem
{
    public string Path { get; init; } = string.Empty;
    public string Name => System.IO.Path.GetFileName(Path);
    public string RelativePath { get; init; } = string.Empty;
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;
    public long Size { get; init; }
    public DateTime Modified { get; init; }
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

public sealed class FileSearchResult
{
    public IReadOnlyList<FileSearchItem> Items { get; init; } = Array.Empty<FileSearchItem>();
    public bool Truncated { get; init; }
    public string? Error { get; init; }
    public bool Success => string.IsNullOrEmpty(Error);
}

public static class FileSearchService
{
    public static FileSearchResult Search(FileSearchOptions options)
    {
        if (!Directory.Exists(options.Folder))
        {
            return new FileSearchResult { Error = "请选择存在的文件夹。" };
        }

        Regex? regex = null;
        if (options.UseRegex && !string.IsNullOrWhiteSpace(options.Pattern))
        {
            try
            {
                regex = new Regex(options.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
            }
            catch (ArgumentException ex)
            {
                return new FileSearchResult { Error = "正则表达式无效：" + ex.Message };
            }
        }

        var extensions = options.Extensions.Split(new[] { ',', ';', ' ', '，', '；' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.StartsWith('.') ? x : "." + x)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        long minBytes = (long)(Math.Max(0, options.MinSizeMb) * 1024 * 1024);
        long maxBytes = options.MaxSizeMb <= 0 ? long.MaxValue : (long)(options.MaxSizeMb * 1024 * 1024);
        var items = new List<FileSearchItem>();
        bool truncated = false;
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
                if (items.Count >= options.MaxResults)
                {
                    truncated = true;
                    break;
                }

                var info = new FileInfo(path);
                if (info.Length < minBytes || info.Length > maxBytes) continue;
                if (extensions.Count > 0 && !extensions.Contains(info.Extension)) continue;
                string relative = Path.GetRelativePath(options.Folder, path);
                if (!Matches(options.Pattern, relative, regex, options.UseRegex)) continue;

                items.Add(new FileSearchItem
                {
                    Path = info.FullName,
                    RelativePath = relative,
                    Size = info.Length,
                    Modified = info.LastWriteTime
                });
            }
        }
        catch (Exception ex)
        {
            return new FileSearchResult { Error = "搜索失败：" + ex.Message };
        }

        return new FileSearchResult { Items = items, Truncated = truncated };
    }

    private static bool Matches(string pattern, string relative, Regex? regex, bool useRegex)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return true;
        if (useRegex) return regex!.IsMatch(relative);

        string value = relative;
        if (pattern.Contains('*') || pattern.Contains('?'))
        {
            string regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(value, regexPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return value.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }
}
