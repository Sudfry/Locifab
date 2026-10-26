using System.Collections.ObjectModel;
using LocalCraft.Core.Journal;

namespace LocalCraft.App.ViewModels;

public sealed class JournalListItem
{
    public JournalListItem(JournalEntry entry)
    {
        Entry = entry;
        Operations = new ObservableCollection<JournalOperationRow>(
            entry.Journal.Operations
                .OrderBy(static x => x.Sequence)
                .Select(static x => new JournalOperationRow(x)));
    }

    public JournalEntry Entry { get; }

    public OperationJournal Journal => Entry.Journal;

    public ObservableCollection<JournalOperationRow> Operations { get; }

    public string IdShort => Journal.Id.Length >= 8 ? Journal.Id[..8] : Journal.Id;

    public string DisplayName => Journal.DisplayName;

    public string CreatedText => Journal.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");

    public string OperationCountText => $"{Journal.OperationCount} 个文件";

    public string SourceDirectory => Journal.SourceDirectory;

    public string StatusText => Journal.Status switch
    {
        JournalStatus.Completed => "可回退",
        JournalStatus.Undone => "已回退",
        JournalStatus.RolledBack => "已自动回滚",
        JournalStatus.Failed => "异常",
        JournalStatus.Undoing => "回退中",
        JournalStatus.Prepared => "未完成",
        _ => Journal.Status.ToString()
    };

    public string StatusKey => Journal.Status switch
    {
        JournalStatus.Completed => "completed",
        JournalStatus.Undone => "undone",
        JournalStatus.RolledBack => "rolledback",
        JournalStatus.Failed => "failed",
        _ => "neutral"
    };

    public bool CanUndo => Journal.Status == JournalStatus.Completed;

    public bool IsConversion => string.Equals(Journal.Kind, "convert", StringComparison.OrdinalIgnoreCase);

    public string UndoHint => CanUndo
        ? IsConversion
            ? "回退将删除本次生成的输出文件，原文件不受影响"
            : "可随时回退，不受时间限制"
        : Journal.Status switch
        {
            JournalStatus.Undone => IsConversion ? "本次输出文件已删除，原文件未改动" : "该批次已恢复到操作前状态",
            JournalStatus.RolledBack => IsConversion ? "转换失败后已自动清理输出" : "执行失败后已自动恢复，无需回退",
            JournalStatus.Failed => "日志记录了异常，请人工检查文件状态",
            JournalStatus.Prepared => "该批次未完整执行，无法自动回退",
            _ => "当前状态不可回退"
        };

    public string UndoActionText => IsConversion ? "↩  删除本次输出" : "↩  一键回退此批次";

    public string TimeRangeText
    {
        get
        {
            if (Journal.CompletedAt is null) return CreatedText;
            if (Journal.UndoneAt is null) return $"执行于 {Journal.CompletedAt:yyyy-MM-dd HH:mm:ss}";
            return $"完成 {Journal.CompletedAt:yyyy-MM-dd HH:mm:ss} · 回退 {Journal.UndoneAt:yyyy-MM-dd HH:mm:ss}";
        }
    }
}
