using System.Collections.ObjectModel;
using LocalCraft.App.Infrastructure;
using LocalCraft.App.Services;
using LocalCraft.Core.Checksums;
using LocalCraft.Core.Journal;

namespace LocalCraft.App.ViewModels;

public enum ChecksumMode
{
    Generate,
    Verify
}

public sealed record ChecksumModeOption(ChecksumMode Mode, string DisplayName, string Description);

public sealed class ChecksumRow
{
    public string RelativePath { get; init; } = string.Empty;

    public string Hash { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public bool IsOk { get; init; } = true;
}

public sealed class ChecksumViewModel : ViewModelBase
{
    private readonly JournalRepository _repository;
    private readonly IDialogService _dialogService;
    private readonly ChecksumService _service = new();

    private ChecksumModeOption _selectedMode;
    private string _folder = string.Empty;
    private string _manifestPath = string.Empty;
    private bool _includeSubfolders = true;
    private bool _isBusy;
    private bool _isProgressIndeterminate;
    private string _busyText = string.Empty;
    private string _summaryText = "生成 SHA-256 清单，或用清单校验整个文件夹。";
    private int _okCount;
    private int _missingCount;
    private int _changedCount;
    private bool _hasPreview;
    private bool _hasResult;
    private bool _resultIsSuccess;
    private string _resultTitle = string.Empty;
    private string _resultMessage = string.Empty;
    private IReadOnlyList<ChecksumEntry> _entries = Array.Empty<ChecksumEntry>();

    public ChecksumViewModel(JournalRepository repository, IDialogService dialogService)
    {
        _repository = repository;
        _dialogService = dialogService;
        Modes = new ObservableCollection<ChecksumModeOption>
        {
            new(ChecksumMode.Generate, "生成清单", "扫描文件夹并写出 SHA-256 清单"),
            new(ChecksumMode.Verify, "校验清单", "按清单逐文件比对当前内容")
        };
        _selectedMode = Modes[0];

        BrowseFolderCommand = new RelayCommand(BrowseFolder, () => !IsBusy);
        BrowseManifestCommand = new RelayCommand(BrowseManifest, () => !IsBusy);
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy && Directory.Exists(Folder));
        WriteCommand = new RelayCommand(WriteManifest, () => !IsBusy && IsGenerate && HasPreview);
        OpenUndoCenterCommand = new RelayCommand(() => UndoCenterRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? UndoCenterRequested;

    public ObservableCollection<ChecksumModeOption> Modes { get; }

    public ObservableCollection<ChecksumRow> Rows { get; } = new();

    public RelayCommand BrowseFolderCommand { get; }
    public RelayCommand BrowseManifestCommand { get; }
    public AsyncRelayCommand ScanCommand { get; }
    public RelayCommand WriteCommand { get; }
    public RelayCommand OpenUndoCenterCommand { get; }

    public ChecksumModeOption SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (value is null || !SetProperty(ref _selectedMode, value)) return;
            ClearPreview();
            OnPropertyChanged(nameof(IsGenerate));
            OnPropertyChanged(nameof(IsVerify));
            OnPropertyChanged(nameof(ScanLabel));
            SummaryText = value.Description;
        }
    }

    public bool IsGenerate => SelectedMode.Mode == ChecksumMode.Generate;

    public bool IsVerify => SelectedMode.Mode == ChecksumMode.Verify;

    public string ScanLabel => IsGenerate ? "只读扫描并计算" : "开始校验";

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

    public string ManifestPath
    {
        get => _manifestPath;
        set
        {
            if (!SetProperty(ref _manifestPath, value)) return;
            ClearPreview();
        }
    }

