using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LocalCraft.Conversion.Documents;

/// <summary>
/// 无外部依赖的 PDF 导出：用 WPF FlowDocument 做中文排版，将页面渲染为位图后封装为 PDF。
/// 优点是完全离线、中文显示稳定；PDF 中文字为页面图像，不包含可选中文本层。
/// </summary>
internal static class PdfRenderer
{
    private const double A4WidthDip = 793.7;
    private const double A4HeightDip = 1122.5;
    private const double RenderDpi = 144;
    private const double PdfWidthPoints = 595.28;
    private const double PdfHeightPoints = 841.89;

    public static void Write(SimpleDocument document, string targetPath)
    {
        byte[][] pages = RenderPages(document);
        if (pages.Length == 0)
        {
            throw new InvalidOperationException("无法生成 PDF：文档没有可排版的页面。");
        }

        WritePdf(targetPath, pages);
    }

    private static byte[][] RenderPages(SimpleDocument document)
    {
        byte[][]? result = null;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                var flow = BuildFlowDocument(document);
                var paginator = ((IDocumentPaginatorSource)flow).DocumentPaginator;
                paginator.PageSize = new Size(A4WidthDip, A4HeightDip);
                paginator.ComputePageCount();

                int pageCount = Math.Max(1, paginator.PageCount);
                var pages = new List<byte[]>(pageCount);
                int pixelWidth = (int)Math.Ceiling(A4WidthDip * RenderDpi / 96.0);
                int pixelHeight = (int)Math.Ceiling(A4HeightDip * RenderDpi / 96.0);

                for (int i = 0; i < pageCount; i++)
                {
                    DocumentPage page = paginator.GetPage(i);
                    var visual = new DrawingVisual();
                    using (DrawingContext drawing = visual.RenderOpen())
                    {
                        drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, A4WidthDip, A4HeightDip));
                        if (page.Visual is not null)
                        {
                            drawing.DrawRectangle(
                                new VisualBrush(page.Visual) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                                null,
                                new Rect(0, 0, A4WidthDip, A4HeightDip));
                        }
                    }

