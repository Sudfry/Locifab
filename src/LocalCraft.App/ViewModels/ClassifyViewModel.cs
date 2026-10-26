using System.Collections.ObjectModel;
using LocalCraft.App.Infrastructure;
using LocalCraft.App.Services;
using LocalCraft.Core.Classify;
using LocalCraft.Core.Journal;
using LocalCraft.Core.Operations;
using LocalCraft.Core.Settings;

namespace LocalCraft.App.ViewModels;

public sealed class ClassifyViewModel : ViewModelBase
{
    private readonly JournalRepository _repository;
    private readonly IDialogService _dialogService;
    private readonly ClassifyPlanner _planner = new();

    private string _folderPath = string.Empty;
    private bool _includeSubfolders;
    private ClassificationModeOption _selectedMode;
    private string _namePrefixLengthText = "1";
    private bool _isBusy;
    private bool _isProgressIndeterminate;
    private double _progressValue;
    private string _busyText = string.Empty;
    private string _previewHint = "选择一个文件夹，先只读扫描并生成移动预览。";
    private int _totalCount;
    private int _selectedCount;
    private int _conflictCount;
    private int _unchangedCount;
    private int _includedConflictCount;
    private bool _hasResult;
    private bool _resultIsSuccess;
    private string _resultTitle = string.Empty;
    private string _resultMessage = string.Empty;
    private string? _lastJournalId;

