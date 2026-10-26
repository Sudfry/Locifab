namespace LocalCraft.Core.Rename;

/// <summary>
/// 预览表中的一行。只描述计划，不包含任何对文件系统的写入。
/// </summary>
public sealed class RenamePlanItem
{
    public required int Index { get; init; }

    public required string SourcePath { get; init; }

    public required string TargetPath { get; init; }

    public required string OriginalName { get; init; }

    public required string ProposedName { get; init; }

    public required RenameItemStatus Status { get; init; }

    public required string Note { get; init; }

    public bool IsCaseOnlyChange { get; init; }

    public bool IsExecutable => Status is RenameItemStatus.Ready or RenameItemStatus.Overwrite;

    public bool IsBlocking => Status is RenameItemStatus.Conflict or RenameItemStatus.Invalid;

    public string SourceDirectory => Path.GetDirectoryName(SourcePath) ?? string.Empty;
}
