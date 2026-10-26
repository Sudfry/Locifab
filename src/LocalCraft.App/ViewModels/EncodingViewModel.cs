using System.Collections.ObjectModel;
using LocalCraft.App.Infrastructure;
using LocalCraft.App.Services;
using LocalCraft.Core.Files;
using LocalCraft.Core.Journal;
using LocalCraft.Core.Operations;

namespace LocalCraft.App.ViewModels;

public sealed record EncodingTargetOption(TextEncodingTarget Value, string Name);
public sealed record NewlineOption(NewlineTarget Value, string Name);
public sealed record SourceEncodingOption(SourceEncodingMode Value, string Name);

public sealed class EncodingViewModel : ViewModelBase
{
    private readonly IDialogService _dialogService;
    private readonly EncodingConversionService _service;
    private string _folder = string.Empty;
    private string _outputFolder = string.Empty;
    private string _extensions = ".txt,.md,.csv,.json,.xml,.html,.css,.js,.cs,.log,.ini,.yml,.yaml";
    private bool _includeSubfolders = true;
    private EncodingTargetOption _target;
    private NewlineOption _newline;
    private SourceEncodingOption _sourceEncoding;
    private bool _isBusy;
    private bool _isProgressIndeterminate;
    private double _progress;
    private string _busyText = string.Empty;
    private string _summary = "扫描文本文件，先预览输出，再执行编码转换。";
    private bool _hasPreview;
    private bool _hasResult;
    private bool _resultSuccess;
    private string _resultTitle = string.Empty;
    private string _resultMessage = string.Empty;
    private EncodingPlan? _plan;

