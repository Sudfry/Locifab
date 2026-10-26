using System.Collections.ObjectModel;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalCraft.App.Infrastructure;
using LocalCraft.App.Services;
using LocalCraft.Conversion.Images;
using LocalCraft.Core.Journal;
using LocalCraft.Core.Operations;

namespace LocalCraft.App.ViewModels;

public sealed record ImageResizeOption(ImageResizeMode Mode, string DisplayName);
public sealed record ImageRotationOption(int Angle, string DisplayName);
public sealed record ImageFormatOption(ImageOutputFormat Format, string DisplayName);

public sealed class ImageBatchRow
{
    public required ImageBatchPlanItem Item { get; init; }
    public ImageSource? AdjustedPreview { get; init; }
    public string FileName => Item.FileName;
    public string Dimensions => Item.Dimensions;
    public string OutputPath => Item.OutputPath;
    public string SourcePath => Item.SourcePath;
}

public sealed class ImageBatchViewModel : ViewModelBase
{
    private readonly IDialogService _dialogService;
    private readonly ImageBatchPlanner _planner = new();
    private readonly ImageBatchExecutor _executor;

    private string _folder = string.Empty;
    private bool _includeSubfolders = true;
    private ImageResizeOption _selectedResizeMode;
    private int _sizeValue = 1920;
    private int _percent = 80;
    private ImageRotationOption _selectedRotation;
    private ImageFormatOption _selectedFormat;
    private int _jpegQuality = 88;
    private string _suffix = "_优化";
    private string _outputFolder = string.Empty;
    private string _watermarkText = string.Empty;
    private bool _stripMetadata = true;
    private bool _isBusy;
    private bool _isProgressIndeterminate;
    private double _progressValue;
    private string _busyText = string.Empty;
    private string _summaryText = "选择图片文件夹，设置压缩/缩放规则，先预览再执行。";
    private bool _hasPreview;
    private bool _hasResult;
    private bool _resultIsSuccess;
    private string _resultTitle = string.Empty;
    private string _resultMessage = string.Empty;
    private ImageBatchPlan? _plan;
    private readonly List<string> _selectedFiles = new();

