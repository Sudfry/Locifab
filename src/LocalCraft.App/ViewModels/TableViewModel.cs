using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using LocalCraft.App.Infrastructure;
using LocalCraft.App.Services;
using LocalCraft.Core.Journal;
using LocalCraft.Core.Tables;

namespace LocalCraft.App.ViewModels;

public sealed record TableFilterOption(TableFilterMode Mode, string DisplayName);

public sealed class TableFileSelection : ViewModelBase
{
    private bool _isSelected;

    public TableFileSelection(TableFileItem file)
    {
        File = file;
    }

    public TableFileItem File { get; }

    public string FileName => File.FileName;

    public string Extension => File.Extension;

    public int RowCount => File.RowCount;

    public string HeaderText => File.HeaderText;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class TableViewModel : ViewModelBase
{
    private readonly IDialogService _dialogService;
    private readonly TableReader _reader = new();
    private readonly TableExporter _exporter;

    private string _folder = string.Empty;
    private bool _includeSubfolders;
    private bool _isBusy;
    private bool _isProgressIndeterminate;
    private string _busyText = string.Empty;
    private string _summaryText = "选择一个文件夹，扫描其中的 CSV / TSV / XLSX 表格。";
    private string _filterColumnText = string.Empty;
    private TableFilterOption _selectedFilterMode;
    private string _filterValue = string.Empty;
    private string _sortColumnText = string.Empty;
    private bool _sortNumeric;
    private bool _sortDescending;
    private string _keepColumnsText = string.Empty;
    private DataView? _previewView;
    private int _previewRowCount;
    private int _selectedFileCount;
    private bool _hasPreview;
    private bool _hasResult;
    private bool _resultIsSuccess;
    private string _resultTitle = string.Empty;
    private string _resultMessage = string.Empty;
    private TableData? _previewData;
    private string? _lastSourcePath;

    public TableViewModel(JournalRepository repository, IDialogService dialogService)
    {
        _dialogService = dialogService;
        _exporter = new TableExporter(repository);
        FilterModes = new ObservableCollection<TableFilterOption>
        {
            new(TableFilterMode.Contains, "包含"),
            new(TableFilterMode.NotContains, "不包含"),
            new(TableFilterMode.Equals, "等于"),
            new(TableFilterMode.NotEquals, "不等于"),
            new(TableFilterMode.GreaterThan, "大于"),
            new(TableFilterMode.LessThan, "小于"),
            new(TableFilterMode.Empty, "为空"),
            new(TableFilterMode.NotEmpty, "不为空")
        };
        _selectedFilterMode = FilterModes[0];

        BrowseCommand = new RelayCommand(Browse, () => !IsBusy);
        AddFilesCommand = new RelayCommand(AddFiles, () => !IsBusy);
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy && Directory.Exists(Folder));
        PreviewCommand = new RelayCommand(BuildPreview, () => !IsBusy && SelectedFileCount > 0);
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => CanExport);
        OpenUndoCenterCommand = new RelayCommand(() => UndoCenterRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? UndoCenterRequested;

    public ObservableCollection<TableFileSelection> Files { get; } = new();

    public ObservableCollection<TableFilterOption> FilterModes { get; }

    public RelayCommand BrowseCommand { get; }
    public RelayCommand AddFilesCommand { get; }
    public AsyncRelayCommand ScanCommand { get; }
    public RelayCommand PreviewCommand { get; }
    public AsyncRelayCommand ExportCommand { get; }
    public RelayCommand OpenUndoCenterCommand { get; }

    public string Folder
    {
        get => _folder;
        set
        {
            if (!SetProperty(ref _folder, value)) return;
            RaiseCommandStates();
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

    public string FilterColumnText
    {
        get => _filterColumnText;
        set => SetProperty(ref _filterColumnText, value);
    }

    public TableFilterOption SelectedFilterMode
    {
        get => _selectedFilterMode;
        set
        {
            if (value is not null) SetProperty(ref _selectedFilterMode, value);
        }
    }

    public string FilterValue
    {
        get => _filterValue;
        set => SetProperty(ref _filterValue, value);
    }

    public string SortColumnText
    {
        get => _sortColumnText;
        set => SetProperty(ref _sortColumnText, value);
    }

    public bool SortNumeric
    {
        get => _sortNumeric;
        set => SetProperty(ref _sortNumeric, value);
    }

    public bool SortDescending
    {
        get => _sortDescending;
        set => SetProperty(ref _sortDescending, value);
    }

    public string KeepColumnsText
    {
        get => _keepColumnsText;
        set => SetProperty(ref _keepColumnsText, value);
    }

    public DataView? PreviewView
    {
        get => _previewView;
        private set => SetProperty(ref _previewView, value);
    }

    public int PreviewRowCount
    {
        get => _previewRowCount;
        private set => SetProperty(ref _previewRowCount, value);
    }

    public int SelectedFileCount
    {
        get => _selectedFileCount;
        private set
        {
            if (!SetProperty(ref _selectedFileCount, value)) return;
            OnPropertyChanged(nameof(SelectedFileText));
            PreviewCommand.RaiseCanExecuteChanged();
            RaiseCommandStates();
        }
    }

    public string SelectedFileText => $"已选 {SelectedFileCount} 个表格";

    public bool HasPreview
    {
        get => _hasPreview;
        private set
        {
            if (!SetProperty(ref _hasPreview, value)) return;
            OnPropertyChanged(nameof(CanExport));
            ExportCommand.RaiseCanExecuteChanged();
        }
    }

    public bool CanExport => !IsBusy && HasPreview && _previewData is { Rows.Count: > 0 };

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
        Files.Clear();
        SelectedFileCount = 0;
        HasPreview = false;
        HasResult = false;
        SummaryText = "文件夹已选择。点击「只读扫描」查看表格。";
    }

    public void AddDroppedFiles(IEnumerable<string> paths)
    {
        AddFilesInternal(paths.Where(File.Exists).ToList());
    }

    private void AddFiles()
    {
        var picked = _dialogService.PickFiles(
            "表格|*.csv;*.tsv;*.xlsx;*.xlsm|所有文件|*.*",
            Directory.Exists(Folder) ? Folder : null);
        if (picked.Count == 0) return;
        AddFilesInternal(picked);
    }

    private void AddFilesInternal(IReadOnlyList<string> picked)
    {
        if (picked.Count == 0) return;
        int added = 0;
        foreach (string path in picked)
        {
            if (Files.Any(x => string.Equals(x.File.FilePath, path, StringComparison.OrdinalIgnoreCase))) continue;
            try
            {
                TableData data = TableReader.Read(path);
                if (data.Headers.Count == 0) continue;
                var item = new TableFileItem
                {
                    FilePath = Path.GetFullPath(path),
                    RowCount = data.Rows.Count,
                    Headers = data.Headers,
                    Data = data
                };
                var row = new TableFileSelection(item) { IsSelected = true };
                row.PropertyChanged += OnFileSelectionChanged;
                Files.Add(row);
                added++;
            }
            catch
            {
                // 单个文件失败不影响其他选择。
            }
        }
        Folder = Path.GetDirectoryName(picked[0]) ?? Folder;
        SelectedFileCount = Files.Count(x => x.IsSelected);
        HasPreview = false;
        HasResult = false;
        SummaryText = $"已添加 {added} 个表格；共 {Files.Count} 个，可直接预览或继续扫描文件夹。";
        RaiseCommandStates();
        if (added > 0) BuildPreview();
    }

    private async Task ScanAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        IsProgressIndeterminate = true;
        BusyText = "正在只读扫描表格…";
        Files.Clear();
        SelectedFileCount = 0;
        HasPreview = false;
        HasResult = false;

        try
        {
            var result = await Task.Run(() => _reader.Scan(Folder, IncludeSubfolders));
            if (!result.Success)
            {
                SummaryText = result.Error ?? "扫描失败。";
                return;
            }

            foreach (var file in result.Files)
            {
                var row = new TableFileSelection(file);
                row.PropertyChanged += OnFileSelectionChanged;
                Files.Add(row);
            }

            SummaryText = result.Files.Count == 0
                ? "没有找到 CSV / TSV / XLSX / XLSM 表格。"
                : $"找到 {result.Files.Count} 个表格，跳过 {result.SkippedCount} 个无法读取的文件。";
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

    private void OnFileSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TableFileSelection.IsSelected)) return;
        SelectedFileCount = Files.Count(x => x.IsSelected);
        if (HasPreview)
        {
            HasPreview = false;
            PreviewView = null;
            _previewData = null;
        }
    }

