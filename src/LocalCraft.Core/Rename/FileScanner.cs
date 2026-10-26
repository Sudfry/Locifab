using LocalCraft.Core.Infrastructure;

namespace LocalCraft.Core.Rename;

public sealed record FileScanResult(
    IReadOnlyList<string> Files,
    bool Truncated,
    string? Warning);

/// <summary>
/// 只读文件夹扫描。默认不进入隐藏/系统项，避免误处理系统文件。
/// </summary>
public static class FileScanner
{
    public const int DefaultMaxFiles = 10_000;

    public static FileScanResult Scan(
        string folderPath,
        bool includeSubdirectories,
        string? extensionFilter = null,
        int maxFiles = DefaultMaxFiles)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return new FileScanResult(Array.Empty<string>(), false, "文件夹不存在或无法访问");
        }

        var extensions = ParseExtensions(extensionFilter);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = includeSubdirectories,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
        };

        try
        {
            var files = new List<string>(Math.Min(maxFiles, 512));
            bool truncated = false;

            foreach (string file in Directory.EnumerateFiles(folderPath, "*", options))
            {
                if (extensions.Count > 0 && !extensions.Contains(Path.GetExtension(file)))
                {
                    continue;
                }

                if (files.Count >= maxFiles)
                {
                    truncated = true;
                    break;
                }

                files.Add(Path.GetFullPath(file));
            }

            files.Sort(static (x, y) =>
                NaturalStringComparer.Instance.Compare(Path.GetFileName(x), Path.GetFileName(y)));

            return new FileScanResult(
                files,
                truncated,
                truncated ? $"文件数量超过 {maxFiles:N0}，仅载入前 {maxFiles:N0} 个" : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new FileScanResult(Array.Empty<string>(), false, "扫描失败：" + ex.Message);
        }
    }

    public static IReadOnlySet<string> ParseExtensions(string? raw)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw)) return result;

        string[] parts = raw.Split(
            new[] { ',', ';', ' ', '\t', '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (string part in parts)
        {
            string value = part.Trim();
            if (value is "*" or "*.*") continue;

            value = value.Replace("*", string.Empty, StringComparison.Ordinal);
            if (!value.StartsWith('.')) value = "." + value;
            if (value.Length > 1) result.Add(value);
        }

        return result;
    }
}
