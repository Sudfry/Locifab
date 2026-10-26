using System.Collections.ObjectModel;
using LocalCraft.App.Infrastructure;
using LocalCraft.App.Services;
using LocalCraft.Core.Archives;
using LocalCraft.Core.Journal;
using LocalCraft.Core.Operations;

namespace LocalCraft.App.ViewModels;

public sealed record ArchiveModeOption(ArchiveMode Mode, string DisplayName, string Description);

public sealed class ArchiveViewModel : ViewModelBase
{
    private readonly IDialogService _dialogService;
    private readonly ArchivePlanner _planner = new();
    private readonly ArchiveExecutor _executor;

    private ArchiveModeOption _selectedMode;
    private string _sourcePath = string.Empty;
    private string _targetPath = string.Empty;
    private string _password = string.Empty;
    private bool _isBusy;
    private bool _isProgressIndeterminate;
    private double _progressValue;
    private string _busyText = string.Empty;
    private string _summaryText = "选择压缩或解压，再选择源与目标。";
    private bool _hasPreview;
    private bool _hasResult;
    private bool _resultIsSuccess;
    private string _resultTitle = string.Empty;
    private string _resultMessage = string.Empty;
    private ArchivePlan? _plan;

    public ArchiveViewModel(JournalRepository repository, IDialogService dialogService)
    {
        _dialogService = dialogService;
        _executor = new ArchiveExecutor(repository);
        Modes = new ObservableCollection<ArchiveModeOption>
        {
            new(ArchiveMode.Compress, "压缩文件夹", "把整个文件夹打包为 ZIP"),
            new(ArchiveMode.Extract, "解压 ZIP", "安全解压到目标文件夹，不覆盖同名文件")
        };
        _selectedMode = Modes[0];

        BrowseSourceCommand = new RelayCommand(BrowseSource, () => !IsBusy);
        BrowseTargetCommand = new RelayCommand(BrowseTarget, () => !IsBusy);
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy);
        ExecuteCommand = new AsyncRelayCommand(ExecuteAsync, () => CanExecute);
        OpenUndoCenterCommand = new RelayCommand(() => UndoCenterRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? UndoCenterRequested;

    public ObservableCollection<ArchiveModeOption> Modes { get; }

    public ObservableCollection<ArchivePlanEntry> Entries { get; } = new();

    public RelayCommand BrowseSourceCommand { get; }
    public RelayCommand BrowseTargetCommand { get; }
    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand ExecuteCommand { get; }
    public RelayCommand OpenUndoCenterCommand { get; }

    public ArchiveModeOption SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (value is null || !SetProperty(ref _selectedMode, value)) return;
            ClearPreview();
            OnPropertyChanged(nameof(IsCompress));
            OnPropertyChanged(nameof(IsExtract));
            OnPropertyChanged(nameof(SourceLabel));
            OnPropertyChanged(nameof(TargetLabel));
            OnPropertyChanged(nameof(PasswordLabel));
            OnPropertyChanged(nameof(PasswordHint));
            SummaryText = value.Description;
        }
    }

    public bool IsCompress => SelectedMode.Mode == ArchiveMode.Compress;

    public bool IsExtract => SelectedMode.Mode == ArchiveMode.Extract;

    public string SourceLabel => IsCompress ? "待压缩文件夹" : "ZIP 文件";

    public string TargetLabel => IsCompress ? "输出 ZIP" : "解压到文件夹";
    public string PasswordLabel => IsCompress ? "加密密码（留空不加密）" : "解压密码（加密包必填）";
    public string PasswordHint => IsCompress
        ? "填写密码后生成加密 ZIP；留空则生成普通 ZIP。"
        : "解压带密码的 ZIP 时，在这里输入原压缩包密码；普通 ZIP 可留空。";

    public string Password
    {
        get => _password;
        set
        {
            if (!SetProperty(ref _password, value ?? string.Empty)) return;
            ClearPreview();
        }
    }

    public string SourcePath
    {
        get => _sourcePath;
        set
        {
            if (!SetProperty(ref _sourcePath, value)) return;
            ClearPreview();
        }
    }

    public string TargetPath
    {
        get => _targetPath;
        set
        {
            if (!SetProperty(ref _targetPath, value)) return;
            ClearPreview();
        }
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

    public bool CanExecute => !IsBusy && HasPreview && _plan is { Entries.Count: > 0 };

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

    private void BrowseSource()
    {
        if (IsCompress)
        {
            string? picked = _dialogService.PickFolder(Directory.Exists(SourcePath) ? SourcePath : null);
            if (picked is not null) SourcePath = picked;
            return;
        }

        IReadOnlyList<string> files = _dialogService.PickFiles(
            "ZIP 压缩包 (*.zip)|*.zip",
            Directory.Exists(Path.GetDirectoryName(SourcePath)) ? Path.GetDirectoryName(SourcePath) : null);
        if (files.Count > 0) SourcePath = files[0];
    }

    private void BrowseTarget()
    {
        if (IsCompress)
        {
            string? file = _dialogService.PickSaveFile(
                "ZIP 压缩包 (*.zip)|*.zip",
                "归档.zip",
                Directory.Exists(SourcePath) ? Path.GetDirectoryName(SourcePath) : null);
            if (file is not null) TargetPath = file;
            return;
        }

        string? folder = _dialogService.PickFolder(Directory.Exists(TargetPath) ? TargetPath : null);
        if (folder is not null) TargetPath = folder;
    }

    private async Task ScanAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        IsProgressIndeterminate = true;
        BusyText = "正在只读检查归档…";
        HasResult = false;
        ClearPreview();

        try
        {
            var plan = await Task.Run(() => _planner.Plan(new ArchiveOptions
            {
                Mode = SelectedMode.Mode,
                SourcePath = SourcePath,
                TargetPath = TargetPath,
                Password = Password
            }));
            if (!plan.Success)
            {
                SummaryText = plan.Error ?? "检查失败。";
                return;
            }

            SourcePath = plan.SourcePath;
            TargetPath = plan.TargetPath;
            _plan = plan;
            foreach (var entry in plan.Entries) Entries.Add(entry);
            HasPreview = true;
            SummaryText = IsCompress
                ? $"将{(string.IsNullOrEmpty(Password) ? "" : "加密")}压缩 {plan.Entries.Count} 个文件，共 {FormatBytes(plan.TotalBytes)}；输出：{plan.TargetPath}"
                : $"将安全解压 {plan.Entries.Count} 个文件到：{plan.TargetPath}";
        }
        catch (Exception ex)
        {
            SummaryText = "检查失败：" + ex.Message;
            _dialogService.ShowError("检查失败", ex.Message);
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
        string action = IsCompress ? "压缩" : "解压";
        if (!_dialogService.Confirm(
                $"执行 ZIP {action}",
                $"将{action} {_plan.Entries.Count} 个文件。\n" +
                "不会覆盖已有目标文件；完成后可在回退中心删除本次输出。是否继续？"))
        {
            return;
        }

        IsBusy = true;
        IsProgressIndeterminate = false;
        ProgressValue = 0;
        BusyText = $"正在{action}…";
        try
        {
            var plan = _plan;
            var progress = new Progress<OperationProgress>(value =>
            {
                ProgressValue = value.Total == 0 ? 0 : value.Completed * 100.0 / value.Total;
                BusyText = $"{value.Stage} · {value.CurrentFile}";
            });
            var result = await Task.Run(() => _executor.Execute(plan, progress));
            ResultIsSuccess = result.Success;
            ResultTitle = result.Success ? $"ZIP {action}完成" : $"ZIP {action}未完成";
            ResultMessage = result.Message;
            HasResult = true;
            if (result.Success)
            {
                ClearPreview();
                SummaryText = $"ZIP {action}已完成，可在回退中心删除输出。";
            }
        }
        catch (Exception ex)
        {
            ResultIsSuccess = false;
            ResultTitle = $"ZIP {action}失败";
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
        Entries.Clear();
        HasPreview = false;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = bytes;
        int index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return $"{value:0.#} {units[index]}";
    }

    private void RaiseCommandStates()
    {
        BrowseSourceCommand.RaiseCanExecuteChanged();
        BrowseTargetCommand.RaiseCanExecuteChanged();
        ScanCommand.RaiseCanExecuteChanged();
        ExecuteCommand.RaiseCanExecuteChanged();
    }
}
