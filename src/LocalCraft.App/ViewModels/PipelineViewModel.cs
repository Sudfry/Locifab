using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalCraft.App.Infrastructure;
using LocalCraft.App.Services;
using LocalCraft.Conversion.Engines;
using LocalCraft.Conversion.Formats;
using LocalCraft.Conversion.Images;
using LocalCraft.Conversion.Metadata;
using LocalCraft.Conversion.Operations;
using LocalCraft.Conversion.Planning;
using LocalCraft.Core.Archives;
using LocalCraft.Core.Backup;
using LocalCraft.Core.Classify;
using LocalCraft.Core.Cleanup;
using LocalCraft.Core.Files;
using LocalCraft.Core.Journal;
using LocalCraft.Core.Operations;
using LocalCraft.Core.Rename;
using LocalCraft.Core.Reports;
using LocalCraft.Core.Tables;
using LocalCraft.Core.TextReplacement;

namespace LocalCraft.App.ViewModels;

public enum PipelineStepKind
{
    Archive,
    Report,
    Metadata,
    Checksum,
    Rename,
    TextReplace,
    ImageBatch,
    Cleanup,
    Backup,
    Classify,
    FileState,
    Template,
    ArchiveExtract,
    FileNameClean,
    RuleCleanup,
    Convert,
    SplitMerge,
    Dedup,
    Table,
    PhotoDedup,
    SpaceScan,
    ImageTarget,
    ImageCheck,
    EmbeddedImages,
    Shortcuts,
    TagFiles
}

public sealed class PipelineStepKindOption
{
    public PipelineStepKind Kind { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Hint { get; init; } = string.Empty;
    public override string ToString() => Name;
}

public sealed class PipelineStepData
{
    public string Kind { get; set; } = nameof(PipelineStepKind.Archive);
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string Text1 { get; set; } = string.Empty;
    public string Text2 { get; set; } = string.Empty;
    public string Text3 { get; set; } = string.Empty;
    public int Number1 { get; set; }
    public int Number2 { get; set; }
    public string Choice { get; set; } = string.Empty;
    public bool Bool1 { get; set; }
    public bool Bool2 { get; set; }
    public string TemplateName { get; set; } = string.Empty;
}

public sealed class PipelineStep : ViewModelBase
{
    private bool _enabled = true;
    private string _text1 = string.Empty;
    private string _text2 = string.Empty;
    private string _text3 = string.Empty;
    private int _number1;
    private int _number2;
    private string _choice = string.Empty;
    private bool _bool1;
    private bool _bool2;
    private string _templateName = string.Empty;

    public PipelineStep(PipelineStepKind kind, Action<PipelineStep> remove, Action<PipelineStep, int> move)
    {
        Kind = kind;
        RemoveCommand = new RelayCommand(() => remove(this));
        MoveUpCommand = new RelayCommand(() => move(this, -1));
        MoveDownCommand = new RelayCommand(() => move(this, 1));
        ApplyDefaults();
    }

    public PipelineStepKind Kind { get; }
    public string KindName => Kind switch
    {
        PipelineStepKind.Archive => "ZIP 压缩",
        PipelineStepKind.Report => "清单报告",
        PipelineStepKind.Metadata => "元数据清理",
        PipelineStepKind.Checksum => "SHA-256 校验",
        PipelineStepKind.Rename => "批量重命名",
        PipelineStepKind.TextReplace => "文本替换",
        PipelineStepKind.ImageBatch => "图片批处理",
        PipelineStepKind.Cleanup => "清理空目录",
        PipelineStepKind.Backup => "备份副本",
        PipelineStepKind.Classify => "分类移动",
        PipelineStepKind.FileState => "时间与属性",
        PipelineStepKind.Template => "目录模板",
        PipelineStepKind.ArchiveExtract => "ZIP 解压",
        PipelineStepKind.FileNameClean => "文件名清洗",
        PipelineStepKind.RuleCleanup => "规则清理",
        PipelineStepKind.Convert => "格式转换",
        PipelineStepKind.SplitMerge => "拆分与合并",
        PipelineStepKind.Dedup => "内容去重",
        PipelineStepKind.Table => "表格汇总",
        PipelineStepKind.PhotoDedup => "相似照片",
        PipelineStepKind.SpaceScan => "空间分析",
        PipelineStepKind.ImageTarget => "图片目标压缩",
        PipelineStepKind.ImageCheck => "图片完整性",
        PipelineStepKind.EmbeddedImages => "内嵌图片提取",
        PipelineStepKind.Shortcuts => "批量快捷方式",
        PipelineStepKind.TagFiles => "标签备注",
        _ => Kind.ToString()
    };

    public string Name => KindName;
    public string Description => Kind switch
    {
        PipelineStepKind.Archive => "压缩工作目录，或解压其中的 ZIP。",
        PipelineStepKind.Report => "生成 CSV/HTML 清单，可选文件哈希。",
        PipelineStepKind.Metadata => "生成去掉作者/EXIF 等元数据的副本。",
        PipelineStepKind.Checksum => "生成 SHA-256 校验清单。",
        PipelineStepKind.Rename => "按前缀、后缀和序号批量改名。",
        PipelineStepKind.TextReplace => "查找并替换文本内容。",
        PipelineStepKind.ImageBatch => "缩放/压缩图片并输出到子目录。",
        PipelineStepKind.Cleanup => "删除工作副本中的空目录和 0 字节文件。",
        PipelineStepKind.Backup => "把工作目录只增不删地复制到输出子目录。",
        PipelineStepKind.Classify => "按类型/年份/前缀移动文件。",
        PipelineStepKind.FileState => "批量修改工作副本的时间和读写属性。",
        PipelineStepKind.Template => "按模板在工作副本中创建目录结构。",
        PipelineStepKind.ArchiveExtract => "把工作目录内的 ZIP 解压到输出子文件夹，可带密码。",
        PipelineStepKind.FileNameClean => "清理非法字符、连续空格和全角字符。",
        PipelineStepKind.RuleCleanup => "按扩展名、天数、大小把文件移动到隔离区。",
        PipelineStepKind.Convert => "把工作目录中的文档或图片转换为统一格式。",
        PipelineStepKind.SplitMerge => "按大小拆分大文件，或把分片重新合并。",
        PipelineStepKind.Dedup => "按 SHA-256 找出完全相同文件，重复项移入隔离区。",
        PipelineStepKind.Table => "把工作目录中的 CSV/XLSX 合并为一张 CSV 汇总表。",
        PipelineStepKind.PhotoDedup => "感知哈希找出相似照片，重复项移入隔离区。",
        PipelineStepKind.SpaceScan => "统计工作目录空间占用并输出 CSV。",
        PipelineStepKind.ImageTarget => "把图片压缩到指定 KB 上限。",
        PipelineStepKind.ImageCheck => "检查损坏图片并移入隔离区。",
        PipelineStepKind.EmbeddedImages => "提取 Office/PDF 内嵌图片。",
        PipelineStepKind.Shortcuts => "为工作目录项目生成快捷方式。",
        PipelineStepKind.TagFiles => "为工作目录文件写入标签与备注。",
        _ => string.Empty
    };

    public string Label1 => Kind switch
    {
        PipelineStepKind.Archive => "ZIP 文件名 / 解压子文件夹",
        PipelineStepKind.Metadata => "输出子文件夹",
        PipelineStepKind.Checksum => "清单文件名",
        PipelineStepKind.Rename => "文件名前缀",
        PipelineStepKind.TextReplace => "查找文字",
        PipelineStepKind.ImageBatch => "输出子文件夹",
        PipelineStepKind.Backup => "备份子文件夹",
        PipelineStepKind.FileState => "修改时间（yyyy-MM-dd HH:mm:ss）",
        PipelineStepKind.Template => "模板（用 | 分行，/ 结尾为目录）",
        PipelineStepKind.ArchiveExtract => "输出子文件夹",
        PipelineStepKind.FileNameClean => "扩展名（留空=全部）",
        PipelineStepKind.RuleCleanup => "扩展名（分号分隔，留空=全部）",
        PipelineStepKind.Convert => "输出子文件夹",
        PipelineStepKind.SplitMerge => "输出子文件夹",
        PipelineStepKind.Dedup => "隔离区子文件夹",
        PipelineStepKind.Table => "输出 CSV 文件名",
        PipelineStepKind.PhotoDedup => "隔离区子文件夹",
        PipelineStepKind.SpaceScan => "CSV 文件名",
        PipelineStepKind.ImageTarget => "输出子文件夹",
        PipelineStepKind.ImageCheck => "隔离子文件夹",
        PipelineStepKind.EmbeddedImages => "输出子文件夹",
        PipelineStepKind.Shortcuts => "快捷方式子文件夹",
        PipelineStepKind.TagFiles => "标签",
        _ => string.Empty
    };
    public bool ShowText1 => Label1.Length > 0;

    public string Label2 => Kind switch
    {
        PipelineStepKind.Rename => "文件名后缀",
        PipelineStepKind.TextReplace => "替换为",
        PipelineStepKind.Archive => "ZIP 密码（留空不加密）",
        PipelineStepKind.ArchiveExtract => "ZIP 密码（没有密码留空）",
        PipelineStepKind.SplitMerge => "合并匹配（如 *.part*）",
        PipelineStepKind.TagFiles => "备注",
        _ => string.Empty
    };
    public bool ShowText2 => Label2.Length > 0;

    public string Label3 => Kind == PipelineStepKind.TextReplace ? "扩展名（留空=文本）" : string.Empty;
    public bool ShowText3 => Label3.Length > 0;