    public ClassifyViewModel(
        JournalRepository repository,
        IDialogService dialogService,
        AppSettingsService? settings = null)
    {
        _repository = repository;
        _dialogService = dialogService;
        _includeSubfolders = settings?.Current.DefaultIncludeSubfolders ?? false;
        Modes = new ObservableCollection<ClassificationModeOption>
        {
            new(ClassificationMode.ByType, "按文件类型"),
            new(ClassificationMode.ByYear, "按修改年份"),
            new(ClassificationMode.ByMonth, "按修改年月"),
            new(ClassificationMode.ByNamePrefix, "按名称前缀")
        };
        _selectedMode = settings is null
            ? Modes[0]
            : Modes.FirstOrDefault(x => string.Equals(
                x.Mode.ToString(),
                settings.Current.DefaultClassificationMode,
                StringComparison.OrdinalIgnoreCase)) ?? Modes[0];

        BrowseCommand = new RelayCommand(Browse, () => !IsBusy);
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy);
        RefreshCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy && Directory.Exists(FolderPath));
        ExecuteCommand = new AsyncRelayCommand(ExecuteAsync, () => CanExecuteBatch);
        ClearConflictsCommand = new RelayCommand(ClearConflicts, () => !IsBusy && HasConflicts);
        OpenUndoCenterCommand = new RelayCommand(() => UndoCenterRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? UndoCenterRequested;

    public ObservableCollection<ClassificationModeOption> Modes { get; }

    public ObservableCollection<ClassifyPreviewRow> PreviewRows { get; } = new();

    public RelayCommand BrowseCommand { get; }
    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand ExecuteCommand { get; }
    public RelayCommand ClearConflictsCommand { get; }
    public RelayCommand OpenUndoCenterCommand { get; }

    public string FolderPath
    {
        get => _folderPath;
        set => SetProperty(ref _folderPath, value);
    }

    public bool IncludeSubfolders
    {
        get => _includeSubfolders;
        set
        {
            if (SetProperty(ref _includeSubfolders, value) && HasPreview && !IsBusy) _ = ScanAsync();
        }
    }

    public ClassificationModeOption SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (!SetProperty(ref _selectedMode, value)) return;
            if (HasPreview && !IsBusy) _ = ScanAsync();
        }
    }

    public string NamePrefixLengthText
    {
        get => _namePrefixLengthText;
        set
        {
            if (SetProperty(ref _namePrefixLengthText, value) && HasPreview && !IsBusy) _ = ScanAsync();
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

    public string PreviewHint
    {
        get => _previewHint;
        private set => SetProperty(ref _previewHint, value);
    }

    public int TotalCount
    {
        get => _totalCount;
        private set => SetProperty(ref _totalCount, value);
    }

    public int SelectedCount
    {
        get => _selectedCount;
        private set => SetProperty(ref _selectedCount, value);
    }

    public int ConflictCount
    {
        get => _conflictCount;
        private set
        {
            if (SetProperty(ref _conflictCount, value)) OnPropertyChanged(nameof(HasConflicts));
        }
    }

    public int UnchangedCount
    {
        get => _unchangedCount;
        private set => SetProperty(ref _unchangedCount, value);
    }

    public int IncludedConflictCount
    {
        get => _includedConflictCount;
        private set
        {
            if (!SetProperty(ref _includedConflictCount, value)) return;
            OnPropertyChanged(nameof(ExecuteHint));
            OnPropertyChanged(nameof(CanExecuteBatch));
            ExecuteCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasConflicts => ConflictCount > 0;

    public bool HasPreview => PreviewRows.Count > 0;

    public bool HasPreviewRows => PreviewRows.Count > 0;

    public bool CanExecuteBatch => !IsBusy && SelectedCount > 0 && IncludedConflictCount == 0;

    public string ExecuteHint
    {
        get
        {
            if (IsBusy) return BusyText;
            if (!HasPreview) return "扫描只读取文件名、类型和修改时间，不会移动文件。";
            if (IncludedConflictCount > 0) return $"有 {IncludedConflictCount} 个冲突项已勾选，请取消后再执行。";
            if (SelectedCount == 0) return "请勾选要分类的文件。";
            return $"将移动 {SelectedCount} 个文件到分类子文件夹；执行前写入日志，可一键回退。";
        }
    }

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

    public string? LastJournalId
    {
        get => _lastJournalId;
        private set
        {
            if (!SetProperty(ref _lastJournalId, value)) return;
            OnPropertyChanged(nameof(HasJournalId));
        }
    }

    public bool HasJournalId => !string.IsNullOrWhiteSpace(LastJournalId);

    private void Browse()
    {
        string? folder = _dialogService.PickFolder(FolderPath);
        if (folder is null) return;
        FolderPath = folder;
        _ = ScanAsync();
    }

    private async Task ScanAsync()
    {
        if (IsBusy) return;
        if (!Directory.Exists(FolderPath))
        {
            _dialogService.ShowError("无法扫描", "请选择一个存在且可访问的文件夹。");
            return;
        }

        int prefixLength = int.TryParse(NamePrefixLengthText, out int parsed) ? Math.Clamp(parsed, 1, 4) : 1;
        var options = new ClassifyOptions
        {
            Mode = SelectedMode.Mode,
            NamePrefixLength = prefixLength,
            IncludeSubdirectories = IncludeSubfolders
        };

        IsBusy = true;
        IsProgressIndeterminate = true;
        BusyText = "正在只读扫描并规划分类…";
        HasResult = false;

        try
        {
            string folder = FolderPath;
            ClassifyPlan plan = await Task.Run(() => _planner.CreatePlan(folder, options));

            foreach (var row in PreviewRows) row.InclusionChanged -= OnRowInclusionChanged;
            PreviewRows.Clear();
            foreach (var item in plan.Items)
            {
                var row = new ClassifyPreviewRow(item);
                row.InclusionChanged += OnRowInclusionChanged;
                PreviewRows.Add(row);
            }

            // 默认取消冲突项，避免误以为会覆盖；用户仍可手动勾选查看。
            foreach (var row in PreviewRows.Where(static r => r.StatusKey == "conflict"))
            {
                row.IsIncluded = false;
            }

            RecalculateCounts();
            PreviewHint = string.IsNullOrEmpty(plan.RootDirectory)
                ? "文件夹不存在。"
                : $"{DateTime.Now:HH:mm:ss} 生成分类预览 · 扫描阶段零写入";
            OnPropertyChanged(nameof(HasPreview));
            OnPropertyChanged(nameof(HasPreviewRows));
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("分类扫描失败", ex.Message);
        }
        finally
        {
            IsBusy = false;
            IsProgressIndeterminate = false;
            RaiseCommandStates();
        }
    }

    private void OnRowInclusionChanged(object? sender, EventArgs e)
    {
        RecalculateCounts();
        RaiseCommandStates();
    }

    private void RecalculateCounts()
    {
        TotalCount = PreviewRows.Count;
        SelectedCount = PreviewRows.Count(static x => x.IsIncluded && x.Status == ClassifyItemStatus.Ready);
        ConflictCount = PreviewRows.Count(static x => x.Status == ClassifyItemStatus.Conflict);
        IncludedConflictCount = PreviewRows.Count(static x => x.IsIncluded
                                                          && x.Status is ClassifyItemStatus.Conflict or ClassifyItemStatus.Invalid);
        UnchangedCount = PreviewRows.Count(static x => x.Status == ClassifyItemStatus.Unchanged);
        OnPropertyChanged(nameof(CanExecuteBatch));
        OnPropertyChanged(nameof(ExecuteHint));
        ClearConflictsCommand.RaiseCanExecuteChanged();
    }

    private void ClearConflicts()
    {
        foreach (var row in PreviewRows.Where(static x => x.Status is ClassifyItemStatus.Conflict or ClassifyItemStatus.Invalid))
        {
            row.IsIncluded = false;
        }
        RecalculateCounts();
    }

    private async Task ExecuteAsync()
    {
        if (!CanExecuteBatch) return;
        var rows = PreviewRows.Where(static x => x.IsIncluded && x.Status == ClassifyItemStatus.Ready).ToList();
        if (rows.Count == 0) return;

        string confirmation =
            $"即将移动 {rows.Count} 个文件到分类子文件夹。{Environment.NewLine}{Environment.NewLine}" +
            $"根文件夹：{FolderPath}{Environment.NewLine}" +
            $"分类规则：{SelectedMode.DisplayName}{Environment.NewLine}{Environment.NewLine}" +
            "扫描预览不会修改文件；确认后将创建分类文件夹并移动文件，操作可一键回退。";

        if (!_dialogService.Confirm("确认执行分类移动", confirmation)) return;

        HasResult = false;
        LastJournalId = null;
        IsBusy = true;
        IsProgressIndeterminate = false;
        ProgressValue = 0;
        BusyText = "正在分类移动…";
        RaiseCommandStates();

        var work = rows.Select(row => new RenameWorkItem(
            row.SourcePath, row.TargetPath, false)).ToList();
        var progress = new Progress<OperationProgress>(value =>
        {
            ProgressValue = value.Total == 0 ? 0 : value.Completed * 100.0 / value.Total;
            BusyText = string.IsNullOrEmpty(value.CurrentFile)
                ? value.Stage
                : $"{value.Stage} · {value.CurrentFile}（{value.Completed}/{value.Total}）";
        });

        BatchExecutionResult result;
        try
        {
            result = await Task.Run(() => new BatchRenameExecutor(_repository).Execute(
                work,
                progress,
                CancellationToken.None,
                kind: "move",
                displayName: "扫描分类",
                createTargetDirectories: true));
        }
        catch (Exception ex)
        {
            result = new BatchExecutionResult(false, null, "分类移动发生异常：" + ex.Message);
        }
        finally
        {
            IsBusy = false;
            IsProgressIndeterminate = false;
            RaiseCommandStates();
        }

        if (result.Success)
        {
            ResultIsSuccess = true;
            ResultTitle = "分类移动完成";
            ResultMessage = $"已处理 {result.Journal?.OperationCount ?? rows.Count} 个文件。日志已保存，可在回退中心一键恢复。";
            HasResult = true;
            LastJournalId = result.Journal?.Id;
            foreach (var row in PreviewRows) row.InclusionChanged -= OnRowInclusionChanged;
            PreviewRows.Clear();
            RecalculateCounts();
            PreviewHint = "分类完成 · 可重新扫描查看当前文件夹";
            OnPropertyChanged(nameof(HasPreview));
            OnPropertyChanged(nameof(HasPreviewRows));
        }
        else
        {
            ResultIsSuccess = false;
            ResultTitle = "分类移动未完成";
            ResultMessage = result.Message;
            HasResult = true;
            LastJournalId = result.Journal?.Id;
            _dialogService.ShowError("分类移动未完成", result.Message);
            await ScanAsync();
        }

        RaiseCommandStates();
    }

    private void RaiseCommandStates()
    {
        BrowseCommand.RaiseCanExecuteChanged();
        ScanCommand.RaiseCanExecuteChanged();
        RefreshCommand.RaiseCanExecuteChanged();
        ExecuteCommand.RaiseCanExecuteChanged();
        ClearConflictsCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanExecuteBatch));
        OnPropertyChanged(nameof(ExecuteHint));
    }
}
