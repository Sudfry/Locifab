using System.Collections.ObjectModel;
using LocalCraft.App.Infrastructure;
using LocalCraft.App.Services;
using LocalCraft.Conversion.Documents;
using LocalCraft.Conversion.Formats;
using LocalCraft.Core.Analysis;
using LocalCraft.Core.Journal;
using LocalCraft.Core.Operations;
using LocalCraft.Core.Settings;

namespace LocalCraft.App.ViewModels;

public sealed class DedupViewModel : ViewModelBase
{
    private readonly JournalRepository _repository;
    private readonly IDialogService _dialogService;
    private readonly DedupEngine _dedupEngine = new();
    private readonly List<DedupDocument> _analyzed = new();
    private readonly List<string> _queuePaths = new();

    private double _thresholdPercent = 88;
    private bool _includeSubfolders;
    private string _quarantineDirectory = string.Empty;
    private bool _isBusy;
    private bool _isProgressIndeterminate;
    private double _progressValue;
    private string _busyText = string.Empty;
    private string _noticeText = string.Empty;
    private DedupGroupItem? _selectedGroup;
    private int _analyzedCount;
    private int _skippedCount;
    private int _duplicateCount;
    private int _selectedRemovalCount;
    private bool _hasAnalyzed;
    private bool _hasResult;
    private bool _resultIsSuccess;
    private string _resultTitle = string.Empty;
    private string _resultMessage = string.Empty;
    private string? _lastJournalId;