    public string Label4 => Kind switch
    {
        PipelineStepKind.Rename => "起始序号",
        PipelineStepKind.ImageBatch => "最大边长",
        PipelineStepKind.RuleCleanup => "大于天数（0=不限）",
        _ => string.Empty
    };
    public bool ShowNumber1 => Label4.Length > 0;

    public string Label5 => Kind switch
    {
        PipelineStepKind.Rename => "序号位数",
        PipelineStepKind.ImageBatch => "JPEG 质量",
        PipelineStepKind.RuleCleanup => "小于 KB（0=不限）",
        PipelineStepKind.SplitMerge => "拆分大小 MB",
        PipelineStepKind.PhotoDedup => "相似度阈值 %",
        PipelineStepKind.ImageTarget => "目标 KB",
        _ => string.Empty
    };
    public bool ShowNumber2 => Label5.Length > 0;

    public bool ShowChoice => Kind is PipelineStepKind.Report or PipelineStepKind.Checksum
        or PipelineStepKind.ImageBatch or PipelineStepKind.Classify or PipelineStepKind.Convert
        or PipelineStepKind.SplitMerge or PipelineStepKind.Archive;
    public bool ShowBool1 => Kind is PipelineStepKind.Archive or PipelineStepKind.Report
        or PipelineStepKind.Rename or PipelineStepKind.Cleanup or PipelineStepKind.FileState;
    public bool ShowBool2 => Kind is PipelineStepKind.Cleanup or PipelineStepKind.FileState;
    public string Bool1Label => Kind switch
    {
        PipelineStepKind.Archive => "包含子文件夹",
        PipelineStepKind.Rename => "添加序号",
        PipelineStepKind.Cleanup => "空目录",
        PipelineStepKind.FileState => "设置只读",
        _ => "清单包含 SHA-256"
    };
    public string Bool2Label => Kind == PipelineStepKind.FileState ? "设置隐藏" : "0 字节文件";

    public IReadOnlyList<string> ChoiceOptions => Kind switch
    {
        PipelineStepKind.Report => new[] { "Csv", "Html" },
        PipelineStepKind.Checksum => new[] { "SHA-256" },
        PipelineStepKind.ImageBatch => new[] { "Keep", "Jpg", "Png" },
        PipelineStepKind.Classify => new[] { "ByType", "ByYear", "ByMonth", "ByNamePrefix" },
        PipelineStepKind.Convert => new[] { "pdf", "docx", "markdown", "txt", "html", "jpg", "png" },
        PipelineStepKind.SplitMerge => new[] { "Split", "Merge" },
        PipelineStepKind.Archive => new[] { "Compress", "Extract" },
        _ => Array.Empty<string>()
    };

    public string TemplateName
    {
        get => _templateName;
        set
        {
            if (!SetProperty(ref _templateName, value ?? string.Empty)) return;
            OnPropertyChanged(nameof(HasTemplate));
            OnPropertyChanged(nameof(TemplateLabel));
            OnPropertyChanged(nameof(ShowParameters));
        }
    }

    public bool HasTemplate => !string.IsNullOrWhiteSpace(_templateName);
    public bool ShowParameters => string.IsNullOrWhiteSpace(_templateName);
    public string TemplateLabel => $"模板：{_templateName}";

    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }
    public string Text1 { get => _text1; set => SetProperty(ref _text1, value ?? string.Empty); }
    public string Text2 { get => _text2; set => SetProperty(ref _text2, value ?? string.Empty); }
    public string Text3 { get => _text3; set => SetProperty(ref _text3, value ?? string.Empty); }
    public int Number1 { get => _number1; set => SetProperty(ref _number1, Math.Max(0, value)); }
    public int Number2 { get => _number2; set => SetProperty(ref _number2, Math.Max(1, value)); }
    public string Choice { get => _choice; set => SetProperty(ref _choice, value ?? string.Empty); }
    public bool Bool1 { get => _bool1; set => SetProperty(ref _bool1, value); }
    public bool Bool2 { get => _bool2; set => SetProperty(ref _bool2, value); }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }

    public PipelineStepData ToData() => new()
    {
        Kind = Kind.ToString(), Name = Name, Enabled = Enabled,
        Text1 = Text1, Text2 = Text2, Text3 = Text3,
        Number1 = Number1, Number2 = Number2,
        Choice = Choice, Bool1 = Bool1, Bool2 = Bool2, TemplateName = TemplateName
    };

    public static PipelineStep FromData(PipelineStepData data, Action<PipelineStep> remove, Action<PipelineStep, int> move)
    {
        Enum.TryParse(data.Kind, out PipelineStepKind kind);
        var step = new PipelineStep(kind, remove, move)
        {
            Enabled = data.Enabled,
            Text1 = data.Text1,
            Text2 = data.Text2,
            Text3 = data.Text3,
            Number1 = data.Number1,
            Number2 = data.Number2,
            Choice = data.Choice,
            Bool1 = data.Bool1,
            Bool2 = data.Bool2,
            TemplateName = data.TemplateName
        };
        return step;
    }

    private void ApplyDefaults()
    {
        switch (Kind)
        {
            case PipelineStepKind.Archive:
                Text1 = "Locifab-归档.zip";
                Choice = "Compress";
                Bool1 = true;
                break;
            case PipelineStepKind.Report:
                Choice = "Csv";
                Bool1 = true;
                break;
            case PipelineStepKind.Metadata:
                Text1 = "去元数据";
                break;
            case PipelineStepKind.Checksum:
                Text1 = "Locifab-SHA256.txt";
                Choice = "SHA-256";
                break;
            case PipelineStepKind.Rename:
                Number1 = 1;
                Number2 = 3;
                Text3 = "_";
                break;
            case PipelineStepKind.TextReplace:
                Choice = "txt,md,csv,json";
                break;
            case PipelineStepKind.ImageBatch:
                Text1 = "图片输出";
                Number1 = 1920;
                Number2 = 88;
                Choice = "Keep";
                break;
            case PipelineStepKind.Cleanup:
                Bool1 = true;
                Bool2 = true;
                break;
            case PipelineStepKind.Backup:
                Text1 = "备份";
                break;
            case PipelineStepKind.Classify:
                Choice = "ByType";
                break;
            case PipelineStepKind.FileState:
                Bool1 = false;
                Bool2 = false;
                break;
            case PipelineStepKind.Template:
                Text1 = "文档/|素材/|输出/|说明.md";
                break;
            case PipelineStepKind.ArchiveExtract:
                Text1 = "解压结果";
                break;
            case PipelineStepKind.FileNameClean:
                break;
            case PipelineStepKind.RuleCleanup:
                Text1 = ".tmp;.log;.bak";
                Number1 = 30;
                Number2 = 1;
                break;
            case PipelineStepKind.Convert:
                Text1 = "转换结果";
                Choice = "pdf";
                break;
            case PipelineStepKind.SplitMerge:
                Text1 = "拆分合并结果";
                Text2 = "*.part*";
                Choice = "Split";
                Number1 = 10;
                break;
            case PipelineStepKind.Dedup:
                Text1 = "重复隔离";
                break;
            case PipelineStepKind.Table:
                Text1 = "表格汇总.csv";
                break;
            case PipelineStepKind.PhotoDedup:
                Text1 = "相似照片隔离";
                Number1 = 92;
                break;
            case PipelineStepKind.SpaceScan:
                Text1 = "空间分析.csv";
                break;
            case PipelineStepKind.ImageTarget:
                Text1 = "图片压缩";
                Number1 = 500;
                break;
            case PipelineStepKind.ImageCheck:
                Text1 = "损坏图片隔离";
                break;
            case PipelineStepKind.EmbeddedImages:
                Text1 = "内嵌图片";
                break;
            case PipelineStepKind.Shortcuts:
                Text1 = "快捷方式";
                break;
            case PipelineStepKind.TagFiles:
                Text1 = "已处理";
                Text2 = "流水线处理";
                break;
        }
    }
}

public sealed class ToolImportGroup
{
    public string ToolKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public ObservableCollection<ToolPreset> Templates { get; } = new();
    public override string ToString() => DisplayName;
}

public sealed class PipelinePreset
{
    public string Name { get; set; } = string.Empty;
    public string SourceFolder { get; set; } = string.Empty;
    public string OutputFolder { get; set; } = string.Empty;
    public List<PipelineStepData> Steps { get; set; } = new();
    public override string ToString() => Name;
}

