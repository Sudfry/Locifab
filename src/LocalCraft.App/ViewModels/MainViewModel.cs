using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using LocalCraft.App.Infrastructure;
using LocalCraft.App.Services;
using LocalCraft.Core.Journal;
using LocalCraft.Core.Settings;

namespace LocalCraft.App.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly IDialogService _dialogService;
    private readonly ToolPresetStore _presetStore = new();
    private string _toolPresetName = string.Empty;
    private ToolPreset? _selectedToolPreset;
    private string _appliedPresetName = string.Empty;
    private readonly NavItem _undoNavItem;
    private readonly NavItem _settingsNavItem;
    private readonly AppSettingsService _settingsService;
    private NavItem _selectedNav;
    private readonly List<NavItem> _navHistory = new();
    private int _navHistoryIndex = -1;
    private bool _navigatingHistory;
    private bool _initializingNavigation;
    private NavItem? _homeNavItem;
    private NavItem? _pipelineNavItem;
    private NavItem? _undoReturnNav;
    private bool _sidebarCollapsed;

    public MainViewModel(
        JournalRepository repository,
        IDialogService dialogService,
        AppSettingsService settingsService)
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _sidebarCollapsed = settingsService.Current.SidebarCollapsed;
        _settingsService.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(SidebarWidthGrid));
            OnPropertyChanged(nameof(ShowSidebarBadges));
            OnPropertyChanged(nameof(SidebarFontSize));
            ApplyBadgeTexts();
            ApplyCategoryColors();
            if (NavItems is { Count: > 0 }) Home.ConfigureFavorites(ToolCatalog());
        };
        Settings = new SettingsViewModel(settingsService, dialogService);
        Guide = new GuideViewModel();
        Rename = new RenameViewModel(repository, dialogService, settingsService);
        Home = new HomeViewModel(settingsService);
        Toolbox = new ToolboxViewModel(repository, dialogService);
        Home.NavigateRequested += (_, key) =>
        {
            SetSelectedByKey(key);
        };
        Convert = new ConvertViewModel(repository, dialogService, settingsService);
        TextReplace = new TextReplaceViewModel(repository, dialogService, settingsService);
        Dedup = new DedupViewModel(repository, dialogService, settingsService);
        Backup = new BackupViewModel(repository, dialogService);
        Watch = new WatchViewModel(dialogService);
        Tables = new TableViewModel(repository, dialogService);
        Archive = new ArchiveViewModel(repository, dialogService);
        Cleanup = new CleanupViewModel(repository, dialogService);
        Checksum = new ChecksumViewModel(repository, dialogService);
        Images = new ImageBatchViewModel(repository, dialogService);
        Photos = new PhotoDedupViewModel(repository, dialogService);
        Metadata = new MetadataViewModel(repository, dialogService);
        Search = new SearchViewModel(dialogService);
        Report = new ReportViewModel(repository, dialogService);
        Pipeline = new PipelineViewModel(repository, dialogService);
        SplitMerge = new SplitMergeViewModel(repository, dialogService);
        FileState = new FileStateViewModel(repository, dialogService);
        Template = new TemplateViewModel(repository, dialogService);
        Diff = new DiffViewModel(dialogService);
        Classify = new ClassifyViewModel(repository, dialogService, settingsService);
        UndoCenter = new UndoCenterViewModel(repository, dialogService);
        Environment = new EnvironmentViewModel(repository, dialogService);
        Links = new LinksViewModel(repository, dialogService);
        Online = new OnlineViewModel();
        OpenSettingsCommand = new RelayCommand(() => SelectedNav = _settingsNavItem!);
        OpenHomeCommand = new RelayCommand(() =>
        {
            if (_homeNavItem is not null) SelectedNav = _homeNavItem;
        });
        ToggleSidebarCommand = new RelayCommand(ToggleSidebar);
        OpenPipelineCommand = new RelayCommand(() =>
        {
            if (_pipelineNavItem is not null) SelectedNav = _pipelineNavItem;
        });
        BackCommand = new RelayCommand(NavigateBack, () => CanGoBack);
        ForwardCommand = new RelayCommand(NavigateForward, () => CanGoForward);
        SaveToolPresetCommand = new RelayCommand(SaveToolPreset);
        ApplyToolPresetCommand = new RelayCommand(ApplyToolPreset, () => SelectedToolPreset is not null);
        DeleteToolPresetCommand = new RelayCommand(DeleteToolPreset, () => SelectedToolPreset is not null);
        ExportToolPresetCommand = new RelayCommand(ExportToolPreset, () => SelectedToolPreset is not null);
        ImportToolPresetCommand = new RelayCommand(ImportToolPreset);
        AddPresetToPipelineCommand = new RelayCommand(AddPresetToPipeline, () => SelectedToolPreset is not null);
        ReturnFromUndoCommand = new RelayCommand(() =>
        {
            if (_undoReturnNav is not null) { SelectedNav = _undoReturnNav; return; }
            if (_homeNavItem is not null) { SelectedNav = _homeNavItem; return; }
            if (NavItems is { Count: > 0 }) { SelectedNav = NavItems[0]; return; }
            SelectedNav = _undoNavItem;
        });

        Rename.UndoCenterRequested += (_, _) => NavigateToUndoCenter();
        Convert.UndoCenterRequested += (_, _) => NavigateToUndoCenter();
        TextReplace.UndoCenterRequested += (_, _) => NavigateToUndoCenter();
        Dedup.UndoCenterRequested += (_, _) => NavigateToUndoCenter();
        Backup.UndoCenterRequested += (_, _) => NavigateToUndoCenter();
        Archive.UndoCenterRequested += (_, _) => NavigateToUndoCenter();
        Cleanup.UndoCenterRequested += (_, _) => NavigateToUndoCenter();
        Checksum.UndoCenterRequested += (_, _) => NavigateToUndoCenter();
        Images.UndoCenterRequested += (_, _) => NavigateToUndoCenter();
        Photos.UndoCenterRequested += (_, _) => NavigateToUndoCenter();
        Metadata.UndoCenterRequested += (_, _) => NavigateToUndoCenter();
        Search.DeliveryRequested += (_, paths) =>
        {
            Convert.AddExternalFiles(paths);
            SelectedNav = NavItems!.First(item => item.Key == "convert");
        };
        Report.UndoCenterRequested += (_, _) => NavigateToUndoCenter();
        Pipeline.UndoCenterRequested += (_, _) => NavigateToUndoCenter();
        Toolbox.UndoCenterRequested += (_, _) => NavigateToUndoCenter();
        SplitMerge.UndoCenterRequested += (_, _) => NavigateToUndoCenter();
        FileState.UndoCenterRequested += (_, _) => NavigateToUndoCenter();
        Template.UndoCenterRequested += (_, _) => NavigateToUndoCenter();
        Classify.UndoCenterRequested += (_, _) => NavigateToUndoCenter();
        Online.OpenLinksRequested += (_, _) =>
            SelectedNav = NavItems!.First(item => item.Key == "links");

        var moduleHome = _homeNavItem = new NavItem
        {
            Key = "home",
            Title = "开始使用",
            Subtitle = "新手引导 · 常用功能",
            IconData = "M 4,11 L 12,4 L 20,11 M 7,10 V 20 H 17 V 10",
            Badge = "开始",
            Page = Home,
            IsAvailable = true
        };

        var module1 = new NavItem
        {
            Key = "rename",
            Title = "批量重命名",
            Subtitle = "预览 · 执行 · 可回退",
            IconData = "M 3.5,16.5 L 4.6,12.4 L 13.2,3.8 C 13.7,3.3 14.4,3.3 14.9,3.8 L 16.2,5.1 C 16.7,5.6 16.7,6.3 16.2,6.8 L 7.6,15.4 Z M 11.9,5.1 L 14.9,8.1",
            Badge = "改名",
            Page = Rename,
            IsAvailable = true
        };

        var module2 = new NavItem
        {
            Key = "convert",
            Title = "格式转换",
            Subtitle = "文档 · 图片 · 可回退",
            IconData = "M 3,6.5 H 15 M 12,3.5 L 15,6.5 L 12,9.5 M 17,13.5 H 5 M 8,10.5 L 5,13.5 L 8,16.5",
            Badge = "转格式",
            Page = Convert,
            IsAvailable = true
        };

        var moduleFindReplace = new NavItem
        {
            Key = "findrep",
            Title = "文本替换",
            Subtitle = "内容改写 · 备份可回退",
            IconData = "M 9,3.5 A 5.5,5.5 0 1 1 9,14.5 A 5.5,5.5 0 1 1 9,3.5 M 13.2,13.2 L 18,18 M 6.5,9 H 11.5",
            Badge = "改写",
            Page = TextReplace,
            IsAvailable = true
        };

        var module3 = new NavItem
        {
            Key = "dedup",
            Title = "内容去重",
            Subtitle = "SimHash · 分组隔离",
            IconData = "M 7,3 H 15 V 13 H 7 Z M 4,6 H 12 V 17 H 4 Z",
            Badge = "查重",
            Page = Dedup,
            IsAvailable = true
        };

        var moduleBackup = new NavItem
        {
            Key = "backup",
            Title = "备份同步",
            Subtitle = "只增不删 · 可回退",
            IconData = "M 3.5,6.5 C 3.5,4.8 7.3,3.5 12,3.5 C 16.7,3.5 20.5,4.8 20.5,6.5 C 20.5,8.2 16.7,9.5 12,9.5 C 7.3,9.5 3.5,8.2 3.5,6.5 Z M 3.5,6.5 V 17 C 3.5,18.7 7.3,20 12,20 C 16.7,20 20.5,18.7 20.5,17 V 6.5 M 3.5,11.8 C 3.5,13.4 7.3,14.8 12,14.8 C 16.7,14.8 20.5,13.4 20.5,11.8",
            Badge = "同步",
            Page = Backup,
            IsAvailable = true
        };

        var moduleWatch = new NavItem
        {
            Key = "watch",
            Title = "实时监控",
            Subtitle = "只发现 · 不自动处理",
            IconData = "M 12,8.5 A 3.5,3.5 0 1 1 12,15.5 A 3.5,3.5 0 1 1 12,8.5 M 12,3.5 V 6 M 12,18 V 20.5 M 3.5,12 H 6 M 18,12 H 20.5 M 6,6 L 7.8,7.8 M 16.2,16.2 L 18,18 M 18,6 L 16.2,7.8 M 7.8,16.2 L 6,18",
            Badge = "监听",
            Page = Watch,
            IsAvailable = true
        };

        var moduleArchive = new NavItem
        {
            Key = "archive",
            Title = "压缩与解压",
            Subtitle = "ZIP · 安全解压",
            IconData = "M 4,4 H 20 V 20 H 4 Z M 10,4 V 8 M 14,4 V 8 M 10,10 V 14 M 14,10 V 14 M 10,16 V 18 M 14,16 V 18",
            Badge = "ZIP",
            Page = Archive,
            IsAvailable = true
        };

        var moduleCleanup = new NavItem
        {
            Key = "cleanup",
            Title = "空目录清理",
            Subtitle = "空文件夹 · 0 字节",
            IconData = "M 4,6 H 8 L 10,8 H 20 V 19 H 4 Z M 8,12 H 16 M 8,15 H 13",
            Badge = "清理",
            Page = Cleanup,
            IsAvailable = true
        };

        var moduleImgCompress = new NavItem
        {
            Key = "imgcompress", Title = "图片压缩", Subtitle = "压缩到目标大小",
            IconData = "M 3,5 H 21 V 19 H 3 Z M 7,15 L 10,11 L 13,14 L 16,9 L 18,12 M 7,18 H 17",
            Badge = "瘦身", Page = Toolbox,
            OnSelected = () => Toolbox.ActiveToolTab = 1, IsAvailable = true
        };
        var moduleImgCheck = new NavItem
        {
            Key = "imgcheck", Title = "图片检查", Subtitle = "损坏检测 · 隔离",
            IconData = "M 12,3 L 19,6 V 11.5 C 19,16.5 16,20 12,21.5 C 8,20 5,16.5 5,11.5 V 6 Z M 8.5,12 L 11,14.5 L 16,9",
            Badge = "检测", Page = Toolbox,
            OnSelected = () => Toolbox.ActiveToolTab = 2, IsAvailable = true
        };
        var moduleEmbedImg = new NavItem
        {
            Key = "embedimg", Title = "内嵌图片", Subtitle = "Office · PDF 提取",
            IconData = "M 4,4 H 16 L 20,8 V 20 H 4 Z M 8,15 L 11,11 L 14,14 L 16,10 L 18,13 M 8,7 H 12",
            Badge = "提取", Page = Toolbox,
            OnSelected = () => Toolbox.ActiveToolTab = 3, IsAvailable = true
        };
        var moduleChecksum = new NavItem
        {
            Key = "checksum",
            Title = "校验与清单",
            Subtitle = "SHA-256 · 完整性",
            IconData = "M 12,3 L 19,6 V 11.5 C 19,16.5 16,20 12,21.5 C 8,20 5,16.5 5,11.5 V 6 Z M 8.5,12.2 L 11,14.8 L 15.6,9.8",
            Badge = "校验",
            Page = Checksum,
            IsAvailable = true
        };

        var moduleImages = new NavItem
        {
            Key = "images",
            Title = "批量图片处理",
            Subtitle = "缩放 · 压缩 · 旋转 · 水印",
            IconData = "M 3.5,4.5 H 20.5 V 19.5 H 3.5 Z M 4.5,16 L 9,11 L 13,15 L 16,12 L 20,16 M 15.5,8.5 A 1.5,1.5 0 1 1 15.5,8.6",
            Badge = "图片",
            Page = Images,
            IsAvailable = true
        };

        var modulePhotos = new NavItem
        {
            Key = "photos",
            Title = "相似照片",
            Subtitle = "感知哈希 · 每组留一张",
            IconData = "M 4,5 H 20 V 19 H 4 Z M 6,16 L 9.5,12 L 12.5,15 L 15,12.5 L 18,16 M 15,8.5 A 1.4,1.4 0 1 1 15,8.6",
            Badge = "照片",
            Page = Photos,
            IsAvailable = true
        };

        var moduleMetadata = new NavItem
        {
            Key = "metadata",
            Title = "隐私元数据",
            Subtitle = "作者 · 公司 · EXIF",
            IconData = "M 4,5 H 20 V 19 H 4 Z M 7,9 H 13 M 7,12 H 17 M 7,15 H 14 M 17,7 A 1.5,1.5 0 1 1 17,7.1",
            Badge = "隐私",
            Page = Metadata,
            IsAvailable = true
        };

        var moduleSearch = new NavItem
        {
            Key = "search",
            Title = "全局搜索",
            Subtitle = "名称 · 正则 · 批量投递",
            IconData = "M 9,3.5 A 5.5,5.5 0 1 1 9,14.5 A 5.5,5.5 0 1 1 9,3.5 M 13.2,13.2 L 18,18 M 6,9 H 12",
            Badge = "搜索",
            Page = Search,
            IsAvailable = true
        };

        var moduleReport = new NavItem
        {
            Key = "report",
            Title = "文件夹清单",
            Subtitle = "CSV · HTML · 审计",
            IconData = "M 5,3 H 15 L 19,7 V 21 H 5 Z M 15,3 V 7 H 19 M 8,11 H 16 M 8,14 H 16 M 8,17 H 13",
            Badge = "报告",
            Page = Report,
            IsAvailable = true
        };

        var modulePipeline = _pipelineNavItem = new NavItem
        {
            Key = "pipeline",
            Title = "任务流水线",
            Subtitle = "步骤 · 预设 · 可回退",
            IconData = "M 4,6 H 9 V 18 H 4 Z M 15,6 H 20 V 18 H 15 Z M 9,9 H 15 M 9,15 H 15 M 15,9 L 12,12 L 15,15",
            Badge = "流水线",
            Page = Pipeline,
            OnSelected = () => Pipeline.LoadImportablePresets(),
            IsAvailable = true
        };

        var moduleSplitMerge = new NavItem
        {
            Key = "splitmerge", Title = "拆分与合并", Subtitle = "大文件分片 · 顺序合并",
            IconData = "M 4,4 H 10 V 10 H 4 Z M 14,4 H 20 V 10 H 14 Z M 4,14 H 10 V 20 H 4 Z M 14,14 H 20 V 20 H 14 Z",
            Badge = "文件", Page = SplitMerge, IsAvailable = true
        };
        var moduleFileState = new NavItem
        {
            Key = "filestate", Title = "时间与属性", Subtitle = "时间戳 · 只读隐藏",
            IconData = "M 12,3 A 9,9 0 1 1 12,21 A 9,9 0 1 1 12,3 M 12,7 V 12 L 15.5,14",
            Badge = "元数据", Page = FileState, IsAvailable = true
        };
        var moduleTemplate = new NavItem
        {
            Key = "template", Title = "目录模板", Subtitle = "批量创建文件夹结构",
            IconData = "M 3,6 H 8 L 10,8 H 18 V 19 H 3 Z M 12,11 V 16 M 9.5,13.5 H 14.5",
            Badge = "目录", Page = Template, IsAvailable = true
        };
        var module6 = new NavItem
        {
            Key = "classify",
            Title = "扫描与分类",
            Subtitle = "只读扫描 · 可回退移动",
            IconData = "M 3,6 H 8 L 10,8 H 17 V 16 H 3 Z M 7,11 H 13 M 7,14 H 11",
            Badge = "归类",
            Page = Classify,
            IsAvailable = true
        };

        var moduleGuide = new NavItem
        {
            Key = "guide",
            Title = "功能说明",
            Subtitle = "用途 · 步骤 · 安全边界",
            IconData = "M 5,3.5 H 19 V 20.5 H 5 Z M 8,8 H 16 M 8,11.5 H 16 M 8,15 H 13",
            Badge = "帮助",
            Page = Guide,
            IsAvailable = true
        };

        _undoNavItem = new NavItem
        {
            Key = "undo",
            Title = "回退中心",
            Subtitle = "本地日志 · 一键恢复",
            IconData = "M 4,8 A 6,6 0 1 1 5,15 M 4,3 V 8 H 9",
            Badge = "日志",
            Page = UndoCenter,
            IsAvailable = true
        };

        var moduleEnvironment = new NavItem
        {
            Key = "env",
            Title = "离线自检",
            Subtitle = "环境 · 引擎 · 数据路径",
            IconData = "M 12,3 L 19,6 V 11.5 C 19,16.5 16,20 12,21.5 C 8,20 5,16.5 5,11.5 V 6 Z M 8.5,12.5 L 11,15 L 15.5,9.5",
            Badge = "自检",
            Page = Environment,
            IsAvailable = true
        };

        var moduleLinks = new NavItem
        {
            Key = "links",
            Title = "常用链接",
            Subtitle = "外部工具 · 系统浏览器",
            IconData = "M 9,15 L 15,9 M 7.5,12.5 L 5.5,14.5 A 3.5,3.5 0 0 0 10.5,19.5 L 12.5,17.5 M 12.5,7.5 L 14.5,5.5 A 3.5,3.5 0 0 1 19.5,10.5 L 17.5,12.5",
            Badge = "外部",
            Page = Links,
            IsAvailable = true
        };

        var moduleOnline = new NavItem
        {
            Key = "online",
            Title = "在线功能",
            Subtitle = "固定离线 · 仅说明",
            IconData = "M 12,3 A 9,9 0 1 1 12,21 A 9,9 0 1 1 12,3 M 3,12 H 21 M 12,3 C 8.5,6.5 8.5,17.5 12,21 M 12,3 C 15.5,6.5 15.5,17.5 12,21",
            Badge = "关闭",
            Page = Online,
            IsAvailable = true
        };

        _settingsNavItem = new NavItem
        {
            Key = "settings",
            Title = "设置",
            Subtitle = "动效 · 默认行为",
            IconData = "M 10,3.2 L 11.1,5.1 L 13.3,5.7 L 15,4.7 L 16.3,6.5 L 15,8.2 L 15.2,10.4 L 16.5,12 L 15,13.7 L 13.2,12.8 L 11.2,13.5 L 10,15.4 L 8.4,14.2 L 6.2,14.1 L 5,12.4 L 3.4,11.4 L 3.8,9.4 L 3.2,7.5 L 4.8,6.3 L 5.5,4.3 L 7.5,3.8 Z M 10,7.2 A 2.8,2.8 0 1 1 10,12.8 A 2.8,2.8 0 1 1 10,7.2",
            Badge = "偏好",
            Page = Settings,
            IsAvailable = true
        };

        NavItems = new ObservableCollection<NavItem>
        {
            module1, module2, moduleFindReplace, module3, module6,
            moduleBackup, moduleWatch, moduleImgCompress, moduleImgCheck, moduleEmbedImg, moduleArchive, moduleCleanup, moduleChecksum, moduleImages,
            modulePhotos, moduleMetadata, moduleSearch, moduleReport,
            moduleSplitMerge, moduleFileState, moduleTemplate,
            moduleGuide, _undoNavItem, moduleEnvironment, moduleLinks, moduleOnline, _settingsNavItem
        };

        bool regroupNav = _settingsService.Current.NavOrderVersion < 4;
        if (regroupNav)
        {
            _settingsService.Current.NavOrder.Clear();
            _settingsService.Current.NavOrderVersion = 4;
        }
        ApplySavedNavOrder();
        foreach (NavItem item in NavItems) item.DefaultBadge = item.Badge;
        ApplyBadgeTexts();
        Settings.ConfigureBadges(NavItems);
        Settings.ConfigureHomeFavorites(ToolCatalog());
        ApplyCategoryColors();
        if (regroupNav)
        {
            ApplyDefaultCategoryOrder();
            _settingsService.Current.NavOrder = NavItems.Select(x => x.Key).ToList();
            try { _settingsService.Save(); } catch { }
        }
        Guide.Configure(NavItems);
        Home.ConfigureFavorites(ToolCatalog());

        _initializingNavigation = true;
        NavItem initial = _settingsService.Current.HasSeenWelcome ? module1 : moduleHome;
        if (_settingsService.Current.RememberLastPage)
        {
            NavItem? remembered = NavItems.FirstOrDefault(
                item => string.Equals(item.Key, _settingsService.Current.LastPageKey, StringComparison.OrdinalIgnoreCase));
            if (remembered is not null) initial = remembered;
            else if (string.Equals(_settingsService.Current.LastPageKey, "pipeline", StringComparison.OrdinalIgnoreCase)
                     && _pipelineNavItem is not null)
                initial = _pipelineNavItem;
        }
        if (_settingsService.Current.StartOnGuide)
        {
            initial = NavItems.First(x => x.Key == "guide");
        }
        if (!_settingsService.Current.HasSeenWelcome
            || string.Equals(_settingsService.Current.LastPageKey, "home", StringComparison.OrdinalIgnoreCase))
        {
            initial = moduleHome;
        }

        _selectedNav = initial;
        CurrentPage = initial.Page;
        initial.OnSelected?.Invoke();
        _initializingNavigation = false;
    }

    public ObservableCollection<NavItem> NavItems { get; }

    public GuideViewModel Guide { get; }

    public HomeViewModel Home { get; }

    public ToolboxViewModel Toolbox { get; }

    public RenameViewModel Rename { get; }

    public ConvertViewModel Convert { get; }

    public TextReplaceViewModel TextReplace { get; }

    public DedupViewModel Dedup { get; }


    public BackupViewModel Backup { get; }

    public WatchViewModel Watch { get; }

    public TableViewModel Tables { get; }

    public ArchiveViewModel Archive { get; }

    public CleanupViewModel Cleanup { get; }

    public ChecksumViewModel Checksum { get; }

    public ImageBatchViewModel Images { get; }

    public PhotoDedupViewModel Photos { get; }

    public MetadataViewModel Metadata { get; }

    public SearchViewModel Search { get; }

    public ReportViewModel Report { get; }

    public PipelineViewModel Pipeline { get; }

    public SplitMergeViewModel SplitMerge { get; }

    public FileStateViewModel FileState { get; }


    public TemplateViewModel Template { get; }

    public DiffViewModel Diff { get; }


    public ClassifyViewModel Classify { get; }

    public UndoCenterViewModel UndoCenter { get; }

    public EnvironmentViewModel Environment { get; }

    public LinksViewModel Links { get; }

    public OnlineViewModel Online { get; }

    public SettingsViewModel Settings { get; }

    public RelayCommand OpenSettingsCommand { get; }

    public AppSettingsService SettingsService => _settingsService;

    public GridLength SidebarWidthGrid => new(IsSidebarCollapsed ? 92 : _settingsService.Current.SidebarWidth);

    public bool IsSidebarCollapsed
    {
        get => _sidebarCollapsed;
        private set
        {
            if (!SetProperty(ref _sidebarCollapsed, value)) return;
            OnPropertyChanged(nameof(SidebarWidthGrid));
        }
    }

    public RelayCommand ToggleSidebarCommand { get; }

    private void ToggleSidebar()
    {
        IsSidebarCollapsed = !IsSidebarCollapsed;
        _settingsService.Current.SidebarCollapsed = IsSidebarCollapsed;
        try { _settingsService.Save(); } catch { }
    }

    private void ApplyCategoryColors()
    {
        var categories = new Dictionary<string, NavCategory>(StringComparer.OrdinalIgnoreCase)
        {
            ["home"] = NavCategory.Workflow,
            ["rename"] = NavCategory.File,
            ["classify"] = NavCategory.File,
            ["backup"] = NavCategory.File,
            ["watch"] = NavCategory.File,
            ["imgcompress"] = NavCategory.Media,
            ["imgcheck"] = NavCategory.Media,
            ["embedimg"] = NavCategory.Text,
            ["archive"] = NavCategory.File,
            ["cleanup"] = NavCategory.File,
            ["filestate"] = NavCategory.File,
            ["checksum"] = NavCategory.File,
            ["report"] = NavCategory.File,
            ["splitmerge"] = NavCategory.File,
            ["template"] = NavCategory.File,
            ["findrep"] = NavCategory.Text,
            ["dedup"] = NavCategory.Text,
            ["encoding"] = NavCategory.Text,
            ["images"] = NavCategory.Media,
            ["photos"] = NavCategory.Media,
            ["metadata"] = NavCategory.Media,
            ["convert"] = NavCategory.Media,
            ["search"] = NavCategory.Workflow,
            ["pipeline"] = NavCategory.Workflow,
            ["guide"] = NavCategory.System,
            ["undo"] = NavCategory.System,
            ["env"] = NavCategory.System,
            ["links"] = NavCategory.System,
            ["online"] = NavCategory.System,
            ["settings"] = NavCategory.System
        };
        var colors = new Dictionary<NavCategory, string>
        {
            [NavCategory.File] = _settingsService.Current.FileCategoryColor,
            [NavCategory.Text] = _settingsService.Current.TextCategoryColor,
            [NavCategory.Media] = _settingsService.Current.MediaCategoryColor,
            [NavCategory.Workflow] = _settingsService.Current.WorkflowCategoryColor,
            [NavCategory.System] = _settingsService.Current.SystemCategoryColor
        };

        foreach (NavItem item in NavItems)
        {
            item.Category = categories.TryGetValue(item.Key, out NavCategory category)
                ? category
                : NavCategory.System;
            item.SetCategoryColor(_settingsService.Current.ShowSidebarColors
                ? colors[item.Category]
                : "#64748B");
        }
        if (_pipelineNavItem is not null)
        {
            _pipelineNavItem.Category = NavCategory.Workflow;
            _pipelineNavItem.SetCategoryColor(_settingsService.Current.ShowSidebarColors
                ? colors[NavCategory.Workflow]
                : "#64748B");
        }
        Guide?.Configure(NavItems);
    }

    private void ApplyBadgeTexts()
    {
        foreach (NavItem item in NavItems)
        {
            item.Badge = _settingsService.Current.NavBadges.TryGetValue(item.Key, out string? custom)
                && !string.IsNullOrWhiteSpace(custom)
                ? custom
                : item.DefaultBadge;
        }
        Guide?.Configure(NavItems);
    }

    public bool ShowSidebarBadges => _settingsService.Current.ShowSidebarBadges;

    public double SidebarFontSize => _settingsService.Current.SidebarFontSize;

    public void MoveNavItem(NavItem source, NavItem target, bool insertAfter)
    {
        int oldIndex = NavItems.IndexOf(source);
        int targetIndex = NavItems.IndexOf(target);
        if (oldIndex < 0 || targetIndex < 0 || ReferenceEquals(source, target)) return;

        int newIndex = insertAfter ? targetIndex + 1 : targetIndex;
        if (oldIndex < newIndex) newIndex--;
        if (newIndex == oldIndex) return;

        NavItems.Move(oldIndex, newIndex);
        SaveNavOrder();
    }

    public void SaveNavOrder()
    {
        _settingsService.Current.NavOrder = NavItems.Select(x => x.Key).ToList();
        try
        {
            _settingsService.Save();
        }
        catch
        {
            // 排序失败不影响当前会话。
        }
    }

    private void ApplyDefaultCategoryOrder()
    {
        var rank = new Dictionary<NavCategory, int>
        {
            [NavCategory.File] = 0,
            [NavCategory.Text] = 1,
            [NavCategory.Media] = 2,
            [NavCategory.Workflow] = 3,
            [NavCategory.System] = 4
        };
        var ordered = NavItems
            .Select((item, index) => (item, index))
            .OrderBy(x => rank.TryGetValue(x.item.Category, out int value) ? value : 9)
            .ThenBy(x => x.item.Key == "settings" ? 1 : 0)
            .ThenBy(x => x.index)
            .Select(x => x.item)
            .ToList();
        NavItems.Clear();
        foreach (NavItem item in ordered) NavItems.Add(item);
    }

    private void ApplySavedNavOrder()
    {
        var saved = _settingsService.Current.NavOrder;
        if (saved.Count == 0) return;

        var byKey = NavItems.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<NavItem>();
        foreach (string key in saved)
        {
            if (byKey.TryGetValue(key, out NavItem? item) && !ordered.Contains(item))
            {
                ordered.Add(item);
            }
        }

        foreach (NavItem item in NavItems)
        {
            if (!ordered.Contains(item)) ordered.Add(item);
        }

        NavItems.Clear();
        foreach (NavItem item in ordered) NavItems.Add(item);
    }

    public object CurrentPage { get; private set; }

    public string PageTitle => SelectedNav.Title;

    public string PageSubtitle => SelectedNav.Subtitle;

    public string PageIntro
    {
        get
        {
            var guide = Guide.Items.FirstOrDefault(x =>
                string.Equals(x.Key, _selectedNav?.Key, StringComparison.OrdinalIgnoreCase));
            string text = guide?.Usage is { Length: > 0 } usage
                ? usage
                : "选择文件或文件夹 → 预览确认 → 执行；所有修改都可在回退中心恢复。";
            return string.IsNullOrWhiteSpace(_appliedPresetName) ? text : $"{text} · 模板：{_appliedPresetName}";
        }
    }

    public RelayCommand OpenHomeCommand { get; }
    public RelayCommand OpenPipelineCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand ForwardCommand { get; }
    public RelayCommand SaveToolPresetCommand { get; }
    public RelayCommand ApplyToolPresetCommand { get; }
    public RelayCommand DeleteToolPresetCommand { get; }
    public RelayCommand ExportToolPresetCommand { get; }
    public RelayCommand ImportToolPresetCommand { get; }
    public RelayCommand AddPresetToPipelineCommand { get; }
    public ObservableCollection<ToolPreset> ToolPresets { get; } = new();
    public string ToolPresetName { get => _toolPresetName; set => SetProperty(ref _toolPresetName, value ?? string.Empty); }
    public ToolPreset? SelectedToolPreset
    {
        get => _selectedToolPreset;
        set
        {
            if (!SetProperty(ref _selectedToolPreset, value)) return;
            ApplyToolPresetCommand.RaiseCanExecuteChanged();
            DeleteToolPresetCommand.RaiseCanExecuteChanged();
            ExportToolPresetCommand.RaiseCanExecuteChanged();
            AddPresetToPipelineCommand.RaiseCanExecuteChanged();
        }
    }
    public bool CanGoBack => _navHistoryIndex > 0;
    public bool CanGoForward => _navHistoryIndex >= 0 && _navHistoryIndex < _navHistory.Count - 1;
    public RelayCommand ReturnFromUndoCommand { get; }
    public bool CanReturnFromUndo => _undoReturnNav is not null;
    public string UndoReturnLabel => _undoReturnNav is null ? "← 返回" : $"← 返回{_undoReturnNav.Title}";
    public bool IsHomeActive => string.Equals(SelectedNav.Key, "home", StringComparison.OrdinalIgnoreCase);
    public bool IsPipelineActive => string.Equals(SelectedNav.Key, "pipeline", StringComparison.OrdinalIgnoreCase);

    private IReadOnlyList<NavItem> ToolCatalog()
    {
        var items = new List<NavItem>();
        if (_pipelineNavItem is not null) items.Add(_pipelineNavItem);
        if (NavItems is not null) items.AddRange(NavItems);
        return items;
    }

    private void RefreshToolPresets()
    {
        ToolPresets.Clear();
        _appliedPresetName = string.Empty;
        SelectedToolPreset = null;
        if (SelectedNav is null) return;
        foreach (var preset in _presetStore.All.Where(x =>
                     x.ToolKey.Equals(SelectedNav.Key, StringComparison.OrdinalIgnoreCase)))
            ToolPresets.Add(preset);
        if (ToolPresets.Count > 0) SelectedToolPreset = ToolPresets[0];
    }

    private void SaveToolPreset()
    {
        if (SelectedNav is null || CurrentPage is null) return;
        string name = ToolPresetName.Trim();
        if (name.Length == 0) name = $"{PageTitle} 配置 {ToolPresets.Count + 1}";
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in CaptureProperties(CurrentPage))
        {
            object? value = property.GetValue(CurrentPage);
            if (value is null) continue;
            values[property.Name] = System.Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        }
        var preset = new ToolPreset
        {
            ToolKey = SelectedNav.Key, Name = name, Values = values,
            Step = ToolPresetMapper.TryCreate(SelectedNav.Key, CurrentPage)
        };
        _presetStore.Upsert(preset);
        RefreshToolPresets();
        SelectedToolPreset = ToolPresets.FirstOrDefault(x => x.Name == name);
        ToolPresetName = name;
    }

    private void ApplyToolPreset()
    {
        if (SelectedToolPreset is null || CurrentPage is null) return;
        foreach (var property in CaptureProperties(CurrentPage))
        {
            if (!SelectedToolPreset.Values.TryGetValue(property.Name, out string? text)) continue;
            try
            {
                object? value = property.PropertyType.IsEnum
                    ? Enum.Parse(property.PropertyType, text, true)
                    : System.Convert.ChangeType(text, property.PropertyType, System.Globalization.CultureInfo.InvariantCulture);
                property.SetValue(CurrentPage, value);
            }
            catch { }
        }
        _appliedPresetName = SelectedToolPreset.Name;
        OnPropertyChanged(nameof(PageIntro));
    }

    private void DeleteToolPreset()
    {
        if (SelectedToolPreset is null) return;
        _presetStore.Remove(SelectedToolPreset);
        RefreshToolPresets();
    }

    private void ExportToolPreset()
    {
        if (SelectedToolPreset is null) return;
        string? file = _dialogService.PickSaveFile("配置|*.json", SelectedToolPreset.Name + ".json");
        if (file is not null) _presetStore.Export(SelectedToolPreset, file);
    }

    private void ImportToolPreset()
    {
        var files = _dialogService.PickFiles("配置|*.json|所有文件|*.*", null);
        if (files.Count == 0) return;
        _presetStore.Import(files[0], out _);
        RefreshToolPresets();
    }

    private void AddPresetToPipeline()
    {
        if (SelectedToolPreset is null) return;
        Pipeline.AddPresetAsStep(SelectedToolPreset);
        if (_pipelineNavItem is not null) SelectedNav = _pipelineNavItem;
    }

    private static IEnumerable<PropertyInfo> CaptureProperties(object page) =>
        page.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite
                        && p.GetIndexParameters().Length == 0
                        && (p.PropertyType.IsPrimitive || p.PropertyType == typeof(string)
                            || p.PropertyType == typeof(decimal) || p.PropertyType.IsEnum));

    private void NavigateBack()
    {
        if (!CanGoBack) return;
        _navigatingHistory = true;
        _navHistoryIndex--;
        SelectedNav = _navHistory[_navHistoryIndex];
        _navigatingHistory = false;
        RaiseHistory();
    }

    private void NavigateForward()
    {
        if (!CanGoForward) return;
        _navigatingHistory = true;
        _navHistoryIndex++;
        SelectedNav = _navHistory[_navHistoryIndex];
        _navigatingHistory = false;
        RaiseHistory();
    }

    private void RaiseHistory()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        BackCommand.RaiseCanExecuteChanged();
        ForwardCommand.RaiseCanExecuteChanged();
    }

    private void PushHistory(NavItem item)
    {
        if (_navigatingHistory) return;
        if (_navHistoryIndex < _navHistory.Count - 1 && _navHistoryIndex >= 0)
            _navHistory.RemoveRange(_navHistoryIndex + 1, _navHistory.Count - _navHistoryIndex - 1);
        if (_navHistoryIndex >= 0 && ReferenceEquals(_navHistory[_navHistoryIndex], item)) return;
        _navHistory.Add(item);
        _navHistoryIndex = _navHistory.Count - 1;
        RaiseHistory();
    }

    public NavItem SelectedNav
    {
        get => _selectedNav;
        set
        {
            if (value is null || !SetProperty(ref _selectedNav, value)) return;

            PushHistory(value);
            CurrentPage = value.Page;
            value.OnSelected?.Invoke();
            OnPropertyChanged(nameof(CurrentPage));
            OnPropertyChanged(nameof(PageTitle));
            OnPropertyChanged(nameof(PageSubtitle));
            OnPropertyChanged(nameof(PageIntro));
            RefreshToolPresets();
            OnPropertyChanged(nameof(IsHomeActive));
            OnPropertyChanged(nameof(IsPipelineActive));
            if (!string.Equals(value.Key, "undo", StringComparison.OrdinalIgnoreCase) && _undoReturnNav is not null)
            {
                _undoReturnNav = null;
                OnPropertyChanged(nameof(CanReturnFromUndo));
                OnPropertyChanged(nameof(UndoReturnLabel));
            }

            if (value.Page is UndoCenterViewModel undoCenter)
            {
                undoCenter.OnActivated();
            }

            if (!_initializingNavigation && _settingsService.Current.RememberLastPage)
            {
                _settingsService.Current.LastPageKey = value.Key;
                try
                {
                    _settingsService.Save();
                }
                catch
                {
                    // 页面记忆失败不应阻断导航。
                }
            }
        }
    }

    private void SetSelectedByKey(string key)
    {
        if (string.Equals(key, "home", StringComparison.OrdinalIgnoreCase) && _homeNavItem is not null)
        {
            SelectedNav = _homeNavItem;
            return;
        }
        if (string.Equals(key, "pipeline", StringComparison.OrdinalIgnoreCase) && _pipelineNavItem is not null)
        {
            SelectedNav = _pipelineNavItem;
            return;
        }
        NavItem? item = NavItems?.FirstOrDefault(
            x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
        if (item is not null) SelectedNav = item;
    }

    public void NavigateToUndoCenter()
    {
        if (!string.Equals(SelectedNav.Key, "undo", StringComparison.OrdinalIgnoreCase))
            _undoReturnNav = SelectedNav;
        SelectedNav = _undoNavItem;
        OnPropertyChanged(nameof(CanReturnFromUndo));
        OnPropertyChanged(nameof(UndoReturnLabel));
    }

    private static NavItem CreateModuleItem(
        string title,
        string subtitle,
        string glyph,
        string badge,
        PlaceholderViewModel page)
    {
        return new NavItem
        {
            Key = "placeholder",
            Title = title,
            Subtitle = subtitle,
            IconData = glyph,
            Badge = badge,
            Page = page,
            IsAvailable = false
        };
    }

    private static PlaceholderViewModel CreateConverterPlaceholder()
    {
        return new PlaceholderViewModel
        {
            ModuleNumber = "模块 2",
            Title = "批量格式转换",
            Description = "文档、图片与相机 RAW 的统一转换队列。转换结果同样进入「预览 → 执行 → 回退」体系，所有引擎在本机离线运行。",
            PreviewLabel = "转换输出预览：新文件列表与命名规则",
            Features = new ObservableCollection<PlaceholderFeature>
            {
                new("文档互转", "Word / PPT / PDF / Markdown / TXT，基于 Pandoc 与 LibreOffice 无头模式。"),
                new("图片转换", "JPG / PNG / HEIC / WebP，使用 Windows Imaging Component 与离线解码器。"),
                new("RAW 解码", "CR2 / CR3 / NEF / ARW / RAF / RW2 等，通过 LibRaw 本机处理。"),
                new("队列与进度", "批量加入转换队列，逐项显示状态；失败项不写入成功日志。")
            },
            TechnicalNotes = new ObservableCollection<string>
            {
                "转换引擎随应用打包，断网可用；启动时进行本地存在性检查。",
                "输出文件默认不覆盖，保留原文件，并把新文件映射写入回退日志。",
                "转换器与日志层解耦，模块 1 的预览和回退底座直接复用。"
            }
        };
    }

    private static PlaceholderViewModel CreateDedupPlaceholder()
    {
        return new PlaceholderViewModel
        {
            ModuleNumber = "模块 3",
            Title = "内容级去重",
            Description = "从文档中提取纯文本并计算 64 位 SimHash 指纹，识别内容高度相似但文件名或格式不同的文档。纯本地计算，不上传任何内容。",
            PreviewLabel = "疑似重复分组预览：按相似度阈值聚类",
            Features = new ObservableCollection<PlaceholderFeature>
            {
                new("文本提取", "读取 Word / PDF / TXT 等文档正文，作为指纹输入。"),
                new("SimHash 指纹", "对文本分词后计算 64 位指纹，使用汉明距离衡量相似度。"),
                new("阈值分组", "用户调节相似度阈值，界面按组展示疑似重复项。"),
                new("安全处理", "由用户勾选确认后再处理，删除 / 移动动作全部进入回退日志。")
            },
            TechnicalNotes = new ObservableCollection<string>
            {
                "分词与指纹为纯 C# 实现，不加载 AI 模型。",
                "相似度结果附带证据：汉明距离、文本长度与关键片段。",
                "去重动作复用统一日志格式，可在回退中心恢复。"
            }
        };
    }

    private static PlaceholderViewModel CreateSummaryPlaceholder()
    {
        return new PlaceholderViewModel
        {
            ModuleNumber = "模块 4",
            Title = "抽取式文档摘要",
            Description = "零配置的本地摘要：使用 TextRank 与 TF-IDF 给句子和关键词排序，只摘取原文句子，不改写、不生成虚假内容。",
            PreviewLabel = "关键句与关键词预览，点击可定位原文",
            Features = new ObservableCollection<PlaceholderFeature>
            {
                new("关键句抽取", "TextRank 句子图排序，输出按权重排列的原文关键句。"),
                new("关键词", "TF-IDF 词频统计，过滤停用词并保留原文位置。"),
                new("可追溯", "每条摘要记录字符偏移与所在段落，可跳转原文。"),
                new("秒级处理", "针对单篇文档设计，流式读取，内存占用受控。")
            },
            TechnicalNotes = new ObservableCollection<string>
            {
                "不调用任何 AI 模型或外部 API。",
                "中文分词使用本地词典与 n-gram 兜底，离线可用。",
                "摘要结果可进入模块 5 的 Markdown / Word / PDF 导出。"
            }
        };
    }

    private static PlaceholderViewModel CreateExportPlaceholder()
    {
        return new PlaceholderViewModel
        {
            ModuleNumber = "模块 5",
            Title = "导出与中文优化",
            Description = "把处理结果导出为 Markdown、Word 或 PDF，重点处理中文排版：标题层级、段间距、标点挤压、上下标与基础样式。",
            PreviewLabel = "排版预览：标题层级与中文段落样式",
            Features = new ObservableCollection<PlaceholderFeature>
            {
                new("Markdown", "正确生成 # 标题层级、列表与引用，中文换行不乱。"),
                new("Word / PDF", "通过 Open XML 与本地渲染引擎生成文档，保留基础样式。"),
                new("中文排版", "优化中英文混排、标点与段间距，避免断行错位。"),
                new("可追溯导出", "导出文件记录来源，重复导出不覆盖已有版本。")
            },
            TechnicalNotes = new ObservableCollection<string>
            {
                "不依赖 Office 安装；打印与 PDF 渲染使用本地组件。",
                "上下标、加粗、斜体等有限样式在 Markdown 与 Word 间映射。",
                "导出操作以新增文件为主，不破坏原始资料。"
            }
        };
    }

    private static PlaceholderViewModel CreateClassificationPlaceholder()
    {
        return new PlaceholderViewModel
        {
            ModuleNumber = "模块 6",
            Title = "本地文件夹扫描与分类",
            Description = "按文件类型、修改时间、名称规则只读扫描文件夹，先生成分组和移动预览，确认后再归类。所有分类动作进入统一回退日志。",
            PreviewLabel = "分类结果预览：来源路径 → 目标文件夹",
            Features = new ObservableCollection<PlaceholderFeature>
            {
                new("只读扫描", "扫描阶段不创建、不移动、不改名，仅建立索引。"),
                new("规则分类", "按扩展名、日期区间、名称包含等条件归类。"),
                new("移动预览", "表格展示旧路径与新路径，冲突和覆盖逐项标记。"),
                new("统一回退", "移动动作写入日志，跨文件夹也能一键恢复。")
            },
            TechnicalNotes = new ObservableCollection<string>
            {
                "复用模块 1 的只读规划器与日志机制，扩展操作类型为 Move。",
                "扫描默认跳过隐藏与系统文件，避免误整理系统目录。",
                "跨磁盘移动采用复制校验后删除，回退时逆向恢复。"
            }
        };
    }
}
