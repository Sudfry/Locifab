using System.Collections.ObjectModel;
using System.Windows.Media;
using LocalCraft.App.Infrastructure;
using LocalCraft.Core.Settings;

namespace LocalCraft.App.ViewModels;

public sealed class HomeAction
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Badge { get; init; }
    public required string Hint { get; init; }
    public Brush CategoryBrush { get; init; } = Brushes.SlateGray;
    public Brush CategorySoftBrush { get; init; } = Brushes.Transparent;
    public Brush CategoryHighlightBrush { get; init; } = Brushes.Transparent;
}

public sealed class HomeViewModel : ViewModelBase
{
    private readonly AppSettingsService? _settings;
    private bool _isWelcomeVisible;

    public HomeViewModel(AppSettingsService? settings = null)
    {
        _settings = settings;
        _isWelcomeVisible = settings?.Current.HasSeenWelcome != true;
        Actions = new ObservableCollection<HomeAction>
        {
            new() { Key = "rename", Title = "批量重命名", Badge = "文件", Hint = "统一文件名、加日期或编号", Description = "先看新旧名称对照，再执行；原文件可回退。" },
            new() { Key = "convert", Title = "格式转换", Badge = "转换", Hint = "文档/图片统一格式", Description = "输出新文件，不覆盖原文件；适合交付前统一格式。" },
            new() { Key = "findrep", Title = "文本替换", Badge = "文本", Hint = "批量改文字内容", Description = "逐处预览、勾选后替换，写入前先备份。" },
            new() { Key = "pipeline", Title = "任务流水线", Badge = "自动化", Hint = "把多个步骤串起来", Description = "重命名、压缩、清单、图片等步骤可组合并保存模板。" },
            new() { Key = "dedup", Title = "内容去重", Badge = "整理", Hint = "找出相似文档", Description = "只移动重复项到隔离目录，不直接删除。" },
            new() { Key = "guide", Title = "功能说明", Badge = "帮助", Hint = "每个工具的用途和例子", Description = "不知道从哪开始？先看每个功能的适用场景。" }
        };
        OpenCommand = new RelayCommand(parameter =>
        {
            string? key = parameter?.ToString();
            if (!string.IsNullOrWhiteSpace(key)) NavigateRequested?.Invoke(this, key);
        });
        DismissWelcomeCommand = new RelayCommand(DismissWelcome);
    }

    public event EventHandler<string>? NavigateRequested;
    public ObservableCollection<HomeAction> Actions { get; }
    public RelayCommand OpenCommand { get; }
    public RelayCommand DismissWelcomeCommand { get; }

    public void ConfigureFavorites(IReadOnlyList<NavItem> navItems)
    {
        var defaults = new[] { "rename", "convert", "findrep", "pipeline", "dedup", "guide" };
        var keys = _settings?.Current.HomeFavoriteKeys;
        if (keys is null || (keys.Count == 0 && _settings?.Current.HasCustomHomeFavorites != true))
            keys = defaults.ToList();
        Actions.Clear();
        foreach (string key in keys.Take(6))
        {
            NavItem? nav = navItems.FirstOrDefault(
                x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
            if (nav is not null) Actions.Add(CreateAction(nav));
        }
    }

    public void AddFavorite(NavItem nav)
    {
        if (Actions.Any(x => string.Equals(x.Key, nav.Key, StringComparison.OrdinalIgnoreCase))) return;
        if (Actions.Count >= 6) return;
        Actions.Add(CreateAction(nav));
        if (_settings is not null)
        {
            _settings.Current.HomeFavoriteKeys = Actions.Select(x => x.Key).ToList();
            _settings.Current.HasCustomHomeFavorites = true;
            try { _settings.Save(); } catch { }
        }
    }

    private static HomeAction CreateAction(NavItem nav) => new()
    {
        Key = nav.Key,
        Title = nav.Title,
        Badge = nav.Badge,
        Hint = nav.Subtitle,
        CategoryBrush = nav.CategoryBrush,
        CategorySoftBrush = nav.CategorySoftBrush,
        CategoryHighlightBrush = nav.CategoryHighlightBrush,
        Description = nav.Key switch
        {
            "rename" => "先看新旧名称对照，再执行；原文件可回退。",
            "convert" => "输出新文件，不覆盖原文件；适合交付前统一格式。",
            "findrep" => "逐处预览、勾选后替换，写入前先备份。",
            "pipeline" => "重命名、压缩、清单、图片等步骤可组合并保存模板。",
            "dedup" => "只移动重复项到隔离目录，不直接删除。",
            "guide" => "不知道从哪开始？先看每个功能的适用场景。",
            _ => nav.Subtitle
        }
    };

    public bool IsWelcomeVisible
    {
        get => _isWelcomeVisible;
        private set => SetProperty(ref _isWelcomeVisible, value);
    }

    public void DismissWelcome()
    {
        IsWelcomeVisible = false;
        if (_settings is null) return;
        try
        {
            _settings.Current.HasSeenWelcome = true;
            _settings.Save();
        }
        catch { }
    }
}
