using System.Collections.ObjectModel;
using LocalCraft.App.Infrastructure;
using LocalCraft.App.Services;
using LocalCraft.Conversion.Engines;
using LocalCraft.Conversion.Formats;
using LocalCraft.Conversion.Operations;
using LocalCraft.Conversion.Planning;
using LocalCraft.Core.Journal;
using LocalCraft.Core.Operations;
using LocalCraft.Core.Settings;

namespace LocalCraft.App.ViewModels;

public sealed class ConvertViewModel : ViewModelBase
{
    private readonly JournalRepository _repository;
    private readonly IDialogService _dialogService;
    private readonly ConversionEngineCatalog _engines;
    private readonly ConversionPlanner _planner;
    private readonly List<string> _queuePaths = new();

    private ConversionTargetFormat _selectedTarget;
    private string _outputDirectory = string.Empty;
    private bool _includeSubfolders;

    private bool _isBusy;
    private bool _isProgressIndeterminate;
    private double _progressValue;
    private string _busyText = string.Empty;
    private CancellationTokenSource? _executionCts;

    private string _noticeText = string.Empty;
    private string _setupWarning = string.Empty;
    private string _engineAlertText = string.Empty;
    private string _previewHint = "添加文件后生成转换预览。";

    private int _totalCount;
    private int _selectedReadyCount;
    private int _unsupportedCount;
    private int _missingEngineCount;
    private int _invalidCount;

    private bool _hasResult;
    private bool _resultIsSuccess;
    private string _resultTitle = string.Empty;
    private string _resultMessage = string.Empty;
    private string? _lastJournalId;

    public ConvertViewModel(
        JournalRepository repository,
        IDialogService dialogService,
        AppSettingsService? settings = null)
    {
        _repository = repository;
        _dialogService = dialogService;
        _includeSubfolders = settings?.Current.DefaultIncludeSubfolders ?? false;
        _engines = new ConversionEngineCatalog();
        _planner = new ConversionPlanner(_engines);

        TargetFormats = new ObservableCollection<ConversionTargetFormat>(ConversionTargetFormat.All);
        _selectedTarget = TargetFormats.First(static x => x.Id == "markdown");

        AddFilesCommand = new RelayCommand(AddFiles, () => !IsBusy);
        AddFolderCommand = new AsyncRelayCommand(AddFolderAsync, () => !IsBusy);
        BrowseOutputCommand = new RelayCommand(BrowseOutput, () => !IsBusy);
        ClearQueueCommand = new RelayCommand(ClearQueue, () => !IsBusy && Queue.Count > 0);
        RefreshPreviewCommand = new RelayCommand(BuildPreview, () => !IsBusy && Queue.Count > 0);
        ExecuteCommand = new AsyncRelayCommand(ExecuteAsync, () => CanExecuteBatch);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy && _executionCts is { IsCancellationRequested: false });
        OpenUndoCenterCommand = new RelayCommand(() => UndoCenterRequested?.Invoke(this, EventArgs.Empty));
        OpenOutputFolderCommand = new RelayCommand(OpenOutputFolder, () => !string.IsNullOrWhiteSpace(OutputDirectory));

