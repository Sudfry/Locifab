using LocalCraft.Conversion.Documents;
using LocalCraft.Conversion.Formats;

namespace LocalCraft.Conversion.Engines;

/// <summary>
/// 纯 C# 文档转换：TXT / Markdown / HTML / DOCX / PPTX 之间转换。
/// 不依赖 Office、不联网；输出文档为最小但兼容的 Open XML 包。
/// </summary>
public sealed class BuiltInDocumentEngine : IConversionEngine
{
    private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".html", ".htm", ".docx", ".pptx"
    };

    private static readonly HashSet<string> TargetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".html", ".docx", ".pdf"
    };

    public string Id => "builtin-doc";

    public string DisplayName => "LocalCraft 内置文档引擎";

    public bool IsAvailable => true;

    public bool CanConvert(string sourcePath, ConversionTargetFormat target)
    {
        string sourceExtension = SourceFormats.NormalizeExtension(sourcePath);
        string targetExtension = SourceFormats.NormalizeExtension(target.Extension);
        if (!SourceExtensions.Contains(sourceExtension)) return false;
        if (!TargetExtensions.Contains(targetExtension)) return false;
        return !IsSameFormat(sourceExtension, targetExtension);
    }

    public async Task ConvertAsync(
        string sourcePath,
        string targetTempPath,
        ConversionTargetFormat target,
        CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            SimpleDocument document = DocumentExtractor.Extract(sourcePath);
            cancellationToken.ThrowIfCancellationRequested();
            DocumentWriter.Write(document, targetTempPath, target.Extension);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsSameFormat(string sourceExtension, string targetExtension)
    {
        if (string.Equals(sourceExtension, targetExtension, StringComparison.OrdinalIgnoreCase)) return true;
        return sourceExtension is ".html" or ".htm" && targetExtension == ".html"
               || sourceExtension is ".md" or ".markdown" && targetExtension == ".md";
    }
}
