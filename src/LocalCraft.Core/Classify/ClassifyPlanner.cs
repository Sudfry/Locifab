using LocalCraft.Core.Infrastructure;

namespace LocalCraft.Core.Classify;

/// <summary>
/// 只读扫描与分类规划。生成目标文件夹和移动预览，不创建目录、不移动文件。
/// </summary>
public sealed class ClassifyPlanner
{
    private static readonly Dictionary<string, string[]> TypeFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["图片"] = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".heic", ".heif", ".tif", ".tiff", ".svg", ".raw", ".cr2", ".cr3", ".nef", ".arw", ".raf", ".rw2" },
        ["文档"] = new[] { ".txt", ".md", ".markdown", ".doc", ".docx", ".pdf", ".ppt", ".pptx", ".xls", ".xlsx", ".csv", ".rtf", ".html", ".htm", ".epub" },
        ["视频"] = new[] { ".mp4", ".mkv", ".mov", ".avi", ".wmv", ".flv", ".webm", ".m4v" },
        ["音频"] = new[] { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a", ".wma" },
        ["压缩包"] = new[] { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz" },
        ["安装包"] = new[] { ".exe", ".msi", ".msix", ".appx" }
    };

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public ClassifyPlan CreatePlan(string folderPath, ClassifyOptions options)
    {
        if (!Directory.Exists(folderPath))
        {
            return new ClassifyPlan(Array.Empty<ClassifyPlanItem>(), folderPath, options.Mode, DateTimeOffset.Now);
        }

        string root = Path.GetFullPath(folderPath);
        List<string> files = Scan(root, options.IncludeSubdirectories);
        files.Sort(static (x, y) =>
            NaturalStringComparer.Instance.Compare(Path.GetFileName(x), Path.GetFileName(y)));

        var items = new List<ClassifyPlanItem>(files.Count);
        for (int i = 0; i < files.Count; i++)
        {
            string source = files[i];
            string category = GetCategory(source, options);
            string safeCategory = SanitizeFolderName(category);
            string target = Path.Combine(root, safeCategory, Path.GetFileName(source));
            ClassifyItemStatus status;
            string note;

            if (string.Equals(Path.GetDirectoryName(source), Path.GetDirectoryName(target), StringComparison.OrdinalIgnoreCase))
            {
                status = ClassifyItemStatus.Unchanged;
                note = "文件已经在目标分类中";
            }
            else if (File.Exists(target) || Directory.Exists(target))
            {
                status = ClassifyItemStatus.Conflict;
                note = "目标位置存在同名文件，已阻断移动";
            }
            else if (target.Length > 259)
            {
                status = ClassifyItemStatus.Invalid;
                note = "目标路径超过 259 个字符";
            }
            else
            {
                try
                {
                    if (!Directory.Exists(Path.GetDirectoryName(target)))
                    {
                        // 规划阶段不创建文件夹，只验证路径可用于创建。
                        string? parent = Path.GetDirectoryName(target);
                        if (string.IsNullOrWhiteSpace(parent)) throw new IOException("目标文件夹路径为空。");
                    }

                    status = ClassifyItemStatus.Ready;
                    note = "准备移动到「" + safeCategory + "」";
                }
                catch (Exception ex) when (ex is IOException or ArgumentException)
                {
                    status = ClassifyItemStatus.Invalid;
                    note = "无法生成目标路径：" + ex.Message;
                }
            }

            items.Add(new ClassifyPlanItem
            {
                Index = i + 1,
                SourcePath = source,
                TargetPath = target,
                Category = safeCategory,
                Status = status,
                Note = note
            });
        }

        return new ClassifyPlan(items, root, options.Mode, DateTimeOffset.Now);
    }

    public static string GetCategory(string filePath, ClassifyOptions options)
    {
        return options.Mode switch
        {
            ClassificationMode.ByType => GetTypeCategory(filePath),
            ClassificationMode.ByYear => File.GetLastWriteTime(filePath).Year.ToString("0000"),
            ClassificationMode.ByMonth => File.GetLastWriteTime(filePath).ToString("yyyy-MM"),
            ClassificationMode.ByNamePrefix => GetNamePrefix(filePath, options.NamePrefixLength),
            _ => "其他"
        };
    }

    private static List<string> Scan(string root, bool includeSubdirectories)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = includeSubdirectories,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
        };

        var files = new List<string>();
        try
        {
            foreach (string file in Directory.EnumerateFiles(root, "*", options))
            {
                files.Add(Path.GetFullPath(file));
                if (files.Count >= 5_000) break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // 部分目录不可访问时保留已扫描结果。
        }

        return files;
    }

    private static string GetTypeCategory(string filePath)
    {
        string extension = Path.GetExtension(filePath);
        foreach (var folder in TypeFolders)
        {
            if (folder.Value.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                return folder.Key;
            }
        }
        return "其他";
    }

    private static string GetNamePrefix(string filePath, int length)
    {
        string name = Path.GetFileNameWithoutExtension(filePath).Trim();
        if (name.Length == 0) return "其他";
        length = Math.Clamp(length, 1, 4);
        return name[..Math.Min(length, name.Length)].ToUpperInvariant();
    }

    private static string SanitizeFolderName(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        name = name.Trim().TrimEnd('.', ' ');
        if (name.Length == 0) name = "其他";
        if (ReservedNames.Contains(name)) name = "_" + name;
        if (name.Length > 80) name = name[..80];
        return name;
    }
}
