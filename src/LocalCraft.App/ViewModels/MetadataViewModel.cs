using System.Collections.ObjectModel;
using System.Windows.Threading;
using LocalCraft.App.Infrastructure;
using LocalCraft.App.Services;
using LocalCraft.Conversion.Metadata;
using LocalCraft.Core.Journal;
using LocalCraft.Core.Operations;

namespace LocalCraft.App.ViewModels;

public sealed class MetadataViewModel : ViewModelBase
{
    private readonly IDialogService _dialogService;
    private readonly MetadataCleaner _cleaner;

    private string _folder = string.Empty;
    private string _outputFolder = string.Empty;
    private bool _includeSubfolders = true;
    private bool _isBusy;
    private bool _isProgressIndeterminate;
    private double _progressValue;
    private string _busyText = string.Empty;
    private string _summaryText = "扫描 Office 文档和图片，生成不包含作者、公司、EXIF 等信息的副本。";
    private bool _hasPreview;
    private bool _hasResult;
    private bool _resultIsSuccess;
    private string _resultTitle = string.Empty;
    private string _resultMessage = string.Empty;
    private MetadataPlan? _plan;

    public MetadataViewModel(JournalRepository repository, IDialogService dialogService)
    {
        _dialogService = dialogService;
        _cleaner = new MetadataCleaner(repository);
        BrowseCommand = new RelayCommand(Browse, () => !IsBusy);
        BrowseOutputCommand = new RelayCommand(BrowseOutput, () => !IsBusy);
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy && Directory.Exists(Folder));
        ExecuteCommand = new AsyncRelayCommand(ExecuteAsync, () => CanExecute);
        OpenUndoCenterCommand = new RelayCommand(() => UndoCenterRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? UndoCenterRequested;

    public ObservableCollection<MetadataPlanItem> Items { get; } = new();

    public RelayCommand BrowseCommand { get; }
    public RelayCommand BrowseOutputCommand { get; }
    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand ExecuteCommand { get; }
    public RelayCommand OpenUndoCenterCommand { get; }

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

    public string OutputFolder
    {
        get => _outputFolder;
        set => SetProperty(ref _outputFolder, value);
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
            ExecuteCommand.RaiseCanExecuteChanged();
        }
    }

    public bool CanExecute => !IsBusy && HasPreview && _plan is { Items.Count: > 0 };

    private readonly DispatcherTimer _resultTimer = new() { Interval = TimeSpan.FromSeconds(6) };
    public bool HasResult { get => _hasResult; private set => SetProperty(ref _hasResult, value); }
    public bool ResultIsSuccess { get => _resultIsSuccess; private set => SetProperty(ref _resultIsSuccess, value); }
    public string ResultTitle { get => _resultTitle; private set => SetProperty(ref _resultTitle, value); }
    public string ResultMessage { get => _resultMessage; private set => SetProperty(ref _resultMessage, value); }

    private void Browse()
    {
        string? picked = _dialogService.PickFolder(Directory.Exists(Folder) ? Folder : null);
        if (picked is null) return;
        Folder = picked;
        if (string.IsNullOrWhiteSpace(OutputFolder))
        {
            OutputFolder = Path.Combine(picked, "_去元数据");
        }
        SummaryText = "文件夹已选择。点击「只读扫描」查看待处理文件。";
    }

    private void BrowseOutput()
    {
        string? picked = _dialogService.PickFolder(Directory.Exists(OutputFolder) ? OutputFolder : null);
        if (picked is not null) OutputFolder = picked;
    }

    private async Task ScanAsync()
    {
        IsBusy = true;
        IsProgressIndeterminate = true;
        BusyText = "正在扫描文档与图片…";
        HasResult = false;
        ClearPreview();
        try
        {
            var plan = await Task.Run(() => _cleaner.Plan(Folder, IncludeSubfolders, OutputFolder));
            if (!plan.Success)
            {
                SummaryText = plan.Error ?? "扫描失败。";
                return;
            }

            _plan = plan;
            foreach (var item in plan.Items) Items.Add(item);
            HasPreview = plan.Items.Count > 0;
            SummaryText = plan.Items.Count == 0
                ? "没有找到 DOCX/PPTX/XLSX 或可处理图片。"
                : $"找到 {plan.Items.Count} 个文件；跳过 {plan.SkippedCount} 个。输出到：{OutputFolder}";
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

    private async Task ExecuteAsync()
    {
        if (!CanExecute || _plan is null) return;
        if (!_dialogService.Confirm(
                "清理隐私元数据",
                $"将生成 {_plan.Items.Count} 个去元数据副本，原文件不修改。\n" +
                "Office 会清空作者/公司/标题/修订等属性；图片会重新编码并移除 EXIF。是否继续？"))
        {
            return;
        }

        IsBusy = true;
        IsProgressIndeterminate = false;
        ProgressValue = 0;
        BusyText = "正在清理元数据…";
        try
        {
            var plan = _plan;
            var progress = new Progress<OperationProgress>(value =>
            {
                ProgressValue = value.Total == 0 ? 0 : value.Completed * 100.0 / value.Total;
                BusyText = $"{value.Stage} · {value.CurrentFile}";
            });
            var result = await Task.Run(() => _cleaner.Execute(plan, progress));
            ResultIsSuccess = result.Success;
            if (result.Success)
            {
                _resultTimer.Stop();
                _resultTimer.Start();
            }
            ResultTitle = result.Success ? "元数据清理完成" : "元数据清理未完成";
            ResultMessage = result.Message;
            HasResult = true;
            if (result.Success)
            {
                ClearPreview();
                SummaryText = "已生成去元数据副本，可在回退中心删除本次输出。";
            }
        }
        catch (Exception ex)
        {
            ResultIsSuccess = false;
            ResultTitle = "元数据清理失败";
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

    private void ClearPreview()
    {
        _plan = null;
        Items.Clear();
        HasPreview = false;
    }

    private void RaiseCommandStates()
    {
        BrowseCommand.RaiseCanExecuteChanged();
        BrowseOutputCommand.RaiseCanExecuteChanged();
        ScanCommand.RaiseCanExecuteChanged();
        ExecuteCommand.RaiseCanExecuteChanged();
    }
}
