using System.Collections.ObjectModel;
using LocalCraft.App.Infrastructure;
using LocalCraft.App.Services;
using LocalCraft.Conversion.Images;
using LocalCraft.Core.Journal;
using LocalCraft.Core.Operations;
using LocalCraft.Core.Rename;

namespace LocalCraft.App.ViewModels;

public sealed class PhotoRow : ViewModelBase
{
    private bool _isSelected;

    public PhotoHashEntry Entry { get; init; } = new();

    public string GroupId { get; init; } = string.Empty;

    public bool IsKeeper { get; init; }

    public int Distance { get; init; }

    public string FileName => Entry.FileName;

    public string Dimensions => Entry.Dimensions;

    public string SizeText => FormatSize(Entry.Size);

    public string RoleText => IsKeeper ? "保留" : "相似";

    public string SimilarityText => IsKeeper ? "基准" : $"{100 - (int)Math.Round(Distance * 100.0 / 64)}%";

    public bool CanSelect => !IsKeeper;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!CanSelect) return;
            SetProperty(ref _isSelected, value);
        }
    }

    private static string FormatSize(long bytes)
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
}

public sealed class PhotoDedupViewModel : ViewModelBase
{
    private readonly JournalRepository _repository;
    private readonly IDialogService _dialogService;
    private readonly BatchRenameExecutor _executor;

    private string _folder = string.Empty;
    private bool _includeSubfolders = true;
    private double _thresholdPercent = 92;
    private string _quarantineDirectory = string.Empty;
    private bool _isBusy;
    private bool _isProgressIndeterminate;
    private double _progressValue;
    private string _busyText = string.Empty;
    private string _summaryText = "扫描相似照片，保留每组分辨率最大的一张，其余可隔离。";
    private int _scannedCount;
    private int _groupCount;
    private int _selectedCount;
    private bool _hasPreview;
    private bool _hasResult;
    private bool _resultIsSuccess;
    private string _resultTitle = string.Empty;
    private string _resultMessage = string.Empty;
    private PhotoScanResult? _scanResult;

