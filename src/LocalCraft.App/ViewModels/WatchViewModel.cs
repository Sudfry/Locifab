using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using LocalCraft.App.Infrastructure;
using LocalCraft.App.Services;

namespace LocalCraft.App.ViewModels;

public sealed class WatchChangeItem : ViewModelBase
{
    private int _count;
    private DateTime _lastSeen;
    private string _changeType = "变化";

    public string Path { get; init; } = string.Empty;

    public string ChangeType
    {
        get => _changeType;
        set => SetProperty(ref _changeType, value);
    }

    public string FileName => System.IO.Path.GetFileName(Path);

    public int Count
    {
        get => _count;
        set => SetProperty(ref _count, value);
    }

    public DateTime LastSeen
    {
        get => _lastSeen;
        set
        {
            if (!SetProperty(ref _lastSeen, value)) return;
            OnPropertyChanged(nameof(LastSeenText));
        }
    }

    public string LastSeenText => LastSeen.ToString("HH:mm:ss");
}

public sealed class WatchViewModel : ViewModelBase, IDisposable
{
    private readonly IDialogService _dialogService;
    private readonly object _gate = new();
    private readonly Dictionary<string, WatchChangeItem> _items = new(StringComparer.OrdinalIgnoreCase);

    private FileSystemWatcher? _watcher;
    private string _folder = string.Empty;
    private bool _includeSubfolders = true;
    private bool _isWatching;
    private string _statusText = "未开始";
    private string _summaryText = "还没有开始监控。";

    public WatchViewModel(IDialogService dialogService)
    {
        _dialogService = dialogService;
        BrowseCommand = new RelayCommand(Browse, () => !IsWatching);
        StartCommand = new RelayCommand(Start, () => !IsWatching && Directory.Exists(Folder));
        StopCommand = new RelayCommand(Stop, () => IsWatching);
        ClearCommand = new RelayCommand(Clear, () => !IsWatching && Changes.Count > 0);
        OpenFolderCommand = new RelayCommand(OpenFolder, () => Directory.Exists(Folder));
    }

    public ObservableCollection<WatchChangeItem> Changes { get; } = new();

    public RelayCommand BrowseCommand { get; }
    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand OpenFolderCommand { get; }

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
        set
        {
            if (!SetProperty(ref _includeSubfolders, value)) return;
            if (IsWatching)
            {
                Stop();
                Start();
            }
        }
    }

    public bool IsWatching
    {
        get => _isWatching;
        private set
        {
            if (!SetProperty(ref _isWatching, value)) return;
            OnPropertyChanged(nameof(IsIdle));
            RaiseCommandStates();
        }
    }

    public bool IsIdle => !IsWatching;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    private void Browse()
    {
        string? picked = _dialogService.PickFolder(Directory.Exists(Folder) ? Folder : null);
        if (picked is null) return;
        Folder = picked;
        StatusText = "已选择文件夹，点击「开始监控」。";
    }

    private void Start()
    {
        if (!Directory.Exists(Folder))
        {
            _dialogService.ShowInfo("实时监控", "请选择存在的文件夹。");
            return;
        }

        try
        {
            _watcher = new FileSystemWatcher(Folder)
            {
                IncludeSubdirectories = IncludeSubfolders,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                               | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
                EnableRaisingEvents = true
            };
            _watcher.Created += OnChanged;
            _watcher.Changed += OnChanged;
            _watcher.Deleted += OnChanged;
            _watcher.Renamed += OnRenamed;
            _watcher.Error += OnError;

            IsWatching = true;
            StatusText = "监控中 · 只记录变化，不自动处理文件";
            SummaryText = "已开始监控。往文件夹里放入或修改文件，变化会出现在下面的队列里。";
        }
        catch (Exception ex)
        {
            _watcher?.Dispose();
            _watcher = null;
            _dialogService.ShowError("无法开始监控", ex.Message);
        }
    }

    private void Stop()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnChanged;
            _watcher.Changed -= OnChanged;
            _watcher.Deleted -= OnChanged;
            _watcher.Renamed -= OnRenamed;
            _watcher.Error -= OnError;
            _watcher.Dispose();
            _watcher = null;
        }

        IsWatching = false;
        StatusText = "已停止";
        SummaryText = Changes.Count == 0 ? "队列是空的。" : $"队列中有 {Changes.Count} 个变化文件。";
        RaiseCommandStates();
    }

    private void Clear()
    {
        lock (_gate) _items.Clear();
        Changes.Clear();
        SummaryText = "队列已清空。";
        RaiseCommandStates();
    }

    private void OpenFolder()
    {
        try
        {
            Services.ShellService.OpenFolder(Folder);
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("无法打开文件夹", ex.Message);
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        AddChange(e.FullPath, e.ChangeType switch
        {
            WatcherChangeTypes.Created => "新建",
            WatcherChangeTypes.Deleted => "删除",
            _ => "修改"
        });
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        AddChange(e.FullPath, "重命名");
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            StatusText = "监控缓冲区溢出或目录不可访问，请停止后重新开始。";
        });
    }

    private void AddChange(string path, string changeType)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            lock (_gate)
            {
                if (_items.TryGetValue(path, out WatchChangeItem? item))
                {
                    item.Count++;
                    item.LastSeen = DateTime.Now;
                    item.ChangeType = changeType;
                }
                else
                {
                    item = new WatchChangeItem
                    {
                        Path = path,
                        ChangeType = changeType,
                        Count = 1,
                        LastSeen = DateTime.Now
                    };
                    _items[path] = item;
                    Changes.Add(item);
                }
            }

            SummaryText = $"监控中 · 队列中有 {Changes.Count} 个变化文件，共 {Changes.Sum(x => x.Count)} 次变化。";
            RaiseCommandStates();
        });
    }

    private void RaiseCommandStates()
    {
        BrowseCommand.RaiseCanExecuteChanged();
        StartCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        ClearCommand.RaiseCanExecuteChanged();
        OpenFolderCommand.RaiseCanExecuteChanged();
    }

    public void Dispose() => Stop();
}
