using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Media;
using LocalCraft.App.Infrastructure;
using LocalCraft.App.Services;
using LocalCraft.Core.Classify;
using LocalCraft.Core.Settings;

namespace LocalCraft.App.ViewModels;

public sealed record MotionLevelOption(MotionLevel Level, string DisplayName, string Description);
public sealed record ThemeOption(ThemeMode Mode, string DisplayName);
public sealed record AccentOption(AccentPalette Accent, string DisplayName);
public sealed record DensityOption(UiDensity Density, string DisplayName);
public sealed class BadgeEditItem : ViewModelBase
{
    private readonly Action<string> _changed;
    private string _text;

    public BadgeEditItem(string key, string title, string text, Action<string> changed)
    {
        Key = key;
        Title = title;
        _text = text;
        _changed = changed;
    }

    public string Key { get; }
    public string Title { get; }

    public string Text
    {
        get => _text;
        set
        {
            if (!SetProperty(ref _text, value ?? string.Empty)) return;
            _changed(_text.Trim());
        }
    }
}

public sealed record BackgroundColorChoice(string Name, string Hex);
public sealed record BackgroundStretchChoice(string Name, string Value);

public sealed record HomeFavoriteOption(string Key, string Title)
{
    public override string ToString() => Title;
}

public sealed record CategoryColorOption(string Name, string Hex)
{
    public Brush Brush { get; } = CreateBrush(Hex);
    private static Brush CreateBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly AppSettingsService _settings;
    private readonly IDialogService _dialogService;
    private bool _loading;

    private bool _animationsEnabled;
    private MotionLevelOption _selectedMotionLevel;
    private ThemeOption _selectedTheme;
    private AccentOption _selectedAccent;
    private DensityOption _selectedDensity;
    private double _sidebarWidth;
    private bool _defaultIncludeSubfolders;
    private bool _rememberLastPage;
    private double _dedupThreshold;
    private int _summarySentenceCount;
    private ClassificationModeOption _selectedClassificationMode;
    private CategoryColorOption _selectedFileColor;
    private CategoryColorOption _selectedTextColor;
    private CategoryColorOption _selectedMediaColor;
    private CategoryColorOption _selectedWorkflowColor;
    private CategoryColorOption _selectedSystemColor;
    private HomeFavoriteOption _homeFavorite1;
    private HomeFavoriteOption _homeFavorite2;
    private HomeFavoriteOption _homeFavorite3;
    private HomeFavoriteOption _homeFavorite4;
    private HomeFavoriteOption _homeFavorite5;
    private HomeFavoriteOption _homeFavorite6;
    private bool _showSidebarBadges;
    private bool _showSidebarColors;
    private double _sidebarFontSize;
    private bool _startOnGuide;
    private string _statusText = "设置会自动保存到本机。";
    private string _selectedBackgroundMode = "纯色";
    private string _backgroundColor = "#F4F6FB";
    private string _backgroundImagePath = string.Empty;
    private string _selectedBackgroundStretch = "UniformToFill";
    private double _backgroundImageOpacity = 0.35;
    private double _panelOpacity = 0.96;

