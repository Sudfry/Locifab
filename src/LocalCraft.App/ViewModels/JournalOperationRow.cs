using LocalCraft.Core.Journal;

namespace LocalCraft.App.ViewModels;

public sealed class JournalOperationRow
{
    public JournalOperationRow(JournalOperation operation)
    {
        Sequence = operation.Sequence;
        SourcePath = operation.SourcePath;
        TargetPath = operation.TargetPath;
        OldName = Path.GetFileName(operation.SourcePath);
        NewName = Path.GetFileName(operation.TargetPath);
        Directory = Path.GetDirectoryName(operation.SourcePath) ?? string.Empty;
        StateText = operation.State switch
        {
            JournalOperationState.Completed => "已执行",
            JournalOperationState.Undone => "已回退",
            JournalOperationState.BackedUp => "已备份",
            JournalOperationState.Staging => "改名中",
            _ => "待执行"
        };
    }

    public int Sequence { get; }

    public string OldName { get; }

    public string NewName { get; }

    public string Directory { get; }

    public string SourcePath { get; }

    public string TargetPath { get; }

    public string StateText { get; }
}
