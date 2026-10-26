using System.Collections.ObjectModel;
using LocalCraft.App.Infrastructure;
using LocalCraft.App.Services;
using LocalCraft.Core.Files;
using LocalCraft.Core.Journal;

namespace LocalCraft.App.ViewModels;

public sealed class FileStateViewModel : ViewModelBase
{
    private readonly IDialogService _dialogService;
    private readonly FileStateService _service;
    private string _folder = string.Empty;
    private bool _includeSubfolders = true;
    private bool _includeHidden;
    private string _extensions = string.Empty;
    private bool _isBusy;
    private string _summary = "扫描后先预览旧值与新值，再执行。";
    private bool _hasResult;
    private bool _resultSuccess;
    private string _resultTitle = string.Empty;
    private string _resultMessage = string.Empty;

    private bool _timestampFixed = true;
    private string _fixedTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    private double _offsetDays = 0;
    private bool _applyCreated = true;
    private bool _applyModified = true;
    private bool _applyAccessed;
    private TimestampPlan? _timestampPlan;

    private bool _setReadOnly = true;
    private bool _setHidden;
    private bool _setArchive;
    private bool _removeAttributes;
    private AttributePlan? _attributePlan;

    public FileStateViewModel(JournalRepository repository, IDialogService dialogService)
    {
        _dialogService = dialogService;
        _service = new FileStateService(repository);
        BrowseCommand = new RelayCommand(Browse, () => !IsBusy);
        ScanTimestampCommand = new RelayCommand(ScanTimestamps, () => !IsBusy && Directory.Exists(Folder));
        ApplyTimestampCommand = new RelayCommand(ApplyTimestamps, () => !IsBusy && TimestampRows.Count > 0);
        ScanAttributeCommand = new RelayCommand(ScanAttributes, () => !IsBusy && Directory.Exists(Folder));
        ApplyAttributeCommand = new RelayCommand(ApplyAttributes, () => !IsBusy && AttributeRows.Count > 0);
        OpenUndoCenterCommand = new RelayCommand(() => UndoCenterRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? UndoCenterRequested;
    public ObservableCollection<TimestampPlanItem> TimestampRows { get; } = new();
    public ObservableCollection<AttributePlanItem> AttributeRows { get; } = new();
    public RelayCommand BrowseCommand { get; }
    public RelayCommand ScanTimestampCommand { get; }
    public RelayCommand ApplyTimestampCommand { get; }
    public RelayCommand ScanAttributeCommand { get; }
    public RelayCommand ApplyAttributeCommand { get; }
    public RelayCommand OpenUndoCenterCommand { get; }

    public string Folder
    {
        get => _folder;
        set
        {
            if (!SetProperty(ref _folder, value)) return;
            Clear();
            RaiseCommands();
        }
    }

    public bool IncludeSubfolders { get => _includeSubfolders; set => SetProperty(ref _includeSubfolders, value); }
    public bool IncludeHidden { get => _includeHidden; set => SetProperty(ref _includeHidden, value); }
    public string Extensions { get => _extensions; set => SetProperty(ref _extensions, value); }
    public bool IsBusy
    {
        get => _isBusy;
        private set { if (!SetProperty(ref _isBusy, value)) return; RaiseCommands(); }
    }

    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
    public bool HasResult { get => _hasResult; private set => SetProperty(ref _hasResult, value); }
    public bool ResultSuccess { get => _resultSuccess; private set => SetProperty(ref _resultSuccess, value); }
    public string ResultTitle { get => _resultTitle; private set => SetProperty(ref _resultTitle, value); }
    public string ResultMessage { get => _resultMessage; private set => SetProperty(ref _resultMessage, value); }

    public bool TimestampFixed
    {
        get => _timestampFixed;
        set { if (SetProperty(ref _timestampFixed, value)) OnPropertyChanged(nameof(TimestampOffset)); }
    }
    public bool TimestampOffset
    {
        get => !_timestampFixed;
        set { if (value) TimestampFixed = false; }
    }
    public string FixedTime { get => _fixedTime; set => SetProperty(ref _fixedTime, value); }
    public double OffsetDays { get => _offsetDays; set => SetProperty(ref _offsetDays, value); }
    public bool ApplyCreated { get => _applyCreated; set => SetProperty(ref _applyCreated, value); }
    public bool ApplyModified { get => _applyModified; set => SetProperty(ref _applyModified, value); }
    public bool ApplyAccessed { get => _applyAccessed; set => SetProperty(ref _applyAccessed, value); }

    public bool SetReadOnly { get => _setReadOnly; set => SetProperty(ref _setReadOnly, value); }
    public bool SetHidden { get => _setHidden; set => SetProperty(ref _setHidden, value); }
    public bool SetArchive { get => _setArchive; set => SetProperty(ref _setArchive, value); }
    public bool RemoveAttributes { get => _removeAttributes; set => SetProperty(ref _removeAttributes, value); }

    private void Browse()
    {
        string? folder = _dialogService.PickFolder(Directory.Exists(Folder) ? Folder : null);
        if (folder is not null) Folder = folder;
    }

    private TimestampOptions BuildTimestampOptions() => new()
    {
        Folder = Folder,
        IncludeSubfolders = IncludeSubfolders,
        IncludeHidden = IncludeHidden,
        Extensions = Extensions,
        Mode = TimestampFixed ? TimestampMode.Fixed : TimestampMode.Offset,
        FixedTime = DateTime.TryParse(FixedTime, out DateTime value) ? value : DateTime.Now,
        OffsetDays = OffsetDays,
        ApplyCreated = ApplyCreated,
        ApplyModified = ApplyModified,
        ApplyAccessed = ApplyAccessed
    };

    private AttributeOptions BuildAttributeOptions() => new()
    {
        Folder = Folder,
        IncludeSubfolders = IncludeSubfolders,
        IncludeHidden = IncludeHidden,
        Extensions = Extensions,
        SetReadOnly = SetReadOnly,
        SetHidden = SetHidden,
        SetArchive = SetArchive,
        Remove = RemoveAttributes
    };

    private void ScanTimestamps()
    {
        TimestampRows.Clear();
        _timestampPlan = _service.PlanTimestamps(BuildTimestampOptions());
        if (!_timestampPlan.Success)
        {
            Summary = _timestampPlan.Error ?? "扫描失败。";
            return;
        }

        foreach (var item in _timestampPlan.Items) TimestampRows.Add(item);
        Summary = $"找到 {TimestampRows.Count} 个文件；旧时间与新时间见下表。";
        RaiseCommands();
    }

    private void ApplyTimestamps()
    {
        if (_timestampPlan is null) return;
        var result = _service.ExecuteTimestamps(_timestampPlan, BuildTimestampOptions());
        ShowResult(result);
        if (result.Success)
        {
            Summary = result.Message + " 可在回退中心恢复原时间。";
            TimestampRows.Clear();
            _timestampPlan = null;
        }
    }

    private void ScanAttributes()
    {
        AttributeRows.Clear();
        _attributePlan = _service.PlanAttributes(BuildAttributeOptions());
        if (!_attributePlan.Success)
        {
            Summary = _attributePlan.Error ?? "扫描失败。";
            return;
        }

        foreach (var item in _attributePlan.Items) AttributeRows.Add(item);
        Summary = $"找到 {AttributeRows.Count} 个文件；只修改勾选的属性。";
        RaiseCommands();
    }

    private void ApplyAttributes()
    {
        if (_attributePlan is null) return;
        var result = _service.ExecuteAttributes(_attributePlan, BuildAttributeOptions());
        ShowResult(result);
        if (result.Success)
        {
            Summary = result.Message + " 可在回退中心恢复原属性。";
            AttributeRows.Clear();
            _attributePlan = null;
        }
    }

    private void ShowResult(SplitResult result)
    {
        ResultSuccess = result.Success;
        ResultTitle = result.Success ? "操作完成" : "操作未完成";
        ResultMessage = result.Message;
        HasResult = true;
        RaiseCommands();
    }

    private void Clear()
    {
        TimestampRows.Clear();
        AttributeRows.Clear();
        _timestampPlan = null;
        _attributePlan = null;
    }

    private void RaiseCommands()
    {
        BrowseCommand.RaiseCanExecuteChanged();
        ScanTimestampCommand.RaiseCanExecuteChanged();
        ApplyTimestampCommand.RaiseCanExecuteChanged();
        ScanAttributeCommand.RaiseCanExecuteChanged();
        ApplyAttributeCommand.RaiseCanExecuteChanged();
    }
}
