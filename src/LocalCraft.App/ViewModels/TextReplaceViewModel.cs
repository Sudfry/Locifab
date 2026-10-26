using System.Collections.ObjectModel;
using LocalCraft.App.Infrastructure;
using LocalCraft.App.Services;
using LocalCraft.Core.Journal;
using LocalCraft.Core.Settings;
using LocalCraft.Core.TextReplacement;

namespace LocalCraft.App.ViewModels;

public sealed class TextReplaceOccurrenceRow : ViewModelBase
{
    private readonly Action _changed;
    private bool _isSelected = true;

    public TextReplaceOccurrenceRow(Action changed)
    {
        _changed = changed;
    }

    public string FilePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public int LineNumber { get; init; }
    public string Before { get; init; } = string.Empty;
    public string After { get; init; } = string.Empty;
    public int Index { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!SetProperty(ref _isSelected, value)) return;
            _changed();
        }
    }
}

public sealed class TextReplaceViewModel : ViewModelBase
{
    private readonly JournalRepository _repository;
    private readonly IDialogService _dialogService;
    private readonly TextReplacePlanner _planner = new();
    private readonly TextReplaceExecutor _executor;

    private string _folder = string.Empty;
    private string _findText = string.Empty;
    private string _replaceText = string.Empty;
    private bool _useRegex;
    private bool _ignoreCase;
    private bool _includeSubfolders;
    private bool _anyExtension;
    private string _includeExtensions = string.Empty;
    private string _excludeExtensions = string.Empty;
    private double _maxFileSizeMb = 64;
    private bool _isBusy;
    private bool _isProgressIndeterminate;
    private double _progressValue;
    private string _busyText = string.Empty;
    private string _summaryText = string.Empty;
    private string _resultTitle = string.Empty;
    private string _resultMessage = string.Empty;
    private bool _hasPreview;
    private bool _hasResult;
    private bool _resultIsSuccess;