    public ImageBatchViewModel(JournalRepository repository, IDialogService dialogService)
    {
        _dialogService = dialogService;
        _executor = new ImageBatchExecutor(repository);
        ResizeModes = new ObservableCollection<ImageResizeOption>
        {
            new(ImageResizeMode.None, "不调整尺寸"),
            new(ImageResizeMode.LongEdge, "限制长边"),
            new(ImageResizeMode.Width, "指定宽度"),
            new(ImageResizeMode.Percent, "按百分比")
        };
        Rotations = new ObservableCollection<ImageRotationOption>
        {
            new(0, "不旋转"), new(90, "顺时针 90°"), new(180, "180°"), new(270, "顺时针 270°")
        };
        Formats = new ObservableCollection<ImageFormatOption>
        {
            new(ImageOutputFormat.Keep, "保持原格式"),
            new(ImageOutputFormat.Jpg, "JPG"),
            new(ImageOutputFormat.Png, "PNG")
        };
        _selectedResizeMode = ResizeModes[0];
        _selectedRotation = Rotations[0];
        _selectedFormat = Formats[0];

        BrowseCommand = new RelayCommand(Browse, () => !IsBusy);
        AddFilesCommand = new RelayCommand(AddFiles, () => !IsBusy);
        BrowseOutputCommand = new RelayCommand(BrowseOutput, () => !IsBusy);
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy && Directory.Exists(Folder));
        ExecuteCommand = new AsyncRelayCommand(ExecuteAsync, () => CanExecute);
        OpenUndoCenterCommand = new RelayCommand(() => UndoCenterRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? UndoCenterRequested;

    public ObservableCollection<ImageResizeOption> ResizeModes { get; }
    public ObservableCollection<ImageRotationOption> Rotations { get; }
    public ObservableCollection<ImageFormatOption> Formats { get; }
    public ObservableCollection<ImageBatchRow> Items { get; } = new();

    public RelayCommand BrowseCommand { get; }
    public RelayCommand AddFilesCommand { get; }
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

    public bool IncludeSubfolders
    {
        get => _includeSubfolders;
        set => SetProperty(ref _includeSubfolders, value);
    }

    public ImageResizeOption SelectedResizeMode
    {
        get => _selectedResizeMode;
        set
        {
            if (value is null || !SetProperty(ref _selectedResizeMode, value)) return;
            OnPropertyChanged(nameof(IsLongEdge));
            OnPropertyChanged(nameof(IsWidth));
            OnPropertyChanged(nameof(IsPercent));
            OnPropertyChanged(nameof(IsSizeEnabled));
        }
    }

    public bool IsLongEdge => SelectedResizeMode.Mode == ImageResizeMode.LongEdge;
    public bool IsWidth => SelectedResizeMode.Mode == ImageResizeMode.Width;
    public bool IsPercent => SelectedResizeMode.Mode == ImageResizeMode.Percent;
    public bool IsSizeEnabled => IsLongEdge || IsWidth;

    public int SizeValue
    {
        get => _sizeValue;
        set => SetProperty(ref _sizeValue, Math.Clamp(value, 16, 20000));
    }

    public int Percent
    {
        get => _percent;
        set => SetProperty(ref _percent, Math.Clamp(value, 1, 400));
    }

    public ImageRotationOption SelectedRotation
    {
        get => _selectedRotation;
        set
        {
            if (value is not null) SetProperty(ref _selectedRotation, value);
        }
    }

    public ImageFormatOption SelectedFormat
    {
        get => _selectedFormat;
        set
        {
            if (value is not null) SetProperty(ref _selectedFormat, value);
        }
    }

    public int JpegQuality
    {
        get => _jpegQuality;
        set
        {
            if (!SetProperty(ref _jpegQuality, Math.Clamp(value, 1, 100))) return;
            OnPropertyChanged(nameof(JpegQualityLabel));
        }
    }

    public string JpegQualityLabel => $"{JpegQuality}%";

    public string Suffix
    {
        get => _suffix;
        set => SetProperty(ref _suffix, value);
    }

    public string OutputFolder
    {
        get => _outputFolder;
        set => SetProperty(ref _outputFolder, value);
    }

    public string WatermarkText
    {
        get => _watermarkText;
        set => SetProperty(ref _watermarkText, value);
    }

    public bool StripMetadata
    {
        get => _stripMetadata;
        set => SetProperty(ref _stripMetadata, value);
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
        _selectedFiles.Clear();
        if (string.IsNullOrWhiteSpace(OutputFolder)) OutputFolder = picked;
        SummaryText = "文件夹已选择。点击「只读扫描并预览」查看输出计划。";
    }

    public async Task AddDroppedFilesAsync(IEnumerable<string> paths)
    {
        var files = paths.Where(File.Exists).ToList();
        if (files.Count == 0) return;
        _selectedFiles.Clear();
        _selectedFiles.AddRange(files);
        Folder = Path.GetDirectoryName(files[0]) ?? Folder;
        if (string.IsNullOrWhiteSpace(OutputFolder)) OutputFolder = Path.Combine(Folder, "_优化");
        RaiseCommandStates();
        await ScanAsync();
    }

    private void AddFiles()
    {
        var files = _dialogService.PickFiles(
            "图片|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.webp|所有文件|*.*",
            Directory.Exists(Folder) ? Folder : null);
        if (files.Count == 0) return;
        _selectedFiles.Clear();
        _selectedFiles.AddRange(files);
        Folder = Path.GetDirectoryName(files[0]) ?? Folder;
        if (string.IsNullOrWhiteSpace(OutputFolder))
            OutputFolder = Path.Combine(Folder, "_优化");
        SummaryText = $"已选择 {files.Count} 张图片。点击「只读扫描并预览」查看调整结果。";
        RaiseCommandStates();
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
        BusyText = "正在读取图片信息…";
        HasResult = false;
        ClearPreview();
        try
        {
            var sources = _selectedFiles.Count > 0
                ? _selectedFiles.ToList()
                : await Task.Run(() => _planner.FindImages(Folder, IncludeSubfolders));
            var plan = await Task.Run(() => _planner.Plan(sources, BuildOptions()));
            _plan = plan;
            var options = BuildOptions();
            foreach (var item in plan.Items)
            {
                Items.Add(new ImageBatchRow
                {
                    Item = item,
                    AdjustedPreview = BuildAdjustedPreview(item, options)
                });
            }
            HasPreview = plan.Items.Count > 0;
            SummaryText = plan.Items.Count == 0
                ? plan.Error ?? "没有找到可处理的图片。"
                : $"找到 {plan.Items.Count} 张图片；跳过 {plan.SkippedCount} 个无法读取的文件。";
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
                "执行图片处理",
                $"将输出 {_plan.Items.Count} 张图片到：{OutputFolder}\n" +
                "不会覆盖已有文件；输出可在回退中心删除。是否继续？"))
        {
            return;
        }

        IsBusy = true;
        IsProgressIndeterminate = false;
        ProgressValue = 0;
        BusyText = "正在处理图片…";
        try
        {
            var plan = _plan;
            var options = BuildOptions();
            var progress = new Progress<OperationProgress>(value =>
            {
                ProgressValue = value.Total == 0 ? 0 : value.Completed * 100.0 / value.Total;
                BusyText = $"{value.Stage} · {value.CurrentFile}";
            });
            var result = await Task.Run(() => _executor.Execute(plan, options, progress));
            ResultIsSuccess = result.Success;
            if (result.Success) { _resultTimer.Stop(); _resultTimer.Start(); }
            ResultTitle = result.Success ? "图片处理完成" : "图片处理未完成";
            ResultMessage = result.Message;
            HasResult = true;
            if (result.Success)
            {
                SummaryText = "图片处理已完成；预览列表保留，可在回退中心删除本次输出。";
            }
        }
        catch (Exception ex)
        {
            ResultIsSuccess = false;
            ResultTitle = "图片处理失败";
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

    private ImageBatchOptions BuildOptions()
    {
        return new ImageBatchOptions
        {
            OutputFolder = string.IsNullOrWhiteSpace(OutputFolder) ? Folder : OutputFolder,
            Suffix = string.IsNullOrWhiteSpace(Suffix) ? "_优化" : Suffix,
            ResizeMode = SelectedResizeMode.Mode,
            SizeValue = SizeValue,
            Percent = Percent,
            Rotation = SelectedRotation.Angle,
            Format = SelectedFormat.Format,
            JpegQuality = JpegQuality,
            WatermarkText = WatermarkText,
            StripMetadata = StripMetadata
        };
    }

    private static ImageSource? BuildAdjustedPreview(ImageBatchPlanItem item, ImageBatchOptions options)
    {
        try
        {
            var source = new BitmapImage();
            source.BeginInit();
            source.CacheOption = BitmapCacheOption.OnLoad;
            source.UriSource = new Uri(item.SourcePath, UriKind.Absolute);
            source.DecodePixelWidth = Math.Max(1, Math.Min(480, item.TargetWidth));
            source.EndInit();
            source.Freeze();

            double scale = Math.Min(1.0, Math.Min(360.0 / Math.Max(1, item.TargetWidth), 240.0 / Math.Max(1, item.TargetHeight)));
            int width = Math.Max(1, (int)Math.Round(item.TargetWidth * scale));
            int height = Math.Max(1, (int)Math.Round(item.TargetHeight * scale));
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawImage(source, new Rect(0, 0, width, height));
                if (!string.IsNullOrWhiteSpace(options.WatermarkText))
                {
                    double fontSize = Math.Max(8, width / 32.0);
                    var text = new FormattedText(
                        options.WatermarkText,
                        System.Globalization.CultureInfo.CurrentUICulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Microsoft YaHei"),
                        fontSize,
                        Brushes.White,
                        1.0);
                    dc.DrawText(text, new Point(Math.Max(4, width - text.Width - 8), Math.Max(4, height - text.Height - 8)));
                }
            }

            var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);
            target.Freeze();
            return target;
        }
        catch
        {
            return null;
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
