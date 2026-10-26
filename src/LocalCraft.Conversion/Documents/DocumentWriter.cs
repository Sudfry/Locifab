using System.IO.Compression;
using System.Net;
using System.Security;
using System.Text;
using LocalCraft.Conversion.Formats;

namespace LocalCraft.Conversion.Documents;

internal static class DocumentWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void Write(SimpleDocument document, string targetPath, string targetExtension)
    {
        switch (SourceFormats.NormalizeExtension(targetExtension))
        {
            case ".txt":
                File.WriteAllText(targetPath, RenderPlainText(document), Utf8NoBom);
                break;
            case ".md":
                File.WriteAllText(targetPath, RenderMarkdown(document), Utf8NoBom);
                break;
            case ".html":
                File.WriteAllText(targetPath, RenderHtml(document), Utf8NoBom);
                break;
            case ".docx":
                WriteDocx(document, targetPath);
                break;
            case ".pdf":
                PdfRenderer.Write(document, targetPath);
                break;
            default:
                throw new NotSupportedException($"不支持的文档输出格式：{targetExtension}");
        }
    }

    public static string RenderPlainText(SimpleDocument document)
    {
        var builder = new StringBuilder();
        foreach (var block in document.Blocks)
        {
            if (builder.Length > 0) builder.AppendLine();
            switch (block.Kind)
            {
                case DocumentBlockKind.Heading:
                    builder.AppendLine(block.Text);
                    break;
                case DocumentBlockKind.Bullet:
                    builder.Append("• ").AppendLine(block.Text);
                    break;
                default:
                    builder.AppendLine(block.Text);
                    break;
            }
        }

        return builder.ToString();
    }

    public static string RenderMarkdown(SimpleDocument document)
    {
        var builder = new StringBuilder();
        foreach (var block in document.Blocks)
        {
            if (builder.Length > 0) builder.AppendLine();
            switch (block.Kind)
            {
                case DocumentBlockKind.Heading:
                    builder.Append('#', Math.Clamp(block.Level, 1, 6))
                        .Append(' ')
                        .AppendLine(block.Text);
                    break;
                case DocumentBlockKind.Bullet:
                    builder.Append("- ").AppendLine(block.Text);
                    break;
                default:
                    builder.AppendLine(block.Text);
                    break;
            }
        }

        return builder.ToString();
    }

    public static string RenderHtml(SimpleDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"zh-CN\">");
        builder.AppendLine("<head>");
        builder.AppendLine("  <meta charset=\"utf-8\" />");
        builder.AppendLine("  <meta name=\"generator\" content=\"LocalCraft\" />");
        builder.AppendLine("  <title>LocalCraft 导出文档</title>");
        builder.AppendLine("  <style>");
        builder.AppendLine("    body { max-width: 860px; margin: 40px auto; padding: 0 28px; font-family: \"Microsoft YaHei\", \"Noto Sans SC\", sans-serif; line-height: 1.8; color: #1b2927; }");
        builder.AppendLine("    h1, h2, h3, h4, h5, h6 { line-height: 1.4; margin-top: 1.4em; }");
        builder.AppendLine("    p { margin: 0.8em 0; }");
        builder.AppendLine("    ul { padding-left: 1.6em; }");
        builder.AppendLine("  </style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");

        bool inList = false;
        foreach (var block in document.Blocks)
        {
            if (block.Kind == DocumentBlockKind.Bullet)
            {
                if (!inList)
                {
                    builder.AppendLine("<ul>");
                    inList = true;
                }

                builder.Append("  <li>").Append(WebUtility.HtmlEncode(block.Text)).AppendLine("</li>");
                continue;
            }

            if (inList)
            {
                builder.AppendLine("</ul>");
                inList = false;
            }

            if (block.Kind == DocumentBlockKind.Heading)
            {
                int level = Math.Clamp(block.Level, 1, 6);
                builder.Append($"<h{level}>")
                    .Append(WebUtility.HtmlEncode(block.Text))
                    .AppendLine($"</h{level}>");
            }
            else
            {
                builder.Append("<p>").Append(WebUtility.HtmlEncode(block.Text)).AppendLine("</p>");
            }
        }

        if (inList) builder.AppendLine("</ul>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

    private static void WriteDocx(SimpleDocument document, string targetPath)
    {
        string? directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        using var stream = new FileStream(targetPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);

        WriteEntry(archive, "[Content_Types].xml", ContentTypesXml);
        WriteEntry(archive, "_rels/.rels", RootRelationshipsXml);
        WriteEntry(archive, "word/_rels/document.xml.rels", DocumentRelationshipsXml);
        WriteEntry(archive, "word/styles.xml", StylesXml);
        WriteEntry(archive, "word/document.xml", BuildDocumentXml(document));
    }

    private static string BuildDocumentXml(SimpleDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        builder.AppendLine("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">");
        builder.AppendLine("<w:body>");

        foreach (var block in document.Blocks)
        {
            string escaped = SecurityElement.Escape(block.Text) ?? string.Empty;
            switch (block.Kind)
            {
                case DocumentBlockKind.Heading:
                {
                    int level = Math.Clamp(block.Level, 1, 3);
                    builder.Append("<w:p><w:pPr><w:pStyle w:val=\"Heading")
                        .Append(level)
                        .Append("\"/></w:pPr><w:r><w:t xml:space=\"preserve\">")
                        .Append(escaped)
                        .AppendLine("</w:t></w:r></w:p>");
                    break;
                }
                case DocumentBlockKind.Bullet:
                    builder.Append("<w:p><w:pPr><w:ind w:left=\"420\" w:hanging=\"210\"/></w:pPr><w:r><w:t xml:space=\"preserve\">• ")
                        .Append(escaped)
                        .AppendLine("</w:t></w:r></w:p>");
                    break;
                default:
                    builder.Append("<w:p><w:r><w:t xml:space=\"preserve\">")
                        .Append(escaped)
                        .AppendLine("</w:t></w:r></w:p>");
                    break;
            }
        }

        builder.AppendLine("<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"851\" w:footer=\"992\" w:gutter=\"0\"/></w:sectPr>");
        builder.AppendLine("</w:body>");
        builder.AppendLine("</w:document>");
        return builder.ToString();
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using Stream entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream, Utf8NoBom);
        writer.Write(content);
    }

    private const string ContentTypesXml = """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
  <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
</Types>
""";

    private const string RootRelationshipsXml = """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
</Relationships>
""";

    private const string DocumentRelationshipsXml = """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
</Relationships>
""";

    private const string StylesXml = """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
  <w:docDefaults>
    <w:rPrDefault>
      <w:rPr>
        <w:rFonts w:ascii="Microsoft YaHei" w:hAnsi="Microsoft YaHei" w:eastAsia="Microsoft YaHei"/>
        <w:sz w:val="22"/>
        <w:szCs w:val="22"/>
        <w:lang w:val="en-US" w:eastAsia="zh-CN"/>
      </w:rPr>
    </w:rPrDefault>
    <w:pPrDefault>
      <w:pPr><w:spacing w:after="160" w:line="300" w:lineRule="auto"/></w:pPr>
    </w:pPrDefault>
  </w:docDefaults>
  <w:style w:type="paragraph" w:default="1" w:styleId="Normal">
    <w:name w:val="Normal"/><w:qFormat/>
  </w:style>
  <w:style w:type="paragraph" w:styleId="Heading1">
    <w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:qFormat/>
    <w:pPr><w:keepNext/><w:spacing w:before="320" w:after="160"/><w:outlineLvl w:val="0"/></w:pPr>
    <w:rPr><w:b/><w:sz w:val="36"/><w:szCs w:val="36"/></w:rPr>
  </w:style>
  <w:style w:type="paragraph" w:styleId="Heading2">
    <w:name w:val="heading 2"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:qFormat/>
    <w:pPr><w:keepNext/><w:spacing w:before="260" w:after="120"/><w:outlineLvl w:val="1"/></w:pPr>
    <w:rPr><w:b/><w:sz w:val="28"/><w:szCs w:val="28"/></w:rPr>
  </w:style>
  <w:style w:type="paragraph" w:styleId="Heading3">
    <w:name w:val="heading 3"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:qFormat/>
    <w:pPr><w:keepNext/><w:spacing w:before="220" w:after="100"/><w:outlineLvl w:val="2"/></w:pPr>
    <w:rPr><w:b/><w:sz w:val="24"/><w:szCs w:val="24"/></w:rPr>
  </w:style>
</w:styles>
""";
}
