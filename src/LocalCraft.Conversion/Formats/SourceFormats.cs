namespace LocalCraft.Conversion.Formats;

public static class SourceFormats
{
    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".html", ".htm", ".docx", ".pptx",
        ".pdf", ".doc", ".ppt", ".rtf", ".odt", ".odp", ".csv"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".avif",
        ".heic", ".heif",
        ".cr2", ".cr3", ".nef", ".arw", ".raf", ".rw2", ".dng", ".orf", ".pef", ".srw"
    };

    private static readonly HashSet<string> RawExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cr2", ".cr3", ".nef", ".arw", ".raf", ".rw2", ".dng", ".orf", ".pef", ".srw"
    };

    public static IReadOnlySet<string> Documents => DocumentExtensions;

    public static IReadOnlySet<string> Images => ImageExtensions;

    public static bool IsRaw(string pathOrExtension) => RawExtensions.Contains(NormalizeExtension(pathOrExtension));

    public static bool IsHeic(string pathOrExtension)
    {
        string extension = NormalizeExtension(pathOrExtension);
        return extension is ".heic" or ".heif";
    }

    public static bool IsSupported(string pathOrExtension)
    {
        string extension = NormalizeExtension(pathOrExtension);
        return DocumentExtensions.Contains(extension) || ImageExtensions.Contains(extension);
    }

    public static ConversionKind? GetKind(string pathOrExtension)
    {
        string extension = NormalizeExtension(pathOrExtension);
        if (DocumentExtensions.Contains(extension)) return ConversionKind.Document;
        if (ImageExtensions.Contains(extension)) return ConversionKind.Image;
        return null;
    }

    public static string GetCategoryLabel(string pathOrExtension)
    {
        return GetKind(pathOrExtension) switch
        {
            ConversionKind.Document => "文档",
            ConversionKind.Image => "图片",
            _ => "其他"
        };
    }

    public static string NormalizeExtension(string pathOrExtension)
    {
        string value = pathOrExtension.Trim();
        if (value.Contains(Path.DirectorySeparatorChar) || value.Contains(Path.AltDirectorySeparatorChar))
        {
            value = Path.GetExtension(value);
        }

        if (!value.StartsWith('.'))
        {
            value = "." + value;
        }

        return value.ToLowerInvariant();
    }

    public static string FileDialogFilter
    {
        get
        {
            string docs = string.Join(";", DocumentExtensions.Select(static x => "*" + x));
            string images = string.Join(";", ImageExtensions.Select(static x => "*" + x));
            string all = docs + ";" + images;
            return $"支持的文件|{all}|文档|{docs}|图片与相机文件|{images}|所有文件|*.*";
        }
    }

    public static string TextDocumentFileDialogFilter =>
        "可分析文档|*.txt;*.md;*.markdown;*.html;*.htm;*.docx;*.pptx;*.pdf|" +
        "Word / PowerPoint|*.docx;*.pptx|Markdown / 文本|*.md;*.markdown;*.txt|PDF|*.pdf|所有文件|*.*";

    public static string ExportSourceFileDialogFilter =>
        "可导出文档|*.txt;*.md;*.markdown;*.html;*.htm;*.docx;*.pptx|" +
        "Word / PowerPoint|*.docx;*.pptx|Markdown / 文本|*.md;*.markdown;*.txt|所有文件|*.*";

    public static bool CanExtractText(string pathOrExtension)
    {
        string extension = NormalizeExtension(pathOrExtension);
        return extension is ".txt" or ".md" or ".markdown" or ".html" or ".htm" or ".docx" or ".pptx" or ".pdf";
    }
}