    public DedupViewModel(
        JournalRepository repository,
        IDialogService dialogService,
        AppSettingsService? settings = null)
    {
        _repository = repository;
        _dialogService = dialogService;
        _thresholdPercent = settings?.Current.DedupThreshold ?? 88;
        _includeSubfolders = settings?.Current.DefaultIncludeSubfolders ?? false;

        AddFilesCommand = new AsyncRelayCommand(AddFilesAsync, () => !IsBusy);
        AddFolderCommand = new AsyncRelayCommand(AddFolderAsync, () => !IsBusy);
        AnalyzeCommand = new AsyncRelayCommand(AnalyzeAsync, () => !IsBusy && Queue.Count > 0);
        ClearCommand = new RelayCommand(Clear, () => !IsBusy && Queue.Count > 0);
        BrowseQuarantineCommand = new RelayCommand(BrowseQuarantine, () => !IsBusy);
        ExecuteCommand = new AsyncRelayCommand(ExecuteAsync, () => CanExecute);
        OpenUndoCenterCommand = new RelayCommand(() => UndoCenterRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? UndoCenterRequested;

    public ObservableCollection<ConversionQueueItem> Queue { get; } = new();

    public ObservableCollection<DedupGroupItem> Groups { get; } = new();

    public AsyncRelayCommand AddFilesCommand { get; }
    public AsyncRelayCommand AddFolderCommand { get; }
    public AsyncRelayCommand AnalyzeCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand BrowseQuarantineCommand { get; }
    public AsyncRelayCommand ExecuteCommand { get; }
    public RelayCommand OpenUndoCenterCommand { get; }

    public bool IncludeSubfolders
    {
        get => _includeSubfolders;
        set => SetProperty(ref _includeSubfolders, value);
    }

    public double ThresholdPercent
    {
        get => _thresholdPercent;
        set
        {
            if (!SetProperty(ref _thresholdPercent, Math.Clamp(value, 50, 100))) return;
            OnPropertyChanged(nameof(ThresholdLabel));
            if (_analyzed.Count > 0 && !IsBusy) BuildGroups();
        }
    }

    public string ThresholdLabel => $"{ThresholdPercent:0}%";

    public string QuarantineDirectory
    {
        get => _quarantineDirectory;
        set
        {
            if (!SetProperty(ref _quarantineDirectory, value)) return;
            if (_analyzed.Count > 0 && !IsBusy) BuildGroups();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(IsIdle));
            OnPropertyChanged(nameof(HasEmptyState));
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

    public string NoticeText
    {
        get => _noticeText;
        private set
        {
            if (SetProperty(ref _noticeText, value)) OnPropertyChanged(nameof(HasNotice));
        }
    }

    public bool HasNotice => !string.IsNullOrWhiteSpace(NoticeText);

    public DedupGroupItem? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (!SetProperty(ref _selectedGroup, value)) return;
            OnPropertyChanged(nameof(HasSelectedGroup));
        }
    }

    public bool HasSelectedGroup => SelectedGroup is not null;

    public int AnalyzedCount
    {
        get => _analyzedCount;
        private set => SetProperty(ref _analyzedCount, value);
    }

    public int SkippedCount
    {
        get => _skippedCount;
        private set => SetProperty(ref _skippedCount, value);
    }

    public int DuplicateCount
    {
        get => _duplicateCount;
        private set
        {
            if (SetProperty(ref _duplicateCount, value)) OnPropertyChanged(nameof(GroupsSummary));
        }
    }

    public int SelectedRemovalCount
    {
        get => _selectedRemovalCount;
        private set
        {
            if (!SetProperty(ref _selectedRemovalCount, value)) return;
            OnPropertyChanged(nameof(CanExecute));
            OnPropertyChanged(nameof(ExecuteHint));
            ExecuteCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasAnalyzed
    {
        get => _hasAnalyzed;
        private set
        {
            if (!SetProperty(ref _hasAnalyzed, value)) return;
            OnPropertyChanged(nameof(HasEmptyState));
        }
    }

    public bool HasGroups => Groups.Count > 0;

    public bool HasEmptyState => !HasGroups && !IsBusy;

    public string GroupsSummary => DuplicateCount == 0
        ? "尚未发现相似文档"
        : $"{Groups.Count} 个相似组 · 可移出 {DuplicateCount} 个文件";

    public string QueueCountText => Queue.Count == 0 ? "尚未添加文档" : $"已加入 {Queue.Count} 个文档";

    public bool CanExecute => !IsBusy && SelectedRemovalCount > 0
                              && !string.IsNullOrWhiteSpace(QuarantineDirectory);

    public string ExecuteHint
    {
        get
        {
            if (IsBusy) return BusyText;
            if (Queue.Count == 0) return "添加 Word、PDF、TXT、Markdown 等文档后计算内容指纹。";
            if (!HasAnalyzed) return "点击「分析内容」；分析阶段只读取文本，不修改或移动文件。";
            if (SelectedRemovalCount == 0) return "在相似组中勾选要移出的重复文件。";
            return $"将把 {SelectedRemovalCount} 个重复文件移入隔离文件夹；移动记录写入回退日志。";
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
        var files = new List<string>();
        foreach (string path in paths)
        {
            if (File.Exists(path)) files.Add(path);
        }
        await AddPathsAsync(files);
        if (Queue.Count > 0 && !IsBusy) await AnalyzeAsync();
    }

    private async Task AddFilesAsync()
    {
        IReadOnlyList<string> selected = _dialogService.PickFiles(
            SourceFormats.TextDocumentFileDialogFilter,
            _queuePaths.Count > 0 ? Path.GetDirectoryName(_queuePaths[0]) : null);
        if (selected.Count > 0) await AddPathsAsync(selected);
    }

    private async Task AddPathsAsync(IEnumerable<string> paths)
    {
        if (IsBusy) return;
        int skipped = 0;
        int added = 0;

        foreach (string path in paths)
        {
            string full;
            try { full = Path.GetFullPath(path); }
            catch { skipped++; continue; }

            if (!File.Exists(full) || !SourceFormats.CanExtractText(full))
            {
                skipped++;
                continue;
            }

            if (_queuePaths.Contains(full, StringComparer.OrdinalIgnoreCase)) continue;
            _queuePaths.Add(full);
            added++;
        }

        if (added > 0)
        {
            _queuePaths.Sort(static (x, y) =>
                LocalCraft.Core.Infrastructure.NaturalStringComparer.Instance.Compare(
                    Path.GetFileName(x), Path.GetFileName(y)));
            Queue.Clear();
            foreach (string path in _queuePaths)
            {
                try { Queue.Add(new ConversionQueueItem(path)); } catch { }
            }

            if (string.IsNullOrWhiteSpace(QuarantineDirectory))
            {
                string? directory = Path.GetDirectoryName(_queuePaths[0]);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    QuarantineDirectory = Path.Combine(directory, "Locifab_重复项");
                }
            }

            ResetAnalysis();
        }

        NoticeText = skipped > 0
            ? $"已添加 {added} 个文档，跳过 {skipped} 个不支持的文件"
            : $"已添加 {added} 个文档";
        OnPropertyChanged(nameof(QueueCountText));
        OnPropertyChanged(nameof(HasEmptyState));
        RaiseCommandStates();
        await Task.CompletedTask;
    }

    private async Task AddFolderAsync()
    {
        if (IsBusy) return;
        string? folder = _dialogService.PickFolder(
            _queuePaths.Count > 0 ? Path.GetDirectoryName(_queuePaths[0]) : null);
        if (folder is null) return;

        IsBusy = true;
        IsProgressIndeterminate = true;
        BusyText = "正在只读扫描文档…";
        try
        {
            var files = await Task.Run(() =>
            {
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = IncludeSubfolders,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
                };
                return Directory.EnumerateFiles(folder, "*", options)
                    .Where(SourceFormats.CanExtractText)
                    .Take(500)
                    .ToList();
            });
            IsBusy = false;
            await AddPathsAsync(files);
        }
        catch (Exception ex)
        {
            NoticeText = "扫描失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
            IsProgressIndeterminate = false;
        }
    }

    private async Task AnalyzeAsync()
    {
        if (IsBusy || Queue.Count == 0) return;

        IsBusy = true;
        OnPropertyChanged(nameof(HasEmptyState));
        IsProgressIndeterminate = false;
        ProgressValue = 0;
        BusyText = "正在提取文本并计算 SimHash…";
        HasResult = false;
        LastJournalId = null;
        _analyzed.Clear();
        int skipped = 0;

        try
        {
            for (int i = 0; i < _queuePaths.Count; i++)
            {
                string path = _queuePaths[i];
                BusyText = $"正在分析 · {Path.GetFileName(path)}（{i + 1}/{_queuePaths.Count}）";
                ProgressValue = (i + 1) * 100.0 / _queuePaths.Count;

                try
                {
                    var info = new FileInfo(path);
                    if (!info.Exists || info.Length > 30L * 1024 * 1024)
                    {
                        skipped++;
                        continue;
                    }

                    string text = await Task.Run(() => DocumentTextService.ExtractText(path));
                    if (text.Length < 20)
                    {
                        skipped++;
                        continue;
                    }

                    var tokens = TextTokenizer.Tokenize(text);
                    if (tokens.Count < DedupEngine.MinimumTokenCount)
                    {
                        skipped++;
                        continue;
                    }

                    _analyzed.Add(new DedupDocument(
                        path,
                        text,
                        SimHash.Compute(tokens),
                        tokens.Count,
                        info.Length));
                }
                catch
                {
                    skipped++;
                }
            }

            AnalyzedCount = _analyzed.Count;
            SkippedCount = skipped;
            HasAnalyzed = true;
            BuildGroups();
            NoticeText = $"已分析 {AnalyzedCount} 个文档" +
                         (skipped > 0 ? $"，跳过 {skipped} 个无文本或过短文件" : "");
        }
        catch (Exception ex)
        {
            NoticeText = "分析失败：" + ex.Message;
            _dialogService.ShowError("内容去重分析失败", ex.Message);
        }
        finally
        {
            IsBusy = false;
            IsProgressIndeterminate = false;
            RaiseCommandStates();
        }
    }

    private void BuildGroups()
    {
        foreach (var group in Groups)
        {
            foreach (var file in group.Files) file.RemovalChanged -= OnRemovalChanged;
        }

        Groups.Clear();
        if (_analyzed.Count == 0)
        {
            DuplicateCount = 0;
            SelectedRemovalCount = 0;
            SelectedGroup = null;
            OnPropertyChanged(nameof(HasGroups));
            OnPropertyChanged(nameof(HasEmptyState));
            OnPropertyChanged(nameof(GroupsSummary));
            RaiseCommandStates();
            return;
        }

        var sourceGroups = _dedupEngine.Group(_analyzed, ThresholdPercent / 100.0);
        int groupIndex = 1;
        var usedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sourceGroup in sourceGroups)
        {
            var files = new ObservableCollection<DedupFileItem>();
            var representative = sourceGroup.Representative;

            foreach (var document in sourceGroup.Documents)
            {
                bool isRepresentative = ReferenceEquals(document, representative);
                double similarity = isRepresentative
                    ? 1
                    : SimHash.Similarity(document.Fingerprint, representative.Fingerprint);
                string target = isRepresentative
                    ? string.Empty
                    : AllocateTargetPath(QuarantineDirectory, document.FileName, usedTargets);

                var fileItem = new DedupFileItem(document, isRepresentative, similarity, target);
                fileItem.RemovalChanged += OnRemovalChanged;
                files.Add(fileItem);
            }

            Groups.Add(new DedupGroupItem(groupIndex++, files, sourceGroup.Similarity));
        }

        DuplicateCount = Groups.Sum(static g => g.Files.Count - 1);
        SelectedGroup = Groups.FirstOrDefault();
        RecalculateRemovalCount();
        OnPropertyChanged(nameof(HasGroups));
        OnPropertyChanged(nameof(HasEmptyState));
        OnPropertyChanged(nameof(GroupsSummary));
        RaiseCommandStates();
    }

    private static string AllocateTargetPath(string root, string fileName, HashSet<string> used)
    {
        if (string.IsNullOrWhiteSpace(root)) root = string.Empty;
        string baseName = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);
        string candidate = Path.Combine(root, fileName);
        int suffix = 1;
        while (used.Contains(candidate) || (!string.IsNullOrEmpty(root) && File.Exists(candidate)))
        {
            candidate = Path.Combine(root, $"{baseName}_{suffix}{extension}");
            suffix++;
        }
        used.Add(candidate);
        return candidate;
    }