    private void BuildPreview()
    {
        var selected = Files.Where(x => x.IsSelected).Select(x => x.File).ToList();
        if (selected.Count == 0)
        {
            _dialogService.ShowInfo("表格处理", "请先勾选至少一个表格。");
            return;
        }

        try
        {
            var options = new TableProcessOptions
            {
                FilterColumn = ParseNullableInt(FilterColumnText, "筛选列号"),
                FilterMode = SelectedFilterMode.Mode,
                FilterValue = FilterValue,
                SortColumn = ParseNullableInt(SortColumnText, "排序列号"),
                SortNumeric = SortNumeric,
                SortDescending = SortDescending,
                KeepColumns = ParseColumns(KeepColumnsText)
            };

            TableData data = TableProcessor.Build(selected, options);
            _previewData = data;
            _lastSourcePath = selected[0].FilePath;
            PreviewView = BuildDataView(data, 300);
            PreviewRowCount = data.Rows.Count;
            HasPreview = data.Rows.Count > 0;
            SummaryText = $"预览 {Math.Min(300, data.Rows.Count)} / {data.Rows.Count} 行，{data.Headers.Count} 列；来自 {selected.Count} 个表格。";
            if (data.Rows.Count == 0)
            {
                ResultIsSuccess = false;
                ResultTitle = "筛选后没有数据";
                ResultMessage = "请放宽筛选条件，或检查筛选列号。";
                HasResult = true;
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("生成预览失败", ex.Message);
        }
    }

    private async Task ExportAsync()
    {
        if (!CanExport || _previewData is null) return;
        string? output = _dialogService.PickSaveFile(
            "CSV 表格 (*.csv)|*.csv",
            "表格结果.csv",
            Directory.Exists(Folder) ? Folder : null);
        if (output is null) return;
        if (File.Exists(output))
        {
            _dialogService.ShowInfo("请换一个文件名", "输出文件已存在。为避免覆盖原文件，请使用一个新的文件名。");
            return;
        }

        IsBusy = true;
        IsProgressIndeterminate = true;
        BusyText = "正在写入 CSV…";
        try
        {
            var result = await Task.Run(() => _exporter.Export(
                _previewData,
                output,
                _lastSourcePath ?? Folder));
            ResultIsSuccess = result.Success;
            ResultTitle = result.Success ? "表格已导出" : "导出失败";
            ResultMessage = result.Message;
            HasResult = true;
            if (result.Success)
            {
                SummaryText = "CSV 已写入。可在回退中心删除本次输出。";
            }
        }
        catch (Exception ex)
        {
            ResultIsSuccess = false;
            ResultTitle = "导出失败";
            ResultMessage = ex.Message;
            HasResult = true;
        }
        finally
        {
            IsBusy = false;
            IsProgressIndeterminate = false;
            RaiseCommandStates();
        }
    }

    private static DataView BuildDataView(TableData data, int maxRows)
    {
        var table = new DataTable();
        for (int i = 0; i < data.Headers.Count; i++)
        {
            string name = data.Headers[i];
            if (string.IsNullOrWhiteSpace(name)) name = $"列{i + 1}";
            string unique = name;
            int suffix = 2;
            while (table.Columns.Contains(unique)) unique = $"{name}_{suffix++}";
            table.Columns.Add(unique, typeof(string));
        }

        foreach (string[] row in data.Rows.Take(maxRows))
        {
            table.Rows.Add(row.Cast<object>().ToArray());
        }

        return table.DefaultView;
    }

    private static int? ParseNullableInt(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (int.TryParse(value.Trim(), out int result) && result >= 0) return result;
        throw new ArgumentException($"{label}必须是从 0 开始的整数。");
    }

    private static IReadOnlyList<int>? ParseColumns(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var result = new List<int>();
        foreach (string part in value.Split(new[] { ',', '，', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(part, out int column) || column < 0)
            {
                throw new ArgumentException("保留列号必须是从 0 开始的整数，用逗号分隔。");
            }

            result.Add(column);
        }

        return result;
    }

    private void RaiseCommandStates()
    {
        BrowseCommand.RaiseCanExecuteChanged();
        ScanCommand.RaiseCanExecuteChanged();
        PreviewCommand.RaiseCanExecuteChanged();
        ExportCommand.RaiseCanExecuteChanged();
    }
}
