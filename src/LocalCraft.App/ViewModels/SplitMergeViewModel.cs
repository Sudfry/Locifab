using System.Collections.ObjectModel;
using LocalCraft.App.Infrastructure;
using LocalCraft.App.Services;
using LocalCraft.Core.Files;
using LocalCraft.Core.Journal;
using LocalCraft.Core.Operations;

namespace LocalCraft.App.ViewModels;

public sealed class SplitMergeRow
{
    public string FileName { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string SizeText { get; init; } = string.Empty;
}

public sealed class SplitMergeViewModel : ViewModelBase
{
    private readonly IDialogService _dialogService;
    private readonly SplitMergeService _service;
    private bool _isSplit = true;
    private string _sourceFile = string.Empty;
    private string _outputFolder = string.Empty;
    private double _sizeMb = 10;
    private int _parts = 2;
    private bool _splitBySize = true;
    private string _mergeFolder = string.Empty;
    private string _mergePattern = "*.part*";
    private string _mergeOutput = string.Empty;
    private bool _isBusy;
    private bool _hasPreview;
    private string _summary = "选择拆分或合并，先预览再执行。";
    private bool _hasResult;
    private bool _resultSuccess;
    private string _resultTitle = string.Empty;
    private string _resultMessage = string.Empty;
    private SplitPlan? _splitPlan;
    private MergePlan? _mergePlan;