    private void OnRemovalChanged(object? sender, EventArgs e)
    {
        RecalculateRemovalCount();
        OnPropertyChanged(nameof(ExecuteHint));
        ExecuteCommand.RaiseCanExecuteChanged();
    }

    private void RecalculateRemovalCount()
    {
        SelectedRemovalCount = Groups.SelectMany(static g => g.Files)
            .Count(static f => f.IsIncludedForRemoval);
        OnPropertyChanged(nameof(CanExecute));
        OnPropertyChanged(nameof(ExecuteHint));
        ExecuteCommand.RaiseCanExecuteChanged();
    }

    private async Task ExecuteAsync()
    {
        if (!CanExecute) return;

        var files = Groups.SelectMany(static g => g.Files)
            .Where(static f => f.IsIncludedForRemoval)
            .ToList();
        if (files.Count == 0) return;

        string confirmation =
            $"将把 {files.Count} 个重复文件移入隔离文件夹：{Environment.NewLine}{QuarantineDirectory}{Environment.NewLine}{Environment.NewLine}" +
            "每个相似组会保留基准文件，不删除任何内容；移动记录会写入回退日志。";

        if (!_dialogService.Confirm("确认移出重复文件", confirmation)) return;

        try
        {
            Directory.CreateDirectory(QuarantineDirectory);
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("无法创建隔离文件夹", ex.Message);
            return;
        }

        HasResult = false;
        LastJournalId = null;
        IsBusy = true;
        IsProgressIndeterminate = false;
        ProgressValue = 0;
        BusyText = "正在移出重复文件…";

        var work = files.Select(file => new RenameWorkItem(
            file.FilePath, file.PotentialTargetPath, false)).ToList();

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
                displayName: "重复项隔离",
                createTargetDirectories: true));
        }
        catch (Exception ex)
        {
            result = new BatchExecutionResult(false, null, "移出重复项时发生异常：" + ex.Message);
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
            ResultTitle = "重复文件已移出";
            ResultMessage = $"已将 {files.Count} 个文件移动到隔离文件夹。回退日志已保存，可随时恢复。";
            HasResult = true;
            LastJournalId = result.Journal?.Id;

            var movedPaths = files.Select(static f => f.FilePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            _analyzed.RemoveAll(document => movedPaths.Contains(document.Path));
            _queuePaths.RemoveAll(path => movedPaths.Contains(path));
            Queue.Clear();
            foreach (string path in _queuePaths)
            {
                try { Queue.Add(new ConversionQueueItem(path)); } catch { }
            }
            OnPropertyChanged(nameof(QueueCountText));
            BuildGroups();
            NoticeText = "重复文件已移动到隔离文件夹，原始分组已刷新。";
        }
        else
        {
            ResultIsSuccess = false;
            ResultTitle = "移出重复文件未完成";
            ResultMessage = result.Message;
            HasResult = true;
            LastJournalId = result.Journal?.Id;
            _dialogService.ShowError("移出重复文件未完成", result.Message);
            BuildGroups();
        }

        RaiseCommandStates();
    }

    private void ResetAnalysis()
    {
        _analyzed.Clear();
        HasAnalyzed = false;
        AnalyzedCount = 0;
        SkippedCount = 0;
        foreach (var group in Groups)
        {
            foreach (var file in group.Files) file.RemovalChanged -= OnRemovalChanged;
        }
        Groups.Clear();
        DuplicateCount = 0;
        SelectedRemovalCount = 0;
        SelectedGroup = null;
        OnPropertyChanged(nameof(HasGroups));
        OnPropertyChanged(nameof(HasEmptyState));
        OnPropertyChanged(nameof(GroupsSummary));
        RaiseCommandStates();
    }

    private void Clear()
    {
        _queuePaths.Clear();
        Queue.Clear();
        ResetAnalysis();
        HasResult = false;
        LastJournalId = null;
        NoticeText = string.Empty;
        OnPropertyChanged(nameof(QueueCountText));
        RaiseCommandStates();
    }

    private void BrowseQuarantine()
    {
        string? folder = _dialogService.PickFolder(
            Directory.Exists(QuarantineDirectory)
                ? QuarantineDirectory
                : _queuePaths.Count > 0
                    ? Path.GetDirectoryName(_queuePaths[0])
                    : null);
        if (folder is not null) QuarantineDirectory = folder;
    }

    private void RaiseCommandStates()
    {
        AddFilesCommand.RaiseCanExecuteChanged();
        AddFolderCommand.RaiseCanExecuteChanged();
        AnalyzeCommand.RaiseCanExecuteChanged();
        ClearCommand.RaiseCanExecuteChanged();
        BrowseQuarantineCommand.RaiseCanExecuteChanged();
        ExecuteCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanExecute));
        OnPropertyChanged(nameof(ExecuteHint));
    }
}
