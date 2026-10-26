using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LocalCraft.Conversion.Documents;

internal static class DocumentExtractor
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";

    public static SimpleDocument Extract(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".txt" => ExtractPlainText(path),
            ".md" or ".markdown" => ExtractMarkdown(path),
            ".html" or ".htm" => ExtractHtml(path),
            ".docx" => ExtractDocx(path),
            ".pptx" => ExtractPptx(path),
            _ => throw new NotSupportedException($"内置文档引擎不支持读取 {extension} 文件。")
        };
    }

    private static SimpleDocument ExtractPlainText(string path)
    {
        var document = new SimpleDocument();
        string text = TextFileReader.ReadAllText(path).Replace("\r\n", "\n").Replace('\r', '\n');
        string[] lines = text.Split('\n');

        var paragraph = new StringBuilder();
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                Flush(paragraph, document);
                continue;
            }

            if (paragraph.Length > 0) paragraph.Append("\n");
            paragraph.Append(line.TrimEnd());
        }

        Flush(paragraph, document);
        return document;
    }

    private static SimpleDocument ExtractMarkdown(string path)
    {
        var document = new SimpleDocument();
        string[] lines = TextFileReader.ReadAllText(path)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n');
        var paragraph = new StringBuilder();
        bool inCodeFence = false;

        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd();
            string trimmed = line.TrimStart();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                Flush(paragraph, document);
                inCodeFence = !inCodeFence;
                continue;
            }

            if (inCodeFence)
            {
                document.AddParagraph(line);
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                Flush(paragraph, document);
                continue;
            }

            int level = CountHeadingLevel(trimmed);
            if (level > 0)
            {
                Flush(paragraph, document);
                document.AddHeading(level, trimmed[level..].Trim());
                continue;
            }

            if (TryGetBulletText(trimmed, out string? bulletText))
            {
                Flush(paragraph, document);
                document.AddBullet(bulletText);
                continue;
            }

            if (paragraph.Length > 0) paragraph.Append('\n');
            paragraph.Append(trimmed);
        }

        Flush(paragraph, document);
        return document;
    }

    private static SimpleDocument ExtractHtml(string path)
    {
        string html = TextFileReader.ReadAllText(path);
        html = Regex.Replace(html, @"<script\b[^>]*>.*?</script>", string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        html = Regex.Replace(html, @"<style\b[^>]*>.*?</style>", string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        html = Regex.Replace(html, @"<h([1-6])\b[^>]*>", match => "\n" + new string('#', int.Parse(match.Groups[1].Value)) + " ",
            RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"</h[1-6]>", "\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<li\b[^>]*>", "\n- ", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"</(p|div|li|tr|table|section|article|header|footer)>", "\n",
            RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<[^>]+>", string.Empty);

        string text = WebUtility.HtmlDecode(html).Replace("\r\n", "\n").Replace('\r', '\n');
        var document = new SimpleDocument();
        var paragraph = new StringBuilder();

        foreach (string rawLine in text.Split('\n'))
        {
            string trimmed = rawLine.Trim();
            if (trimmed.Length == 0)
            {
                Flush(paragraph, document);
                continue;
            }

            int level = CountHeadingLevel(trimmed);
            if (level > 0)
            {
                Flush(paragraph, document);
                document.AddHeading(level, trimmed[(level + 1)..].Trim());
                continue;
            }

            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                Flush(paragraph, document);
                document.AddBullet(trimmed[2..].Trim());
                continue;
            }

            if (paragraph.Length > 0) paragraph.Append('\n');
            paragraph.Append(trimmed);
        }

        Flush(paragraph, document);
        return document;
    }

    private static SimpleDocument ExtractDocx(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry("word/document.xml")
                    ?? throw new InvalidOperationException("DOCX 中缺少 word/document.xml。");
        using var stream = entry.Open();
        XDocument xml = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
        var body = xml.Root?.Element(W + "body")
                   ?? throw new InvalidOperationException("DOCX 文档结构不完整。");

        var document = new SimpleDocument();
        foreach (XElement element in body.Elements())
        {
            if (element.Name == W + "p")
            {
                AddDocxParagraph(element, document);
            }
            else if (element.Name == W + "tbl")
            {
                foreach (XElement row in element.Elements(W + "tr"))
                {
                    var cells = row.Elements(W + "tc")
                        .Select(cell => string.Concat(cell.Descendants(W + "t").Select(t => t.Value)).Trim())
                        .Where(static x => x.Length > 0);
                    string line = string.Join(" | ", cells);
                    document.AddParagraph(line);
                }
            }
        }

        return document;
    }

    private static void AddDocxParagraph(XElement paragraph, SimpleDocument document)
    {
        string text = string.Concat(paragraph.Descendants(W + "t").Select(t => t.Value));
        text = text.Replace('\u00A0', ' ').Trim();
        if (text.Length == 0) return;

        string? style = paragraph.Element(W + "pPr")?.Element(W + "pStyle")?.Attribute(W + "val")?.Value;
        int headingLevel = ParseHeadingLevel(style);
        if (headingLevel > 0)
        {
            document.AddHeading(headingLevel, text);
            return;
        }

        if (paragraph.Element(W + "pPr")?.Element(W + "numPr") is not null)
        {
            document.AddBullet(text);
            return;
        }

        document.AddParagraph(text);
    }

    private static SimpleDocument ExtractPptx(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var slideEntries = archive.Entries
            .Where(static e => e.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase)
                               && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Select(e => new { Entry = e, Number = ParseSlideNumber(e.FullName) })
            .Where(static x => x.Number > 0)
            .OrderBy(static x => x.Number)
            .ToList();

        var document = new SimpleDocument();
        foreach (var slide in slideEntries)
        {
            using Stream stream = slide.Entry.Open();
            XDocument xml = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
            var texts = xml.Descendants(A + "t")
                .Select(t => t.Value.Trim())
                .Where(static x => x.Length > 0)
                .ToList();

            if (texts.Count == 0)
            {
                document.AddHeading(2, $"第 {slide.Number} 页");
                document.AddParagraph("（此页没有可提取文字）");
                continue;
            }

            document.AddHeading(2, $"第 {slide.Number} 页 · {texts[0]}");
            foreach (string text in texts.Skip(1))
            {
                document.AddParagraph(text);
            }
        }

        return document;
    }

    private static int ParseSlideNumber(string entryName)
    {
        string file = Path.GetFileNameWithoutExtension(entryName);
        return int.TryParse(file.AsSpan("slide".Length), out int number) ? number : 0;
    }

    private static int ParseHeadingLevel(string? style)
    {
        if (string.IsNullOrWhiteSpace(style)) return 0;
        Match match = Regex.Match(style, @"(?:heading|标题)\s*([1-6])", RegexOptions.IgnoreCase);
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    private static int CountHeadingLevel(string trimmedLine)
    {
        int count = 0;
        while (count < trimmedLine.Length && count < 6 && trimmedLine[count] == '#') count++;
        if (count == 0 || count >= trimmedLine.Length || trimmedLine[count] != ' ') return 0;
        return count;
    }

    private static bool TryGetBulletText(string trimmedLine, out string text)
    {
        text = string.Empty;
        if (trimmedLine.StartsWith("- ", StringComparison.Ordinal)
            || trimmedLine.StartsWith("* ", StringComparison.Ordinal)
            || trimmedLine.StartsWith("+ ", StringComparison.Ordinal))
        {
            text = trimmedLine[2..].Trim();
            return text.Length > 0;
        }

        Match numbered = Regex.Match(trimmedLine, @"^\d+[.)]\s+(.+)$");
        if (numbered.Success)
        {
            text = numbered.Groups[1].Value.Trim();
            return text.Length > 0;
        }

        return false;
    }

    private static void Flush(StringBuilder paragraph, SimpleDocument document)
    {
        if (paragraph.Length == 0) return;
        document.AddParagraph(paragraph.ToString().Trim());
        paragraph.Clear();
    }
}
