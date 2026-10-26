namespace LocalCraft.Core.Cleanup;

/// <summary>
/// 只读扫描空文件夹和 0 字节文件。不会删除任何内容。
/// </summary>
public sealed class CleanupPlanner
{
    public CleanupPlan Scan(CleanupOptions options)
    {
        if (!Directory.Exists(options.Folder))
        {
            return new CleanupPlan { Error = "请选择存在的文件夹。" };
        }

        string root = Path.GetFullPath(options.Folder);
        var items = new List<CleanupPlanItem>();
        var search = options.IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        try
        {
            if (options.RemoveZeroByteFiles)
            {
                foreach (string file in Directory.EnumerateFiles(root, "*", search))
                {
                    var info = new FileInfo(file);
                    if (info.Length != 0) continue;
                    if (!options.IncludeHidden
                        && info.Attributes.HasFlag(FileAttributes.Hidden | FileAttributes.System))
                    {
                        continue;
                    }

                    items.Add(new CleanupPlanItem { Path = info.FullName, IsDirectory = false });
                }
            }

            if (options.RemoveEmptyDirectories)
            {
                var allDirectories = Directory.EnumerateDirectories(root, "*", search)
                    .Select(Path.GetFullPath)
                    .ToList();
                var empty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string directory in allDirectories.OrderByDescending(Depth))
                {
                    if (!options.IncludeHidden
                        && new DirectoryInfo(directory).Attributes.HasFlag(FileAttributes.Hidden | FileAttributes.System))
                    {
                        continue;
                    }

                    bool hasFile = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly).Any();
                    bool hasNonEmptyChild = Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly)
                        .Any(child => !empty.Contains(Path.GetFullPath(child)));
                    if (!hasFile && !hasNonEmptyChild)
                    {
                        empty.Add(directory);
                        items.Add(new CleanupPlanItem { Path = directory, IsDirectory = true });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            return new CleanupPlan { Error = "扫描失败：" + ex.Message };
        }

        return new CleanupPlan
        {
            Items = items.OrderByDescending(x => x.IsDirectory)
                .ThenByDescending(x => Depth(x.Path))
                .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private static int Depth(string path) => path.Count(c => c == Path.DirectorySeparatorChar);
}
