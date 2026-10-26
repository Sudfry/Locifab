using LocalCraft.App.Infrastructure;
using LocalCraft.Core.Classify;

namespace LocalCraft.App.ViewModels;

public sealed class ClassifyPreviewRow : ViewModelBase
{
    private readonly ClassifyPlanItem _item;
    private bool _isIncluded;

    public ClassifyPreviewRow(ClassifyPlanItem item)
    {
        _item = item;
        _isIncluded = item.Status == ClassifyItemStatus.Ready;
    }

    public event EventHandler? InclusionChanged;

    public ClassifyPlanItem Item => _item;

    public string IndexText => _item.Index.ToString("00");

    public string FileName => _item.FileName;

    public string SourcePath => _item.SourcePath;

    public string TargetPath => _item.TargetPath;

    public string Category => _item.Category;

    public string TargetFolder => _item.TargetFolder;

    public string Note => _item.Note;

    public ClassifyItemStatus Status => _item.Status;

    public string StatusText => _item.Status switch
    {
        ClassifyItemStatus.Ready => "将移动",
        ClassifyItemStatus.Conflict => "冲突",
        ClassifyItemStatus.Unchanged => "已归类",
        _ => "无效"
    };

    public string StatusKey => _item.Status switch
    {
        ClassifyItemStatus.Ready => "ready",
        ClassifyItemStatus.Conflict => "conflict",
        ClassifyItemStatus.Unchanged => "unchanged",
        _ => "invalid"
    };

    public bool CanToggle => _item.Status is ClassifyItemStatus.Ready or ClassifyItemStatus.Conflict or ClassifyItemStatus.Invalid;

    public bool IsIncluded
    {
        get => _isIncluded;
        set
        {
            if (!CanToggle) value = false;
            if (!SetProperty(ref _isIncluded, value)) return;
            InclusionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

public sealed record ClassificationModeOption(ClassificationMode Mode, string DisplayName);
