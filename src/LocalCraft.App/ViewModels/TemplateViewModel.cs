using System.Collections.ObjectModel;
using LocalCraft.App.Infrastructure;
using LocalCraft.App.Services;
using LocalCraft.Core.Files;
using LocalCraft.Core.Journal;

namespace LocalCraft.App.ViewModels;

public sealed record TemplatePreset(string Name, string Text);

public sealed class TemplateViewModel : ViewModelBase
{
    private readonly IDialogService _dialogService;
    private readonly FolderTemplateService _service;
    private string _root = string.Empty;
    private string _template = "项目/\n  文档/\n  素材/\n  输出/\n说明.md";
    private bool _isBusy;
    private string _summary = "每行一个路径；以 / 结尾创建文件夹，其余创建空文件。";
    private bool _hasPreview;
    private bool _hasResult;
    private bool _resultSuccess;
    private string _resultTitle = string.Empty;
    private string _resultMessage = string.Empty;
    private TemplatePlan? _plan;
    private TemplatePreset _selectedPreset;

    public TemplateViewModel(JournalRepository repository, IDialogService dialogService)
    {
        _dialogService = dialogService;
        _service = new FolderTemplateService(repository);
        Presets = new ObservableCollection<TemplatePreset>
        {
            new("通用项目", "项目/\n  文档/\n  素材/\n  输出/\n  说明.md"),
            new("客户交付", "客户/\n  合同/\n  交付物/\n  验收/\n交付说明.md"),
            new("课程资料", "课程/\n  讲义/\n  作业/\n  参考答案/\n课程说明.md"),
            new("照片归档", "照片/\n  原图/\n  精选/\n  导出/\n说明.md"),
            new("空目录结构", "输入/\n  处理/\n  输出/\n")
        };
        _selectedPreset = Presets[0];
        BrowseCommand = new RelayCommand(Browse, () => !IsBusy);
        PreviewCommand = new RelayCommand(Preview, () => !IsBusy && Directory.Exists(Root));
        ExecuteCommand = new RelayCommand(Execute, () => !IsBusy && HasPreview);
        OpenUndoCenterCommand = new RelayCommand(() => UndoCenterRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? UndoCenterRequested;
    public ObservableCollection<TemplateItem> Rows { get; } = new();
    public ObservableCollection<TemplatePreset> Presets { get; }

    public TemplatePreset SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (value is null || !SetProperty(ref _selectedPreset, value)) return;
            Template = value.Text;
        }
    }
    public RelayCommand BrowseCommand { get; }
    public RelayCommand PreviewCommand { get; }
    public RelayCommand ExecuteCommand { get; }
    public RelayCommand OpenUndoCenterCommand { get; }

    public string Root
    {
        get => _root;
        set
        {
            if (!SetProperty(ref _root, value)) return;
            Clear();
            PreviewCommand.RaiseCanExecuteChanged();
        }
    }

    public string Template
    {
        get => _template;
        set
        {
            if (!SetProperty(ref _template, value)) return;
            Clear();
        }
    }
    public bool IsBusy { get => _isBusy; private set { if (!SetProperty(ref _isBusy, value)) return; RaiseCommands(); } }
    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
    public bool HasPreview { get => _hasPreview; private set { if (!SetProperty(ref _hasPreview, value)) return; ExecuteCommand.RaiseCanExecuteChanged(); } }
    public bool HasResult { get => _hasResult; private set => SetProperty(ref _hasResult, value); }
    public bool ResultSuccess { get => _resultSuccess; private set => SetProperty(ref _resultSuccess, value); }
    public string ResultTitle { get => _resultTitle; private set => SetProperty(ref _resultTitle, value); }
    public string ResultMessage { get => _resultMessage; private set => SetProperty(ref _resultMessage, value); }

    private void Browse()
    {
        string? folder = _dialogService.PickFolder(Directory.Exists(Root) ? Root : null);
        if (folder is not null) Root = folder;
    }

    private void Preview()
    {
        Clear();
        _plan = _service.Plan(Root, Template);
        if (!_plan.Success)
        {
            Summary = _plan.Error ?? "模板无法解析。";
            return;
        }
        foreach (var item in _plan.Items) Rows.Add(item);
        HasPreview = Rows.Count > 0;
        Summary = $"将处理 {Rows.Count} 项；已存在的不会覆盖。";
    }

    private void Execute()
    {
        if (_plan is null) return;
        var result = _service.Execute(_plan);
        ResultSuccess = result.Success;
        ResultTitle = result.Success ? "目录结构已创建" : "创建未完成";
        ResultMessage = result.Message;
        HasResult = true;
        if (result.Success)
        {
            Summary = result.Message + " 回退只删除本次创建的空目录/空文件。";
            Clear();
        }
    }

    private void Clear()
    {
        Rows.Clear();
        _plan = null;
        HasPreview = false;
    }

    private void RaiseCommands()
    {
        BrowseCommand.RaiseCanExecuteChanged();
        PreviewCommand.RaiseCanExecuteChanged();
        ExecuteCommand.RaiseCanExecuteChanged();
    }
}
