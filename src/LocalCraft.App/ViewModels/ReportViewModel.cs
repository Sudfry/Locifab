using System.Collections.ObjectModel;
using LocalCraft.App.Infrastructure;
using LocalCraft.App.Services;
using LocalCraft.Core.Journal;
using LocalCraft.Core.Reports;

namespace LocalCraft.App.ViewModels;

public enum ReportFormat { Csv, Html }

public sealed record ReportFormatOption(ReportFormat Format, string DisplayName, string Extension);

public sealed class ReportNode
{
    public string Name { get; set; } = string.Empty;
    public string SizeText { get; set; } = string.Empty;
    public string KindText { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public bool IsExpanded { get; set; } = true;
    public ObservableCollection<ReportNode> Children { get; set; } = new();
}

public sealed class ReportViewModel : ViewModelBase
{
    private readonly JournalRepository _repository;
    private readonly IDialogService _dialogService;
    private string _folder = string.Empty;
    private bool _includeSubfolders = true;
    private bool _includeHidden;
    private bool _includeHash;
    private ReportFormatOption _selectedFormat;
    private string _outputPath = string.Empty;
    private bool _isBusy;
    private bool _isProgressIndeterminate;
    private string _busyText = string.Empty;
    private string _summaryText = "扫描文件夹并生成 CSV 或 HTML 清单。";
    private bool _hasPreview;
    private bool _hasResult;
    private bool _resultIsSuccess;
    private string _resultTitle = string.Empty;
    private string _resultMessage = string.Empty;
    private IReadOnlyList<FileReportRow> _rows = Array.Empty<FileReportRow>();
    private long _totalBytes;

    public ReportViewModel(JournalRepository repository, IDialogService dialogService)
    {
        _repository = repository;
        _dialogService = dialogService;
        Formats = new ObservableCollection<ReportFormatOption>
        {
            new(ReportFormat.Csv, "CSV 清单", ".csv"),
            new(ReportFormat.Html, "HTML 报告", ".html")
        };
        _selectedFormat = Formats[0];
        BrowseCommand = new RelayCommand(Browse, () => !IsBusy);
        BrowseOutputCommand = new RelayCommand(BrowseOutput, () => !IsBusy);
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy && Directory.Exists(Folder));
        ExportCommand = new RelayCommand(Export, () => !IsBusy && HasPreview && !string.IsNullOrWhiteSpace(OutputPath));
        OpenUndoCenterCommand = new RelayCommand(() => UndoCenterRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? UndoCenterRequested;

    public ObservableCollection<FileReportRow> Rows { get; } = new();
    public ObservableCollection<ReportNode> TreeNodes { get; } = new();
    public ObservableCollection<ReportFormatOption> Formats { get; }
    public RelayCommand BrowseCommand { get; }
    public RelayCommand BrowseOutputCommand { get; }
    public AsyncRelayCommand ScanCommand { get; }
    public RelayCommand ExportCommand { get; }
    public RelayCommand OpenUndoCenterCommand { get; }

    public string Folder
    {
        get => _folder;
        set
        {
            if (!SetProperty(ref _folder, value)) return;
            ClearPreview();
            ScanCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IncludeSubfolders { get => _includeSubfolders; set => SetProperty(ref _includeSubfolders, value); }
    public bool IncludeHidden { get => _includeHidden; set => SetProperty(ref _includeHidden, value); }
    public bool IncludeHash { get => _includeHash; set => SetProperty(ref _includeHash, value); }

    public ReportFormatOption SelectedFormat
    {
        get => _selectedFormat;
        set
        {
            if (value is null || !SetProperty(ref _selectedFormat, value)) return;
            if (!string.IsNullOrWhiteSpace(OutputPath))
            {
                OutputPath = Path.ChangeExtension(OutputPath, value.Extension);
            }
        }
    }

    public string OutputPath
    {
        get => _outputPath;
        set
        {
            if (!SetProperty(ref _outputPath, value)) return;
            ExportCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(IsIdle));
            RaiseCommands();
        }
    }

    public bool IsIdle => !IsBusy;
    public bool IsProgressIndeterminate { get => _isProgressIndeterminate; private set => SetProperty(ref _isProgressIndeterminate, value); }
    public string BusyText { get => _busyText; private set => SetProperty(ref _busyText, value); }
    public string SummaryText { get => _summaryText; private set => SetProperty(ref _summaryText, value); }
    public bool HasPreview { get => _hasPreview; private set { if (!SetProperty(ref _hasPreview, value)) return; ExportCommand.RaiseCanExecuteChanged(); } }
    public bool HasResult { get => _hasResult; private set => SetProperty(ref _hasResult, value); }
    public bool ResultIsSuccess { get => _resultIsSuccess; private set => SetProperty(ref _resultIsSuccess, value); }
    public string ResultTitle { get => _resultTitle; private set => SetProperty(ref _resultTitle, value); }
    public string ResultMessage { get => _resultMessage; private set => SetProperty(ref _resultMessage, value); }
    public string TotalText => $"共 {Rows.Count} 个文件 · {FormatBytes(_totalBytes)}";

    private void Browse()
    {
        string? picked = _dialogService.PickFolder(Directory.Exists(Folder) ? Folder : null);
        if (picked is null) return;
        Folder = picked;
        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            OutputPath = Path.Combine(picked, "Locifab-清单" + SelectedFormat.Extension);
        }
    }

    private void BrowseOutput()
    {
        string? file = _dialogService.PickSaveFile(
            SelectedFormat.Format == ReportFormat.Csv
                ? "CSV 清单 (*.csv)|*.csv"
                : "HTML 报告 (*.html)|*.html",
            "Locifab-清单" + SelectedFormat.Extension,
            Directory.Exists(Folder) ? Folder : null);
        if (file is not null) OutputPath = file;
    }

    private async Task ScanAsync()
    {
        IsBusy = true;
        IsProgressIndeterminate = true;
        BusyText = IncludeHash ? "正在读取文件并计算 SHA-256…" : "正在读取文件信息…";
        HasResult = false;
        ClearPreview();
        try
        {
            var result = await Task.Run(() => FileReportService.Scan(new FileReportOptions
            {
                Folder = Folder,
                IncludeSubfolders = IncludeSubfolders,
                IncludeHidden = IncludeHidden,
                IncludeHash = IncludeHash
            }));
            if (!result.Success)
            {
                SummaryText = result.Error ?? "扫描失败。";
                return;
            }

            _rows = result.Rows;
            _totalBytes = result.TotalBytes;
            foreach (var row in result.Rows) Rows.Add(row);
            BuildTree(result.Rows);
            HasPreview = Rows.Count > 0;
            SummaryText = $"扫描 {result.Rows.Count} 个文件，跳过 {result.SkippedCount} 个；" +
                          (IncludeHash ? "已计算 SHA-256。" : "未计算哈希。");
            OnPropertyChanged(nameof(TotalText));
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
            RaiseCommands();
        }
    }

    private void BuildTree(IReadOnlyList<FileReportRow> rows)
    {
        TreeNodes.Clear();
        var folders = new Dictionary<string, ReportNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            string relative = row.RelativePath.Replace('/', '\\');
            string[] parts = relative.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            ObservableCollection<ReportNode> parent = TreeNodes;
            string path = string.Empty;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                path = path.Length == 0 ? parts[i] : path + "\\" + parts[i];
                if (!folders.TryGetValue(path, out var folder))
                {
                    folder = new ReportNode { Name = parts[i], KindText = "文件夹", IsExpanded = i == 0 };
                    folders[path] = folder;
                    parent.Add(folder);
                }
                parent = folder.Children;
            }
            parent.Add(new ReportNode
            {
                Name = parts.Length > 0 ? parts[^1] : relative,
                SizeText = row.SizeText,
                KindText = row.Extension,
                Hash = row.Hash,
                IsExpanded = false
            });
        }
        SortTree(TreeNodes);
    }

