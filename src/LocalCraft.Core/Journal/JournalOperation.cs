namespace LocalCraft.Core.Journal;

public sealed class JournalOperation
{
    public int Sequence { get; set; }

    public string SourcePath { get; set; } = string.Empty;

    public string TargetPath { get; set; } = string.Empty;

    /// <summary>被覆盖目标文件的本地备份路径；没有覆盖时为空。</summary>
    public string? BackupPath { get; set; }

    /// <summary>仅大小写变化时使用的临时路径。</summary>
    public string? StagingPath { get; set; }

    public bool OverwroteTarget { get; set; }

    public JournalOperationState State { get; set; } = JournalOperationState.Pending;

    public string? Note { get; set; }
}
