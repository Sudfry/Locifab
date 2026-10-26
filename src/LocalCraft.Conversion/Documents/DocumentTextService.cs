using LocalCraft.Conversion.Formats;

namespace LocalCraft.Conversion.Documents;

/// <summary>
/// 供去重与摘要模块使用的本地文本提取入口。
/// 支持 TXT / Markdown / HTML / DOCX / PPTX / PDF（文本层）。
/// </summary>
public static class DocumentTextService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".html", ".htm", ".docx", ".pptx", ".pdf"
    };

    public static bool CanExtract(string pathOrExtension)
    {
        return SupportedExtensions.Contains(SourceFormats.NormalizeExtension(pathOrExtension));
    }

    public static string ExtractText(string path, int maxCharacters = 4_000_000)
    {
        string extension = SourceFormats.NormalizeExtension(path);
        string text = extension switch
        {
            ".pdf" => PdfTextExtractor.ExtractText(path, maxCharacters),
            ".txt" => TextFileReader.ReadAllText(path),
            ".md" or ".markdown" or ".html" or ".htm" or ".docx" or ".pptx" =>
                DocumentWriter.RenderPlainText(DocumentExtractor.Extract(path)),
            _ => throw new NotSupportedException($"不支持从 {extension} 提取文本。")
        };

        text = text.Trim();
        return text.Length > maxCharacters ? text[..maxCharacters] : text;
    }
}
