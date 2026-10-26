using System.Collections.ObjectModel;
using LocalCraft.App.Infrastructure;
using LocalCraft.App.Services;
using LocalCraft.Core.Backup;
using LocalCraft.Core.Checksums;
using LocalCraft.Core.Journal;
using LocalCraft.Core.Settings;

namespace LocalCraft.App.ViewModels;

public sealed class BackupViewModel : ViewModelBase
{
    private readonly IDialogService _dialogService;
    private readonly BackupPlanner _planner = new();
    private readonly BackupExecutor _executor;

    private string _sourceFolder = string.Empty;
    private string _targetFolder = string.Empty;
    private bool _includeSubfolders = true;
    private bool _skipHidden = true;
    private bool _strictCompare;
    private bool _verifyAfterCopy = true;
    private string _includeExtensions = string.Empty;
    private string _excludeExtensions = string.Empty;
    private double _maxFileSizeMb = 8192;
    private bool _isBusy;
    private bool _isProgressIndeterminate;
    private double _progressValue;
    private string _busyText = string.Empty;
    private string _summaryText = "选择源文件夹和目标文件夹，先只读分析差异。";
    private int _newCount;
    private int _updateCount;
    private int _sameCount;
    private bool _hasPreview;
    private bool _hasResult;
    private bool _resultIsSuccess;
    private string _resultTitle = string.Empty;
    private string _resultMessage = string.Empty;

    public BackupViewModel(JournalRepository repository, IDialogService dialogService)
    {
        _dialogService = dialogService;
        _executor = new BackupExecutor(repository);

        BrowseSourceCommand = new RelayCommand(() => Browse(true), () => !IsBusy);
        BrowseTargetCommand = new RelayCommand(() => Browse(false), () => !IsBusy);
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy && Directory.Exists(SourceFolder));
        ExecuteCommand = new AsyncRelayCommand(ExecuteAsync, () => CanExecute);
        OpenUndoCenterCommand = new RelayCommand(() => UndoCenterRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? UndoCenterRequested;

    public ObservableCollection<BackupPlanItem> PlanItems { get; } = new();

    public RelayCommand BrowseSourceCommand { get; }
    public RelayCommand BrowseTargetCommand { get; }
    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand ExecuteCommand { get; }
    public RelayCommand OpenUndoCenterCommand { get; }

    public string SourceFolder
    {
        get => _sourceFolder;
        set
        {
            if (!SetProperty(ref _sourceFolder, value)) return;
            RaiseCommandStates();
        }
    }

    public string TargetFolder
    {
        get => _targetFolder;
        set => SetProperty(ref _targetFolder, value);
    }

    public bool IncludeSubfolders
    {
        get => _includeSubfolders;
        set => SetProperty(ref _includeSubfolders, value);
    }

    public bool SkipHidden
    {
        get => _skipHidden;
        set => SetProperty(ref _skipHidden, value);
    }

    public bool StrictCompare
    {
        get => _strictCompare;
        set => SetProperty(ref _strictCompare, value);
    }

    public bool VerifyAfterCopy
    {
        get => _verifyAfterCopy;
        set => SetProperty(ref _verifyAfterCopy, value);
    }

    public string IncludeExtensions
    {
        get => _includeExtensions;
        set => SetProperty(ref _includeExtensions, value);
    }

    public string ExcludeExtensions
    {
        get => _excludeExtensions;
        set => SetProperty(ref _excludeExtensions, value);
    }

    public double MaxFileSizeMb
    {
        get => _maxFileSizeMb;
        set => SetProperty(ref _maxFileSizeMb, Math.Clamp(value, 1, 32768));
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

    public int NewCount
    {
        get => _newCount;
        private set => SetProperty(ref _newCount, value);
    }

    public int UpdateCount
    {
        get => _updateCount;
        private set => SetProperty(ref _updateCount, value);
    }

    public int SameCount
    {
        get => _sameCount;
        private set => SetProperty(ref _sameCount, value);
    }

    public bool HasPreview
    {
        get => _hasPreview;
        private set
        {
            if (!SetProperty(ref _hasPreview, value)) return;
            OnPropertyChanged(nameof(CanExecute));
            RaiseCommandStates();
        }
    }

    public bool CanExecute => !IsBusy && HasPreview && (NewCount + UpdateCount) > 0;

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

    private void Browse(bool source)
    {
        string current = source ? SourceFolder : TargetFolder;
        string? picked = _dialogService.PickFolder(Directory.Exists(current) ? current : null);
        if (picked is null) return;
        if (source) SourceFolder = picked;
        else TargetFolder = picked;
        HasPreview = false;
        HasResult = false;
        PlanItems.Clear();
        SummaryText = "路径已更新。点击「只读分析」查看差异。";
    }

    private async Task ScanAsync()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(TargetFolder))
        {
            _dialogService.ShowInfo("备份同步", "请先选择目标文件夹。");
            return;
        }

        IsBusy = true;
        IsProgressIndeterminate = true;
        BusyText = "正在只读分析源和目标…";
        HasResult = false;
        HasPreview = false;
        PlanItems.Clear();

        try
        {
            var result = await Task.Run(() => _planner.Scan(BuildOptions()));
            if (!result.Success)
            {
                SummaryText = result.Error ?? "分析失败。";
                return;
            }

            foreach (var item in result.Items) PlanItems.Add(item);
            NewCount = result.NewCount;
            UpdateCount = result.UpdateCount;
            SameCount = result.SameCount;
            SummaryText = $"新增 {NewCount}、更新 {UpdateCount}、相同 {SameCount}；跳过 {result.SkippedCount} 个文件。只增不删。";
            HasPreview = true;
            if (CanExecute == false && PlanItems.Count > 0)
            {
                ResultIsSuccess = true;
                ResultTitle = "目标已经是最新";
                ResultMessage = "没有需要新增或更新的文件。";
                HasResult = true;
            }
        }
        catch (Exception ex)
        {
            SummaryText = "分析失败：" + ex.Message;
            _dialogService.ShowError("分析失败", ex.Message);
        }
        finally
        {
            IsBusy = false;
            IsProgressIndeterminate = false;
        }
    }

