using System.IO.Compression;

namespace LocalCraft.Core.Archives;

/// <summary>
/// 只读规划压缩/解压。解压目标会避开 zip slip，并自动给同名文件加序号。
/// </summary>
public sealed class ArchivePlanner
{
    private static readonly EnumerationOptions Enumeration = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        ReturnSpecialDirectories = false
    };

    public ArchivePlan Plan(ArchiveOptions options)
    {
        return options.Mode == ArchiveMode.Compress
            ? PlanCompress(options)
            : PlanExtract(options);
    }

    private static ArchivePlan PlanCompress(ArchiveOptions options)
    {
        if (!Directory.Exists(options.SourcePath))
        {
            return new ArchivePlan { Mode = ArchiveMode.Compress, Error = "请选择要压缩的文件夹。" };
        }

        if (string.IsNullOrWhiteSpace(options.TargetPath))
        {
            return new ArchivePlan { Mode = ArchiveMode.Compress, Error = "请选择输出的 ZIP 文件。" };
        }

        string source = Path.GetFullPath(options.SourcePath);
        string target = Path.GetFullPath(options.TargetPath);
        if (!string.Equals(Path.GetExtension(target), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            target += ".zip";
        }

        if (File.Exists(target))
        {
            return new ArchivePlan { Mode = ArchiveMode.Compress, Error = "输出 ZIP 已存在，请换一个文件名。" };
        }

        if (IsSubPath(target, source))
        {
            return new ArchivePlan { Mode = ArchiveMode.Compress, Error = "输出 ZIP 不能放在被压缩的文件夹内部。" };
        }

        var entries = new List<ArchivePlanEntry>();
        try
        {
            foreach (string file in Directory.EnumerateFiles(source, "*", Enumeration))
            {
                var info = new FileInfo(file);
                string relative = Path.GetRelativePath(source, file);
                entries.Add(new ArchivePlanEntry
                {
                    Name = relative,
                    Size = info.Length,
                    TargetPath = target
                });
            }
        }
        catch (Exception ex)
        {
            return new ArchivePlan { Mode = ArchiveMode.Compress, Error = "扫描压缩源失败：" + ex.Message };
        }

        if (entries.Count == 0)
        {
            return new ArchivePlan { Mode = ArchiveMode.Compress, Error = "源文件夹里没有可压缩的文件。" };
        }

        return new ArchivePlan
        {
            Mode = ArchiveMode.Compress,
            SourcePath = source,
            TargetPath = target,
            Password = options.Password ?? string.Empty,
            Entries = entries
        };
    }

    private static ArchivePlan PlanExtract(ArchiveOptions options)
    {
        if (!File.Exists(options.SourcePath)
            || !string.Equals(Path.GetExtension(options.SourcePath), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            return new ArchivePlan { Mode = ArchiveMode.Extract, Error = "请选择一个存在的 ZIP 文件。" };
        }

        if (string.IsNullOrWhiteSpace(options.TargetPath))
        {
            return new ArchivePlan { Mode = ArchiveMode.Extract, Error = "请选择解压到的文件夹。" };
        }

        string source = Path.GetFullPath(options.SourcePath);
        string targetRoot = Path.GetFullPath(options.TargetPath);
        var entries = new List<ArchivePlanEntry>();
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(source);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                string safeRelative = NormalizeEntry(entry.FullName);
                if (safeRelative.Length == 0) continue;
                if (!seen.Add(safeRelative)) continue;

                string target = Path.Combine(targetRoot, safeRelative);
                if (!IsSubPath(target, targetRoot))
                {
                    return new ArchivePlan { Mode = ArchiveMode.Extract, Error = "ZIP 包含不安全路径，已拒绝解压：" + entry.FullName };
                }

                entries.Add(new ArchivePlanEntry
                {
                    Name = safeRelative.Replace('\\', '/'),
                    Size = entry.Length,
                    TargetPath = GetUniquePath(target)
                });
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return new ArchivePlan { Mode = ArchiveMode.Extract, Error = "无法读取 ZIP：" + ex.Message };
        }

        if (entries.Count == 0)
        {
            return new ArchivePlan { Mode = ArchiveMode.Extract, Error = "ZIP 里没有文件。" };
        }

        return new ArchivePlan
        {
            Mode = ArchiveMode.Extract,
            SourcePath = source,
            TargetPath = targetRoot,
            Password = options.Password ?? string.Empty,
            Entries = entries
        };
    }

    public static string NormalizeEntry(string entryName)
    {
        string normalized = entryName.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
        var parts = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(Path.DirectorySeparatorChar, parts.Where(x => x != "." && x != ".."));
    }

    public static bool IsSubPath(string child, string parent)
    {
        string c = Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string p = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return c.StartsWith(p, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetUniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        string directory = Path.GetDirectoryName(path) ?? string.Empty;
        string name = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        for (int i = 2; i < 10_000; i++)
        {
            string candidate = Path.Combine(directory, $"{name} ({i}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }

        throw new IOException("无法为解压文件生成不冲突的名称：" + path);
    }
}