    private static void SortTree(ObservableCollection<ReportNode> nodes)
    {
        var ordered = nodes.OrderByDescending(x => x.KindText == "文件夹").ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        nodes.Clear();
        foreach (var node in ordered) { SortTree(node.Children); nodes.Add(node); }
    }

    private void Export()
    {
        if (!HasPreview || _rows.Count == 0) return;
        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            _dialogService.ShowInfo("导出清单", "请选择输出文件。");
            return;
        }

        if (File.Exists(OutputPath))
        {
            _dialogService.ShowInfo("请换一个文件名", "输出文件已存在，为避免覆盖，请使用新文件名。");
            return;
        }

        try
        {
            string full = Path.GetFullPath(OutputPath);
            var journal = new OperationJournal
            {
                Id = Guid.NewGuid().ToString("N"),
                Kind = "report",
                DisplayName = "文件夹清单",
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
                        TargetPath = full,
                        State = JournalOperationState.Completed
                    }
                }
            };
            _repository.Save(journal);
            if (SelectedFormat.Format == ReportFormat.Csv) FileReportService.WriteCsv(full, _rows);
            else FileReportService.WriteHtml(full, Folder, _rows);
            ResultIsSuccess = true;
            ResultTitle = "清单已生成";
            ResultMessage = $"已写入 {_rows.Count} 条记录：{full}";
            HasResult = true;
            SummaryText = "清单已输出，可在回退中心删除本次文件。";
        }
        catch (Exception ex)
        {
            ResultIsSuccess = false;
            ResultTitle = "生成清单失败";
            ResultMessage = ex.Message;
            HasResult = true;
        }
    }

    private void ClearPreview()
    {
        Rows.Clear();
        TreeNodes.Clear();
        _rows = Array.Empty<FileReportRow>();
        _totalBytes = 0;
        HasPreview = false;
        OnPropertyChanged(nameof(TotalText));
    }

    private void RaiseCommands()
    {
        BrowseCommand.RaiseCanExecuteChanged();
        BrowseOutputCommand.RaiseCanExecuteChanged();
        ScanCommand.RaiseCanExecuteChanged();
        ExportCommand.RaiseCanExecuteChanged();
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
}
