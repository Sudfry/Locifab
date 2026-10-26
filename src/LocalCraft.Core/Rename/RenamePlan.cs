namespace LocalCraft.Core.Rename;

public sealed class RenamePlan
{
    public RenamePlan(IReadOnlyList<RenamePlanItem> items, RenameOptions options, DateTimeOffset generatedAt)
    {
        Items = items;
        Options = options;
        GeneratedAt = generatedAt;
    }

    public IReadOnlyList<RenamePlanItem> Items { get; }

    public RenameOptions Options { get; }

    public DateTimeOffset GeneratedAt { get; }

    public int ReadyCount => Items.Count(x => x.Status == RenameItemStatus.Ready);

    public int OverwriteCount => Items.Count(x => x.Status == RenameItemStatus.Overwrite);

    public int ConflictCount => Items.Count(x => x.IsBlocking);

    public int UnchangedCount => Items.Count(x => x.Status == RenameItemStatus.Unchanged);

    public int ExecutableCount => Items.Count(x => x.IsExecutable);

    public bool HasBlockingItems => ConflictCount > 0;

    public string SourceDirectory => Items.Count > 0
        ? Items[0].SourceDirectory
        : string.Empty;
}
