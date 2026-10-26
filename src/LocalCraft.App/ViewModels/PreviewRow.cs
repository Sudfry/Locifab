using LocalCraft.App.Infrastructure;
using LocalCraft.Core.Rename;

namespace LocalCraft.App.ViewModels;

public sealed class PreviewRow : ViewModelBase
{
    private readonly RenamePlanItem _item;
    private bool _isIncluded;

    public PreviewRow(RenamePlanItem item)
    {
        _item = item;
        _isIncluded = item.IsExecutable;
    }

    public event EventHandler? InclusionChanged;

    public RenamePlanItem Item => _item;

    public int Index => _item.Index;

    public string IndexText => _item.Index.ToString("00");

    public string OriginalName => _item.OriginalName;

    public string ProposedName => _item.ProposedName;

    public string SourcePath => _item.SourcePath;

    public string TargetPath => _item.TargetPath;

    public string Note => _item.Note;

    public RenameItemStatus Status => _item.Status;

    public string StatusText => _item.Status switch
    {
        RenameItemStatus.Ready => "将重命名",
        RenameItemStatus.Overwrite => "将覆盖",
        RenameItemStatus.Conflict => "冲突",
        RenameItemStatus.Invalid => "无效",
        RenameItemStatus.Unchanged => "无变化",
        _ => _item.Status.ToString()
    };

    public string StatusKey => _item.Status switch
    {
        RenameItemStatus.Ready => "ready",
        RenameItemStatus.Overwrite => "overwrite",
        RenameItemStatus.Conflict => "conflict",
        RenameItemStatus.Invalid => "invalid",
        RenameItemStatus.Unchanged => "unchanged",
        _ => "unchanged"
    };

    public bool IsExecutable => _item.IsExecutable;

    public bool IsBlocking => _item.IsBlocking;

    public bool CanToggle => _item.Status != RenameItemStatus.Unchanged;

    public bool IsIncluded
    {
        get => _isIncluded;
        set
        {
            if (!CanToggle)
            {
                value = false;
            }

            if (SetProperty(ref _isIncluded, value))
            {
                InclusionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
