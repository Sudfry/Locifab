using System.Security.Cryptography;

namespace LocalCraft.Core.Backup;

/// <summary>
/// 只读分析源文件夹与目标文件夹的差异。只增不删，不修改源和目标。
/// </summary>
public sealed class BackupPlanner
{
    public BackupPlan Scan(BackupOptions options)
    {
        if (!Directory.Exists(options.SourceFolder))
        {
            return new BackupPlan { Error = "源文件夹不存在。" };
        }

        if (string.IsNullOrWhiteSpace(options.TargetFolder))
        {
            return new BackupPlan { Error = "请选择目标文件夹。" };
        }

        string sourceRoot = Path.GetFullPath(options.SourceFolder);
        string targetRoot = Path.GetFullPath(options.TargetFolder);
        if (string.Equals(sourceRoot, targetRoot, StringComparison.OrdinalIgnoreCase))
        {
            return new BackupPlan { Error = "源文件夹和目标文件夹不能相同。" };
        }

        if (IsSubPath(targetRoot, sourceRoot))
        {
            return new BackupPlan { Error = "目标文件夹不能位于源文件夹内部，否则会递归复制。" };
        }

        var include = ParseExtensions(options.IncludeExtensions);
        var exclude = ParseExtensions(options.ExcludeExtensions);
        long maxBytes = Math.Max(1, (long)(options.MaxFileSizeMb * 1024 * 1024));
        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = options.IncludeSubfolders,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = options.SkipHidden ? FileAttributes.Hidden | FileAttributes.System : 0
        };

        var items = new List<BackupPlanItem>();
        int skipped = 0;
        try
        {
            foreach (string source in Directory.EnumerateFiles(sourceRoot, "*", enumeration))
            {
                var info = new FileInfo(source);
                if (info.Length > maxBytes || !MatchesExtension(info.Extension, include, exclude))
                {
                    skipped++;
                    continue;
                }

                string relative = Path.GetRelativePath(sourceRoot, source);
                string target = Path.Combine(targetRoot, relative);
                BackupItemStatus status;
                if (!File.Exists(target))
                {
                    status = BackupItemStatus.New;
                }
                else
                {
                    var existing = new FileInfo(target);
                    bool same = existing.Length == info.Length
                                && Math.Abs((existing.LastWriteTimeUtc - info.LastWriteTimeUtc).TotalSeconds) < 2;
                    if (same && options.StrictCompare)
                    {
                        same = HashEquals(source, target);
                    }

                    status = same ? BackupItemStatus.Same : BackupItemStatus.Update;
                }

                items.Add(new BackupPlanItem
                {
                    SourcePath = info.FullName,
                    TargetPath = target,
                    RelativePath = relative,
                    Size = info.Length,
                    LastWriteTimeUtc = info.LastWriteTimeUtc,
                    Status = status
                });
            }
        }
        catch (Exception ex)
        {
            return new BackupPlan { Error = "分析失败：" + ex.Message };
        }

        items.Sort(static (x, y) => string.Compare(x.RelativePath, y.RelativePath, StringComparison.OrdinalIgnoreCase));
        return new BackupPlan { Items = items, SkippedCount = skipped };
    }

    public static bool IsSubPath(string child, string parent)
    {
        string normalizedChild = Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string normalizedParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return normalizedChild.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HashEquals(string first, string second)
    {
        using var a = SHA256.Create();
        using var b = SHA256.Create();
        using FileStream streamA = File.OpenRead(first);
        using FileStream streamB = File.OpenRead(second);
        return Convert.ToHexString(a.ComputeHash(streamA)) == Convert.ToHexString(b.ComputeHash(streamB));
    }

    private static bool MatchesExtension(string extension, HashSet<string> include, HashSet<string> exclude)
    {
        if (exclude.Contains(extension)) return false;
        return include.Count == 0 || include.Contains(extension);
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