    public SplitMergeViewModel(JournalRepository repository, IDialogService dialogService)
    {
        _dialogService = dialogService;
        _service = new SplitMergeService(repository);
        BrowseSourceCommand = new RelayCommand(BrowseSource, () => !IsBusy);
        BrowseOutputCommand = new RelayCommand(BrowseOutput, () => !IsBusy);
        BrowseMergeFolderCommand = new RelayCommand(BrowseMergeFolder, () => !IsBusy);
        BrowseMergeOutputCommand = new RelayCommand(BrowseMergeOutput, () => !IsBusy);
        PreviewCommand = new RelayCommand(BuildPreview, () => !IsBusy);
        ExecuteCommand = new RelayCommand(Execute, () => !IsBusy && HasPreview);
        OpenUndoCenterCommand = new RelayCommand(() => UndoCenterRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? UndoCenterRequested;
    public ObservableCollection<SplitMergeRow> Rows { get; } = new();
    public RelayCommand BrowseSourceCommand { get; }
    public RelayCommand BrowseOutputCommand { get; }
    public RelayCommand BrowseMergeFolderCommand { get; }
    public RelayCommand BrowseMergeOutputCommand { get; }
    public RelayCommand PreviewCommand { get; }
    public RelayCommand ExecuteCommand { get; }
    public RelayCommand OpenUndoCenterCommand { get; }

    public bool IsSplit
    {
        get => _isSplit;
        set
        {
            if (!SetProperty(ref _isSplit, value)) return;
            OnPropertyChanged(nameof(IsMerge));
            ClearPreview();
        }
    }

    public bool IsMerge
    {
        get => !_isSplit;
        set { if (value) IsSplit = false; }
    }

    public string SourceFile { get => _sourceFile; set { if (SetProperty(ref _sourceFile, value)) ClearPreview(); } }
    public string OutputFolder { get => _outputFolder; set { if (SetProperty(ref _outputFolder, value)) ClearPreview(); } }
    public double SizeMb { get => _sizeMb; set { if (SetProperty(ref _sizeMb, Math.Max(0.1, value))) ClearPreview(); } }
    public int Parts { get => _parts; set { if (SetProperty(ref _parts, Math.Clamp(value, 2, 9999))) ClearPreview(); } }
    public bool SplitBySize
    {
        get => _splitBySize;
        set { if (SetProperty(ref _splitBySize, value)) { OnPropertyChanged(nameof(SplitByParts)); ClearPreview(); } }
    }
    public bool SplitByParts
    {
        get => !_splitBySize;
        set { if (value) SplitBySize = false; }
    }
    public string MergeFolder { get => _mergeFolder; set { if (SetProperty(ref _mergeFolder, value)) ClearPreview(); } }
    public string MergePattern { get => _mergePattern; set { if (SetProperty(ref _mergePattern, value)) ClearPreview(); } }
    public string MergeOutput { get => _mergeOutput; set { if (SetProperty(ref _mergeOutput, value)) ClearPreview(); } }

    public bool IsBusy
    {
        get => _isBusy;
        private set { if (!SetProperty(ref _isBusy, value)) return; RaiseCommands(); }
    }

    public bool HasPreview
    {
        get => _hasPreview;
        private set { if (!SetProperty(ref _hasPreview, value)) return; OnPropertyChanged(nameof(CanExecute)); ExecuteCommand.RaiseCanExecuteChanged(); }
    }

    public bool CanExecute => !IsBusy && HasPreview;
    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
    public bool HasResult { get => _hasResult; private set => SetProperty(ref _hasResult, value); }
    public bool ResultSuccess { get => _resultSuccess; private set => SetProperty(ref _resultSuccess, value); }
    public string ResultTitle { get => _resultTitle; private set => SetProperty(ref _resultTitle, value); }
    public string ResultMessage { get => _resultMessage; private set => SetProperty(ref _resultMessage, value); }

    private void BrowseSource()
    {
        var files = _dialogService.PickFiles("所有文件|*.*", null);
        if (files.Count > 0) SourceFile = files[0];
    }

    private void BrowseOutput()
    {
        string? folder = _dialogService.PickFolder(Directory.Exists(OutputFolder) ? OutputFolder : null);
        if (folder is not null) OutputFolder = folder;
    }

    private void BrowseMergeFolder()
    {
        string? folder = _dialogService.PickFolder(Directory.Exists(MergeFolder) ? MergeFolder : null);
        if (folder is null) return;
        MergeFolder = folder;
        // 留空时由 PlanMerge 按第一个分片的原始扩展名生成输出名，
        // 避免所有合并结果都被命名成 merged.bin。
        MergeOutput = string.Empty;
    }

    private void BrowseMergeOutput()
    {
        string? file = _dialogService.PickSaveFile("所有文件|*.*", "merged.bin",
            Directory.Exists(MergeFolder) ? MergeFolder : null);
        if (file is not null) MergeOutput = file;
    }

    private void BuildPreview()
    {
        ClearPreview();
        if (IsSplit)
        {
            var plan = _service.PlanSplit(new SplitOptions
            {
                SourceFile = SourceFile,
                OutputFolder = OutputFolder,
                Mode = SplitBySize ? SplitMode.BySize : SplitMode.ByParts,
                SizeMb = SizeMb,
                Parts = Parts
            });
            if (!plan.Success)
            {
                Summary = plan.Error ?? "无法生成拆分预览。";
                return;
            }
            _splitPlan = plan;
            foreach (var part in plan.Parts)
                Rows.Add(new SplitMergeRow { FileName = part.FileName, Target = part.TargetPath, SizeText = part.SizeText });
            Summary = $"将拆分为 {plan.Parts.Count} 个分片；原文件不修改。";
        }
        else
        {
            var plan = _service.PlanMerge(MergeFolder, MergePattern, MergeOutput);
            if (!plan.Success)
            {
                Summary = plan.Error ?? "无法生成合并预览。";
                return;
            }
            _mergePlan = plan;
            foreach (string source in plan.Sources)
                Rows.Add(new SplitMergeRow
                {
                    FileName = Path.GetFileName(source),
                    Target = plan.OutputFile,
                    SizeText = new FileInfo(source).Length.ToString("N0") + " B"
                });
            Summary = $"将合并 {plan.Sources.Count} 个分片 → {Path.GetFileName(plan.OutputFile)}；分片不删除。";
        }
        HasPreview = Rows.Count > 0;
    }

    private void Execute()
    {
        if (!HasPreview) return;
        var result = IsSplit && _splitPlan is not null
            ? _service.ExecuteSplit(_splitPlan)
            : _mergePlan is not null ? _service.ExecuteMerge(_mergePlan) : null;
        if (result is null) return;
        ResultSuccess = result.Success;
        ResultTitle = result.Success ? "操作完成" : "操作未完成";
        ResultMessage = result.Message;
        HasResult = true;
        if (result.Success)
        {
            Summary = result.Message + " 可在回退中心删除本次输出。";
            ClearPreview();
        }
    }

    private void ClearPreview()
    {
        Rows.Clear();
        _splitPlan = null;
        _mergePlan = null;
        HasPreview = false;
    }

    private void RaiseCommands()
    {
        BrowseSourceCommand.RaiseCanExecuteChanged();
        BrowseOutputCommand.RaiseCanExecuteChanged();
        BrowseMergeFolderCommand.RaiseCanExecuteChanged();
        BrowseMergeOutputCommand.RaiseCanExecuteChanged();
        PreviewCommand.RaiseCanExecuteChanged();
        ExecuteCommand.RaiseCanExecuteChanged();
    }
}
