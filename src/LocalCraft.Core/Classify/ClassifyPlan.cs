namespace LocalCraft.Core.Classify;

public sealed class ClassifyPlan
{
    public ClassifyPlan(
        IReadOnlyList<ClassifyPlanItem> items,
        string rootDirectory,
        ClassificationMode mode,
        DateTimeOffset generatedAt)
    {
        Items = items;
        RootDirectory = rootDirectory;
        Mode = mode;
        GeneratedAt = generatedAt;
    }

    public IReadOnlyList<ClassifyPlanItem> Items { get; }

    public string RootDirectory { get; }

    public ClassificationMode Mode { get; }

    public DateTimeOffset GeneratedAt { get; }

    public int ReadyCount => Items.Count(static x => x.Status == ClassifyItemStatus.Ready);

    public int ConflictCount => Items.Count(static x => x.Status == ClassifyItemStatus.Conflict);

    public int UnchangedCount => Items.Count(static x => x.Status == ClassifyItemStatus.Unchanged);

    public int InvalidCount => Items.Count(static x => x.Status == ClassifyItemStatus.Invalid);

    public int TotalCount => Items.Count;
}
