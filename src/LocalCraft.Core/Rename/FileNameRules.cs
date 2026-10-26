using System.Globalization;

namespace LocalCraft.Core.Rename;

internal static class FileNameRules
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string BuildName(string originalName, int sequence, RenameOptions options)
    {
        string extension = Path.GetExtension(originalName);
        string stem = Path.GetFileNameWithoutExtension(originalName);
        string workingName = originalName;

        if (options.FindText.Length > 0 && options.IncludeExtension)
        {
            workingName = Replace(workingName, options.FindText, options.ReplaceText, options.MatchCase);
            extension = Path.GetExtension(workingName);
            stem = Path.GetFileNameWithoutExtension(workingName);
        }
        else if (options.FindText.Length > 0)
        {
            stem = Replace(stem, options.FindText, options.ReplaceText, options.MatchCase);
        }

        foreach (RenameReplaceRule rule in options.ReplaceRules)
        {
            if (rule is null || rule.FindText.Length == 0) continue;
            if (options.IncludeExtension)
            {
                workingName = Replace(workingName, rule.FindText, rule.ReplaceText, options.MatchCase);
                extension = Path.GetExtension(workingName);
                stem = Path.GetFileNameWithoutExtension(workingName);
            }
            else
            {
                stem = Replace(stem, rule.FindText, rule.ReplaceText, options.MatchCase);
            }
        }

        if (options.UseNumbering)
        {
            int padding = Math.Clamp(options.NumberPadding, 1, 10);
            string number = sequence.ToString("D" + padding, CultureInfo.InvariantCulture);
            string separator = options.NumberSeparator ?? string.Empty;
            stem = options.NumberBeforeName
                ? number + separator + stem
                : stem + separator + number;
        }

        return (options.Prefix ?? string.Empty) + stem + (options.Suffix ?? string.Empty) + extension;
    }

    public static bool TryValidate(string fileName, string sourcePath, out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(fileName))
        {
            error = "新文件名为空";
            return false;
        }

        if (fileName.Length > 255)
        {
            error = "文件名超过 255 个字符";
            return false;
        }

        if (fileName.EndsWith(' ') || fileName.EndsWith('.'))
        {
            error = "文件名不能以空格或句点结尾";
            return false;
        }

        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            error = "文件名包含非法字符";
            return false;
        }

        string stem = Path.GetFileNameWithoutExtension(fileName).TrimEnd(' ');
        if (ReservedNames.Contains(stem))
        {
            error = $"「{stem}」是 Windows 保留名称";
            return false;
        }

        if (string.IsNullOrWhiteSpace(stem))
        {
            error = "文件名主体不能为空";
            return false;
        }

        string directory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
        string targetPath = Path.Combine(directory, fileName);
        if (targetPath.Length > 259)
        {
            error = "目标完整路径超过 259 个字符";
            return false;
        }

        return true;
    }

    private static string Replace(string value, string oldValue, string newValue, bool matchCase)
    {
        if (oldValue.Length == 0) return value;
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return value.Replace(oldValue, newValue ?? string.Empty, comparison);
    }
}
