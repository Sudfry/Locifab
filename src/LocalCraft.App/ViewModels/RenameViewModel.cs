using System.Collections.ObjectModel;
using System.Windows.Threading;
using LocalCraft.App.Infrastructure;
using LocalCraft.App.Services;
using LocalCraft.Core.Journal;
using LocalCraft.Core.Operations;
using LocalCraft.Core.Rename;
using LocalCraft.Core.Settings;

namespace LocalCraft.App.ViewModels;

public sealed class ReplaceRuleItem : ViewModelBase
{
    private readonly Action _changed;
    private readonly Action<ReplaceRuleItem> _remove;
    private string _findText = string.Empty;
    private string _replaceText = string.Empty;

    public ReplaceRuleItem(Action changed, Action<ReplaceRuleItem> remove)
    {
        _changed = changed;
        _remove = remove;
        RemoveCommand = new RelayCommand(() => _remove(this));
    }

    public string FindText
    {
        get => _findText;
        set
        {
            if (SetProperty(ref _findText, value ?? string.Empty)) _changed();
        }
    }

    public string ReplaceText
    {
        get => _replaceText;
        set
        {
            if (SetProperty(ref _replaceText, value ?? string.Empty)) _changed();
        }
    }

    public RelayCommand RemoveCommand { get; }
}

public sealed class RenameViewModel : ViewModelBase
{
    private readonly JournalRepository _repository;
    private readonly IDialogService _dialogService;
    private readonly RenamePlanner _planner = new();
    private readonly DispatcherTimer _previewTimer;
    private readonly DispatcherTimer _autoScanTimer;

    private List<string> _allFiles = new();
    private string _selectedSort = "文件名";
    private RenamePlan? _currentPlan;
    private bool _suppressAuto;

    private string _folderPath = string.Empty;
    private bool _includeSubfolders;
    private string _extensionFilter = string.Empty;
    private string _prefix = string.Empty;
    private string _suffix = string.Empty;
    private string _findText = string.Empty;
    private string _replaceText = string.Empty;
    private bool _matchCase;
    private bool _includeExtension;
    private bool _useNumbering;
    private string _startNumberText = "1";
    private string _numberPaddingText = "3";
    private string _numberSeparator = "_";
    private bool _numberBeforeName;
    private bool _allowOverwrite;

    private bool _isBusy;
    private bool _isProgressIndeterminate;
    private double _progressValue;
    private string _busyText = string.Empty;
    private string _scanWarning = string.Empty;
    private string _previewHint = "选择文件夹后生成只读预览。";

    private int _totalCount;
    private int _selectedRenameCount;
    private int _selectedOverwriteCount;
    private int _conflictCount;
    private int _includedConflictCount;
    private int _unchangedCount;

    private bool _hasResult;
    private bool _resultIsSuccess;
    private string _resultTitle = string.Empty;
    private string _resultMessage = string.Empty;
    private string? _lastJournalId;

    public RenameViewModel(
        JournalRepository repository,
        IDialogService dialogService,
        AppSettingsService? settings = null)
    {
        _repository = repository;
        _dialogService = dialogService;
        _includeSubfolders = settings?.Current.DefaultIncludeSubfolders ?? false;

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            BuildPreview();
        };

        _autoScanTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _autoScanTimer.Tick += async (_, _) =>
        {
            _autoScanTimer.Stop();
            if (!IsBusy && Directory.Exists(FolderPath))
            {
                await ScanAsync(silent: true);
            }
        };

