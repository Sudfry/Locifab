using LocalCraft.App.Infrastructure;
using LocalCraft.Conversion.Planning;

namespace LocalCraft.App.ViewModels;

public sealed class ConversionPreviewRow : ViewModelBase
{
    private readonly ConversionPlanItem _item;
    private bool _isIncluded;

    public ConversionPreviewRow(ConversionPlanItem item)
    {
        _item = item;
        _isIncluded = item.IsExecutable;
    }

    public event EventHandler? InclusionChanged;

    public ConversionPlanItem Item => _item;

    public int Index => _item.Index;

    public string IndexText => _item.Index.ToString("00");

    public string SourceName => _item.SourceName;

    public string TargetName => string.IsNullOrWhiteSpace(_item.TargetName) ? "—" : _item.TargetName;

    public string SourcePath => _item.SourcePath;

    public string TargetPath => _item.TargetPath;

    public string Note => _item.Note;

    public string EngineName => _item.EngineName ?? "—";

    public string FormatName => _item.TargetFormat.DisplayName;

    public string StatusKey => _item.Status switch
    {
        ConversionItemStatus.Ready => "ready",
        ConversionItemStatus.MissingEngine => "missing",
        ConversionItemStatus.Invalid => "invalid",
        _ => "unsupported"
    };

    public string StatusText => _item.Status switch
    {
        ConversionItemStatus.Ready => "可转换",
        ConversionItemStatus.MissingEngine => "缺少引擎",
        ConversionItemStatus.Invalid => "无效",
        _ => "不支持"
    };

    public bool IsReady => _item.IsExecutable;

    public bool CanToggle => _item.IsExecutable;

    public bool IsIncluded
    {
        get => _isIncluded;
        set
        {
            if (!CanToggle) value = false;
            if (SetProperty(ref _isIncluded, value))
            {
                InclusionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