    public SettingsViewModel(AppSettingsService settings, IDialogService dialogService)
    {
        _settings = settings;
        _dialogService = dialogService;

        MotionLevels = new ObservableCollection<MotionLevelOption>
        {
            new(MotionLevel.Low, "轻量", "仅页面淡入，减少持续动画"),
            new(MotionLevel.Normal, "标准", "页面切换、提示入场和按压缩放"),
            new(MotionLevel.Rich, "丰富", "更明显的页面位移与提示动画")
        };
        ThemeOptions = new ObservableCollection<ThemeOption>
        {
            new(ThemeMode.Auto, "跟随系统"),
            new(ThemeMode.Light, "浅色"),
            new(ThemeMode.Dark, "深色")
        };
        AccentOptions = new ObservableCollection<AccentOption>
        {
            new(AccentPalette.Indigo, "靛蓝"),
            new(AccentPalette.Blue, "蓝"),
            new(AccentPalette.Teal, "青"),
            new(AccentPalette.Green, "绿"),
            new(AccentPalette.Amber, "琥珀"),
            new(AccentPalette.Rose, "玫红"),
            new(AccentPalette.Slate, "石墨")
        };
        DensityOptions = new ObservableCollection<DensityOption>
        {
            new(UiDensity.Cozy, "舒适"),
            new(UiDensity.Compact, "紧凑")
        };
        HomeFavoriteOptions = new ObservableCollection<HomeFavoriteOption>
        {
            new(string.Empty, "（不显示）")
        };
        _homeFavorite1 = _homeFavorite2 = _homeFavorite3 = _homeFavorite4 = _homeFavorite5 = _homeFavorite6 = HomeFavoriteOptions[0];
        CategoryColors = new ObservableCollection<CategoryColorOption>
        {
            new("蓝", "#3B82F6"), new("靛蓝", "#6366F1"), new("紫", "#8B5CF6"),
            new("玫红", "#F43F5E"), new("橙", "#F59E0B"), new("琥珀", "#D97706"),
            new("绿", "#10B981"), new("青", "#14B8A6"), new("红", "#EF4444"),
            new("石墨", "#64748B")
        };
        SummarySentenceOptions = new ObservableCollection<int> { 3, 5, 8, 10 };
        ClassificationModes = new ObservableCollection<ClassificationModeOption>
        {
            new(ClassificationMode.ByType, "按文件类型"),
            new(ClassificationMode.ByYear, "按修改年份"),
            new(ClassificationMode.ByMonth, "按修改年月"),
            new(ClassificationMode.ByNamePrefix, "按名称前缀")
        };

        _animationsEnabled = settings.Current.AnimationsEnabled;
        _selectedMotionLevel = MotionLevels.FirstOrDefault(x => x.Level == settings.Current.MotionLevel) ?? MotionLevels[1];
        _selectedTheme = ThemeOptions.FirstOrDefault(x => x.Mode == settings.Current.Theme) ?? ThemeOptions[0];
        _selectedAccent = AccentOptions.FirstOrDefault(x => x.Accent == settings.Current.Accent) ?? AccentOptions[0];
        _selectedDensity = DensityOptions.FirstOrDefault(x => x.Density == settings.Current.Density) ?? DensityOptions[0];
        _sidebarWidth = settings.Current.SidebarWidth;
        _defaultIncludeSubfolders = settings.Current.DefaultIncludeSubfolders;
        _rememberLastPage = settings.Current.RememberLastPage;
        _dedupThreshold = settings.Current.DedupThreshold;
        _summarySentenceCount = settings.Current.SummarySentenceCount;
        _selectedClassificationMode = ClassificationModes.FirstOrDefault(
            x => string.Equals(x.Mode.ToString(), settings.Current.DefaultClassificationMode, StringComparison.OrdinalIgnoreCase))
            ?? ClassificationModes[0];
        _selectedFileColor = FindColor(settings.Current.FileCategoryColor);
        _selectedTextColor = FindColor(settings.Current.TextCategoryColor);
        _selectedMediaColor = FindColor(settings.Current.MediaCategoryColor);
        _selectedWorkflowColor = FindColor(settings.Current.WorkflowCategoryColor);
        _selectedSystemColor = FindColor(settings.Current.SystemCategoryColor);
        _showSidebarBadges = settings.Current.ShowSidebarBadges;
        _showSidebarColors = settings.Current.ShowSidebarColors;
        _sidebarFontSize = settings.Current.SidebarFontSize;
        _startOnGuide = settings.Current.StartOnGuide;
        _selectedBackgroundMode = settings.Current.BackgroundMode == "Image" ? "图片" : "纯色";
        _backgroundColor = settings.Current.BackgroundColor;
        _backgroundImagePath = settings.Current.BackgroundImagePath;
        _selectedBackgroundStretch = StretchOptions.Any(x => x.Value == settings.Current.BackgroundImageStretch)
            ? settings.Current.BackgroundImageStretch : "UniformToFill";
        _backgroundImageOpacity = settings.Current.BackgroundImageOpacity;
        _panelOpacity = settings.Current.PanelOpacity;
        BadgeItems = new ObservableCollection<BadgeEditItem>();

        OpenDataFolderCommand = new RelayCommand(OpenDataFolder);
        OpenSettingsFileCommand = new RelayCommand(OpenSettingsFile);
        BrowseBackgroundImageCommand = new RelayCommand(BrowseBackgroundImage);
        ClearBackgroundImageCommand = new RelayCommand(ClearBackgroundImage);
        OpenGithubCommand = new RelayCommand(() =>
        {
            try { Process.Start(new ProcessStartInfo("https://github.com/Sudfry/Locifab") { UseShellExecute = true }); }
            catch { }
        });
        ResetCommand = new RelayCommand(Reset, () => !string.IsNullOrWhiteSpace(SettingsFilePath));
        PreviewMotionCommand = new RelayCommand(() => PreviewMotionRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? PreviewMotionRequested;

    public ObservableCollection<MotionLevelOption> MotionLevels { get; }

    public ObservableCollection<ThemeOption> ThemeOptions { get; }

    public ObservableCollection<AccentOption> AccentOptions { get; }

    public ObservableCollection<DensityOption> DensityOptions { get; }

    public ObservableCollection<int> SummarySentenceOptions { get; }

    public ObservableCollection<ClassificationModeOption> ClassificationModes { get; }

    public RelayCommand OpenDataFolderCommand { get; }
    public RelayCommand OpenSettingsFileCommand { get; }
    public RelayCommand BrowseBackgroundImageCommand { get; }
    public RelayCommand ClearBackgroundImageCommand { get; }
    public RelayCommand OpenGithubCommand { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand PreviewMotionCommand { get; }

    public string SettingsFilePath => _settings.FilePath;

    public string DataRoot => _settings.DataRoot;

    public bool AnimationsEnabled
    {
        get => _animationsEnabled;
        set
        {
            if (!SetProperty(ref _animationsEnabled, value)) return;
            _settings.Current.AnimationsEnabled = value;
            SaveSettings("动画设置已更新");
        }
    }

    public ThemeOption SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (value is null || !SetProperty(ref _selectedTheme, value)) return;
            _settings.Current.Theme = value.Mode;
            SaveSettings("主题已更新");
        }
    }

    public AccentOption SelectedAccent
    {
        get => _selectedAccent;
        set
        {
            if (value is null || !SetProperty(ref _selectedAccent, value)) return;
            _settings.Current.Accent = value.Accent;
            SaveSettings("主题色已更新");
        }
    }

    public DensityOption SelectedDensity
    {
        get => _selectedDensity;
        set
        {
            if (value is null || !SetProperty(ref _selectedDensity, value)) return;
            _settings.Current.Density = value.Density;
            SaveSettings("界面密度已更新");
        }
    }

    public double SidebarWidth
    {
        get => _sidebarWidth;
        set
        {
            value = Math.Clamp(Math.Round(value), 200, 300);
            if (!SetProperty(ref _sidebarWidth, value)) return;
            OnPropertyChanged(nameof(SidebarWidthLabel));
            _settings.Current.SidebarWidth = (int)value;
            SaveSettings("侧栏宽度已更新");
        }
    }

    public string SidebarWidthLabel => $"{SidebarWidth:0} px";

    public MotionLevelOption SelectedMotionLevel
    {
        get => _selectedMotionLevel;
        set
        {
            if (value is null || !SetProperty(ref _selectedMotionLevel, value)) return;
            _settings.Current.MotionLevel = value.Level;
            SaveSettings("动效等级已更新");
        }
    }

    public bool DefaultIncludeSubfolders
    {
        get => _defaultIncludeSubfolders;
        set
        {
            if (!SetProperty(ref _defaultIncludeSubfolders, value)) return;
            _settings.Current.DefaultIncludeSubfolders = value;
            SaveSettings("子文件夹默认设置已保存");
        }
    }

    public bool RememberLastPage
    {
        get => _rememberLastPage;
        set
        {
            if (!SetProperty(ref _rememberLastPage, value)) return;
            _settings.Current.RememberLastPage = value;
            SaveSettings("页面记忆设置已保存");
        }
    }

    public double DedupThreshold
    {
        get => _dedupThreshold;
        set
        {
            value = Math.Clamp(value, 50, 100);
            if (!SetProperty(ref _dedupThreshold, value)) return;
            OnPropertyChanged(nameof(DedupThresholdLabel));
            _settings.Current.DedupThreshold = value;
            SaveSettings("去重阈值默认值已保存");
        }
    }

    public string DedupThresholdLabel => $"{DedupThreshold:0}%";

    public int SummarySentenceCount
    {
        get => _summarySentenceCount;
        set
        {
            if (!SetProperty(ref _summarySentenceCount, value)) return;
            _settings.Current.SummarySentenceCount = value;
            SaveSettings("摘要句数默认值已保存");
        }
    }

    public ClassificationModeOption SelectedClassificationMode
    {
        get => _selectedClassificationMode;
        set
        {
            if (value is null || !SetProperty(ref _selectedClassificationMode, value)) return;
            _settings.Current.DefaultClassificationMode = value.Mode.ToString();
            SaveSettings("分类默认方式已保存");
        }
    }

    public ObservableCollection<HomeFavoriteOption> HomeFavoriteOptions { get; }

    public void ConfigureHomeFavorites(IEnumerable<NavItem> navItems)
    {
        _loading = true;
        try
        {
            HomeFavoriteOptions.Clear();
            HomeFavoriteOptions.Add(new HomeFavoriteOption(string.Empty, "（不显示）"));
            foreach (NavItem nav in navItems)
            {
                if (nav.Key is "home" or "undo" or "settings") continue;
                HomeFavoriteOptions.Add(new HomeFavoriteOption(nav.Key, nav.Title));
            }

            var keys = _settings.Current.HomeFavoriteKeys ?? new List<string>();
            if (keys.Count == 0 && !_settings.Current.HasCustomHomeFavorites)
                keys = new List<string> { "rename", "convert", "findrep", "pipeline", "dedup", "guide" };
            _homeFavorite1 = FindFavorite(keys.ElementAtOrDefault(0));
            _homeFavorite2 = FindFavorite(keys.ElementAtOrDefault(1));
            _homeFavorite3 = FindFavorite(keys.ElementAtOrDefault(2));
            _homeFavorite4 = FindFavorite(keys.ElementAtOrDefault(3));
            _homeFavorite5 = FindFavorite(keys.ElementAtOrDefault(4));
            _homeFavorite6 = FindFavorite(keys.ElementAtOrDefault(5));
            OnPropertyChanged(nameof(HomeFavorite1));
            OnPropertyChanged(nameof(HomeFavorite2));
            OnPropertyChanged(nameof(HomeFavorite3));
            OnPropertyChanged(nameof(HomeFavorite4));
            OnPropertyChanged(nameof(HomeFavorite5));
            OnPropertyChanged(nameof(HomeFavorite6));
        }
        finally
        {
            _loading = false;
        }
    }

    private HomeFavoriteOption FindFavorite(string? key) =>
        HomeFavoriteOptions.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase))
        ?? HomeFavoriteOptions[0];

    public HomeFavoriteOption HomeFavorite1 { get => _homeFavorite1; set => SetFavorite(ref _homeFavorite1, value, nameof(HomeFavorite1)); }
    public HomeFavoriteOption HomeFavorite2 { get => _homeFavorite2; set => SetFavorite(ref _homeFavorite2, value, nameof(HomeFavorite2)); }
    public HomeFavoriteOption HomeFavorite3 { get => _homeFavorite3; set => SetFavorite(ref _homeFavorite3, value, nameof(HomeFavorite3)); }
    public HomeFavoriteOption HomeFavorite4 { get => _homeFavorite4; set => SetFavorite(ref _homeFavorite4, value, nameof(HomeFavorite4)); }
    public HomeFavoriteOption HomeFavorite5 { get => _homeFavorite5; set => SetFavorite(ref _homeFavorite5, value, nameof(HomeFavorite5)); }
    public HomeFavoriteOption HomeFavorite6 { get => _homeFavorite6; set => SetFavorite(ref _homeFavorite6, value, nameof(HomeFavorite6)); }

    private void SetFavorite(ref HomeFavoriteOption field, HomeFavoriteOption value, string propertyName)
    {
        if (value is null || ReferenceEquals(field, value)) return;
        field = value;
        OnPropertyChanged(propertyName);
        if (_loading) return;
        var keys = new[]
        {
            HomeFavorite1.Key, HomeFavorite2.Key, HomeFavorite3.Key,
            HomeFavorite4.Key, HomeFavorite5.Key, HomeFavorite6.Key
        }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToList();
        _settings.Current.HomeFavoriteKeys = keys;
        _settings.Current.HasCustomHomeFavorites = true;
        SaveSettings("首页常用功能已更新");
    }

    public ObservableCollection<CategoryColorOption> CategoryColors { get; }

    public CategoryColorOption SelectedFileColor
    {
        get => _selectedFileColor;
        set
        {
            if (value is null || !SetProperty(ref _selectedFileColor, value)) return;
            _settings.Current.FileCategoryColor = value.Hex;
            SaveSettings("文件分类颜色已更新");
        }
    }

    public CategoryColorOption SelectedTextColor
    {
        get => _selectedTextColor;
        set
        {
            if (value is null || !SetProperty(ref _selectedTextColor, value)) return;
            _settings.Current.TextCategoryColor = value.Hex;
            SaveSettings("文本分类颜色已更新");
        }
    }

    public CategoryColorOption SelectedMediaColor
    {
        get => _selectedMediaColor;
        set
        {
            if (value is null || !SetProperty(ref _selectedMediaColor, value)) return;
            _settings.Current.MediaCategoryColor = value.Hex;
            SaveSettings("图片分类颜色已更新");
        }
    }

    public CategoryColorOption SelectedWorkflowColor
    {
        get => _selectedWorkflowColor;
        set
        {
            if (value is null || !SetProperty(ref _selectedWorkflowColor, value)) return;
            _settings.Current.WorkflowCategoryColor = value.Hex;
            SaveSettings("效率分类颜色已更新");
        }
    }

    public CategoryColorOption SelectedSystemColor
    {
        get => _selectedSystemColor;
        set
        {
            if (value is null || !SetProperty(ref _selectedSystemColor, value)) return;
            _settings.Current.SystemCategoryColor = value.Hex;
            SaveSettings("系统分类颜色已更新");
        }
    }

    private CategoryColorOption FindColor(string hex) =>
        CategoryColors.FirstOrDefault(x => string.Equals(x.Hex, hex, StringComparison.OrdinalIgnoreCase))
        ?? CategoryColors[0];

    public IReadOnlyList<string> BackgroundModeOptions { get; } = new[] { "纯色", "图片" };
    public IReadOnlyList<BackgroundColorChoice> BackgroundColorOptions { get; } = new[]
    {
        new BackgroundColorChoice("雾白", "#F4F6FB"),
        new BackgroundColorChoice("暖白", "#FAF7F2"),
        new BackgroundColorChoice("薄荷", "#F1F8F4"),
        new BackgroundColorChoice("淡紫", "#F4F1FB"),
        new BackgroundColorChoice("深灰", "#2B2F3A"),
        new BackgroundColorChoice("午夜", "#1E2430")
    };
    public IReadOnlyList<BackgroundStretchChoice> StretchOptions { get; } = new[]
    {
        new BackgroundStretchChoice("填满", "UniformToFill"),
        new BackgroundStretchChoice("适应", "Uniform"),
        new BackgroundStretchChoice("拉伸", "Fill"),
        new BackgroundStretchChoice("居中", "None")
    };
    public ObservableCollection<BadgeEditItem> BadgeItems { get; }

    public void ConfigureBadges(IEnumerable<NavItem> navItems)
    {
        BadgeItems.Clear();
        foreach (NavItem nav in navItems)
        {
            string text = _settings.Current.NavBadges.TryGetValue(nav.Key, out string? custom)
                && !string.IsNullOrWhiteSpace(custom)
                ? custom
                : nav.DefaultBadge;
            string key = nav.Key;
            BadgeItems.Add(new BadgeEditItem(key, nav.Title, text, value =>
            {
                if (string.IsNullOrWhiteSpace(value)) _settings.Current.NavBadges.Remove(key);
                else _settings.Current.NavBadges[key] = value;
                SaveSettings("侧栏小字已更新");
            }));
        }
    }

    public bool ShowSidebarBadges
    {
        get => _showSidebarBadges;
        set
        {
            if (!SetProperty(ref _showSidebarBadges, value)) return;
            _settings.Current.ShowSidebarBadges = value;
            SaveSettings("侧栏徽标显示设置已保存");
        }
    }

    public bool ShowSidebarColors
    {
        get => _showSidebarColors;
        set
        {
            if (!SetProperty(ref _showSidebarColors, value)) return;
            _settings.Current.ShowSidebarColors = value;
            SaveSettings("侧栏分类颜色设置已保存");
        }
    }

    public double SidebarFontSize
    {
        get => _sidebarFontSize;
        set
        {
            value = Math.Clamp(Math.Round(value, 1), 10, 15);
            if (!SetProperty(ref _sidebarFontSize, value)) return;
            OnPropertyChanged(nameof(SidebarFontSizeLabel));
            _settings.Current.SidebarFontSize = value;
            SaveSettings("侧栏字号已更新");
        }
    }

    public string SidebarFontSizeLabel => $"{SidebarFontSize:0.0} px";

    public string SelectedBackgroundMode
    {
        get => _selectedBackgroundMode;
        set
        {
            if (!SetProperty(ref _selectedBackgroundMode, value ?? "纯色")) return;
            _settings.Current.BackgroundMode = value == "图片" ? "Image" : "Solid";
            SaveSettings("页面背景模式已更新");
        }
    }

    public string BackgroundColor
    {
        get => _backgroundColor;
        set
        {
            if (!SetProperty(ref _backgroundColor, value ?? string.Empty)) return;
            _settings.Current.BackgroundColor = _backgroundColor.Trim();
            SaveSettings("背景颜色已更新");
        }
    }

    public string BackgroundImagePath
    {
        get => _backgroundImagePath;
        set
        {
            if (!SetProperty(ref _backgroundImagePath, value ?? string.Empty)) return;
            _settings.Current.BackgroundImagePath = _backgroundImagePath.Trim();
            SaveSettings("背景图片已更新");
        }
    }

    public string SelectedBackgroundStretch
    {
        get => _selectedBackgroundStretch;
        set
        {
            if (!SetProperty(ref _selectedBackgroundStretch, value ?? "UniformToFill")) return;
            _settings.Current.BackgroundImageStretch = _selectedBackgroundStretch;
            SaveSettings("背景伸缩方式已更新");
        }
    }

    public double BackgroundImageOpacity
    {
        get => _backgroundImageOpacity;
        set
        {
            value = Math.Clamp(Math.Round(value, 2), 0.05, 1);
            if (!SetProperty(ref _backgroundImageOpacity, value)) return;
            _settings.Current.BackgroundImageOpacity = value;
            SaveSettings("背景图片透明度已更新");
        }
    }

    private void BrowseBackgroundImage()
    {
        var files = _dialogService.PickFiles("图片|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|所有文件|*.*", null);
        if (files.Count > 0)
        {
            BackgroundImagePath = files[0];
            SelectedBackgroundMode = "图片";
        }
    }

    private void ClearBackgroundImage()
    {
        BackgroundImagePath = string.Empty;
        SelectedBackgroundMode = "纯色";
    }

    public double PanelOpacity
    {
        get => _panelOpacity;
        set
        {
            value = Math.Clamp(Math.Round(value, 2), 0.6, 1);
            if (!SetProperty(ref _panelOpacity, value)) return;
            _settings.Current.PanelOpacity = value;
            SaveSettings("面板透明度已更新");
        }
    }

    public string PanelOpacityLabel => $"{PanelOpacity:P0}";

    public bool StartOnGuide
    {
        get => _startOnGuide;
        set
        {
            if (!SetProperty(ref _startOnGuide, value)) return;
            _settings.Current.StartOnGuide = value;
            SaveSettings("启动页面设置已保存");
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    private void SaveSettings(string message)
    {
        if (_loading) return;
        try
        {
            _settings.Save();
            MotionPreferences.Apply(_settings.Current);
            ThemeService.Apply(_settings.Current);
            StatusText = $"{message} · {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            StatusText = "设置保存失败：" + ex.Message;
            _dialogService.ShowError("设置保存失败", ex.Message);
        }
    }

    private void OpenDataFolder()
    {
        try
        {
            ShellService.OpenFolder(_settings.DataRoot);
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("无法打开数据目录", ex.Message);
        }
    }

    private void OpenSettingsFile()
    {
        try
        {
            if (File.Exists(SettingsFilePath)) ShellService.RevealFile(SettingsFilePath);
            else OpenDataFolder();
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("无法打开设置文件", ex.Message);
        }
    }

    private void Reset()
    {
        if (!_dialogService.Confirm("恢复默认设置", "将恢复动效、默认处理参数和页面记忆设置。已有文件与操作日志不受影响。"))
        {
            return;
        }

        try
        {
            _settings.ResetToDefaults();
            MotionPreferences.Apply(_settings.Current);
            ThemeService.Apply(_settings.Current);
            LoadFromSettings();
            StatusText = $"已恢复默认设置 · {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("恢复默认设置失败", ex.Message);
        }
    }

    private void LoadFromSettings()
    {
        _loading = true;
        try
        {
            AnimationsEnabled = _settings.Current.AnimationsEnabled;
            SelectedMotionLevel = MotionLevels.FirstOrDefault(x => x.Level == _settings.Current.MotionLevel) ?? MotionLevels[1];
            SelectedTheme = ThemeOptions.FirstOrDefault(x => x.Mode == _settings.Current.Theme) ?? ThemeOptions[0];
            SelectedAccent = AccentOptions.FirstOrDefault(x => x.Accent == _settings.Current.Accent) ?? AccentOptions[0];
            SelectedDensity = DensityOptions.FirstOrDefault(x => x.Density == _settings.Current.Density) ?? DensityOptions[0];
            SidebarWidth = _settings.Current.SidebarWidth;
            DefaultIncludeSubfolders = _settings.Current.DefaultIncludeSubfolders;
            RememberLastPage = _settings.Current.RememberLastPage;
            DedupThreshold = _settings.Current.DedupThreshold;
            SummarySentenceCount = _settings.Current.SummarySentenceCount;
            SelectedClassificationMode = ClassificationModes.FirstOrDefault(
                x => string.Equals(x.Mode.ToString(), _settings.Current.DefaultClassificationMode, StringComparison.OrdinalIgnoreCase))
                ?? ClassificationModes[0];
            SelectedFileColor = FindColor(_settings.Current.FileCategoryColor);
            SelectedTextColor = FindColor(_settings.Current.TextCategoryColor);
            SelectedMediaColor = FindColor(_settings.Current.MediaCategoryColor);
            SelectedWorkflowColor = FindColor(_settings.Current.WorkflowCategoryColor);
            SelectedSystemColor = FindColor(_settings.Current.SystemCategoryColor);
            ShowSidebarBadges = _settings.Current.ShowSidebarBadges;
            ShowSidebarColors = _settings.Current.ShowSidebarColors;
            SidebarFontSize = _settings.Current.SidebarFontSize;
            StartOnGuide = _settings.Current.StartOnGuide;
        }
        finally
        {
            _loading = false;
        }
    }
}