        BrowseCommand = new AsyncRelayCommand(BrowseAsync);
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy);
        RefreshPreviewCommand = new RelayCommand(BuildPreview, () => !IsBusy && _allFiles.Count > 0);
        SelectAllCommand = new RelayCommand(SelectAll, () => !IsBusy && PreviewRows.Count > 0);
        ClearConflictsCommand = new RelayCommand(ClearConflicts, () => !IsBusy && HasConflicts);
        ResetRulesCommand = new RelayCommand(ResetRules, () => !IsBusy);
        AddReplaceRuleCommand = new RelayCommand(AddReplaceRule, () => !IsBusy);
        ExecuteCommand = new AsyncRelayCommand(ExecuteAsync, () => CanExecuteBatch);
        ReplaceRules.Add(CreateReplaceRule());
        OpenUndoCenterCommand = new RelayCommand(() => UndoCenterRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? UndoCenterRequested;

    public ObservableCollection<PreviewRow> PreviewRows { get; } = new();

    public ObservableCollection<ReplaceRuleItem> ReplaceRules { get; } = new();

    public AsyncRelayCommand BrowseCommand { get; }
    public AsyncRelayCommand ScanCommand { get; }
    public RelayCommand RefreshPreviewCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand ClearConflictsCommand { get; }
    public RelayCommand ResetRulesCommand { get; }
    public IReadOnlyList<string> SortOptions { get; } = new[] { "文件名", "修改时间", "创建时间", "大小", "扩展名" };
    public RelayCommand AddReplaceRuleCommand { get; }
    public AsyncRelayCommand ExecuteCommand { get; }
    public RelayCommand OpenUndoCenterCommand { get; }

    public string SelectedSort
    {
        get => _selectedSort;
        set
        {
            if (!SetProperty(ref _selectedSort, value ?? "文件名")) return;
            SortFiles();
            if (_allFiles.Count > 0) SchedulePreview();
        }
    }

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
            if (SetProperty(ref _includeSubfolders, value))
            {
                ScheduleAutoScan();
            }
        }
    }

    public string ExtensionFilter
    {
        get => _extensionFilter;
        set
        {
            if (SetProperty(ref _extensionFilter, value)) ScheduleAutoScan();
        }
    }

    public string Prefix
    {
        get => _prefix;
        set
        {
            if (SetProperty(ref _prefix, value)) SchedulePreview();
        }
    }

    public string Suffix
    {
        get => _suffix;
        set
        {
            if (SetProperty(ref _suffix, value)) SchedulePreview();
        }
    }

    public string FindText
    {
        get => _findText;
        set
        {
            if (SetProperty(ref _findText, value)) SchedulePreview();
        }
    }

    public string ReplaceText
    {
        get => _replaceText;
        set
        {
            if (SetProperty(ref _replaceText, value)) SchedulePreview();
        }
    }

    public bool MatchCase
    {
        get => _matchCase;
        set
        {
            if (SetProperty(ref _matchCase, value)) SchedulePreview();
        }
    }

    public bool IncludeExtension
    {
        get => _includeExtension;
        set
        {
            if (SetProperty(ref _includeExtension, value)) SchedulePreview();
        }
    }

    public bool UseNumbering
    {
        get => _useNumbering;
        set
        {
            if (SetProperty(ref _useNumbering, value)) SchedulePreview();
        }
    }

    public string StartNumberText
    {
        get => _startNumberText;
        set
        {
            if (SetProperty(ref _startNumberText, value)) SchedulePreview();
        }
    }

    public string NumberPaddingText
    {
        get => _numberPaddingText;
        set
        {
            if (SetProperty(ref _numberPaddingText, value)) SchedulePreview();
        }
    }

    public string NumberSeparator
    {
        get => _numberSeparator;
        set
        {
            if (SetProperty(ref _numberSeparator, value)) SchedulePreview();
        }
    }

    public bool NumberBeforeName
    {
        get => _numberBeforeName;
        set
        {
            if (SetProperty(ref _numberBeforeName, value)) SchedulePreview();
        }
    }

    public bool AllowOverwrite
    {
        get => _allowOverwrite;
        set
        {
            if (SetProperty(ref _allowOverwrite, value)) SchedulePreview();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(IsIdle));
            OnPropertyChanged(nameof(ExecuteHint));
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

    public string ScanWarning
    {
        get => _scanWarning;
        private set
        {
            if (SetProperty(ref _scanWarning, value)) OnPropertyChanged(nameof(HasScanWarning));
        }
    }

    public bool HasScanWarning => !string.IsNullOrWhiteSpace(ScanWarning);

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

    public int SelectedRenameCount
    {
        get => _selectedRenameCount;
        private set => SetProperty(ref _selectedRenameCount, value);
    }

    public int SelectedOverwriteCount
    {
        get => _selectedOverwriteCount;
        private set => SetProperty(ref _selectedOverwriteCount, value);
    }

    public int ConflictCount
    {
        get => _conflictCount;
        private set
        {
            if (SetProperty(ref _conflictCount, value)) OnPropertyChanged(nameof(HasConflicts));
        }
    }

    public int IncludedConflictCount
    {
        get => _includedConflictCount;
        private set
        {
            if (!SetProperty(ref _includedConflictCount, value)) return;
            OnPropertyChanged(nameof(HasIncludedConflicts));
            OnPropertyChanged(nameof(ExecuteHint));
            OnPropertyChanged(nameof(CanExecuteBatch));
            ExecuteCommand.RaiseCanExecuteChanged();
        }
    }

    public int UnchangedCount
    {
        get => _unchangedCount;
        private set => SetProperty(ref _unchangedCount, value);
    }

    public bool HasConflicts => ConflictCount > 0;

    public bool HasIncludedConflicts => IncludedConflictCount > 0;

    public bool HasPreviewRows => PreviewRows.Count > 0;

    public bool CanExecuteBatch => !IsBusy && IncludedConflictCount == 0
                                              && (SelectedRenameCount + SelectedOverwriteCount) > 0;

    public string ExecuteHint
    {
        get
        {
            if (IsBusy) return BusyText;
            if (PreviewRows.Count == 0) return "预览会显示旧文件名 → 新文件名，确认前不会改动任何文件。";
            if (IncludedConflictCount > 0)
            {
                return $"有 {IncludedConflictCount} 个冲突项已勾选。请取消勾选，或调整规则后重新预览。";
            }

            int count = SelectedRenameCount + SelectedOverwriteCount;
            if (count == 0) return "请勾选要处理的文件。";

            return SelectedOverwriteCount > 0
                ? $"将重命名 {count} 个文件，其中 {SelectedOverwriteCount} 个会覆盖同名文件（会先备份原文件）。"
                : $"将重命名 {count} 个文件。执行日志会自动保存在本机。";
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

    public async Task LoadFolderAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        FolderPath = path;
        await ScanAsync();
    }

    private async Task BrowseAsync()
    {
        if (IsBusy) return;
        string? folder = _dialogService.PickFolder(FolderPath);
        if (folder is null) return;
        FolderPath = folder;
        await ScanAsync();
    }

    private async Task ScanAsync()
    {
        await ScanAsync(silent: false);
    }

    private async Task ScanAsync(bool silent)
    {
        if (IsBusy) return;

        if (!Directory.Exists(FolderPath))
        {
            if (!silent)
            {
                _dialogService.ShowError("无法扫描", "请选择一个存在且可访问的文件夹。");
            }
            return;
        }

        IsBusy = true;
        IsProgressIndeterminate = true;
        ProgressValue = 0;
        BusyText = "正在只读扫描文件夹…";
        ScanWarning = string.Empty;
        OnPropertyChanged(nameof(ExecuteHint));
        RaiseCommandStates();

        try
        {
            string folder = FolderPath;
            bool includeSubfolders = IncludeSubfolders;
            string extensionFilter = ExtensionFilter;

            var result = await Task.Run(() => FileScanner.Scan(folder, includeSubfolders, extensionFilter));

            _allFiles = result.Files.ToList();
            SortFiles();
            ScanWarning = result.Warning ?? string.Empty;
            PreviewHint = _allFiles.Count == 0
                ? "没有找到符合条件的文件。"
                : $"已载入 {_allFiles.Count} 个文件 · 只读预览，未修改任何文件";

            BuildPreview();
        }
        catch (Exception ex)
        {
            ScanWarning = "扫描失败：" + ex.Message;
            _allFiles.Clear();
            BuildPreview();
            if (!silent)
            {
                _dialogService.ShowError("扫描失败", ex.Message);
            }
        }
        finally
        {
            IsBusy = false;
            IsProgressIndeterminate = false;
            RaiseCommandStates();
        }
    }

    private void SortFiles()
    {
        _allFiles.Sort(SelectedSort switch
        {
            "修改时间" => (a, b) => SafeTime(b, true).CompareTo(SafeTime(a, true)),
            "创建时间" => (a, b) => SafeTime(b, false).CompareTo(SafeTime(a, false)),
            "大小" => (a, b) => SafeSize(b).CompareTo(SafeSize(a)),
            "扩展名" => (a, b) => string.Compare(Path.GetExtension(a), Path.GetExtension(b), StringComparison.OrdinalIgnoreCase),
            _ => (a, b) => string.Compare(Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase)
        });
    }

    private static DateTime SafeTime(string path, bool modified)
    {
        try { return modified ? File.GetLastWriteTime(path) : File.GetCreationTime(path); }
        catch { return DateTime.MinValue; }
    }

    private static long SafeSize(string path)
    {
        try { return new FileInfo(path).Length; } catch { return 0; }
    }

    private void BuildPreview()
    {
        if (_suppressAuto) return;

        foreach (var row in PreviewRows)
        {
            row.InclusionChanged -= OnRowInclusionChanged;
        }

        PreviewRows.Clear();

        if (_allFiles.Count == 0)
        {
            _currentPlan = null;
            RecalculateCounts();
            OnPropertyChanged(nameof(HasPreviewRows));
            PreviewHint = string.IsNullOrWhiteSpace(FolderPath)
                ? "选择文件夹后生成只读预览。"
                : PreviewHint;
            RaiseCommandStates();
            return;
        }

        var options = new RenameOptions
        {
            Prefix = Prefix,
            Suffix = Suffix,
            FindText = FindText,
            ReplaceText = ReplaceText,
            ReplaceRules = ReplaceRules
                .Where(x => !string.IsNullOrWhiteSpace(x.FindText))
                .Select(x => new RenameReplaceRule(x.FindText, x.ReplaceText))
                .ToList(),
            MatchCase = MatchCase,
            IncludeExtension = IncludeExtension,
            UseNumbering = UseNumbering,
            StartNumber = ParseInt(StartNumberText, 1, 0, 999_999),
            NumberPadding = ParseInt(NumberPaddingText, 3, 1, 10),
            NumberSeparator = NumberSeparator ?? string.Empty,
            NumberBeforeName = NumberBeforeName,
            AllowOverwrite = AllowOverwrite
        };

        _currentPlan = _planner.CreatePlan(_allFiles, options);

        foreach (var item in _currentPlan.Items)
        {
            var row = new PreviewRow(item);
            row.InclusionChanged += OnRowInclusionChanged;
            PreviewRows.Add(row);
        }

        RecalculateCounts();
        OnPropertyChanged(nameof(HasPreviewRows));
        PreviewHint = $"{DateTime.Now:HH:mm:ss} 生成预览 · 新文件名仅存在于预览中";
        RaiseCommandStates();
    }

    private void OnRowInclusionChanged(object? sender, EventArgs e)
    {
        RecalculateCounts();
        ExecuteCommand.RaiseCanExecuteChanged();
    }

    private void RecalculateCounts()
    {
        TotalCount = PreviewRows.Count;
        SelectedRenameCount = PreviewRows.Count(x => x.IsIncluded && x.Status == RenameItemStatus.Ready);
        SelectedOverwriteCount = PreviewRows.Count(x => x.IsIncluded && x.Status == RenameItemStatus.Overwrite);
        ConflictCount = PreviewRows.Count(x => x.IsBlocking);
        IncludedConflictCount = PreviewRows.Count(x => x.IsIncluded && x.IsBlocking);
        UnchangedCount = PreviewRows.Count(x => x.Status == RenameItemStatus.Unchanged);

        OnPropertyChanged(nameof(HasConflicts));
        OnPropertyChanged(nameof(HasIncludedConflicts));
        OnPropertyChanged(nameof(CanExecuteBatch));
        OnPropertyChanged(nameof(ExecuteHint));
        ClearConflictsCommand.RaiseCanExecuteChanged();
        SelectAllCommand.RaiseCanExecuteChanged();
        RefreshPreviewCommand.RaiseCanExecuteChanged();
        ExecuteCommand.RaiseCanExecuteChanged();
    }

    private void SelectAll()
    {
        foreach (var row in PreviewRows)
        {
            row.IsIncluded = row.CanToggle;
        }
        RecalculateCounts();
    }

    private void ClearConflicts()
    {
        foreach (var row in PreviewRows.Where(static x => x.IsBlocking))
        {
            row.IsIncluded = false;
        }
        RecalculateCounts();
    }

    private void ResetRules()
    {
        ResetRuleFields();
        BuildPreview();
    }

    private void ResetRuleFields()
    {
        _suppressAuto = true;
        Prefix = string.Empty;
        Suffix = string.Empty;
        FindText = string.Empty;
        ReplaceText = string.Empty;
        ReplaceRules.Clear();
        ReplaceRules.Add(CreateReplaceRule());
        MatchCase = false;
        IncludeExtension = false;
        UseNumbering = false;
        StartNumberText = "1";
        NumberPaddingText = "3";
        NumberSeparator = "_";
        NumberBeforeName = false;
        AllowOverwrite = false;
        _suppressAuto = false;
    }

    private ReplaceRuleItem CreateReplaceRule() =>
        new(SchedulePreview, RemoveReplaceRule);

    private void AddReplaceRule()
    {
        ReplaceRules.Add(CreateReplaceRule());
        SchedulePreview();
    }

    private void RemoveReplaceRule(ReplaceRuleItem item)
    {
        ReplaceRules.Remove(item);
        SchedulePreview();
    }

    private async Task ExecuteAsync()
    {
        if (!CanExecuteBatch) return;

        var selectedRows = PreviewRows.Where(x => x.IsIncluded && x.IsExecutable).ToList();
        int renameCount = selectedRows.Count(x => x.Status == RenameItemStatus.Ready);
        int overwriteCount = selectedRows.Count(x => x.Status == RenameItemStatus.Overwrite);

        string confirmation =
            $"即将重命名 {selectedRows.Count} 个文件。{Environment.NewLine}{Environment.NewLine}" +
            $"普通重命名：{renameCount} 个{Environment.NewLine}" +
            $"覆盖已有文件：{overwriteCount} 个{Environment.NewLine}{Environment.NewLine}" +
            "执行前会自动写入回退日志；覆盖的原文件会先备份到本机。";

        if (overwriteCount > 0)
        {
            confirmation += $"{Environment.NewLine}{Environment.NewLine}注意：有 {overwriteCount} 个目标文件将被替换，但可以一键回退。";
        }

        if (!_dialogService.Confirm("确认执行批量重命名", confirmation))
        {
            return;
        }

        _previewTimer.Stop();
        _autoScanTimer.Stop();
        HasResult = false;
        LastJournalId = null;
        IsBusy = true;
        IsProgressIndeterminate = false;
        ProgressValue = 0;
        BusyText = "正在准备执行…";
        OnPropertyChanged(nameof(ExecuteHint));
        RaiseCommandStates();

        var workItems = selectedRows
            .Select(x => new RenameWorkItem(
                x.SourcePath,
                x.TargetPath,
                x.Status == RenameItemStatus.Overwrite))
            .ToList();

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
            result = await Task.Run(() => new BatchRenameExecutor(_repository).Execute(workItems, progress));
        }
        catch (Exception ex)
        {
            result = new BatchExecutionResult(false, null, "执行器发生异常：" + ex.Message);
        }
        finally
        {
            IsBusy = false;
            IsProgressIndeterminate = false;
            RaiseCommandStates();
        }

        if (result.Success && result.Journal is not null)
        {
            var renamed = selectedRows.ToDictionary(
                x => x.SourcePath,
                x => x.TargetPath,
                StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < _allFiles.Count; i++)
            {
                if (renamed.TryGetValue(_allFiles[i], out string? target))
                {
                    _allFiles[i] = target;
                }
            }

            _allFiles.Sort(static (x, y) =>
                LocalCraft.Core.Infrastructure.NaturalStringComparer.Instance.Compare(
                    Path.GetFileName(x), Path.GetFileName(y)));

            LastJournalId = result.Journal.Id;
            ResultIsSuccess = true;
            ResultTitle = "重命名完成";
            ResultMessage =
                $"已处理 {result.Journal.OperationCount} 个文件。回退日志已保存到本机，可随时一键恢复；" +
                "预览已刷新并清空规则，避免对同一批文件重复执行。";
            HasResult = true;

            // 完成后清空规则并刷新预览，避免下一次点击对已改名文件再次叠加前缀。
            ResetRuleFields();
            BuildPreview();
            PreviewHint = "操作已完成，规则已清空 · 可设置下一批规则";
        }
        else
        {
            ResultIsSuccess = false;
            ResultTitle = "本次操作未完成";
            ResultMessage = result.Message;
            HasResult = true;
            _dialogService.ShowError("重命名未完成", result.Message);

            if (result.Journal is not null)
            {
                LastJournalId = result.Journal.Id;
            }

            // 失败后重新读取目录，保证界面展示的状态与磁盘一致。
            if (Directory.Exists(FolderPath))
            {
                var scan = await Task.Run(() =>
                    FileScanner.Scan(FolderPath, IncludeSubfolders, ExtensionFilter));
                _allFiles = scan.Files.ToList();
                ScanWarning = scan.Warning ?? string.Empty;
                BuildPreview();
            }
        }

        RaiseCommandStates();
    }

    private void SchedulePreview()
    {
        if (_suppressAuto || IsBusy || _allFiles.Count == 0) return;
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void ScheduleAutoScan()
    {
        if (_suppressAuto || IsBusy || !Directory.Exists(FolderPath)) return;
        _autoScanTimer.Stop();
        _autoScanTimer.Start();
    }

    private void RaiseCommandStates()
    {
        ScanCommand.RaiseCanExecuteChanged();
        RefreshPreviewCommand.RaiseCanExecuteChanged();
        SelectAllCommand.RaiseCanExecuteChanged();
        ClearConflictsCommand.RaiseCanExecuteChanged();
        ResetRulesCommand.RaiseCanExecuteChanged();
        ExecuteCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanExecuteBatch));
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(ExecuteHint));
    }

    private static int ParseInt(string? text, int fallback, int min, int max)
    {
        return int.TryParse(text, out int value)
            ? Math.Clamp(value, min, max)
            : fallback;
    }
}