    public bool IncludeSubfolders
    {
        get => _includeSubfolders;
        set => SetProperty(ref _includeSubfolders, value);
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

    public int OkCount
    {
        get => _okCount;
        private set => SetProperty(ref _okCount, value);
    }

    public int MissingCount
    {
        get => _missingCount;
        private set => SetProperty(ref _missingCount, value);
    }

    public int ChangedCount
    {
        get => _changedCount;
        private set => SetProperty(ref _changedCount, value);
    }

    public bool HasPreview
    {
        get => _hasPreview;
        private set
        {
            if (!SetProperty(ref _hasPreview, value)) return;
            WriteCommand.RaiseCanExecuteChanged();
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

    private void BrowseFolder()
    {
        string? picked = _dialogService.PickFolder(Directory.Exists(Folder) ? Folder : null);
        if (picked is null) return;
        Folder = picked;
        if (IsGenerate && string.IsNullOrWhiteSpace(ManifestPath))
        {
            ManifestPath = Path.Combine(picked, "checksums.sha256");
        }
    }

    private void BrowseManifest()
    {
        if (IsGenerate)
        {
            string? file = _dialogService.PickSaveFile(
                "SHA-256 清单 (*.sha256)|*.sha256|文本文件 (*.txt)|*.txt",
                "checksums.sha256",
                Directory.Exists(Folder) ? Folder : null);
            if (file is not null) ManifestPath = file;
            return;
        }

        IReadOnlyList<string> files = _dialogService.PickFiles(
            "SHA-256 清单 (*.sha256;*.txt)|*.sha256;*.txt|所有文件 (*.*)|*.*",
            Directory.Exists(Folder) ? Folder : null);
        if (files.Count > 0) ManifestPath = files[0];
    }

    private async Task ScanAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        IsProgressIndeterminate = true;
        BusyText = IsGenerate ? "正在计算 SHA-256…" : "正在按清单校验…";
        HasResult = false;
        ClearPreview();

        try
        {
            if (IsGenerate)
            {
                var result = await Task.Run(() => _service.BuildManifest(Folder, IncludeSubfolders));
                if (!result.Success)
                {
                    SummaryText = result.Error ?? "扫描失败。";
                    return;
                }

                _entries = result.Entries;
                foreach (var entry in result.Entries)
                {
                    Rows.Add(new ChecksumRow
                    {
                        RelativePath = entry.RelativePath,
                        Hash = entry.Hash[..16] + "…",
                        Status = "待写入"
                    });
                }

                HasPreview = Rows.Count > 0;
                SummaryText = $"计算出 {Rows.Count} 个文件的 SHA-256；跳过 {result.SkippedCount} 个文件。";
            }
            else
            {
                if (string.IsNullOrWhiteSpace(ManifestPath) || !File.Exists(ManifestPath))
                {
                    SummaryText = "请先选择有效的 SHA-256 清单。";
                    return;
                }

                var result = await Task.Run(() => _service.Verify(Folder, ManifestPath));
                if (!result.Success)
                {
                    SummaryText = result.Error ?? "校验失败。";
                    return;
                }

                OkCount = result.OkCount;
                MissingCount = result.MissingCount;
                ChangedCount = result.ChangedCount;
                foreach (var item in result.Items)
                {
                    Rows.Add(new ChecksumRow
                    {
                        RelativePath = item.RelativePath,
                        Hash = item.ActualHash.Length > 0 ? item.ActualHash[..16] + "…" : "—",
                        Status = item.StateText,
                        IsOk = item.State == ChecksumVerifyState.Ok
                    });
                }

                HasPreview = Rows.Count > 0;
                SummaryText = $"校验完成：一致 {OkCount}、缺失 {MissingCount}、已变化 {ChangedCount}。";
                ResultIsSuccess = MissingCount == 0 && ChangedCount == 0;
                ResultTitle = ResultIsSuccess ? "校验全部通过" : "校验发现问题";
                ResultMessage = ResultIsSuccess
                    ? "清单中的文件全部与当前内容一致。"
                    : "存在缺失或内容变化的文件，请查看列表。";
                HasResult = true;
            }
        }
        catch (Exception ex)
        {
            SummaryText = "操作失败：" + ex.Message;
            _dialogService.ShowError("校验失败", ex.Message);
        }
        finally
        {
            IsBusy = false;
            IsProgressIndeterminate = false;
            RaiseCommandStates();
        }
    }

    private void WriteManifest()
    {
        if (!IsGenerate || !HasPreview || _entries.Count == 0) return;
        if (string.IsNullOrWhiteSpace(ManifestPath))
        {
            _dialogService.ShowInfo("写出清单", "请选择清单输出文件。");
            return;
        }

        try
        {
            if (File.Exists(ManifestPath))
            {
                _dialogService.ShowInfo("请换一个文件名", "清单文件已存在，为避免覆盖，请使用新文件名。");
                return;
            }

            string fullPath = Path.GetFullPath(ManifestPath);
            var journal = new OperationJournal
            {
                Id = Guid.NewGuid().ToString("N"),
                Kind = "checksum",
                DisplayName = "生成 SHA-256 清单",
                SourceDirectory = Folder,
                CreatedAt = DateTimeOffset.Now,
                Status = JournalStatus.Completed,
                CompletedAt = DateTimeOffset.Now,
                Operations =
                {
                    new JournalOperation
                    {
                        Sequence = 1,
                        SourcePath = Folder,
                        TargetPath = fullPath,
                        State = JournalOperationState.Completed
                    }
                }
            };
            _repository.Save(journal);
            _service.WriteManifest(fullPath, _entries);
            ManifestPath = fullPath;
            ResultIsSuccess = true;
            ResultTitle = "清单已写出";
            ResultMessage = $"已写入 {_entries.Count} 条 SHA-256 记录：{fullPath}";
            HasResult = true;
            SummaryText = "清单已写入。可在回退中心删除本次输出。";
        }
        catch (Exception ex)
        {
            ResultIsSuccess = false;
            ResultTitle = "写出清单失败";
            ResultMessage = ex.Message;
            HasResult = true;
        }
    }

    private void ClearPreview()
    {
        Rows.Clear();
        _entries = Array.Empty<ChecksumEntry>();
        HasPreview = false;
        OkCount = 0;
        MissingCount = 0;
        ChangedCount = 0;
    }

    private void RaiseCommandStates()
    {
        BrowseFolderCommand.RaiseCanExecuteChanged();
        BrowseManifestCommand.RaiseCanExecuteChanged();
        ScanCommand.RaiseCanExecuteChanged();
        WriteCommand.RaiseCanExecuteChanged();
    }
}