    public TextReplaceViewModel(
        JournalRepository repository,
        IDialogService dialogService,
        AppSettingsService? settings = null)
    {
        _repository = repository;
        _dialogService = dialogService;
        _executor = new TextReplaceExecutor(repository);
        _includeSubfolders = settings?.Current.DefaultIncludeSubfolders ?? false;

        BrowseCommand = new RelayCommand(Browse, () => !IsBusy);
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy && Directory.Exists(Folder));
        ExecuteCommand = new AsyncRelayCommand(ExecuteAsync, () => CanExecute);
        OpenUndoCenterCommand = new RelayCommand(() => UndoCenterRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? UndoCenterRequested;

    public ObservableCollection<TextReplaceFilePlan> Plans { get; } = new();
    public ObservableCollection<TextReplaceOccurrenceRow> Occurrences { get; } = new();

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
            OnPropertyChanged(nameof(CanScan));
            RaiseCommandStates();
        }
    }

    public string FindText
    {
        get => _findText;
        set
        {
            if (!SetProperty(ref _findText, value)) return;
            RaiseCommandStates();
        }
    }

    public string ReplaceText
    {
        get => _replaceText;
        set => SetProperty(ref _replaceText, value);
    }

    public bool UseRegex
    {
        get => _useRegex;
        set => SetProperty(ref _useRegex, value);
    }

    public bool IgnoreCase
    {
        get => _ignoreCase;
        set => SetProperty(ref _ignoreCase, value);
    }

    public bool IncludeSubfolders
    {
        get => _includeSubfolders;
        set => SetProperty(ref _includeSubfolders, value);
    }

    public bool AnyExtension
    {
        get => _anyExtension;
        set => SetProperty(ref _anyExtension, value);
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
        set => SetProperty(ref _maxFileSizeMb, Math.Clamp(value, 1, 2048));
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(IsIdle));
            OnPropertyChanged(nameof(CanScan));
            RaiseCommandStates();
        }
    }

    public bool IsIdle => !IsBusy;

    public bool CanScan => !IsBusy && Directory.Exists(Folder);

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
            RaiseCommandStates();
        }
    }

    public bool CanExecute => !IsBusy && Occurrences.Any(x => x.IsSelected);

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
        Plans.Clear();
        Occurrences.Clear();
        HasPreview = false;
        HasResult = false;
        SummaryText = "已选择文件夹。点击「只读扫描并预览」查看命中情况。";
    }

    private async Task ScanAsync()
    {
        if (IsBusy) return;
        if (string.IsNullOrEmpty(FindText))
        {
            _dialogService.ShowInfo("文本替换", "请先输入要查找的内容。");
            return;
        }

        IsBusy = true;
        IsProgressIndeterminate = true;
        BusyText = "正在只读扫描文本文件…";
        HasResult = false;
        Plans.Clear();
        Occurrences.Clear();
        HasPreview = false;

        try
        {
            var options = BuildOptions();
            var result = await Task.Run(() => _planner.Scan(options));
            if (!result.Success)
            {
                SummaryText = result.Error ?? "扫描失败。";
                return;
            }

            foreach (var plan in result.Files)
            {
                Plans.Add(plan);
                foreach (var match in plan.Matches)
                {
                    Occurrences.Add(new TextReplaceOccurrenceRow(OnOccurrenceChanged)
                    {
                        FilePath = plan.FilePath,
                        FileName = plan.FileName,
                        LineNumber = match.LineNumber,
                        Before = match.Before,
                        After = match.After,
                        Index = match.Index
                    });
                }
            }
            SummaryText = $"命中 {Plans.Count} 个文件、{result.TotalMatches} 处；已列出 {Occurrences.Count} 处，可勾选要替换的位置。";
            HasPreview = Occurrences.Count > 0;
            if (Plans.Count == 0)
            {
                ResultIsSuccess = false;
                ResultTitle = "没有找到可替换内容";
                ResultMessage = result.SkippedFiles > 0
                    ? $"已跳过 {result.SkippedFiles} 个二进制/超限/非文本文件。"
                    : "可以调整查找内容、扩展名范围或包含子文件夹后重试。";
                HasResult = true;
            }
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
        }
    }

    private async Task ExecuteAsync()
    {
        if (!CanExecute) return;
        var selectedRows = Occurrences.Where(x => x.IsSelected).ToList();
        if (selectedRows.Count == 0) return;
        var selectedByFile = selectedRows
            .GroupBy(x => x.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlySet<int>)g.Select(x => x.Index).ToHashSet(),
                StringComparer.OrdinalIgnoreCase);
        var selectedPlans = Plans.Where(x => selectedByFile.ContainsKey(x.FilePath)).ToList();
        if (!_dialogService.Confirm(
                "执行文本替换",
                $"将修改 {selectedPlans.Count} 个文本文件，共 {selectedRows.Count} 处（只替换勾选位置）。\n" +
                "原文件会先备份，执行完成后可在回退中心恢复。是否继续？"))
        {
            return;
        }

        IsBusy = true;
        IsProgressIndeterminate = false;
        ProgressValue = 0;
        BusyText = "正在备份并替换…";
        var options = BuildOptions();
        var plans = selectedPlans;

        try
        {
            var progress = new Progress<LocalCraft.Core.Operations.OperationProgress>(value =>
            {
                ProgressValue = value.Total == 0 ? 0 : value.Completed * 100.0 / value.Total;
                BusyText = $"{value.Stage} · {value.CurrentFile}";
            });

            var result = await Task.Run(() => _executor.Execute(plans, options, progress, default, selectedByFile));
            ResultIsSuccess = result.Success;
            ResultTitle = result.Success ? "文本替换完成" : "文本替换未完成";
            ResultMessage = result.Message;
            HasResult = true;
            if (result.Success)
            {
                HasPreview = false;
                Plans.Clear();
                Occurrences.Clear();
                SummaryText = "替换已完成，可在回退中心恢复原文。";
            }
        }
        catch (Exception ex)
        {
            ResultIsSuccess = false;
            ResultTitle = "文本替换失败";
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

    private TextReplaceOptions BuildOptions()
    {
        return new TextReplaceOptions
        {
            Folder = Folder,
            FindText = FindText,
            ReplaceText = ReplaceText,
            UseRegex = UseRegex,
            IgnoreCase = IgnoreCase,
            IncludeSubfolders = IncludeSubfolders,
            AnyExtension = AnyExtension,
            IncludeExtensions = IncludeExtensions,
            ExcludeExtensions = ExcludeExtensions,
            MaxFileSizeMb = MaxFileSizeMb
        };
    }

    private void OnOccurrenceChanged()
    {
        OnPropertyChanged(nameof(CanExecute));
        OnPropertyChanged(nameof(SelectedCount));
        ExecuteCommand.RaiseCanExecuteChanged();
    }

    public int SelectedCount => Occurrences.Count(x => x.IsSelected);

    private void RaiseCommandStates()
    {
        BrowseCommand.RaiseCanExecuteChanged();
        ScanCommand.RaiseCanExecuteChanged();
        ExecuteCommand.RaiseCanExecuteChanged();
    }
}
