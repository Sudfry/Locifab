namespace LocalCraft.Conversion.Formats;

public sealed record ConversionTargetFormat(
    string Id,
    string DisplayName,
    string Extension,
    ConversionKind Kind,
    bool PrefersExternalEngine = false)
{
    public string FileDialogFilter => $"{DisplayName} (*{Extension})|*{Extension}";

    public static IReadOnlyList<ConversionTargetFormat> All { get; } = new List<ConversionTargetFormat>
    {
        new("markdown", "Markdown", ".md", ConversionKind.Document),
        new("txt", "纯文本 TXT", ".txt", ConversionKind.Document),
        new("html", "HTML 网页", ".html", ConversionKind.Document),
        new("docx", "Word 文档 DOCX", ".docx", ConversionKind.Document),
        new("pdf", "PDF 文档", ".pdf", ConversionKind.Document),
        new("pptx", "PowerPoint PPTX（需要 LibreOffice）", ".pptx", ConversionKind.Document, true),
        new("jpg", "JPEG 图片", ".jpg", ConversionKind.Image),
        new("png", "PNG 图片", ".png", ConversionKind.Image),
        new("bmp", "BMP 图片", ".bmp", ConversionKind.Image),
        new("gif", "GIF 图片", ".gif", ConversionKind.Image),
        new("tif", "TIFF 图片", ".tif", ConversionKind.Image)
    };

    public static ConversionTargetFormat FromId(string? id)
    {
        return All.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase))
               ?? All[0];
    }

    public override string ToString() => DisplayName;
}
