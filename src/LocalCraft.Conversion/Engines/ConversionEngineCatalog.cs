using LocalCraft.Conversion.Formats;

namespace LocalCraft.Conversion.Engines;

public sealed class ConversionEngineCatalog
{
    private readonly BuiltInDocumentEngine _documentEngine = new();
    private readonly BuiltInImageEngine _imageEngine = new();
    private readonly PandocEngine _pandocEngine;
    private readonly LibreOfficeEngine _libreOfficeEngine;

    public ConversionEngineCatalog(string? engineRoot = null)
    {
        EngineRoot = Path.GetFullPath(engineRoot ?? Path.Combine(AppContext.BaseDirectory, "engines"));
        _pandocEngine = new PandocEngine(EngineRoot);
        _libreOfficeEngine = new LibreOfficeEngine(EngineRoot);

        Statuses = new List<ConversionEngineStatus>
        {
            new("builtin-doc", "内置文档引擎", true, "DOCX / PPTX / Markdown / TXT / HTML → MD / TXT / HTML / DOCX / PDF，纯本地 C#"),
            new("builtin-image", "内置图像引擎", true, "JPG / PNG / BMP / GIF / TIFF；HEIC 与 RAW 视系统解码器"),
            new("libreoffice", "LibreOffice", _libreOfficeEngine.IsAvailable,
                _libreOfficeEngine.IsAvailable
                    ? "已检测到：" + _libreOfficeEngine.ExecutablePath
                    : "未检测到；PDF、旧版 Office 与 PPTX 输出需要它"),
            new("pandoc", "Pandoc", _pandocEngine.IsAvailable,
                _pandocEngine.IsAvailable
                    ? "已检测到：" + _pandocEngine.ExecutablePath
                    : "未检测到；可作为高质量 Markdown / DOCX 转换的可选引擎"),
            new("libraw", "LibRaw", false, "当前版本未打包 LibRaw；RAW 输入先尝试 Windows 图像解码器")
        };
    }

    public string EngineRoot { get; }

    public IReadOnlyList<ConversionEngineStatus> Statuses { get; }

    public IConversionEngine? GetEngine(string engineId)
    {
        return engineId switch
        {
            "builtin-doc" => _documentEngine,
            "builtin-image" => _imageEngine,
            "pandoc" => _pandocEngine,
            "libreoffice" => _libreOfficeEngine,
            _ => null
        };
    }

    public ConversionEngineMatch Resolve(string sourcePath, ConversionTargetFormat target)
    {
        string sourceExtension = SourceFormats.NormalizeExtension(sourcePath);
        string targetExtension = SourceFormats.NormalizeExtension(target.Extension);
        ConversionKind? sourceKind = SourceFormats.GetKind(sourceExtension);

        if (sourceKind is null)
        {
            return new ConversionEngineMatch(false, null, "不支持的源文件类型");
        }

        if (sourceKind == ConversionKind.Image && target.Kind != ConversionKind.Image
            || sourceKind == ConversionKind.Document && target.Kind != ConversionKind.Document)
        {
            return new ConversionEngineMatch(false, null, "文档与图片不能跨类转换");
        }

        if (sourceKind == ConversionKind.Image && IsSameImageFormat(sourceExtension, targetExtension))
        {
            return new ConversionEngineMatch(false, null, "源文件已经是目标格式");
        }

        if (sourceKind == ConversionKind.Document
            && string.Equals(sourceExtension, targetExtension, StringComparison.OrdinalIgnoreCase))
        {
            return new ConversionEngineMatch(false, null, "源文件已经是目标格式");
        }

        foreach (IConversionEngine engine in new IConversionEngine[]
                 {
                     _documentEngine, _imageEngine, _libreOfficeEngine, _pandocEngine
                 })
        {
            if (engine.IsAvailable && engine.CanConvert(sourcePath, target))
            {
                string note = engine.Id switch
                {
                    "builtin-image" when SourceFormats.IsRaw(sourceExtension) =>
                        $"{engine.DisplayName} · RAW 依赖系统解码器",
                    "builtin-image" when SourceFormats.IsHeic(sourceExtension) =>
                        $"{engine.DisplayName} · HEIC 依赖系统解码器",
                    _ => engine.DisplayName
                };
                return new ConversionEngineMatch(true, engine, note);
            }
        }

        return new ConversionEngineMatch(false, null, BuildMissingReason(sourceExtension, targetExtension, target));
    }

    private string BuildMissingReason(string sourceExtension, string targetExtension, ConversionTargetFormat target)
    {
        if (targetExtension == ".pptx")
        {
            return _libreOfficeEngine.IsAvailable
                ? "LibreOffice 无法完成此格式组合"
                : "PPTX 输出需要本地 LibreOffice 引擎（未检测到）";
        }

        if (sourceExtension == ".pdf")
        {
            return _libreOfficeEngine.IsAvailable
                ? "LibreOffice 无法完成该 PDF 转换"
                : "PDF 输入需要本地 LibreOffice 引擎（未检测到）";
        }

        if (sourceExtension is ".doc" or ".ppt" or ".rtf" or ".odt" or ".odp")
        {
            return "该旧版 Office 格式需要本地 LibreOffice 引擎（未检测到）";
        }

        if (SourceFormats.IsRaw(sourceExtension))
        {
            return "RAW 解码需要系统 RAW 图像扩展或 LibRaw 引擎";
        }

        if (SourceFormats.IsHeic(sourceExtension))
        {
            return "HEIC 解码需要系统 HEIF 图像扩展";
        }

        return "内置引擎不支持此格式组合";
    }

    private static bool IsSameImageFormat(string source, string target)
    {
        return NormalizeImage(source) == NormalizeImage(target);
    }

    private static string NormalizeImage(string extension)
    {
        return SourceFormats.NormalizeExtension(extension) switch
        {
            ".jpeg" => ".jpg",
            ".tiff" => ".tif",
            _ => SourceFormats.NormalizeExtension(extension)
        };
    }
}