    public PhotoDedupViewModel(JournalRepository repository, IDialogService dialogService)
    {
        _repository = repository;
        _dialogService = dialogService;
        _executor = new BatchRenameExecutor(repository);

        BrowseCommand = new RelayCommand(Browse, () => !IsBusy);
        BrowseQuarantineCommand = new RelayCommand(BrowseQuarantine, () => !IsBusy);
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy && Directory.Exists(Folder));
        SelectAllCommand = new RelayCommand(SelectAll, () => !IsBusy && Rows.Any(x => x.CanSelect));
        ClearSelectionCommand = new RelayCommand(ClearSelection, () => !IsBusy && SelectedCount > 0);
        ExecuteCommand = new AsyncRelayCommand(ExecuteAsync, () => CanExecute);
        OpenUndoCenterCommand = new RelayCommand(() => UndoCenterRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? UndoCenterRequested;

    public ObservableCollection<PhotoRow> Rows { get; } = new();

    public RelayCommand BrowseCommand { get; }
    public RelayCommand BrowseQuarantineCommand { get; }
    public AsyncRelayCommand ScanCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }
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
            if (!SetProperty(ref _thresholdPercent, Math.Clamp(value, 70, 100))) return;
            OnPropertyChanged(nameof(ThresholdLabel));
        }
    }

    public string ThresholdLabel => $"{ThresholdPercent:0}%";

    public string QuarantineDirectory
    {
        get => _quarantineDirectory;
        set
        {
            if (!SetProperty(ref _quarantineDirectory, value)) return;
            OnPropertyChanged(nameof(CanExecute));
            ExecuteCommand.RaiseCanExecuteChanged();
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

    public int ScannedCount
    {
        get => _scannedCount;
        private set => SetProperty(ref _scannedCount, value);
    }

    public int GroupCount
    {
        get => _groupCount;
        private set => SetProperty(ref _groupCount, value);
    }

    public int SelectedCount
    {
        get => _selectedCount;
        private set
        {
            if (!SetProperty(ref _selectedCount, value)) return;
            OnPropertyChanged(nameof(SelectedCountText));
            OnPropertyChanged(nameof(CanExecute));
            ExecuteCommand.RaiseCanExecuteChanged();
            ClearSelectionCommand.RaiseCanExecuteChanged();
        }
    }

    public string SelectedCountText => $"已选 {SelectedCount} 张";

    public bool CanExecute => !IsBusy && HasPreview && SelectedCount > 0
                              && !string.IsNullOrWhiteSpace(QuarantineDirectory);

    public bool HasPreview
    {
        get => _hasPreview;
        private set => SetProperty(ref _hasPreview, value);
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

    public bool CanScan => !IsBusy && Directory.Exists(Folder);

    private void Browse()
    {
        string? picked = _dialogService.PickFolder(Directory.Exists(Folder) ? Folder : null);
        if (picked is null) return;
        Folder = picked;
        if (string.IsNullOrWhiteSpace(QuarantineDirectory))
        {
            QuarantineDirectory = Path.Combine(picked, "_相似照片隔离");
        }
        SummaryText = "文件夹已选择。点击「扫描相似照片」开始。";
    }

    private void BrowseQuarantine()
    {
        string? picked = _dialogService.PickFolder(Directory.Exists(QuarantineDirectory) ? QuarantineDirectory : null);
        if (picked is not null) QuarantineDirectory = picked;
    }

    private async Task ScanAsync()
    {
        IsBusy = true;
        IsProgressIndeterminate = true;
        BusyText = "正在计算图片感知哈希…";
        HasResult = false;
        ClearRows();
        try
        {
            var result = await Task.Run(() => PerceptualHash.Scan(Folder, IncludeSubfolders, ThresholdPercent));
            _scanResult = result;
            if (!result.Success)
            {
                SummaryText = result.Error ?? "扫描失败。";
                return;
            }

            ScannedCount = result.ScannedCount;
            GroupCount = result.Groups.Count;
            foreach (var group in result.Groups)
            {
                foreach (var member in group.Members)
                {
                    int distance = PerceptualHash.Hamming(group.Keeper.Hash, member.Hash);
                    var row = new PhotoRow
                    {
                        Entry = member,
                        GroupId = group.Id,
                        IsKeeper = ReferenceEquals(member, group.Keeper),
                        Distance = distance
                    };
                    row.PropertyChanged += (_, _) => UpdateSelection();
                    Rows.Add(row);
                }
            }

            HasPreview = Rows.Count > 0;
            SummaryText = $"扫描 {result.ScannedCount} 张，找到 {result.Groups.Count} 组相似照片；跳过 {result.SkippedCount} 张无法读取的图片。";
            UpdateSelection();
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

    private void SelectAll()
    {
        foreach (var row in Rows.Where(x => x.CanSelect)) row.IsSelected = true;
        UpdateSelection();
    }

    private void ClearSelection()
    {
        foreach (var row in Rows) row.IsSelected = false;
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        SelectedCount = Rows.Count(x => x.IsSelected);
    }

    private async Task ExecuteAsync()
    {
        if (!CanExecute) return;
        var selected = Rows.Where(x => x.IsSelected).ToList();
        if (!_dialogService.Confirm(
                "隔离相似照片",
                $"将把 {selected.Count} 张相似照片移动到：\n{QuarantineDirectory}\n" +
                "每组保留一张；移动写入日志，可在回退中心恢复。是否继续？"))
        {
            return;
        }

        IsBusy = true;
        IsProgressIndeterminate = false;
        ProgressValue = 0;
        BusyText = "正在隔离相似照片…";
        try
        {
            var work = new List<RenameWorkItem>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in selected)
            {
                string target = UniqueTarget(QuarantineDirectory, Path.GetFileName(row.Entry.Path), used);
                work.Add(new RenameWorkItem(row.Entry.Path, target, false));
            }

            var progress = new Progress<OperationProgress>(value =>
            {
                ProgressValue = value.Total == 0 ? 0 : value.Completed * 100.0 / value.Total;
                BusyText = $"{value.Stage} · {value.CurrentFile}";
            });
            var result = await Task.Run(() => _executor.Execute(
                work,
                progress,
                CancellationToken.None,
                "move",
                "相似照片隔离",
                true));

            ResultIsSuccess = result.Success;
            ResultTitle = result.Success ? "相似照片已隔离" : "隔离未完成";
            ResultMessage = result.Message;
            HasResult = true;
            if (result.Success)
            {
                foreach (var row in selected) Rows.Remove(row);
                HasPreview = Rows.Any();
                SummaryText = "已隔离选中照片；每组保留一张。可在回退中心恢复。";
                UpdateSelection();
            }
        }
        catch (Exception ex)
        {
            ResultIsSuccess = false;
            ResultTitle = "隔离失败";
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

    private static string UniqueTarget(string directory, string fileName, HashSet<string> used)
    {
        string candidate = Path.Combine(directory, fileName);
        string name = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);
        int index = 2;
        while (File.Exists(candidate) || Directory.Exists(candidate) || !used.Add(candidate))
        {
            candidate = Path.Combine(directory, $"{name} ({index++}){extension}");
        }

        return candidate;
    }

    private void ClearRows()
    {
        Rows.Clear();
        _scanResult = null;
        ScannedCount = 0;
        GroupCount = 0;
        SelectedCount = 0;
        HasPreview = false;
    }

    private void RaiseCommandStates()
    {
        BrowseCommand.RaiseCanExecuteChanged();
        BrowseQuarantineCommand.RaiseCanExecuteChanged();
        ScanCommand.RaiseCanExecuteChanged();
        SelectAllCommand.RaiseCanExecuteChanged();
        ExecuteCommand.RaiseCanExecuteChanged();
        ClearSelectionCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanExecute));
    }
}
