namespace LocalCraft.Core.Journal;

/// <summary>
/// 可长期保存的操作日志。日志先于文件操作落盘，记录完整 old → new 映射。
/// </summary>
public sealed class OperationJournal
{
    public int FormatVersion { get; set; } = 1;

    public string Id { get; set; } = string.Empty;

    public string Kind { get; set; } = "rename";

    public string DisplayName { get; set; } = "批量重命名";

    public string SourceDirectory { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset? UndoneAt { get; set; }

    public JournalStatus Status { get; set; } = JournalStatus.Prepared;

    public string? Error { get; set; }

    public List<JournalOperation> Operations { get; set; } = new();

    public int CompletedOperationCount => Operations.Count(x =>
        x.State is JournalOperationState.Completed or JournalOperationState.Undone);

    public int OperationCount => Operations.Count;
}