                    var bitmap = new RenderTargetBitmap(
                        pixelWidth,
                        pixelHeight,
                        RenderDpi,
                        RenderDpi,
                        PixelFormats.Pbgra32);
                    bitmap.Render(visual);
                    pages.Add(ToRgbFlate(bitmap));
                }

                result = pages.ToArray();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new InvalidOperationException("PDF 排版渲染失败：" + failure.Message, failure);
        }

        return result ?? Array.Empty<byte[]>();
    }

    private static FlowDocument BuildFlowDocument(SimpleDocument document)
    {
        var flow = new FlowDocument
        {
            PageWidth = A4WidthDip,
            PageHeight = A4HeightDip,
            PagePadding = new Thickness(
                Math.Max(20, DocumentTypography.PageMargin),
                Math.Max(20, DocumentTypography.PageMargin * 0.75),
                Math.Max(20, DocumentTypography.PageMargin),
                Math.Max(20, DocumentTypography.PageMargin * 0.75)),
            ColumnWidth = double.PositiveInfinity,
            FontFamily = new FontFamily(DocumentTypography.FontFamily),
            FontSize = DocumentTypography.BodyFontSize,
            Foreground = Brushes.Black,
            Background = Brushes.White
        };

        foreach (var block in document.Blocks)
        {
            Paragraph paragraph;
            switch (block.Kind)
            {
                case DocumentBlockKind.Heading:
                {
                    int level = Math.Clamp(block.Level, 1, 6);
                    paragraph = new Paragraph
                    {
                        FontSize = DocumentTypography.BodyFontSize * level switch { 1 => 1.8, 2 => 1.5, 3 => 1.3, 4 => 1.15, 5 => 1.08, _ => 1.0 },
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, level == 1 ? 6 : 16, 0, 10),
                        LineHeight = DocumentTypography.BodyFontSize * DocumentTypography.LineHeightFactor * (level <= 2 ? 1.2 : 1.0),
                        KeepWithNext = true
                    };
                    AddInline(paragraph, block.Text);
                    break;
                }
                case DocumentBlockKind.Bullet:
                    paragraph = new Paragraph
                    {
                        Margin = new Thickness(16, 0, 0, 6),
                        LineHeight = DocumentTypography.BodyFontSize * DocumentTypography.LineHeightFactor,
                        TextIndent = -14
                    };
                    paragraph.Inlines.Add(new Run("•  "));
                    AddInline(paragraph, block.Text);
                    break;
                default:
                    paragraph = new Paragraph
                    {
                        Margin = new Thickness(0, 0, 0, 9),
                        LineHeight = DocumentTypography.BodyFontSize * DocumentTypography.LineHeightFactor
                    };
                    AddInline(paragraph, block.Text);
                    break;
            }

            flow.Blocks.Add(paragraph);
        }

        if (flow.Blocks.Count == 0)
        {
            flow.Blocks.Add(new Paragraph(new Run("（空文档）")));
        }

        return flow;
    }

    private static void AddInline(Paragraph paragraph, string text)
    {
        // 支持基础 Markdown 强调与 HTML 上下标，输出到 PDF 时保留视觉层次。
        int index = 0;
        while (index < text.Length)
        {
            if (TryConsumeTag(text, index, "**", out string? bold, out int consumed)
                || TryConsumeTag(text, index, "__", out bold, out consumed))
            {
                paragraph.Inlines.Add(new Run(bold) { FontWeight = FontWeights.Bold });
                index += consumed;
                continue;
            }

            if (TryConsumeTag(text, index, "<sup>", "</sup>", out string? superscript, out consumed))
            {
                paragraph.Inlines.Add(new Run(superscript) { BaselineAlignment = BaselineAlignment.Superscript, FontSize = 9 });
                index += consumed;
                continue;
            }

            if (TryConsumeTag(text, index, "<sub>", "</sub>", out string? subscript, out consumed))
            {
                paragraph.Inlines.Add(new Run(subscript) { BaselineAlignment = BaselineAlignment.Subscript, FontSize = 9 });
                index += consumed;
                continue;
            }

            if (TryConsumeTag(text, index, "<b>", "</b>", out string? htmlBold, out consumed))
            {
                paragraph.Inlines.Add(new Run(htmlBold) { FontWeight = FontWeights.Bold });
                index += consumed;
                continue;
            }

            if (TryConsumeTag(text, index, "<i>", "</i>", out string? italic, out consumed))
            {
                paragraph.Inlines.Add(new Run(italic) { FontStyle = FontStyles.Italic });
                index += consumed;
                continue;
            }

            if (text[index] == '*' && index + 1 < text.Length)
            {
                int close = text.IndexOf('*', index + 1);
                if (close > index + 1)
                {
                    paragraph.Inlines.Add(new Run(text[(index + 1)..close]) { FontStyle = FontStyles.Italic });
                    index = close + 1;
                    continue;
                }
            }

            paragraph.Inlines.Add(new Run(text[index].ToString()));
            index++;
        }
    }

    private static bool TryConsumeTag(string text, int index, string delimiter, out string? content, out int consumed)
    {
        return TryConsumeTag(text, index, delimiter, delimiter, out content, out consumed);
    }

    private static bool TryConsumeTag(
        string text,
        int index,
        string open,
        string close,
        out string? content,
        out int consumed)
    {
        content = null;
        consumed = 0;
        if (!text.AsSpan(index).StartsWith(open, StringComparison.Ordinal)) return false;

        int end = text.IndexOf(close, index + open.Length, StringComparison.Ordinal);
        if (end < 0) return false;

        content = text[(index + open.Length)..end];
        consumed = end + close.Length - index;
        return true;
    }

    private static byte[] ToRgbFlate(BitmapSource bitmap)
    {
        int width = bitmap.PixelWidth;
        int height = bitmap.PixelHeight;
        int sourceStride = width * 4;
        byte[] bgra = new byte[sourceStride * height];
        bitmap.CopyPixels(bgra, sourceStride, 0);

        int targetStride = width * 3;
        byte[] rgb = new byte[targetStride * height];
        for (int y = 0; y < height; y++)
        {
            int sourceRow = y * sourceStride;
            int targetRow = y * targetStride;
            for (int x = 0; x < width; x++)
            {
                int sourceIndex = sourceRow + x * 4;
                int targetIndex = targetRow + x * 3;
                int alpha = bgra[sourceIndex + 3];
                int blue = bgra[sourceIndex];
                int green = bgra[sourceIndex + 1];
                int red = bgra[sourceIndex + 2];
                rgb[targetIndex] = (byte)((blue * alpha + 255 * (255 - alpha) + 127) / 255);
                rgb[targetIndex + 1] = (byte)((green * alpha + 255 * (255 - alpha) + 127) / 255);
                rgb[targetIndex + 2] = (byte)((red * alpha + 255 * (255 - alpha) + 127) / 255);
            }
        }

        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(rgb, 0, rgb.Length);
        }

        return output.ToArray();
    }

    private static void WritePdf(string targetPath, byte[][] compressedPages)
    {
        var objects = new List<byte[]>();
        int pageCount = compressedPages.Length;
        int firstPageObject = 3;

        var kids = new StringBuilder();
        for (int i = 0; i < pageCount; i++)
        {
            if (i > 0) kids.Append(' ');
            kids.Append(firstPageObject + i * 3).Append(" 0 R");
        }

        objects.Add(Ascii("<< /Type /Catalog /Pages 2 0 R >>"));
        objects.Add(Ascii($"<< /Type /Pages /Kids [{kids}] /Count {pageCount} >>"));

        for (int i = 0; i < pageCount; i++)
        {
            int pageObject = firstPageObject + i * 3;
            int contentObject = pageObject + 1;
            int imageObject = pageObject + 2;

            objects.Add(Ascii(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PdfWidthPoints:0.##} {PdfHeightPoints:0.##}] " +
                $"/Resources << /XObject << /Im0 {imageObject} 0 R >> >> /Contents {contentObject} 0 R >>"));

            string content = $"q\n{PdfWidthPoints:0.##} 0 0 {PdfHeightPoints:0.##} 0 0 cm\n/Im0 Do\nQ\n";
            byte[] contentBytes = Ascii(content);
            objects.Add(BuildStreamObject($"<< /Length {contentBytes.Length} >>", contentBytes));

            int pixelWidth = (int)Math.Ceiling(A4WidthDip * RenderDpi / 96.0);
            int pixelHeight = (int)Math.Ceiling(A4HeightDip * RenderDpi / 96.0);
            string imageDictionary =
                $"<< /Type /XObject /Subtype /Image /Width {pixelWidth} /Height {pixelHeight} " +
                $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /Length {compressedPages[i].Length} >>";
            objects.Add(BuildStreamObject(imageDictionary, compressedPages[i]));
        }

        var output = new MemoryStream();
        WriteBytes(output, Encoding.ASCII.GetBytes("%PDF-1.4\n"));
        WriteBytes(output, new byte[] { 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A });

        var offsets = new long[objects.Count + 1];
        for (int i = 0; i < objects.Count; i++)
        {
            offsets[i + 1] = output.Position;
            WriteBytes(output, Ascii($"{i + 1} 0 obj\n"));
            WriteBytes(output, objects[i]);
            WriteBytes(output, Ascii("\nendobj\n"));
        }

        long xrefOffset = output.Position;
        WriteBytes(output, Ascii($"xref\n0 {objects.Count + 1}\n"));
        WriteBytes(output, Ascii("0000000000 65535 f \n"));
        for (int i = 1; i <= objects.Count; i++)
        {
            WriteBytes(output, Ascii($"{offsets[i]:D10} 00000 n \n"));
        }

        WriteBytes(output, Ascii($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R /Producer (LocalCraft) >>\n"));
        WriteBytes(output, Ascii($"startxref\n{xrefOffset}\n%%EOF\n"));

        string? directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllBytes(targetPath, output.ToArray());
    }

    private static byte[] BuildStreamObject(string dictionary, byte[] data)
    {
        using var output = new MemoryStream();
        WriteBytes(output, Ascii(dictionary + "\nstream\n"));
        WriteBytes(output, data);
        WriteBytes(output, Ascii("\nendstream"));
        return output.ToArray();
    }

    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);

    private static void WriteBytes(Stream stream, byte[] bytes) => stream.Write(bytes, 0, bytes.Length);
}