        EngineStatusText = string.Join("  ·  ", _engines.Statuses.Select(static s =>
            (s.Available ? "✓ " : "— ") + s.Name + (s.Available ? "" : " 未检测到")));
        EngineDetailsText = string.Join(
            Environment.NewLine,
            _engines.Statuses.Select(static s => $"{s.Name}：{s.Description}"));
    }

    public event EventHandler? UndoCenterRequested;

    public void AddExternalFiles(IEnumerable<string> paths)
    {
        AddPaths(paths);
    }

    public ObservableCollection<ConversionQueueItem> Queue { get; } = new();

    public ObservableCollection<ConversionPreviewRow> PreviewRows { get; } = new();

    public ObservableCollection<ConversionTargetFormat> TargetFormats { get; }

    public RelayCommand AddFilesCommand { get; }
    public AsyncRelayCommand AddFolderCommand { get; }
    public RelayCommand BrowseOutputCommand { get; }
    public RelayCommand ClearQueueCommand { get; }
    public RelayCommand RefreshPreviewCommand { get; }
    public AsyncRelayCommand ExecuteCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand OpenUndoCenterCommand { get; }
    public RelayCommand OpenOutputFolderCommand { get; }

    public string EngineStatusText { get; }

    public string EngineDetailsText { get; }

    public ConversionTargetFormat SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (value is null || !SetProperty(ref _selectedTarget, value)) return;
            OnPropertyChanged(nameof(TargetHint));
            OnPropertyChanged(nameof(TargetWarning));
            OnPropertyChanged(nameof(HasTargetWarning));
            BuildPreview();
        }
    }

    public string OutputDirectory
    {
        get => _outputDirectory;
        set
        {
            if (!SetProperty(ref _outputDirectory, value)) return;
            OnPropertyChanged(nameof(HasOutputDirectory));
            BuildPreview();
        }
    }

    public bool HasOutputDirectory => !string.IsNullOrWhiteSpace(OutputDirectory);

    public bool IncludeSubfolders
    {
        get => _includeSubfolders;
        set => SetProperty(ref _includeSubfolders, value);
    }

    public string TargetHint => SelectedTarget.Kind == ConversionKind.Image
        ? $"图片转换 · 输出扩展名 {SelectedTarget.Extension}"
        : $"文档转换 · 输出扩展名 {SelectedTarget.Extension}";

    public string TargetWarning
    {
        get
        {
            if (SelectedTarget.Id != "pptx") return string.Empty;
            ConversionEngineStatus? lo = _engines.Statuses.FirstOrDefault(static x => x.Id == "libreoffice");
            if (lo?.Available == true) return string.Empty;
            return "PPTX 输出需要 LibreOffice 引擎；当前未检测到，相关文件会标记为「缺少引擎」。";
        }
    }

    public bool HasTargetWarning => !string.IsNullOrWhiteSpace(TargetWarning);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(IsIdle));
            OnPropertyChanged(nameof(IsBusyVisible));
            RaiseCommandStates();
        }
    }

    public bool IsIdle => !IsBusy;

    public bool IsBusyVisible => IsBusy;

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

    public string NoticeText
    {
        get => _noticeText;
        private set
        {
            if (SetProperty(ref _noticeText, value)) OnPropertyChanged(nameof(HasNotice));
        }
    }

    public bool HasNotice => !string.IsNullOrWhiteSpace(NoticeText);

    public string SetupWarning
    {
        get => _setupWarning;
        private set
        {
            if (SetProperty(ref _setupWarning, value)) OnPropertyChanged(nameof(HasSetupWarning));
        }
    }

    public bool HasSetupWarning => !string.IsNullOrWhiteSpace(SetupWarning);

    public string EngineAlertText
    {
        get => _engineAlertText;
        private set
        {
            if (SetProperty(ref _engineAlertText, value)) OnPropertyChanged(nameof(HasEngineAlert));
        }
    }

    public bool HasEngineAlert => !string.IsNullOrWhiteSpace(EngineAlertText);

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

    public int SelectedReadyCount
    {
        get => _selectedReadyCount;
        private set => SetProperty(ref _selectedReadyCount, value);
    }

    public int UnsupportedCount
    {
        get => _unsupportedCount;
        private set => SetProperty(ref _unsupportedCount, value);
    }

    public int MissingEngineCount
    {
        get => _missingEngineCount;
        private set => SetProperty(ref _missingEngineCount, value);
    }

    public int InvalidCount
    {
        get => _invalidCount;
        private set => SetProperty(ref _invalidCount, value);
    }

    public bool HasQueue => Queue.Count > 0;

    public bool HasPreviewRows => PreviewRows.Count > 0;

    public string QueueCountText => Queue.Count == 0 ? "尚未添加文件" : $"队列中 {Queue.Count} 个文件";

    public bool CanExecuteBatch => !IsBusy && SelectedReadyCount > 0;

    public string ExecuteHint
    {
        get
        {
            if (IsBusy) return BusyText;
            if (Queue.Count == 0) return "添加文档或图片后，会先显示「源文件 → 输出文件」对照，再执行转换。";
            if (SelectedReadyCount == 0)
            {
                return MissingEngineCount > 0
                    ? "当前任务缺少可用的转换引擎。可改变目标格式，或补充本地引擎。"
                    : "当前没有可执行的转换任务，请调整目标格式或移除不支持的文件。";
            }

            return $"将生成 {SelectedReadyCount} 个新文件；原文件保留，输出文件可一键回退。";
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

    public async Task AddDroppedPathsAsync(IEnumerable<string> paths)
    {
        if (IsBusy) return;

        var files = new List<string>();
        var directories = new List<string>();
        foreach (string path in paths)
        {
            if (Directory.Exists(path)) directories.Add(path);
            else if (File.Exists(path)) files.Add(path);
        }

        if (directories.Count > 0)
        {
            IsBusy = true;
            IsProgressIndeterminate = true;
            BusyText = "正在只读扫描拖入的文件夹…";
            try
            {
                foreach (string directory in directories)
                {
                    var scan = await Task.Run(() =>
                        ConversionFileScanner.Scan(directory, IncludeSubfolders));
                    files.AddRange(scan.Files);
                }
            }
            finally
            {
                IsBusy = false;
                IsProgressIndeterminate = false;
            }
        }

        AddPaths(files);
    }

    public void AddDroppedFiles(IEnumerable<string> paths)
    {
        AddPaths(paths.Where(File.Exists));
    }

    private void AddFiles()
    {
        IReadOnlyList<string> files = _dialogService.PickFiles(
            SourceFormats.FileDialogFilter,
            _queuePaths.Count > 0 ? Path.GetDirectoryName(_queuePaths[0]) : null);

        if (files.Count > 0) AddPaths(files);
    }

    private async Task AddFolderAsync()
    {
        if (IsBusy) return;
        string? folder = _dialogService.PickFolder(
            _queuePaths.Count > 0 ? Path.GetDirectoryName(_queuePaths[0]) : null);
        if (folder is null) return;

        IsBusy = true;
        IsProgressIndeterminate = true;
        BusyText = "正在只读扫描文件夹…";
        try
        {
            var scan = await Task.Run(() =>
                ConversionFileScanner.Scan(folder, IncludeSubfolders));
            IsBusy = false;
            IsProgressIndeterminate = false;
            AddPaths(scan.Files);
            if (scan.Truncated)
            {
                NoticeText = "文件夹中的支持文件过多，仅载入前 5,000 个。";
            }
        }
        catch (Exception ex)
        {
            NoticeText = "扫描文件夹失败：" + ex.Message;
            _dialogService.ShowError("无法扫描文件夹", ex.Message);
        }
        finally
        {
            IsBusy = false;
            IsProgressIndeterminate = false;
        }
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        if (IsBusy) return;

        bool wasEmpty = _queuePaths.Count == 0;
        int skippedUnsupported = 0;
        int duplicates = 0;
        int added = 0;

        foreach (string rawPath in paths)
        {
            string path;
            try
            {
                path = Path.GetFullPath(rawPath);
            }
            catch
            {
                skippedUnsupported++;
                continue;
            }

            if (!File.Exists(path) || !SourceFormats.IsSupported(path))
            {
                skippedUnsupported++;
                continue;
            }

            if (_queuePaths.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                duplicates++;
                continue;
            }

            _queuePaths.Add(path);
            added++;
        }

        if (added > 0)
        {
            _queuePaths.Sort(static (x, y) =>
                LocalCraft.Core.Infrastructure.NaturalStringComparer.Instance.Compare(
                    Path.GetFileName(x), Path.GetFileName(y)));

            RebuildQueueCollection();

            if (wasEmpty)
            {
                InitializeDefaultsForQueue();
            }

            BuildPreview();
        }

        var noticeParts = new List<string>();
        if (added > 0) noticeParts.Add($"已添加 {added} 个文件");
        if (skippedUnsupported > 0) noticeParts.Add($"跳过 {skippedUnsupported} 个不支持或不可访问的文件");
        if (duplicates > 0) noticeParts.Add($"忽略 {duplicates} 个重复项");
        NoticeText = noticeParts.Count > 0 ? string.Join("；", noticeParts) : string.Empty;

        OnPropertyChanged(nameof(HasQueue));
        OnPropertyChanged(nameof(QueueCountText));
        RaiseCommandStates();
    }

    private void RebuildQueueCollection()
    {
        Queue.Clear();
        foreach (string path in _queuePaths)
        {
            try
            {
                Queue.Add(new ConversionQueueItem(path));
            }
            catch
            {
                // 文件在加入队列后被移除时忽略。
            }
        }
    }

    private void InitializeDefaultsForQueue()
    {
        if (_queuePaths.Count == 0) return;

        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            string? directory = Path.GetDirectoryName(_queuePaths[0]);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                _outputDirectory = Path.Combine(directory, "Locifab_转换");
                OnPropertyChanged(nameof(OutputDirectory));
                OnPropertyChanged(nameof(HasOutputDirectory));
            }
        }

        ConversionKind? kind = SourceFormats.GetKind(_queuePaths[0]);
        string sourceExtension = SourceFormats.NormalizeExtension(_queuePaths[0]);
        string preferredId = kind == ConversionKind.Image
            ? sourceExtension is ".jpg" or ".jpeg"
                ? "png"
                : sourceExtension is ".png"
                    ? "jpg"
                    : "png"
            : sourceExtension is ".md" or ".markdown"
                ? "docx"
                : "markdown";

        ConversionTargetFormat? preferred = TargetFormats.FirstOrDefault(x => x.Id == preferredId);

        if (preferred is not null && !string.Equals(preferred.Id, _selectedTarget.Id, StringComparison.Ordinal))
        {
            _selectedTarget = preferred;
            OnPropertyChanged(nameof(SelectedTarget));
            OnPropertyChanged(nameof(TargetHint));
            OnPropertyChanged(nameof(TargetWarning));
            OnPropertyChanged(nameof(HasTargetWarning));
        }
    }

    private void BrowseOutput()
    {
        string? folder = _dialogService.PickFolder(
            Directory.Exists(OutputDirectory)
                ? OutputDirectory
                : _queuePaths.Count > 0
                    ? Path.GetDirectoryName(_queuePaths[0])
                    : null);

        if (folder is not null)
        {
            OutputDirectory = folder;
        }
    }

    private void ClearQueue()
    {
        foreach (var row in PreviewRows) row.InclusionChanged -= OnRowInclusionChanged;
        PreviewRows.Clear();
        Queue.Clear();
        _queuePaths.Clear();

        HasResult = false;
        LastJournalId = null;
        NoticeText = string.Empty;
        EngineAlertText = string.Empty;
        SetupWarning = string.Empty;
        PreviewHint = "添加文件后生成转换预览。";
        RecalculateCounts();
        OnPropertyChanged(nameof(HasQueue));
        OnPropertyChanged(nameof(HasPreviewRows));
        OnPropertyChanged(nameof(QueueCountText));
        RaiseCommandStates();
    }

    private void BuildPreview()
    {
        foreach (var row in PreviewRows) row.InclusionChanged -= OnRowInclusionChanged;
        PreviewRows.Clear();

        if (_queuePaths.Count == 0)
        {
            EngineAlertText = string.Empty;
            SetupWarning = string.Empty;
            PreviewHint = "添加文件后生成转换预览。";
            RecalculateCounts();
            OnPropertyChanged(nameof(HasPreviewRows));
            RaiseCommandStates();
            return;
        }

        var plan = _planner.CreatePlan(_queuePaths, OutputDirectory, SelectedTarget);
        foreach (var item in plan.Items)
        {
            var row = new ConversionPreviewRow(item);
            row.InclusionChanged += OnRowInclusionChanged;
            PreviewRows.Add(row);
        }

        SetupWarning = plan.SetupError ?? string.Empty;

        var reasons = plan.Items
            .Where(static x => x.Status is ConversionItemStatus.MissingEngine or ConversionItemStatus.Unsupported or ConversionItemStatus.Invalid)
            .Select(static x => x.Note)
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Take(3)
            .ToList();
        EngineAlertText = string.Join("；", reasons);

        PreviewHint = $"{DateTime.Now:HH:mm:ss} 生成预览 · 尚未创建输出文件";
        RecalculateCounts();
        OnPropertyChanged(nameof(HasPreviewRows));
        RaiseCommandStates();
    }

    private void OnRowInclusionChanged(object? sender, EventArgs e)
    {
        RecalculateCounts();
        RaiseCommandStates();
    }

    private void RecalculateCounts()
    {
        TotalCount = PreviewRows.Count;
        SelectedReadyCount = PreviewRows.Count(static x => x.IsIncluded && x.IsReady);
        UnsupportedCount = PreviewRows.Count(static x => x.StatusKey == "unsupported");
        MissingEngineCount = PreviewRows.Count(static x => x.StatusKey == "missing");
        InvalidCount = PreviewRows.Count(static x => x.StatusKey == "invalid");
        OnPropertyChanged(nameof(CanExecuteBatch));
        OnPropertyChanged(nameof(ExecuteHint));
    }

    private async Task ExecuteAsync()
    {
        if (!CanExecuteBatch) return;

        var selectedRows = PreviewRows.Where(static x => x.IsIncluded && x.IsReady).ToList();
        if (selectedRows.Count == 0) return;

        string confirmation =
            $"即将转换 {selectedRows.Count} 个文件为 {SelectedTarget.DisplayName}。{Environment.NewLine}{Environment.NewLine}" +
            $"输出文件夹：{OutputDirectory}{Environment.NewLine}{Environment.NewLine}" +
            "原文件会保留；转换输出会写入本地回退日志。回退时将删除本次生成的输出文件。";

        if (!_dialogService.Confirm("确认执行批量格式转换", confirmation))
        {
            return;
        }

        HasResult = false;
        LastJournalId = null;
        IsBusy = true;
        IsProgressIndeterminate = false;
        ProgressValue = 0;
        BusyText = "正在准备转换…";
        _executionCts = new CancellationTokenSource();
        RaiseCommandStates();

        var workItems = selectedRows.Select(row => new ConversionWorkItem(
            row.SourcePath,
            row.TargetPath,
            SelectedTarget,
            row.Item.EngineId ?? string.Empty)).ToList();

        var progress = new Progress<OperationProgress>(value =>
        {
            ProgressValue = value.Total == 0 ? 0 : value.Completed * 100.0 / value.Total;
            BusyText = string.IsNullOrEmpty(value.CurrentFile)
                ? value.Stage
                : $"{value.Stage} · {value.CurrentFile}（{value.Completed}/{value.Total}）";
        });

        ConversionBatchResult result;
        try
        {
            var executor = new ConversionExecutor(_repository, _engines);
            result = await executor.ExecuteAsync(workItems, progress, _executionCts.Token);
        }
        catch (Exception ex)
        {
            result = new ConversionBatchResult(false, null, "转换执行器发生异常：" + ex.Message);
        }
        finally
        {
            _executionCts.Dispose();
            _executionCts = null;
            IsBusy = false;
            IsProgressIndeterminate = false;
            RaiseCommandStates();
        }

        if (result.Success && result.Journal is not null)
        {
            LastJournalId = result.Journal.Id;
            ResultIsSuccess = true;
            ResultTitle = "格式转换完成";
            ResultMessage =
                $"已生成 {result.Journal.OperationCount} 个输出文件，保存在「{OutputDirectory}」。" +
                "原文件未改动；回退中心可删除本批输出。";
            HasResult = true;

            foreach (var row in PreviewRows) row.InclusionChanged -= OnRowInclusionChanged;
            PreviewRows.Clear();
            Queue.Clear();
            _queuePaths.Clear();
            RecalculateCounts();
            OnPropertyChanged(nameof(HasQueue));
            OnPropertyChanged(nameof(HasPreviewRows));
            OnPropertyChanged(nameof(QueueCountText));
            PreviewHint = "转换完成 · 队列已清空，可添加下一批文件";
            NoticeText = "转换完成，队列已清空。输出文件已写入回退日志。";
            EngineAlertText = string.Empty;
            SetupWarning = string.Empty;
        }
        else
        {
            ResultIsSuccess = false;
            ResultTitle = "本次转换未完成";
            ResultMessage = result.Message;
            HasResult = true;
            if (result.Journal is not null) LastJournalId = result.Journal.Id;
            _dialogService.ShowError("格式转换未完成", result.Message);
            BuildPreview();
        }

        RaiseCommandStates();
    }

    private void Cancel()
    {
        if (_executionCts is null || _executionCts.IsCancellationRequested) return;
        BusyText = "正在取消并清理本次输出…";
        _executionCts.Cancel();
        CancelCommand.RaiseCanExecuteChanged();
    }

    private void OpenOutputFolder()
    {
        try
        {
            ShellService.OpenFolder(OutputDirectory);
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("无法打开输出文件夹", ex.Message);
        }
    }

    private void RaiseCommandStates()
    {
        AddFilesCommand.RaiseCanExecuteChanged();
        AddFolderCommand.RaiseCanExecuteChanged();
        BrowseOutputCommand.RaiseCanExecuteChanged();
        ClearQueueCommand.RaiseCanExecuteChanged();
        RefreshPreviewCommand.RaiseCanExecuteChanged();
        ExecuteCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        OpenOutputFolderCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanExecuteBatch));
        OnPropertyChanged(nameof(ExecuteHint));
    }
}
