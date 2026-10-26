namespace LocalCraft.Core.Backup;

public enum BackupItemStatus
{
    New,
    Update,
    Same
}

public sealed class BackupPlanItem
{
    public string SourcePath { get; init; } = string.Empty;

    public string TargetPath { get; init; } = string.Empty;

    public string RelativePath { get; init; } = string.Empty;

    public long Size { get; init; }

    public DateTime LastWriteTimeUtc { get; init; }

    public BackupItemStatus Status { get; init; }

    public string StatusText => Status switch
    {
        BackupItemStatus.New => "新增",
        BackupItemStatus.Update => "更新",
        _ => "相同"
    };
}

public sealed class BackupPlan
{
    public IReadOnlyList<BackupPlanItem> Items { get; init; } = Array.Empty<BackupPlanItem>();

    public int SkippedCount { get; init; }

    public string? Error { get; init; }

    public bool Success => string.IsNullOrEmpty(Error);

    public int NewCount => Items.Count(x => x.Status == BackupItemStatus.New);

    public int UpdateCount => Items.Count(x => x.Status == BackupItemStatus.Update);

    public int SameCount => Items.Count(x => x.Status == BackupItemStatus.Same);

    public IReadOnlyList<BackupPlanItem> PendingItems => Items.Where(x => x.Status != BackupItemStatus.Same).ToList();
}
