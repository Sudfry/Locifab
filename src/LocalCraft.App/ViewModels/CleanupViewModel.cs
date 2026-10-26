using System.Collections.ObjectModel;
using LocalCraft.App.Infrastructure;
using LocalCraft.App.Services;
using LocalCraft.Core.Cleanup;
using LocalCraft.Core.Journal;
using LocalCraft.Core.Operations;

namespace LocalCraft.App.ViewModels;

public sealed class CleanupViewModel : ViewModelBase
{
    private readonly IDialogService _dialogService;
    private readonly CleanupPlanner _planner = new();
    private readonly CleanupExecutor _executor;

    private string _folder = string.Empty;
    private bool _includeSubfolders = true;
    private bool _removeEmptyDirectories = true;
    private bool _removeZeroByteFiles = true;
    private bool _includeHidden;
    private bool _isBusy;
    private bool _isProgressIndeterminate;
    private double _progressValue;
    private string _busyText = string.Empty;
    private string _summaryText = "先扫描，确认后再清理空目录和 0 字节文件。所有清理都可回退。";
    private bool _hasPreview;
    private bool _hasResult;
    private bool _resultIsSuccess;
    private string _resultTitle = string.Empty;
    private string _resultMessage = string.Empty;
    private CleanupPlan? _plan;

    public CleanupViewModel(JournalRepository repository, IDialogService dialogService)
    {
        _dialogService = dialogService;
        _executor = new CleanupExecutor(repository);
        BrowseCommand = new RelayCommand(Browse, () => !IsBusy);
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy && Directory.Exists(Folder));
        ExecuteCommand = new AsyncRelayCommand(ExecuteAsync, () => CanExecute);
        OpenUndoCenterCommand = new RelayCommand(() => UndoCenterRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? UndoCenterRequested;

    public ObservableCollection<CleanupPlanItem> Items { get; } = new();

    public RelayCommand BrowseCommand { get; }
    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand ExecuteCommand { get; }
    public RelayCommand OpenUndoCenterCommand { get; }

    public string Folder
    {
        get => _folder;
        set
        {
            if (!SetProperty(ref _folder, value)) return;
            ClearPreview();
            RaiseCommandStates();
        }
    }

    public bool IncludeSubfolders
    {
        get => _includeSubfolders;
        set => SetProperty(ref _includeSubfolders, value);
    }

    public bool RemoveEmptyDirectories
    {
        get => _removeEmptyDirectories;
        set => SetProperty(ref _removeEmptyDirectories, value);
    }

    public bool RemoveZeroByteFiles
    {
        get => _removeZeroByteFiles;
        set => SetProperty(ref _removeZeroByteFiles, value);
    }

    public bool IncludeHidden
    {
        get => _includeHidden;
        set => SetProperty(ref _includeHidden, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(IsIdle));
            RaiseCommandStates();
        }
    }

    public bool IsIdle => !IsBusy;

    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        private set => SetProperty(ref _isProgressIndeterminate, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public string BusyText
    {
        get => _busyText;
        private set => SetProperty(ref _busyText, value);
    }

    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    public bool HasPreview
    {
        get => _hasPreview;
        private set
        {
            if (!SetProperty(ref _hasPreview, value)) return;
            OnPropertyChanged(nameof(CanExecute));
            ExecuteCommand.RaiseCanExecuteChanged();
        }
    }

    public bool CanExecute => !IsBusy && HasPreview && _plan is { Items.Count: > 0 };

    public bool HasResult
    {
        get => _hasResult;
        private set => SetProperty(ref _hasResult, value);
    }

    public bool ResultIsSuccess
    {
        get => _resultIsSuccess;
        private set => SetProperty(ref _resultIsSuccess, value);
    }

    public string ResultTitle
    {
        get => _resultTitle;
        private set => SetProperty(ref _resultTitle, value);
    }

    public string ResultMessage
    {
        get => _resultMessage;
        private set => SetProperty(ref _resultMessage, value);
    }

    private void Browse()
    {
        string? picked = _dialogService.PickFolder(Directory.Exists(Folder) ? Folder : null);
        if (picked is null) return;
        Folder = picked;
        SummaryText = "文件夹已选择。点击「只读扫描」生成清理预览。";
    }

    private async Task ScanAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        IsProgressIndeterminate = true;
        BusyText = "正在扫描空目录和零字节文件…";
        HasResult = false;
        ClearPreview();

        try
        {
            var plan = await Task.Run(() => _planner.Scan(new CleanupOptions
            {
                Folder = Folder,
                IncludeSubfolders = IncludeSubfolders,
                RemoveEmptyDirectories = RemoveEmptyDirectories,
                RemoveZeroByteFiles = RemoveZeroByteFiles,
                IncludeHidden = IncludeHidden
            }));
            if (!plan.Success)
            {
                SummaryText = plan.Error ?? "扫描失败。";
                return;
            }

            _plan = plan;
            foreach (var item in plan.Items) Items.Add(item);
            HasPreview = true;
            int dirs = plan.Items.Count(x => x.IsDirectory);
            int files = plan.Items.Count - dirs;
            SummaryText = $"找到 {dirs} 个空文件夹、{files} 个 0 字节文件。确认后再清理，可回退。";
        }
        catch (Exception ex)
        {
            SummaryText = "扫描失败：" + ex.Message;
            _dialogService.ShowError("扫描失败", ex.Message);
        }
        finally
        {
            IsBusy = false;
            IsProgressIndeterminate = false;
            RaiseCommandStates();
        }
    }

    private async Task ExecuteAsync()
    {
        if (!CanExecute || _plan is null) return;
        if (!_dialogService.Confirm(
                "执行清理",
                $"将清理 {_plan.Items.Count} 个空目录/零字节文件。\n" +
                "空目录和零字节文件本身不含数据；清理会写入日志，可在回退中心恢复。是否继续？"))
        {
            return;
        }

        IsBusy = true;
        IsProgressIndeterminate = false;
        ProgressValue = 0;
        BusyText = "正在清理…";
        var plan = _plan;
        try
        {
            var progress = new Progress<OperationProgress>(value =>
            {
                ProgressValue = value.Total == 0 ? 0 : value.Completed * 100.0 / value.Total;
                BusyText = $"{value.Stage} · {value.CurrentFile}";
            });
            var result = await Task.Run(() => _executor.Execute(plan, progress));
            ResultIsSuccess = result.Success;
            ResultTitle = result.Success ? "清理完成" : "清理未完成";
            ResultMessage = result.Message;
            HasResult = true;
            if (result.Success)
            {
                ClearPreview();
                SummaryText = "清理已完成，可在回退中心恢复。";
            }
        }
        catch (Exception ex)
        {
            ResultIsSuccess = false;
            ResultTitle = "清理失败";
            ResultMessage = ex.Message;
            HasResult = true;
        }
        finally
        {
            IsBusy = false;
            IsProgressIndeterminate = false;
            ProgressValue = 0;
            RaiseCommandStates();
        }
    }

    private void ClearPreview()
    {
        _plan = null;
        Items.Clear();
        HasPreview = false;
    }

    private void RaiseCommandStates()
    {
        BrowseCommand.RaiseCanExecuteChanged();
        ScanCommand.RaiseCanExecuteChanged();
        ExecuteCommand.RaiseCanExecuteChanged();
    }
}