    private async Task ExecuteAsync()
    {
        if (!CanExecute) return;
        if (!_dialogService.Confirm(
                "执行备份同步",
                $"将新增 {NewCount} 个、更新 {UpdateCount} 个文件到目标文件夹。\n" +
                "不会删除目标里的任何文件；被更新的目标版本会先备份，可在回退中心恢复。是否继续？"))
        {
            return;
        }

        IsBusy = true;
        IsProgressIndeterminate = false;
        ProgressValue = 0;
        BusyText = "正在备份…";
        try
        {
            var plan = new BackupPlan { Items = PlanItems.ToList() };
            var progress = new Progress<LocalCraft.Core.Operations.OperationProgress>(value =>
            {
                ProgressValue = value.Total == 0 ? 0 : value.Completed * 100.0 / value.Total;
                BusyText = $"{value.Stage} · {value.CurrentFile}";
            });
            var result = await Task.Run(() => _executor.Execute(plan, progress));
            bool verified = true;
            string verifyMessage = string.Empty;
            if (result.Success && VerifyAfterCopy)
            {
                BusyText = "正在校验复制结果…";
                var pending = plan.PendingItems;
                var mismatches = await Task.Run(() => VerifyCopiedFiles(pending));
                verified = mismatches.Count == 0;
                verifyMessage = verified
                    ? $" SHA-256 校验通过 {pending.Count} 个文件。"
                    : $" SHA-256 校验失败：{mismatches.Count} 个文件不一致（{string.Join("、", mismatches.Take(3))}）。";
            }

            ResultIsSuccess = result.Success && verified;
            ResultTitle = result.Success
                ? (verified ? "备份同步完成" : "复制完成但校验失败")
                : "备份同步未完成";
            ResultMessage = result.Message + verifyMessage;
            HasResult = true;
            if (result.Success && verified)
            {
                HasPreview = false;
                PlanItems.Clear();
                SummaryText = "备份已完成并通过 SHA-256 校验。可在回退中心恢复目标文件。";
            }
            else if (result.Success)
            {
                SummaryText = "复制已写入，但校验发现不一致；建议回退并检查磁盘。";
            }
        }
        catch (Exception ex)
        {
            ResultIsSuccess = false;
            ResultTitle = "备份同步失败";
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

    private static List<string> VerifyCopiedFiles(IReadOnlyList<BackupPlanItem> items)
    {
        var mismatches = new List<string>();
        foreach (var item in items)
        {
            try
            {
                if (!File.Exists(item.TargetPath))
                {
                    mismatches.Add(item.RelativePath + "（缺失）");
                    continue;
                }

                string sourceHash = ChecksumService.ComputeFileSha256(item.SourcePath);
                string targetHash = ChecksumService.ComputeFileSha256(item.TargetPath);
                if (!string.Equals(sourceHash, targetHash, StringComparison.OrdinalIgnoreCase))
                {
                    mismatches.Add(item.RelativePath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                mismatches.Add(item.RelativePath + "（无法读取）");
            }
        }

        return mismatches;
    }

    private BackupOptions BuildOptions()
    {
        return new BackupOptions
        {
            SourceFolder = SourceFolder,
            TargetFolder = TargetFolder,
            IncludeSubfolders = IncludeSubfolders,
            SkipHidden = SkipHidden,
            StrictCompare = StrictCompare,
            IncludeExtensions = IncludeExtensions,
            ExcludeExtensions = ExcludeExtensions,
            MaxFileSizeMb = MaxFileSizeMb
        };
    }

    private void RaiseCommandStates()
    {
        BrowseSourceCommand.RaiseCanExecuteChanged();
        BrowseTargetCommand.RaiseCanExecuteChanged();
        ScanCommand.RaiseCanExecuteChanged();
        ExecuteCommand.RaiseCanExecuteChanged();
    }
}