public sealed class PipelineViewModel : ViewModelBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly JournalRepository _repository;
    private readonly ToolPresetStore _presetStore = new();
    private readonly IDialogService _dialogService;
    private readonly ArchivePlanner _archivePlanner = new();
    private readonly ArchiveExecutor _archiveExecutor;
    private readonly MetadataCleaner _metadataCleaner;
    private readonly ConversionPlanner _conversionPlanner;
    private readonly ConversionExecutor _conversionExecutor;
    private readonly SplitMergeService _splitMerge;
    private readonly TableExporter _tableExporter;

    private string _sourceFolder = string.Empty;
    private string _outputFolder = string.Empty;
    private string _presetName = "我的流水线";
    private PipelinePreset? _selectedPreset;
    private PipelineStepKindOption _selectedStepKind;
    private PipelineStep? _selectedStep;
    private bool _isBusy;
    private string _busyText = string.Empty;
    private string _summaryText = "添加步骤，设置每一步的参数，保存为模板即可重复运行。";
    private bool _hasPreview;
    private bool _hasResult;
    private bool _resultIsSuccess;
    private string _resultTitle = string.Empty;
    private string _resultMessage = string.Empty;

    public PipelineViewModel(JournalRepository repository, IDialogService dialogService)
    {
        _repository = repository;
        _dialogService = dialogService;
        _archiveExecutor = new ArchiveExecutor(repository);
        _metadataCleaner = new MetadataCleaner(repository);
        _splitMerge = new SplitMergeService(repository);
        _tableExporter = new TableExporter(repository);
        var engines = new ConversionEngineCatalog();
        _conversionPlanner = new ConversionPlanner(engines);
        _conversionExecutor = new ConversionExecutor(repository, engines);
        PresetPath = Path.Combine(repository.DataRoot, "pipeline-presets.json");

        StepKinds = new ObservableCollection<PipelineStepKindOption>
        {
            new() { Kind = PipelineStepKind.Archive, Name = "ZIP 处理", Hint = "压缩 / 解压" },
            new() { Kind = PipelineStepKind.Rename, Name = "批量重命名", Hint = "前缀/后缀/序号" },
            new() { Kind = PipelineStepKind.TextReplace, Name = "文本替换", Hint = "查找并替换" },
            new() { Kind = PipelineStepKind.Cleanup, Name = "清理空目录", Hint = "空目录/0 字节" },
            new() { Kind = PipelineStepKind.Convert, Name = "格式转换", Hint = "统一文档/图片格式" },
            new() { Kind = PipelineStepKind.ImageBatch, Name = "图片批处理", Hint = "缩放/压缩" },
            new() { Kind = PipelineStepKind.Dedup, Name = "内容去重", Hint = "SHA-256 精确隔离" },
            new() { Kind = PipelineStepKind.PhotoDedup, Name = "相似照片", Hint = "感知哈希隔离" },
            new() { Kind = PipelineStepKind.ImageTarget, Name = "图片目标压缩", Hint = "压缩到指定 KB" },
            new() { Kind = PipelineStepKind.ImageCheck, Name = "图片完整性", Hint = "损坏隔离" },
            new() { Kind = PipelineStepKind.EmbeddedImages, Name = "内嵌图片提取", Hint = "Office/PDF" },
            new() { Kind = PipelineStepKind.SplitMerge, Name = "拆分与合并", Hint = "大文件拆分/分片合并" },
            new() { Kind = PipelineStepKind.Report, Name = "清单报告", Hint = "CSV / HTML" },
            new() { Kind = PipelineStepKind.Metadata, Name = "元数据清理", Hint = "生成隐私副本" },
            new() { Kind = PipelineStepKind.Checksum, Name = "SHA-256 校验", Hint = "生成哈希清单" },
            new() { Kind = PipelineStepKind.Backup, Name = "备份副本", Hint = "只增不删" },
            new() { Kind = PipelineStepKind.Classify, Name = "分类移动", Hint = "类型/年份" },
            new() { Kind = PipelineStepKind.FileState, Name = "时间与属性", Hint = "时间/只读/隐藏" },
            new() { Kind = PipelineStepKind.Template, Name = "目录模板", Hint = "批量建目录" }
        };
        _selectedStepKind = StepKinds[0];

        BrowseSourceCommand = new RelayCommand(BrowseSource, () => !IsBusy);
        BrowseOutputCommand = new RelayCommand(BrowseOutput, () => !IsBusy);
        AddStepCommand = new RelayCommand(AddStep, () => !IsBusy);
        PreviewCommand = new RelayCommand(BuildPreview, () => !IsBusy && Directory.Exists(SourceFolder));
        ExecuteCommand = new AsyncRelayCommand(ExecuteAsync, () => CanExecute);
        SavePresetCommand = new RelayCommand(SavePreset, () => !IsBusy && !string.IsNullOrWhiteSpace(PresetName));
        DeletePresetCommand = new RelayCommand(DeletePreset, () => !IsBusy && SelectedPreset is not null);
        OpenUndoCenterCommand = new RelayCommand(() => UndoCenterRequested?.Invoke(this, EventArgs.Empty));
        LoadPresets();
        LoadImportablePresets();
        if (Presets.Count > 0) SelectedPreset = Presets[0];
        ImportPresetCommand = new RelayCommand(() =>
        {
            if (SelectedImportPreset is not null) AddPresetAsStep(SelectedImportPreset);
        }, () => SelectedImportPreset is not null);
    }

    public event EventHandler? UndoCenterRequested;

    public ObservableCollection<PipelineStep> Steps { get; } = new();
    public ObservableCollection<PipelinePreset> Presets { get; } = new();
    public ObservableCollection<string> PreviewSteps { get; } = new();
    public ObservableCollection<PipelineStepKindOption> StepKinds { get; }
    public RelayCommand BrowseSourceCommand { get; }
    public RelayCommand BrowseOutputCommand { get; }
    public RelayCommand AddStepCommand { get; }
    public RelayCommand PreviewCommand { get; }
    public AsyncRelayCommand ExecuteCommand { get; }
    public RelayCommand SavePresetCommand { get; }
    public RelayCommand DeletePresetCommand { get; }
    public RelayCommand OpenUndoCenterCommand { get; }
    public ObservableCollection<ToolImportGroup> ImportToolGroups { get; } = new();
    public ObservableCollection<ToolPreset> ImportToolPresets { get; } = new();
    public RelayCommand ImportPresetCommand { get; }
    private ToolImportGroup? _selectedImportTool;
    public ToolImportGroup? SelectedImportTool
    {
        get => _selectedImportTool;
        set
        {
            if (!SetProperty(ref _selectedImportTool, value)) return;
            ImportToolPresets.Clear();
            if (value is not null) foreach (var preset in value.Templates) ImportToolPresets.Add(preset);
            SelectedImportPreset = ImportToolPresets.FirstOrDefault();
        }
    }
    private ToolPreset? _selectedImportPreset;
    public ToolPreset? SelectedImportPreset
    {
        get => _selectedImportPreset;
        set
        {
            if (!SetProperty(ref _selectedImportPreset, value)) return;
            ImportPresetCommand?.RaiseCanExecuteChanged();
        }
    }
    public string PresetPath { get; }

    public PipelineStepKindOption SelectedStepKind
    {
        get => _selectedStepKind;
        set { if (value is not null) SetProperty(ref _selectedStepKind, value); }
    }

    public PipelineStep? SelectedStep
    {
        get => _selectedStep;
        set => SetProperty(ref _selectedStep, value);
    }

    public string SourceFolder
    {
        get => _sourceFolder;
        set { if (SetProperty(ref _sourceFolder, value)) { ClearPreview(); PreviewCommand.RaiseCanExecuteChanged(); } }
    }

    public string OutputFolder
    {
        get => _outputFolder;
        set { if (SetProperty(ref _outputFolder, value)) ClearPreview(); }
    }

    public string PresetName
    {
        get => _presetName;
        set { if (SetProperty(ref _presetName, value)) SavePresetCommand.RaiseCanExecuteChanged(); }
    }

    public PipelinePreset? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (!SetProperty(ref _selectedPreset, value) || value is null) return;
            PresetName = value.Name;
            if (!string.IsNullOrWhiteSpace(value.SourceFolder)) SourceFolder = value.SourceFolder;
            if (!string.IsNullOrWhiteSpace(value.OutputFolder)) OutputFolder = value.OutputFolder;
            Steps.Clear();
            foreach (var data in value.Steps ?? new List<PipelineStepData>())
                Steps.Add(PipelineStep.FromData(data, RemoveStep, MoveStep));
            ClearPreview();
            DeletePresetCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set { if (!SetProperty(ref _isBusy, value)) return; OnPropertyChanged(nameof(IsIdle)); RaiseCommands(); }
    }

    public bool IsIdle => !IsBusy;
    public string BusyText { get => _busyText; private set => SetProperty(ref _busyText, value); }
    public string SummaryText { get => _summaryText; private set => SetProperty(ref _summaryText, value); }
    public bool HasPreview { get => _hasPreview; private set { if (!SetProperty(ref _hasPreview, value)) return; OnPropertyChanged(nameof(CanExecute)); ExecuteCommand.RaiseCanExecuteChanged(); } }
    public bool CanExecute => !IsBusy && HasPreview;
    public bool HasResult { get => _hasResult; private set => SetProperty(ref _hasResult, value); }
    public bool ResultIsSuccess { get => _resultIsSuccess; private set => SetProperty(ref _resultIsSuccess, value); }
    public string ResultTitle { get => _resultTitle; private set => SetProperty(ref _resultTitle, value); }
    public string ResultMessage { get => _resultMessage; private set => SetProperty(ref _resultMessage, value); }

    private void DetachPreset()
    {
        if (_selectedPreset is null) return;
        _selectedPreset = null;
        OnPropertyChanged(nameof(SelectedPreset));
        PresetName = "自定义流水线";
        DeletePresetCommand.RaiseCanExecuteChanged();
    }

    public void LoadImportablePresets()
    {
        ImportToolGroups.Clear();
        _presetStore.Load();
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["rename"] = "批量重命名", ["convert"] = "格式转换", ["findrep"] = "文本替换",
            ["imgcompress"] = "图片压缩", ["imgcheck"] = "图片检查", ["embedimg"] = "内嵌图片",
            ["archive"] = "压缩与解压", ["metadata"] = "元数据清理", ["checksum"] = "校验与清单",
            ["classify"] = "扫描与分类", ["template"] = "目录模板", ["report"] = "文件夹清单",
            ["filestate"] = "时间与属性", ["splitmerge"] = "拆分与合并", ["cleanup"] = "空目录清理",
            ["backup"] = "备份同步", ["dedup"] = "内容去重", ["photos"] = "相似照片",
            ["images"] = "图片批处理"
        };
        foreach (var group in _presetStore.All
                     .Where(x => names.ContainsKey(x.ToolKey))
                     .GroupBy(x => x.ToolKey, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(x => names[x.Key]))
        {
            var item = new ToolImportGroup { ToolKey = group.Key, DisplayName = names[group.Key] };
            foreach (var preset in group.OrderBy(x => x.Name)) item.Templates.Add(preset);
            ImportToolGroups.Add(item);
        }
        SelectedImportTool = ImportToolGroups.FirstOrDefault();
    }

    public void AddPresetAsStep(ToolPreset preset)
    {
        if (preset.Step is not null)
        {
            DetachPreset();
            var snapshot = PipelineStep.FromData(preset.Step, RemoveStep, MoveStep);
            snapshot.TemplateName = preset.Name;
            Steps.Add(snapshot);
            SelectedStep = snapshot;
            SummaryText = $"已导入配置“{preset.Name}”作为流水线步骤；参数已按保存时设置还原。";
            RaiseCommands();
            return;
        }
        PipelineStepKind? kind = preset.ToolKey.ToLowerInvariant() switch
        {
            "rename" => PipelineStepKind.Rename,
            "convert" => PipelineStepKind.Convert,
            "findrep" => PipelineStepKind.TextReplace,
            "imgcompress" => PipelineStepKind.ImageTarget,
            "imgcheck" => PipelineStepKind.ImageCheck,
            "embedimg" => PipelineStepKind.EmbeddedImages,
            "archive" => PipelineStepKind.Archive,
            "metadata" => PipelineStepKind.Metadata,
            "checksum" => PipelineStepKind.Checksum,
            "classify" => PipelineStepKind.Classify,
            "template" => PipelineStepKind.Template,
            "report" => PipelineStepKind.Report,
            "filestate" => PipelineStepKind.FileState,
            "splitmerge" => PipelineStepKind.SplitMerge,
            "cleanup" => PipelineStepKind.Cleanup,
            "backup" => PipelineStepKind.Backup,
            "dedup" => PipelineStepKind.Dedup,
            "photos" => PipelineStepKind.PhotoDedup,
            "images" => PipelineStepKind.ImageBatch,
            "table" => PipelineStepKind.Table,
            _ => null
        };
        if (kind is null) return;
        DetachPreset();
        var step = new PipelineStep(kind.Value, RemoveStep, MoveStep) { TemplateName = preset.Name };
        if (preset.Values.TryGetValue("OutputFolder", out var output) && !string.IsNullOrWhiteSpace(output))
            step.Text1 = Path.GetFileName(output.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        else if (preset.Values.TryGetValue("OutputDirectory", out output) && !string.IsNullOrWhiteSpace(output))
            step.Text1 = Path.GetFileName(output.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (preset.Values.TryGetValue("TargetKb", out var target) && int.TryParse(target, out int kb))
            step.Number1 = kb;
        if (preset.Values.TryGetValue("Password", out var password)) step.Text2 = password;
        ApplyPresetValues(step, preset);
        Steps.Add(step);
        SelectedStep = step;
        SummaryText = $"已导入配置“{preset.Name}”作为流水线步骤；可继续调整参数。";
        RaiseCommands();
    }

    private static void ApplyPresetValues(PipelineStep step, ToolPreset preset)
    {
        string Get(string key) => preset.Values.TryGetValue(key, out var value) ? value : string.Empty;
        switch (step.Kind)
        {
            case PipelineStepKind.Rename:
                if (Get("Prefix") is { Length: > 0 } prefix) step.Text1 = prefix;
                if (Get("Suffix") is { Length: > 0 } suffix) step.Text2 = suffix;
                if (int.TryParse(Get("StartNumberText"), out int start)) step.Number1 = start;
                if (int.TryParse(Get("NumberPaddingText"), out int digits)) step.Number2 = digits;
                if (Get("NumberSeparator") is { Length: > 0 } separator) step.Text3 = separator;
                if (bool.TryParse(Get("UseNumbering"), out bool useNumbering)) step.Bool1 = useNumbering;
                break;
            case PipelineStepKind.ImageTarget:
                if (double.TryParse(Get("TargetKb"), out double target)) step.Number1 = Math.Max(1, (int)Math.Round(target));
                break;
            case PipelineStepKind.ImageBatch:
                if (Get("OutputFolder") is { Length: > 0 } imageOutput)
                    step.Text1 = Path.GetFileName(imageOutput.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                break;
            case PipelineStepKind.Template:
                if (Get("Template") is { Length: > 0 } template) step.Text1 = template;
                break;
            case PipelineStepKind.Convert:
                if (Get("OutputDirectory") is { Length: > 0 } convertOutput)
                    step.Text1 = Path.GetFileName(convertOutput.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                break;
        }
    }

    public void AddQuickStep(PipelineStepKind kind)
    {
        DetachPreset();
        Steps.Clear();
        var step = new PipelineStep(kind, RemoveStep, MoveStep);
        Steps.Add(step);
        SelectedStep = step;
        HasPreview = false;
        SummaryText = "已从工具栏载入单步流水线；选择源/输出后即可预览执行。";
        RaiseCommands();
    }

    private void AddStep()
    {
        if (!IsBusy)
        {
            DetachPreset();
            Steps.Add(new PipelineStep(SelectedStepKind.Kind, RemoveStep, MoveStep));
            ClearPreview();
        }
    }

    private void RemoveStep(PipelineStep step)
    {
        DetachPreset();
        Steps.Remove(step);
        ClearPreview();
    }

    private void MoveStep(PipelineStep step, int delta)
    {
        int index = Steps.IndexOf(step);
        int target = index + delta;
        if (index < 0 || target < 0 || target >= Steps.Count) return;
        DetachPreset();
        Steps.Move(index, target);
        ClearPreview();
    }

    private void BrowseSource()
    {
        string? picked = _dialogService.PickFolder(Directory.Exists(SourceFolder) ? SourceFolder : null);
        if (picked is null) return;
        SourceFolder = picked;
        if (string.IsNullOrWhiteSpace(OutputFolder)) OutputFolder = DefaultOutput(picked);
    }

    private void BrowseOutput()
    {
        string? picked = _dialogService.PickFolder(Directory.Exists(OutputFolder) ? OutputFolder : null);
        if (picked is not null) OutputFolder = picked;
    }

    private void BuildPreview()
    {
        PreviewSteps.Clear();
        if (!Directory.Exists(SourceFolder))
        {
            SummaryText = "请选择存在的源文件夹。";
            HasPreview = false;
            return;
        }

        var enabled = Steps.Where(x => x.Enabled).ToList();
        if (enabled.Count == 0)
        {
            SummaryText = "请至少添加一个步骤。";
            HasPreview = false;
            return;
        }

        for (int i = 0; i < enabled.Count; i++)
            PreviewSteps.Add($"{i + 1}. {enabled[i].Name} · {StepSummary(enabled[i])}");

        HasPreview = true;
        SummaryText = $"共 {enabled.Count} 个步骤，按顺序执行；修改类步骤会在工作副本上执行，源目录不直接改动。";
    }

    private static string StepSummary(PipelineStep step) => step.Kind switch
    {
        PipelineStepKind.Archive => $"{step.Choice} · {step.Text1}",
        PipelineStepKind.Report => $"{step.Choice}，哈希：{(step.Bool1 ? "是" : "否")}",
        PipelineStepKind.Metadata => step.Text1,
        PipelineStepKind.Checksum => step.Text1,
        PipelineStepKind.Rename => $"前缀“{step.Text1}”，后缀“{step.Text2}”，序号 {step.Number1}",
        PipelineStepKind.TextReplace => $"“{step.Text1}”→“{step.Text2}”",
        PipelineStepKind.ImageBatch => $"{step.Number1}px · 质量 {step.Number2} · {step.Choice}",
        PipelineStepKind.Cleanup => $"空目录：{(step.Bool1 ? "是" : "否")}，0 字节：{(step.Bool2 ? "是" : "否")}",
        PipelineStepKind.Backup => step.Text1,
        PipelineStepKind.Classify => step.Choice,
        PipelineStepKind.FileState => $"时间 {step.Text1}，只读 {(step.Bool1 ? "是" : "否")}，隐藏 {(step.Bool2 ? "是" : "否")}",
        PipelineStepKind.Template => step.Text1,
        PipelineStepKind.ArchiveExtract => $"{step.Text1} · 密码{(string.IsNullOrEmpty(step.Text2) ? "无" : "有")}",
        PipelineStepKind.FileNameClean => $"扩展名：{step.Text1}",
        PipelineStepKind.RuleCleanup => $"{step.Text1} · >{step.Number1}天 · <{step.Number2}KB",
        PipelineStepKind.Convert => $"{step.Choice} · {step.Text1}",
        PipelineStepKind.SplitMerge => $"{step.Choice} · {step.Number1}MB",
        PipelineStepKind.Dedup => step.Text1,
        PipelineStepKind.Table => step.Text1,
        PipelineStepKind.PhotoDedup => $"{step.Number1}% · {step.Text1}",
        PipelineStepKind.SpaceScan => step.Text1,
        PipelineStepKind.ImageTarget => $"{step.Number1} KB · {step.Text1}",
        PipelineStepKind.ImageCheck => step.Text1,
        PipelineStepKind.EmbeddedImages => step.Text1,
        PipelineStepKind.Shortcuts => step.Text1,
        PipelineStepKind.TagFiles => $"{step.Text1} · {step.Text2}",
        _ => string.Empty
    };

    private async Task ExecuteAsync()
    {
        if (!CanExecute) return;
        var enabled = Steps.Where(x => x.Enabled).ToList();
        if (enabled.Count == 0) return;
        if (!_dialogService.Confirm("执行任务流水线",
                $"将按顺序执行 {enabled.Count} 个步骤：\n" + string.Join("\n", PreviewSteps) +
                "\n\n修改类步骤在副本上执行；输出和日志写入本机。是否继续？"))
            return;

        IsBusy = true;
        BusyText = "正在执行流水线…";
        var messages = new List<string>();
        try
        {
            string output = string.IsNullOrWhiteSpace(OutputFolder) ? DefaultOutput(SourceFolder) : OutputFolder;
            Directory.CreateDirectory(output);
            bool modifying = enabled.Any(x => x.Kind is PipelineStepKind.Rename or PipelineStepKind.TextReplace
                or PipelineStepKind.Cleanup or PipelineStepKind.Classify
                or PipelineStepKind.FileState or PipelineStepKind.Template
                or PipelineStepKind.FileNameClean or PipelineStepKind.RuleCleanup
                or PipelineStepKind.Dedup or PipelineStepKind.PhotoDedup
                or PipelineStepKind.ImageCheck or PipelineStepKind.TagFiles);
            if (modifying && IsSubPath(output, SourceFolder))
                throw new InvalidOperationException("输出根目录不能位于源文件夹内部；请选择源文件夹之外的位置。");
            string work = SourceFolder;
            if (modifying)
            {
                work = Path.Combine(output, "_pipeline-work");
                if (Directory.Exists(work)) work = UniqueDirectory(work);
                CopyDirectory(SourceFolder, work);
                messages.Add("已创建源目录工作副本");
            }

            for (int i = 0; i < enabled.Count; i++)
            {
                var step = enabled[i];
                BusyText = $"步骤 {i + 1}/{enabled.Count}：{step.Name}";
                switch (step.Kind)
                {
                    case PipelineStepKind.Archive:
                        if (string.Equals(step.Choice, "Extract", StringComparison.OrdinalIgnoreCase))
                        {
                            await RunArchiveExtract(work, output, step);
                            messages.Add("ZIP 解压");
                        }
                        else
                        {
                            await RunArchive(work, output, step);
                            messages.Add("ZIP 压缩");
                        }
                        break;
                    case PipelineStepKind.Report:
                        await RunReport(work, output, step);
                        messages.Add("清单");
                        break;
                    case PipelineStepKind.Metadata:
                        await RunMetadata(work, output, step);
                        messages.Add("元数据");
                        break;
                    case PipelineStepKind.Checksum:
                        await RunChecksum(work, output, step);
                        messages.Add("SHA-256");
                        break;
                    case PipelineStepKind.Rename:
                        RunRename(work, step);
                        messages.Add("重命名");
                        break;
                    case PipelineStepKind.TextReplace:
                        RunTextReplace(work, step);
                        messages.Add("文本替换");
                        break;
                    case PipelineStepKind.ImageBatch:
                        await RunImageBatch(work, output, step);
                        messages.Add("图片批处理");
                        break;
                    case PipelineStepKind.Cleanup:
                        await RunCleanup(work, step);
                        messages.Add("清理");
                        break;
                    case PipelineStepKind.Backup:
                        await RunBackup(work, output, step);
                        messages.Add("备份");
                        break;
                    case PipelineStepKind.Classify:
                        await RunClassify(work, step);
                        messages.Add("分类移动");
                        break;
                    case PipelineStepKind.FileState:
                        RunFileState(work, step);
                        messages.Add("时间与属性");
                        break;
                    case PipelineStepKind.Template:
                        RunTemplate(work, step);
                        messages.Add("目录模板");
                        break;
                    case PipelineStepKind.ArchiveExtract:
                        await RunArchiveExtract(work, output, step);
                        messages.Add("ZIP 解压");
                        break;
                    case PipelineStepKind.FileNameClean:
                        RunFileNameClean(work, step);
                        messages.Add("文件名清洗");
                        break;
                    case PipelineStepKind.RuleCleanup:
                        RunRuleCleanup(work, output, step);
                        messages.Add("规则清理");
                        break;
                    case PipelineStepKind.Convert:
                        await RunConvert(work, output, step);
                        messages.Add("格式转换");
                        break;
                    case PipelineStepKind.SplitMerge:
                        await RunSplitMerge(work, output, step);
                        messages.Add("拆分合并");
                        break;
                    case PipelineStepKind.Dedup:
                        RunDedup(work, output, step);
                        messages.Add("内容去重");
                        break;
                    case PipelineStepKind.Table:
                        RunTable(work, output, step);
                        messages.Add("表格汇总");
                        break;
                    case PipelineStepKind.PhotoDedup:
                        await RunPhotoDedup(work, output, step);
                        messages.Add("相似照片");
                        break;
                    case PipelineStepKind.SpaceScan:
                        await RunSpaceScan(work, output, step);
                        messages.Add("空间分析");
                        break;
                    case PipelineStepKind.ImageTarget:
                        await RunImageTarget(work, output, step);
                        messages.Add("图片压缩");
                        break;
                    case PipelineStepKind.ImageCheck:
                        await RunImageCheck(work, output, step);
                        messages.Add("图片检查");
                        break;
                    case PipelineStepKind.EmbeddedImages:
                        await RunEmbeddedImages(work, output, step);
                        messages.Add("内嵌图片");
                        break;
                    case PipelineStepKind.Shortcuts:
                        RunShortcuts(work, output, step);
                        messages.Add("快捷方式");
                        break;
                    case PipelineStepKind.TagFiles:
                        RunTagFiles(work, step);
                        messages.Add("标签备注");
                        break;
                }
            }

            ResultIsSuccess = true;
            ResultTitle = "流水线执行完成";
            ResultMessage = string.Join(" · ", messages) + "。输出已写入本地日志。";
            HasResult = true;
            SummaryText = "流水线已完成。可保存为模板，下次一键运行。";
        }
        catch (Exception ex)
        {
            ResultIsSuccess = false;
            ResultTitle = "流水线中断";
            ResultMessage = ex.Message + " 已完成步骤可在回退中心检查。";
            HasResult = true;
        }
        finally
        {
            IsBusy = false;
            BusyText = string.Empty;
            ExecuteCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task RunArchive(string work, string output, PipelineStep step)
    {
        string name = string.IsNullOrWhiteSpace(step.Text1) ? "Locifab-归档.zip" : step.Text1;
        if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) name += ".zip";
        var plan = _archivePlanner.Plan(new ArchiveOptions
        {
            Mode = ArchiveMode.Compress,
            SourcePath = work,
            TargetPath = UniquePath(Path.Combine(output, name)),
            Password = step.Text2
        });
        if (!plan.Success) throw new InvalidOperationException(plan.Error);
        var result = await Task.Run(() => _archiveExecutor.Execute(plan));
        if (!result.Success) throw new InvalidOperationException(result.Message);
    }

    private async Task RunArchiveExtract(string work, string output, PipelineStep step)
    {
        var zipFiles = Directory.EnumerateFiles(work, "*.zip", SearchOption.AllDirectories).ToList();
        if (zipFiles.Count == 0) throw new InvalidOperationException("工作目录里没有 ZIP 文件。");
        string sub = string.IsNullOrWhiteSpace(step.Text1) ? "解压结果" : step.Text1;
        string targetRoot = UniqueDirectory(Path.Combine(output, sub));
        foreach (string zip in zipFiles)
        {
            string name = Path.GetFileNameWithoutExtension(zip);
            var plan = _archivePlanner.Plan(new ArchiveOptions
            {
                Mode = ArchiveMode.Extract,
                SourcePath = zip,
                TargetPath = Path.Combine(targetRoot, name),
                Password = step.Text2
            });
            if (!plan.Success) throw new InvalidOperationException(plan.Error);
            var result = await Task.Run(() => _archiveExecutor.Execute(plan));
            if (!result.Success) throw new InvalidOperationException(result.Message);
        }
    }

    private static void RunFileNameClean(string work, PipelineStep step)
    {
        var filter = ExtensionFilter(step.Text1);
        foreach (string file in Directory.EnumerateFiles(work, "*", SearchOption.AllDirectories).ToList())
        {
            if (filter.Count > 0 && !filter.Contains(Path.GetExtension(file))) continue;
            string directory = Path.GetDirectoryName(file) ?? work;
            string cleaned = CleanFileName(Path.GetFileNameWithoutExtension(file));
            string target = Path.Combine(directory, cleaned + Path.GetExtension(file));
            if (string.Equals(file, target, StringComparison.Ordinal)) continue;
            target = UniquePath(target);
            File.Move(file, target);
        }
    }

    private static void RunRuleCleanup(string work, string output, PipelineStep step)
    {
        var filter = ExtensionFilter(step.Text1);
        DateTime cutoff = step.Number1 > 0 ? DateTime.Now.AddDays(-step.Number1) : DateTime.MinValue;
        long sizeLimit = step.Number2 > 0 ? step.Number2 * 1024L : 0;
        string quarantine = UniqueDirectory(Path.Combine(output, "隔离区"));
        foreach (string file in Directory.EnumerateFiles(work, "*", SearchOption.AllDirectories).ToList())
        {
            if (filter.Count > 0 && !filter.Contains(Path.GetExtension(file))) continue;
            var info = new FileInfo(file);
            if (cutoff != DateTime.MinValue && info.LastWriteTime >= cutoff) continue;
            if (sizeLimit > 0 && info.Length >= sizeLimit) continue;
            string relative = Path.GetRelativePath(work, file);
            string target = UniquePath(Path.Combine(quarantine, relative));
            Directory.CreateDirectory(Path.GetDirectoryName(target) ?? quarantine);
            File.Move(file, target);
        }
    }

    private static HashSet<string> ExtensionFilter(string text)
    {
        return (text ?? string.Empty)
            .Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.StartsWith('.') ? x : "." + x)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string CleanFileName(string name)
    {
        var builder = new StringBuilder(name.Length);
        bool lastSpace = false;
        foreach (char raw in name)
        {
            char c = raw;
            if (c >= '\uFF01' && c <= '\uFF5E') c = (char)(c - 0xFEE0);
            else if (c == '\u3000') c = ' ';
            if (Path.GetInvalidFileNameChars().Contains(c) || char.IsControl(c)) c = '_';
            if (char.IsWhiteSpace(c))
            {
                if (lastSpace) continue;
                c = ' ';
                lastSpace = true;
            }
            else lastSpace = false;
            builder.Append(c);
        }
        string cleaned = builder.ToString().Trim(' ', '.');
        return cleaned.Length == 0 ? "未命名" : cleaned;
    }

    private async Task RunConvert(string work, string output, PipelineStep step)
    {
        var format = ConversionTargetFormat.FromId(step.Choice);
        string sub = string.IsNullOrWhiteSpace(step.Text1) ? "转换结果" : step.Text1;
        string target = UniqueDirectory(Path.Combine(output, sub));
        var files = Directory.EnumerateFiles(work, "*", SearchOption.AllDirectories).ToList();
        var plan = _conversionPlanner.CreatePlan(files, target, format);
        if (!string.IsNullOrWhiteSpace(plan.SetupError)) throw new InvalidOperationException(plan.SetupError);
        var requests = plan.Items
            .Where(x => x.IsExecutable && !string.IsNullOrWhiteSpace(x.EngineId))
            .Select(x => new ConversionWorkItem(x.SourcePath, x.TargetPath, x.TargetFormat, x.EngineId!))
            .ToList();
        if (requests.Count == 0)
            throw new InvalidOperationException($"没有可转换为 {format.DisplayName} 的文件。");
        int converted = 0;
        var failures = new List<string>();
        foreach (var request in requests)
        {
            var result = await _conversionExecutor.ExecuteAsync(new[] { request });
            if (result.Success) converted++;
            else failures.Add(Path.GetFileName(request.SourcePath) + "：" + result.Message);
        }
        if (converted == 0)
            throw new InvalidOperationException("没有文件转换成功。" + (failures.Count > 0 ? " " + failures[0] : string.Empty));
    }

    private async Task RunSplitMerge(string work, string output, PipelineStep step)
    {
        string sub = string.IsNullOrWhiteSpace(step.Text1) ? "拆分合并结果" : step.Text1;
        string target = UniqueDirectory(Path.Combine(output, sub));
        bool merge = string.Equals(step.Choice, "Merge", StringComparison.OrdinalIgnoreCase);
        if (merge)
        {
            string pattern = string.IsNullOrWhiteSpace(step.Text2) ? "*.part*" : step.Text2;
            var plan = _splitMerge.PlanMerge(work, pattern, UniquePath(Path.Combine(target, "合并结果.bin")));
            if (!plan.Success) throw new InvalidOperationException(plan.Error);
            var result = await Task.Run(() => _splitMerge.ExecuteMerge(plan));
            if (!result.Success) throw new InvalidOperationException(result.Message);
            return;
        }

        double sizeMb = step.Number1 > 0 ? step.Number1 : 10;
        var files = Directory.EnumerateFiles(work, "*", SearchOption.AllDirectories)
            .Where(x => new FileInfo(x).Length > sizeMb * 1024 * 1024)
            .ToList();
        if (files.Count == 0) throw new InvalidOperationException($"工作目录里没有大于 {sizeMb} MB 的文件。");
        foreach (string file in files)
        {
            string baseName = Path.GetFileName(file);
            var plan = _splitMerge.PlanSplit(new SplitOptions
            {
                SourceFile = file,
                OutputFolder = UniqueDirectory(Path.Combine(target, baseName + "_分片")),
                Mode = SplitMode.BySize,
                SizeMb = sizeMb
            });
            if (!plan.Success) throw new InvalidOperationException(plan.Error);
            var result = await Task.Run(() => _splitMerge.ExecuteSplit(plan));
            if (!result.Success) throw new InvalidOperationException(result.Message);
        }
    }

    private static void RunDedup(string work, string output, PipelineStep step)
    {
        string sub = string.IsNullOrWhiteSpace(step.Text1) ? "重复隔离" : step.Text1;
        string quarantine = UniqueDirectory(Path.Combine(output, sub));
        var files = Directory.EnumerateFiles(work, "*", SearchOption.AllDirectories)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in files)
        {
            string hash;
            using (var stream = File.OpenRead(file))
                hash = Convert.ToHexString(SHA256.HashData(stream));
            if (seen.TryAdd(hash, file)) continue;
            string relative = Path.GetRelativePath(work, file);
            string target = UniquePath(Path.Combine(quarantine, relative));
            Directory.CreateDirectory(Path.GetDirectoryName(target) ?? quarantine);
            File.Move(file, target);
        }
    }

    private void RunTable(string work, string output, PipelineStep step)
    {
        var files = Directory.EnumerateFiles(work, "*", SearchOption.AllDirectories)
            .Where(x => Path.GetExtension(x) is ".csv" or ".xlsx" or ".xls")
            .ToList();
        if (files.Count == 0) throw new InvalidOperationException("工作目录里没有 CSV/XLSX 表格。");
        var tables = new List<TableData>();
        foreach (string file in files)
        {
            try { tables.Add(TableReader.Read(file)); }
            catch { }
        }
        var first = tables.FirstOrDefault(x => x.Headers.Count > 0);
        if (first is null) throw new InvalidOperationException("没有可读取的表格数据。");
        var rows = tables.SelectMany(x => x.Rows).ToList();
        var combined = new TableData { Headers = first.Headers, Rows = rows };
        string name = string.IsNullOrWhiteSpace(step.Text1) ? "表格汇总.csv" : step.Text1;
        if (!name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) name += ".csv";
        var result = _tableExporter.Export(combined, UniquePath(Path.Combine(output, name)), work, "流水线表格汇总");
        if (!result.Success) throw new InvalidOperationException(result.Message);
    }

    private async Task RunPhotoDedup(string work, string output, PipelineStep step)
    {
        double threshold = Math.Clamp(step.Number1 <= 0 ? 92 : step.Number1, 50, 100);
        var scan = await Task.Run(() => PerceptualHash.Scan(work, true, threshold));
        if (!scan.Success) throw new InvalidOperationException(scan.Error);
        string sub = string.IsNullOrWhiteSpace(step.Text1) ? "相似照片隔离" : step.Text1;
        string quarantine = UniqueDirectory(Path.Combine(output, sub));
        foreach (var group in scan.Groups)
        {
            foreach (var entry in group.Members.Where(x => !string.Equals(x.Path, group.Keeper.Path, StringComparison.OrdinalIgnoreCase)))
            {
                if (!File.Exists(entry.Path)) continue;
                string relative = Path.GetRelativePath(work, entry.Path);
                string target = UniquePath(Path.Combine(quarantine, relative));
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? quarantine);
                File.Move(entry.Path, target);
            }
        }
    }

    private async Task RunSpaceScan(string work, string output, PipelineStep step)
    {
        var files = await Task.Run(() => Directory.EnumerateFiles(work, "*", SearchOption.AllDirectories)
            .Select(x => new FileInfo(x)).Where(x => x.Exists).OrderByDescending(x => x.Length).ToList());
        string name = string.IsNullOrWhiteSpace(step.Text1) ? "空间分析.csv" : step.Text1;
        if (!name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) name += ".csv";
        string target = UniquePath(Path.Combine(output, name));
        var lines = new List<string> { "路径,字节,大小,类型" };
        lines.AddRange(files.Select(f => $"\"{f.FullName}\",{f.Length},{f.Length},{f.Extension.ToLowerInvariant()}"));
        await File.WriteAllLinesAsync(target, lines, Encoding.UTF8);
    }

    private async Task RunImageTarget(string work, string output, PipelineStep step)
    {
        string sub = string.IsNullOrWhiteSpace(step.Text1) ? "图片压缩" : step.Text1;
        string targetDir = UniqueDirectory(Path.Combine(output, sub));
        Directory.CreateDirectory(targetDir);
        long target = Math.Max(20, step.Number1) * 1024L;
        var files = Directory.EnumerateFiles(work, "*", SearchOption.TopDirectoryOnly)
            .Where(x => IsImage(Path.GetExtension(x))).ToList();
        foreach (string file in files)
        {
            byte[] data = await Task.Run(() => CompressToTarget(file, target));
            string path = UniquePath(Path.Combine(targetDir, Path.GetFileNameWithoutExtension(file) + ".jpg"));
            await File.WriteAllBytesAsync(path, data);
        }
    }

    private async Task RunImageCheck(string work, string output, PipelineStep step)
    {
        string sub = string.IsNullOrWhiteSpace(step.Text1) ? "损坏图片隔离" : step.Text1;
        string quarantine = UniqueDirectory(Path.Combine(output, sub));
        var files = Directory.EnumerateFiles(work, "*", SearchOption.AllDirectories)
            .Where(x => IsImage(Path.GetExtension(x))).ToList();
        var bad = new List<string>();
        await Task.Run(() =>
        {
            foreach (string file in files)
            {
                try
                {
                    using var stream = File.OpenRead(file);
                    BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                }
                catch { bad.Add(file); }
            }
        });
        foreach (string file in bad)
        {
            string target = UniquePath(Path.Combine(quarantine, Path.GetFileName(file)));
            Directory.CreateDirectory(quarantine);
            File.Move(file, target);
        }
    }

    private async Task RunEmbeddedImages(string work, string output, PipelineStep step)
    {
        string sub = string.IsNullOrWhiteSpace(step.Text1) ? "内嵌图片" : step.Text1;
        string targetDir = UniqueDirectory(Path.Combine(output, sub));
        Directory.CreateDirectory(targetDir);
        await Task.Run(() =>
        {
            foreach (string file in Directory.EnumerateFiles(work, "*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is not (".docx" or ".xlsx" or ".pptx")) continue;
                try
                {
                    using var zip = ZipFile.OpenRead(file);
                    foreach (var entry in zip.Entries.Where(x => x.FullName.Contains("/media/", StringComparison.OrdinalIgnoreCase)))
                    {
                        string target = UniquePath(Path.Combine(targetDir, Path.GetFileNameWithoutExtension(file) + "_" + Path.GetFileName(entry.FullName)));
                        entry.ExtractToFile(target, true);
                    }
                }
                catch { }
            }
        });
    }

    private static void RunShortcuts(string work, string output, PipelineStep step)
    {
        string sub = string.IsNullOrWhiteSpace(step.Text1) ? "快捷方式" : step.Text1;
        string targetDir = UniqueDirectory(Path.Combine(output, sub));
        Directory.CreateDirectory(targetDir);
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) return;
        dynamic shell = Activator.CreateInstance(shellType)!;
        foreach (string item in Directory.EnumerateFileSystemEntries(work))
        {
            string target = UniquePath(Path.Combine(targetDir, Path.GetFileNameWithoutExtension(item) + ".lnk"));
            dynamic link = shell.CreateShortcut(target);
            link.TargetPath = item;
            link.WorkingDirectory = Directory.Exists(item) ? item : Path.GetDirectoryName(item) ?? work;
            link.Save();
        }
    }

    private static void RunTagFiles(string work, PipelineStep step)
    {
        var files = Directory.EnumerateFiles(work, "*", SearchOption.TopDirectoryOnly)
            .Select(x => new { FileName = Path.GetFileName(x), Tag = step.Text1, Note = step.Text2 })
            .ToList();
        File.WriteAllText(Path.Combine(work, ".locifab-tags.json"),
            JsonSerializer.Serialize(files, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static bool IsImage(string extension) =>
        extension.ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".tif" or ".tiff" or ".webp";

    private static byte[] CompressToTarget(string path, long targetBytes)
    {
        using var stream = File.OpenRead(path);
        BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        BitmapSource source = decoder.Frames[0];
        if (source.PixelWidth > 6000)
            source = new TransformedBitmap(source, new ScaleTransform(6000.0 / source.PixelWidth, 6000.0 / source.PixelWidth));
        for (int quality = 92; quality >= 35; quality -= 7)
        {
            var encoder = new JpegBitmapEncoder { QualityLevel = quality };
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            if (ms.Length <= targetBytes || quality <= 35) return ms.ToArray();
        }
        throw new InvalidOperationException("无法压缩到目标大小。");
    }

    private async Task RunReport(string work, string output, PipelineStep step)
    {
        var scan = await Task.Run(() => FileReportService.Scan(new FileReportOptions
        {
            Folder = work, IncludeSubfolders = true, IncludeHash = step.Bool1
        }));
        if (!scan.Success) throw new InvalidOperationException(scan.Error);
        bool html = string.Equals(step.Choice, "Html", StringComparison.OrdinalIgnoreCase);
        string path = UniquePath(Path.Combine(output, "Locifab-清单" + (html ? ".html" : ".csv")));
        var journal = NewJournal("report", work, path);
        _repository.Save(journal);
        if (html) FileReportService.WriteHtml(path, work, scan.Rows);
        else FileReportService.WriteCsv(path, scan.Rows);
    }

    private async Task RunMetadata(string work, string output, PipelineStep step)
    {
        string sub = string.IsNullOrWhiteSpace(step.Text1) ? "去元数据" : step.Text1;
        var plan = _metadataCleaner.Plan(work, true, UniqueDirectory(Path.Combine(output, sub)));
        if (!plan.Success) throw new InvalidOperationException(plan.Error);
        var result = await Task.Run(() => _metadataCleaner.Execute(plan));
        if (!result.Success) throw new InvalidOperationException(result.Message);
    }

    private async Task RunChecksum(string work, string output, PipelineStep step)
    {
        var scan = await Task.Run(() => FileReportService.Scan(new FileReportOptions
        {
            Folder = work, IncludeSubfolders = true, IncludeHash = true
        }));
        if (!scan.Success) throw new InvalidOperationException(scan.Error);
        string name = string.IsNullOrWhiteSpace(step.Text1) ? "Locifab-SHA256.txt" : step.Text1;
        string path = UniquePath(Path.Combine(output, name));
        var journal = NewJournal("checksum", work, path);
        _repository.Save(journal);
        var builder = new StringBuilder();
        foreach (var row in scan.Rows)
            builder.AppendLine($"{row.Hash}  {row.RelativePath}");
        await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(false));
    }

    private void RunRename(string work, PipelineStep step)
    {
        var files = Directory.EnumerateFiles(work, "*", SearchOption.AllDirectories).ToList();
        var plan = new RenamePlanner().CreatePlan(files, new RenameOptions
        {
            Prefix = step.Text1,
            Suffix = step.Text2,
            NumberSeparator = string.IsNullOrEmpty(step.Text3) ? "_" : step.Text3,
            UseNumbering = step.Bool1,
            StartNumber = Math.Max(0, step.Number1),
            NumberPadding = Math.Clamp(step.Number2, 1, 10)
        });
        var usedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var workItems = new List<RenameWorkItem>();
        foreach (var item in plan.Items.Where(x => x.IsExecutable))
        {
            string target = item.TargetPath;
            if (!usedTargets.Add(target))
            {
                string directory = Path.GetDirectoryName(target) ?? work;
                string name = Path.GetFileNameWithoutExtension(target);
                string extension = Path.GetExtension(target);
                for (int i = 2; ; i++)
                {
                    string candidate = Path.Combine(directory, $"{name}_{i}{extension}");
                    if (usedTargets.Add(candidate)) { target = candidate; break; }
                }
            }
            workItems.Add(new RenameWorkItem(item.SourcePath, target, false));
        }
        if (workItems.Count == 0) return;
        var result = new BatchRenameExecutor(_repository).Execute(workItems, kind: "pipeline-rename", displayName: "流水线重命名");
        if (!result.Success) throw new InvalidOperationException(result.Message);
    }

    private void RunTextReplace(string work, PipelineStep step)
    {
        if (string.IsNullOrEmpty(step.Text1)) throw new InvalidOperationException("文本替换步骤缺少查找文字。");
        var options = new TextReplaceOptions
        {
            Folder = work,
            FindText = step.Text1,
            ReplaceText = step.Text2,
            IncludeSubfolders = true,
            IncludeExtensions = step.Text3,
            AnyExtension = string.IsNullOrWhiteSpace(step.Text3)
        };
        var planner = new TextReplacePlanner();
        var scan = planner.Scan(options);
        if (!scan.Success) throw new InvalidOperationException(scan.Error);
        if (scan.Files.Count == 0) return;
        var result = new TextReplaceExecutor(_repository).Execute(scan.Files, options);
        if (!result.Success) throw new InvalidOperationException(result.Message);
    }

    private async Task RunImageBatch(string work, string output, PipelineStep step)
    {
        var files = new ImageBatchPlanner().FindImages(work, true);
        if (files.Count == 0) return;
        string sub = string.IsNullOrWhiteSpace(step.Text1) ? "图片输出" : step.Text1;
        var options = new ImageBatchOptions
        {
            OutputFolder = UniqueDirectory(Path.Combine(output, sub)),
            ResizeMode = step.Number1 > 0 ? ImageResizeMode.LongEdge : ImageResizeMode.None,
            SizeValue = Math.Max(1, step.Number1),
            JpegQuality = Math.Clamp(step.Number2, 1, 100),
            Format = Enum.TryParse(step.Choice, out ImageOutputFormat format) ? format : ImageOutputFormat.Keep,
            StripMetadata = true
        };
        var plan = new ImageBatchPlanner().Plan(files, options);
        if (!plan.Success) throw new InvalidOperationException(plan.Error);
        var result = await Task.Run(() => new ImageBatchExecutor(_repository).Execute(plan, options));
        if (!result.Success) throw new InvalidOperationException(result.Message);
    }

    private async Task RunCleanup(string work, PipelineStep step)
    {
        var planner = new CleanupPlanner();
        var plan = planner.Scan(new CleanupOptions
        {
            Folder = work,
            IncludeSubfolders = true,
            RemoveEmptyDirectories = step.Bool1,
            RemoveZeroByteFiles = step.Bool2
        });
        if (!plan.Success) throw new InvalidOperationException(plan.Error);
        if (plan.Items.Count == 0) return;
        var result = await Task.Run(() => new CleanupExecutor(_repository).Execute(plan));
        if (!result.Success) throw new InvalidOperationException(result.Message);
    }

    private async Task RunBackup(string work, string output, PipelineStep step)
    {
        string sub = string.IsNullOrWhiteSpace(step.Text1) ? "备份" : step.Text1;
        var planner = new BackupPlanner();
        var plan = planner.Scan(new BackupOptions
        {
            SourceFolder = work,
            TargetFolder = UniqueDirectory(Path.Combine(output, sub)),
            IncludeSubfolders = true,
            SkipHidden = false
        });
        if (!plan.Success) throw new InvalidOperationException(plan.Error);
        var result = await Task.Run(() => new BackupExecutor(_repository).Execute(plan));
        if (!result.Success) throw new InvalidOperationException(result.Message);
    }

    private async Task RunClassify(string work, PipelineStep step)
    {
        ClassificationMode mode = Enum.TryParse(step.Choice, out ClassificationMode parsed)
            ? parsed
            : ClassificationMode.ByType;
        var plan = new ClassifyPlanner().CreatePlan(work, new ClassifyOptions
        {
            Mode = mode,
            IncludeSubdirectories = true
        });
        var usedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var workItems = new List<RenameWorkItem>();
        foreach (var item in plan.Items.Where(x => x.IsExecutable))
        {
            string target = item.TargetPath;
            if (!usedTargets.Add(target))
            {
                string directory = Path.GetDirectoryName(target) ?? work;
                string name = Path.GetFileNameWithoutExtension(target);
                string extension = Path.GetExtension(target);
                for (int i = 2; ; i++)
                {
                    string candidate = Path.Combine(directory, $"{name}_{i}{extension}");
                    if (usedTargets.Add(candidate)) { target = candidate; break; }
                }
            }
            workItems.Add(new RenameWorkItem(item.SourcePath, target, false));
        }
        if (workItems.Count == 0) return;
        var result = await Task.Run(() => new BatchRenameExecutor(_repository).Execute(
            workItems, kind: "pipeline-classify", displayName: "流水线分类", createTargetDirectories: true));
        if (!result.Success) throw new InvalidOperationException(result.Message);
    }

    private void RunFileState(string work, PipelineStep step)
    {
        var service = new FileStateService(_repository);
        if (DateTime.TryParse(step.Text1, out DateTime fixedTime))
        {
            var plan = service.PlanTimestamps(new TimestampOptions
            {
                Folder = work,
                IncludeSubfolders = true,
                Mode = TimestampMode.Fixed,
                FixedTime = fixedTime,
                ApplyCreated = true,
                ApplyModified = true
            });
            if (!plan.Success) throw new InvalidOperationException(plan.Error);
            if (plan.Items.Count > 0)
            {
                var result = service.ExecuteTimestamps(plan, new TimestampOptions
                {
                    Folder = work, IncludeSubfolders = true, Mode = TimestampMode.Fixed,
                    FixedTime = fixedTime, ApplyCreated = true, ApplyModified = true
                });
                if (!result.Success) throw new InvalidOperationException(result.Message);
            }
        }

        if (step.Bool1 || step.Bool2)
        {
            var options = new AttributeOptions
            {
                Folder = work,
                IncludeSubfolders = true,
                SetReadOnly = step.Bool1,
                SetHidden = step.Bool2,
                Remove = false
            };
            var plan = service.PlanAttributes(options);
            if (!plan.Success) throw new InvalidOperationException(plan.Error);
            if (plan.Items.Count > 0)
            {
                var result = service.ExecuteAttributes(plan, options);
                if (!result.Success) throw new InvalidOperationException(result.Message);
            }
        }
    }

    private void RunTemplate(string work, PipelineStep step)
    {
        string template = (step.Text1 ?? string.Empty).Replace("|", Environment.NewLine);
        if (string.IsNullOrWhiteSpace(template)) return;
        var service = new FolderTemplateService(_repository);
        var plan = service.Plan(work, template);
        if (!plan.Success) throw new InvalidOperationException(plan.Error);
        var result = service.Execute(plan);
        if (!result.Success) throw new InvalidOperationException(result.Message);
    }

    private OperationJournal NewJournal(string kind, string source, string target) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Kind = kind,
        DisplayName = "流水线输出",
        SourceDirectory = source,
        CreatedAt = DateTimeOffset.Now,
        Status = JournalStatus.Completed,
        CompletedAt = DateTimeOffset.Now,
        Operations =
        {
            new JournalOperation { Sequence = 1, SourcePath = source, TargetPath = target, State = JournalOperationState.Completed }
        }
    };

    private void SavePreset()
    {
        string name = PresetName.Trim();
        if (name.Length == 0) return;
        var preset = new PipelinePreset
        {
            Name = name,
            SourceFolder = SourceFolder,
            OutputFolder = OutputFolder,
            Steps = Steps.Select(x => x.ToData()).ToList()
        };
        var existing = Presets.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) Presets.Remove(existing);
        Presets.Add(preset);
        WritePresets();
        SelectedPreset = preset;
        SummaryText = "模板已保存到本机。";
    }

    private void DeletePreset()
    {
        if (SelectedPreset is null) return;
        Presets.Remove(SelectedPreset);
        SelectedPreset = null;
        WritePresets();
        SummaryText = "模板已删除。";
    }

    private void LoadPresets()
    {
        try
        {
            if (File.Exists(PresetPath))
            {
                var presets = JsonSerializer.Deserialize<List<PipelinePreset>>(File.ReadAllText(PresetPath), JsonOptions) ?? new();
                foreach (var preset in presets.Where(x => !string.IsNullOrWhiteSpace(x.Name))) Presets.Add(preset);
            }
            bool added = false;
            foreach (var (name, kinds) in BuiltInPresets())
            {
                if (Presets.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) continue;
                Presets.Add(DefaultPreset(name, kinds));
                added = true;
            }
            if (added) WritePresets();
        }
        catch { }
    }

    private static IEnumerable<(string Name, PipelineStepKind[] Kinds)> BuiltInPresets()
    {
        yield return ("交付打包", new[] { PipelineStepKind.Rename, PipelineStepKind.Archive, PipelineStepKind.Report });
        yield return ("隐私交付", new[] { PipelineStepKind.Metadata, PipelineStepKind.Checksum });
        yield return ("照片交付包", new[] { PipelineStepKind.ImageTarget, PipelineStepKind.Report });
        yield return ("审计清单", new[] { PipelineStepKind.Report, PipelineStepKind.Checksum });
        yield return ("Office 素材提取", new[] { PipelineStepKind.EmbeddedImages, PipelineStepKind.Report });
        yield return ("项目目录初始化", new[] { PipelineStepKind.Template, PipelineStepKind.Report });
        yield return ("上架图片包", new[] { PipelineStepKind.ImageBatch, PipelineStepKind.ImageTarget, PipelineStepKind.Report });
        yield return ("备份前审计", new[] { PipelineStepKind.Checksum, PipelineStepKind.Report, PipelineStepKind.Backup });
        yield return ("文档隐私交付", new[] { PipelineStepKind.Metadata, PipelineStepKind.Checksum, PipelineStepKind.Report });
        yield return ("课程资料归档", new[] { PipelineStepKind.Classify, PipelineStepKind.Rename, PipelineStepKind.Report });
    }

    private PipelinePreset DefaultPreset(string name, PipelineStepKind[] kinds)
    {
        var preset = new PipelinePreset { Name = name };
        foreach (var kind in kinds)
            preset.Steps.Add(new PipelineStep(kind, _ => { }, (_, _) => { }).ToData());
        return preset;
    }

    private void WritePresets()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PresetPath) ?? string.Empty);
            File.WriteAllText(PresetPath, JsonSerializer.Serialize(Presets.ToList(), JsonOptions), new UTF8Encoding(false));
        }
        catch { }
    }

    private void ClearPreview() { PreviewSteps.Clear(); HasPreview = false; }
    private void RaiseCommands() { BrowseSourceCommand.RaiseCanExecuteChanged(); BrowseOutputCommand.RaiseCanExecuteChanged(); AddStepCommand.RaiseCanExecuteChanged(); PreviewCommand.RaiseCanExecuteChanged(); ExecuteCommand.RaiseCanExecuteChanged(); }
    private static string DefaultOutput(string source) => Path.Combine(Path.GetDirectoryName(source) ?? source, Path.GetFileName(source) + "_pipeline");
    private static bool IsSubPath(string path, string root) => Path.GetFullPath(path).StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase);
    private static string UniqueDirectory(string path) { if (!Directory.Exists(path)) return path; for (int i = 2; i < 1000; i++) { var p = path + " (" + i + ")"; if (!Directory.Exists(p)) return p; } return path; }
    private static string UniquePath(string path) { if (!File.Exists(path)) return path; string dir = Path.GetDirectoryName(path) ?? ""; string name = Path.GetFileNameWithoutExtension(path); string ext = Path.GetExtension(path); for (int i = 2; i < 1000; i++) { var p = Path.Combine(dir, name + " (" + i + ")" + ext); if (!File.Exists(p)) return p; } return path; }
    private static void CopyDirectory(string source, string target) { Directory.CreateDirectory(target); foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true); foreach (var dir in Directory.EnumerateDirectories(source)) CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir))); }
}
