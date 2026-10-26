using System.Collections.ObjectModel;
using System.Windows;
using LocalCraft.App.Infrastructure;
using LocalCraft.App.Services;
using LocalCraft.Core.Search;

namespace LocalCraft.App.ViewModels;

public sealed class SearchRow : ViewModelBase
{
    private bool _isSelected;

    public FileSearchItem Item { get; init; } = new();
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string Name => Item.Name;
    public string RelativePath => Item.RelativePath;
    public string Folder => Item.Folder;
    public string SizeText => Item.SizeText;
    public string ModifiedText => Item.ModifiedText;
}

public sealed class SearchViewModel : ViewModelBase
{
    private readonly IDialogService _dialogService;
    private string _folder = string.Empty;
    private string _pattern = string.Empty;
    private bool _useRegex;
    private bool _includeSubfolders = true;
    private bool _includeHidden;
    private string _extensions = string.Empty;
    private double _minSizeMb;
    private double _maxSizeMb;
    private bool _isBusy;
    private string _busyText = string.Empty;
    private string _summaryText = "输入关键词、通配符或正则，扫描本机文件夹。";
    private int _selectedCount;

    public SearchViewModel(IDialogService dialogService)
    {
        _dialogService = dialogService;
        BrowseCommand = new RelayCommand(Browse, () => !IsBusy);
        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !IsBusy && Directory.Exists(Folder));
        SelectAllCommand = new RelayCommand(SelectAll, () => !IsBusy && Rows.Count > 0);
        ClearSelectionCommand = new RelayCommand(ClearSelection, () => !IsBusy && SelectedCount > 0);
        CopyPathsCommand = new RelayCommand(CopyPaths, () => SelectedCount > 0);
        RevealCommand = new RelayCommand(Reveal, () => SelectedCount > 0);
        DeliverCommand = new RelayCommand(Deliver, () => SelectedCount > 0);
    }

    public event EventHandler<IReadOnlyList<string>>? DeliveryRequested;

    public ObservableCollection<SearchRow> Rows { get; } = new();
    public RelayCommand BrowseCommand { get; }
    public AsyncRelayCommand SearchCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }
    public RelayCommand CopyPathsCommand { get; }
    public RelayCommand RevealCommand { get; }
    public RelayCommand DeliverCommand { get; }

    public string Folder
    {
        get => _folder;
        set
        {
            if (!SetProperty(ref _folder, value)) return;
            SearchCommand.RaiseCanExecuteChanged();
        }
    }

    public string Pattern { get => _pattern; set => SetProperty(ref _pattern, value); }
    public bool UseRegex { get => _useRegex; set => SetProperty(ref _useRegex, value); }
    public bool IncludeSubfolders { get => _includeSubfolders; set => SetProperty(ref _includeSubfolders, value); }
    public bool IncludeHidden { get => _includeHidden; set => SetProperty(ref _includeHidden, value); }
    public string Extensions { get => _extensions; set => SetProperty(ref _extensions, value); }
    public double MinSizeMb { get => _minSizeMb; set => SetProperty(ref _minSizeMb, Math.Max(0, value)); }
    public double MaxSizeMb { get => _maxSizeMb; set => SetProperty(ref _maxSizeMb, Math.Max(0, value)); }

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
    public string BusyText { get => _busyText; private set => SetProperty(ref _busyText, value); }
    public string SummaryText { get => _summaryText; private set => SetProperty(ref _summaryText, value); }

    public int SelectedCount
    {
        get => _selectedCount;
        private set
        {
            if (!SetProperty(ref _selectedCount, value)) return;
            OnPropertyChanged(nameof(SelectedText));
            RaiseCommands();
        }
    }

    public string SelectedText => $"已选 {SelectedCount} 个";

    private void Browse()
    {
        string? picked = _dialogService.PickFolder(Directory.Exists(Folder) ? Folder : null);
        if (picked is null) return;
        Folder = picked;
    }

    private async Task SearchAsync()
    {
        IsBusy = true;
        BusyText = "正在搜索文件…";
        Rows.Clear();
        SelectedCount = 0;
        try
        {
            var result = await Task.Run(() => FileSearchService.Search(new FileSearchOptions
            {
                Folder = Folder,
                Pattern = Pattern,
                UseRegex = UseRegex,
                IncludeSubfolders = IncludeSubfolders,
                IncludeHidden = IncludeHidden,
                Extensions = Extensions,
                MinSizeMb = MinSizeMb,
                MaxSizeMb = MaxSizeMb
            }));
            if (!result.Success)
            {
                SummaryText = result.Error ?? "搜索失败。";
                return;
            }

            foreach (var item in result.Items)
            {
                var row = new SearchRow { Item = item };
                row.PropertyChanged += (_, _) => UpdateSelection();
                Rows.Add(row);
            }

            SummaryText = result.Truncated
                ? $"显示前 {Rows.Count} 个结果（已达到上限）。"
                : $"找到 {Rows.Count} 个文件。";
        }
        catch (Exception ex)
        {
            SummaryText = "搜索失败：" + ex.Message;
            _dialogService.ShowError("搜索失败", ex.Message);
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
        }
    }

    private void SelectAll()
    {
        foreach (var row in Rows) row.IsSelected = true;
        UpdateSelection();
    }

    private void ClearSelection()
    {
        foreach (var row in Rows) row.IsSelected = false;
        UpdateSelection();
    }

    private void UpdateSelection() => SelectedCount = Rows.Count(x => x.IsSelected);

    private void CopyPaths()
    {
        var paths = Rows.Where(x => x.IsSelected).Select(x => x.Item.Path).ToArray();
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, paths));
            SummaryText = $"已复制 {paths.Length} 条路径到剪贴板。";
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("复制失败", ex.Message);
        }
    }

    private void Reveal()
    {
        var row = Rows.FirstOrDefault(x => x.IsSelected);
        if (row is null) return;
        try
        {
            ShellService.RevealFile(row.Item.Path);
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("无法打开文件位置", ex.Message);
        }
    }

    private void Deliver()
    {
        var paths = Rows.Where(x => x.IsSelected).Select(x => x.Item.Path).ToArray();
        if (paths.Length == 0) return;
        DeliveryRequested?.Invoke(this, paths);
    }

    private void RaiseCommands()
    {
        BrowseCommand.RaiseCanExecuteChanged();
        SearchCommand.RaiseCanExecuteChanged();
        SelectAllCommand.RaiseCanExecuteChanged();
        ClearSelectionCommand.RaiseCanExecuteChanged();
        CopyPathsCommand.RaiseCanExecuteChanged();
        RevealCommand.RaiseCanExecuteChanged();
        DeliverCommand.RaiseCanExecuteChanged();
    }
}
