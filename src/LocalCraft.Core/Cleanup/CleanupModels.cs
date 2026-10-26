namespace LocalCraft.Core.Cleanup;

public sealed class CleanupOptions
{
    public string Folder { get; set; } = string.Empty;

    public bool IncludeSubfolders { get; set; } = true;

    public bool RemoveEmptyDirectories { get; set; } = true;

    public bool RemoveZeroByteFiles { get; set; } = true;

    public bool IncludeHidden { get; set; }
}

public sealed class CleanupPlanItem
{
    public string Path { get; init; } = string.Empty;

    public bool IsDirectory { get; init; }

    public string Name => System.IO.Path.GetFileName(Path);

    public string KindText => IsDirectory ? "空文件夹" : "0 字节文件";
}

public sealed class CleanupPlan
{
    public IReadOnlyList<CleanupPlanItem> Items { get; init; } = Array.Empty<CleanupPlanItem>();

    public string? Error { get; init; }

    public bool Success => string.IsNullOrEmpty(Error);
}