    public EncodingViewModel(JournalRepository repository, IDialogService dialogService)
    {
        _dialogService = dialogService;
        _service = new EncodingConversionService(repository);
        Targets = new ObservableCollection<EncodingTargetOption>
        {
            new(TextEncodingTarget.Utf8, "UTF-8（无 BOM）"),
            new(TextEncodingTarget.Utf8Bom, "UTF-8（带 BOM）"),
            new(TextEncodingTarget.Gbk, "GBK / GB18030")
        };
        Newlines = new ObservableCollection<NewlineOption>
        {
            new(NewlineTarget.Keep, "保持原换行"),
            new(NewlineTarget.Crlf, "统一为 CRLF"),
            new(NewlineTarget.Lf, "统一为 LF")
        };
        _target = Targets[0];
        _newline = Newlines[0];
        SourceEncodings = new ObservableCollection<SourceEncodingOption>
        {
            new(SourceEncodingMode.Auto, "源编码：自动检测"),
            new(SourceEncodingMode.Utf8, "源编码：UTF-8"),
            new(SourceEncodingMode.Gbk, "源编码：GBK")
        };
        _sourceEncoding = SourceEncodings[0];
        BrowseCommand = new RelayCommand(Browse, () => !IsBusy);
        BrowseOutputCommand = new RelayCommand(BrowseOutput, () => !IsBusy);
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy && Directory.Exists(Folder));
        ExecuteCommand = new AsyncRelayCommand(ExecuteAsync, () => !IsBusy && HasPreview);
        OpenUndoCenterCommand = new RelayCommand(() => UndoCenterRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? UndoCenterRequested;
    public ObservableCollection<EncodingTargetOption> Targets { get; }
    public ObservableCollection<NewlineOption> Newlines { get; }
    public ObservableCollection<SourceEncodingOption> SourceEncodings { get; }
    public ObservableCollection<EncodingPlanItem> Rows { get; } = new();
    public RelayCommand BrowseCommand { get; }
    public RelayCommand BrowseOutputCommand { get; }
    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand ExecuteCommand { get; }
    public RelayCommand OpenUndoCenterCommand { get; }

    public string Folder { get => _folder; set { if (SetProperty(ref _folder, value)) Clear(); } }
    public string OutputFolder { get => _outputFolder; set => SetProperty(ref _outputFolder, value); }
    public string Extensions { get => _extensions; set => SetProperty(ref _extensions, value); }
    public bool IncludeSubfolders { get => _includeSubfolders; set => SetProperty(ref _includeSubfolders, value); }
    public EncodingTargetOption Target { get => _target; set { if (value is not null && SetProperty(ref _target, value)) Clear(); } }
    public NewlineOption Newline { get => _newline; set { if (value is not null && SetProperty(ref _newline, value)) Clear(); } }
    public SourceEncodingOption SourceEncoding { get => _sourceEncoding; set { if (value is not null && SetProperty(ref _sourceEncoding, value)) Clear(); } }
    public bool IsBusy { get => _isBusy; private set { if (!SetProperty(ref _isBusy, value)) return; RaiseCommands(); } }
    public bool IsProgressIndeterminate { get => _isProgressIndeterminate; private set => SetProperty(ref _isProgressIndeterminate, value); }
    public double Progress { get => _progress; private set => SetProperty(ref _progress, value); }
    public string BusyText { get => _busyText; private set => SetProperty(ref _busyText, value); }
    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
    public bool HasPreview { get => _hasPreview; private set { if (!SetProperty(ref _hasPreview, value)) return; ExecuteCommand.RaiseCanExecuteChanged(); } }
    public bool HasResult { get => _hasResult; private set => SetProperty(ref _hasResult, value); }
    public bool ResultSuccess { get => _resultSuccess; private set => SetProperty(ref _resultSuccess, value); }
    public string ResultTitle { get => _resultTitle; private set => SetProperty(ref _resultTitle, value); }
    public string ResultMessage { get => _resultMessage; private set => SetProperty(ref _resultMessage, value); }

    private EncodingOptions Build() => new()
    {
        Folder = Folder,
        IncludeSubfolders = IncludeSubfolders,
        Extensions = Extensions,
        OutputFolder = OutputFolder,
        SourceEncoding = SourceEncoding.Value,
        Target = Target.Value,
        Newline = Newline.Value
    };

    private void Browse()
    {
        string? folder = _dialogService.PickFolder(Directory.Exists(Folder) ? Folder : null);
        if (folder is null) return;
        Folder = folder;
        if (string.IsNullOrWhiteSpace(OutputFolder)) OutputFolder = Path.Combine(folder, "_编码转换");
    }

    private void BrowseOutput()
    {
        string? folder = _dialogService.PickFolder(Directory.Exists(OutputFolder) ? OutputFolder : null);
        if (folder is not null) OutputFolder = folder;
    }

    private async Task ScanAsync()
    {
        IsBusy = true;
        IsProgressIndeterminate = true;
        BusyText = "正在检测编码…";
        Clear();
        try
        {
            var plan = await Task.Run(() => _service.Plan(Build()));
            if (!plan.Success)
            {
                Summary = plan.Error ?? "扫描失败。";
                return;
            }
            _plan = plan;
            foreach (var item in plan.Items) Rows.Add(item);
            HasPreview = Rows.Count > 0;
            Summary = $"找到 {Rows.Count} 个文本文件；跳过 {plan.SkippedCount} 个。";
        }
        catch (Exception ex)
        {
            Summary = "扫描失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
            IsProgressIndeterminate = false;
            BusyText = string.Empty;
        }
    }

    private async Task ExecuteAsync()
    {
        if (_plan is null) return;
        IsBusy = true;
        IsProgressIndeterminate = false;
        Progress = 0;
        BusyText = "正在转换编码…";
        try
        {
            var progress = new Progress<OperationProgress>(value =>
            {
                Progress = value.Total == 0 ? 0 : value.Completed * 100.0 / value.Total;
                BusyText = $"{value.Stage} · {value.CurrentFile}";
            });
            var result = await Task.Run(() => _service.Execute(_plan, Build(), progress));
            ResultSuccess = result.Success;
            ResultTitle = result.Success ? "编码转换完成" : "编码转换未完成";
            ResultMessage = result.Message;
            HasResult = true;
            if (result.Success)
            {
                Summary = result.Message + " 可在回退中心删除输出。";
                Clear();
            }
        }
        catch (Exception ex)
        {
            ResultSuccess = false;
            ResultTitle = "编码转换失败";
            ResultMessage = ex.Message;
            HasResult = true;
        }
        finally
        {
            IsBusy = false;
            IsProgressIndeterminate = false;
            Progress = 0;
            BusyText = string.Empty;
        }
    }

    private void Clear()
    {
        Rows.Clear();
        _plan = null;
        HasPreview = false;
    }

    private void RaiseCommands()
    {
        BrowseCommand.RaiseCanExecuteChanged();
        BrowseOutputCommand.RaiseCanExecuteChanged();
        ScanCommand.RaiseCanExecuteChanged();
        ExecuteCommand.RaiseCanExecuteChanged();
    }
}
