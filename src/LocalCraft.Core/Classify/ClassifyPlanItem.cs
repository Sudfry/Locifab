namespace LocalCraft.Core.Classify;

public sealed class ClassifyPlanItem
{
    public required int Index { get; init; }

    public required string SourcePath { get; init; }

    public required string TargetPath { get; init; }

    public required string Category { get; init; }

    public required ClassifyItemStatus Status { get; init; }

    public required string Note { get; init; }

    public string FileName => Path.GetFileName(SourcePath);

    public string TargetFolder => Path.GetDirectoryName(TargetPath) ?? string.Empty;

    public bool IsExecutable => Status == ClassifyItemStatus.Ready;
}
