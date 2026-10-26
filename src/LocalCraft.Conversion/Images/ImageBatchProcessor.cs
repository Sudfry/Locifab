using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalCraft.Core.Journal;
using LocalCraft.Core.Operations;

namespace LocalCraft.Conversion.Images;

public enum ImageResizeMode
{
    None,
    LongEdge,
    Width,
    Percent
}

public enum ImageOutputFormat
{
    Keep,
    Jpg,
    Png
}

public sealed class ImageBatchOptions
{
    public string OutputFolder { get; set; } = string.Empty;

    public string Suffix { get; set; } = "_优化";

    public ImageResizeMode ResizeMode { get; set; } = ImageResizeMode.LongEdge;

    public int SizeValue { get; set; } = 1920;

    public int Percent { get; set; } = 80;

    public int Rotation { get; set; }

    public ImageOutputFormat Format { get; set; } = ImageOutputFormat.Keep;

    public int JpegQuality { get; set; } = 88;

    public string WatermarkText { get; set; } = string.Empty;

    public bool StripMetadata { get; set; } = true;
}

public sealed class ImageBatchPlanItem
{
    public string SourcePath { get; init; } = string.Empty;

    public string OutputPath { get; init; } = string.Empty;

    public int SourceWidth { get; init; }

    public int SourceHeight { get; init; }

    public int TargetWidth { get; init; }

    public int TargetHeight { get; init; }

    public long Size { get; init; }

    public string FileName => Path.GetFileName(SourcePath);

    public string Dimensions => $"{SourceWidth}×{SourceHeight} → {TargetWidth}×{TargetHeight}";
}

public sealed class ImageBatchPlan
{
    public IReadOnlyList<ImageBatchPlanItem> Items { get; init; } = Array.Empty<ImageBatchPlanItem>();

    public int SkippedCount { get; init; }

    public string? Error { get; init; }

    public bool Success => string.IsNullOrEmpty(Error);
}

public sealed class ImageBatchResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public OperationJournal? Journal { get; init; }
}

public sealed class ImageBatchPlanner
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp"
    };

    public IReadOnlyList<string> FindImages(string folder, bool includeSubfolders)
    {
        if (!Directory.Exists(folder)) return Array.Empty<string>();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = includeSubfolders,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false
        };
        return Directory.EnumerateFiles(folder, "*", options)
            .Where(x => Extensions.Contains(Path.GetExtension(x)))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public ImageBatchPlan Plan(IReadOnlyList<string> sourceFiles, ImageBatchOptions options)
    {
        if (sourceFiles.Count == 0)
        {
            return new ImageBatchPlan { Error = "没有可处理的图片。" };
        }

        string outputFolder = string.IsNullOrWhiteSpace(options.OutputFolder)
            ? Path.GetDirectoryName(sourceFiles[0]) ?? string.Empty
            : Path.GetFullPath(options.OutputFolder);
        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            return new ImageBatchPlan { Error = "无法确定输出文件夹。" };
        }

        var items = new List<ImageBatchPlanItem>();
        var planned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int skipped = 0;
        foreach (string sourcePath in sourceFiles)
        {
            try
            {
                if (!File.Exists(sourcePath)) { skipped++; continue; }
                BitmapDecoder decoder = Decode(sourcePath);
                if (decoder.Frames.Count == 0) { skipped++; continue; }
                BitmapFrame frame = decoder.Frames[0];
                int width = frame.PixelWidth;
                int height = frame.PixelHeight;
                (int targetWidth, int targetHeight) = ComputeSize(width, height, options);
                if (options.Rotation is 90 or 270) (targetWidth, targetHeight) = (targetHeight, targetWidth);

                string extension = OutputExtension(sourcePath, options.Format);
                string baseName = Path.GetFileNameWithoutExtension(sourcePath) + options.Suffix;
                string output = UniquePath(Path.Combine(outputFolder, baseName + extension), planned);
                planned.Add(output);
                items.Add(new ImageBatchPlanItem
                {
                    SourcePath = Path.GetFullPath(sourcePath),
                    OutputPath = output,
                    SourceWidth = width,
                    SourceHeight = height,
                    TargetWidth = Math.Max(1, targetWidth),
                    TargetHeight = Math.Max(1, targetHeight),
                    Size = new FileInfo(sourcePath).Length
                });
            }
            catch
            {
                skipped++;
            }
        }

        return new ImageBatchPlan { Items = items, SkippedCount = skipped };
    }

    public static (int Width, int Height) ComputeSize(int width, int height, ImageBatchOptions options)
    {
        double scale = options.ResizeMode switch
        {
            ImageResizeMode.LongEdge => options.SizeValue <= 0 ? 1 : Math.Min(1, options.SizeValue / (double)Math.Max(width, height)),
            ImageResizeMode.Width => options.SizeValue <= 0 ? 1 : options.SizeValue / (double)width,
            ImageResizeMode.Percent => Math.Max(1, options.Percent) / 100.0,
            _ => 1
        };
        return ((int)Math.Round(width * scale), (int)Math.Round(height * scale));
    }

    private static string OutputExtension(string sourcePath, ImageOutputFormat format)
    {
        if (format == ImageOutputFormat.Jpg) return ".jpg";
        if (format == ImageOutputFormat.Png) return ".png";
        string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        return extension is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".tif" or ".tiff" ? extension : ".png";
    }

    private static string UniquePath(string path, HashSet<string> planned)
    {
        if (!File.Exists(path) && !planned.Contains(path)) return path;
        string directory = Path.GetDirectoryName(path) ?? string.Empty;
        string name = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        for (int i = 2; i < 10_000; i++)
        {
            string candidate = Path.Combine(directory, $"{name} ({i}){extension}");
            if (!File.Exists(candidate) && !planned.Contains(candidate)) return candidate;
        }

        throw new IOException("无法生成不冲突的输出文件名：" + path);
    }

    private static BitmapDecoder Decode(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
    }
}

public sealed class ImageBatchExecutor
{
    private readonly JournalRepository _repository;

    public ImageBatchExecutor(JournalRepository repository)
    {
        _repository = repository;
    }

    public ImageBatchResult Execute(
        ImageBatchPlan plan,
        ImageBatchOptions options,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var items = plan.Items.Where(x => !File.Exists(x.OutputPath)).ToList();
        if (items.Count == 0)
        {
            return new ImageBatchResult { Success = false, Message = "没有可执行的图片输出。" };
        }

        var journal = new OperationJournal
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = "image",
            DisplayName = "批量图片处理",
            SourceDirectory = Path.GetDirectoryName(items[0].SourcePath) ?? string.Empty,
            CreatedAt = DateTimeOffset.Now,
            Status = JournalStatus.Prepared,
            Operations = items.Select((item, index) => new JournalOperation
            {
                Sequence = index + 1,
                SourcePath = item.SourcePath,
                TargetPath = item.OutputPath,
                State = JournalOperationState.Pending
            }).ToList()
        };

        try
        {
            _repository.Save(journal);
        }
        catch (Exception ex)
        {
            return new ImageBatchResult { Success = false, Message = "无法写入操作日志，未处理图片：" + ex.Message };
        }

        var created = new List<string>();
        try
        {
            for (int i = 0; i < items.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = items[i];
                progress?.Report(new OperationProgress(i, items.Count, item.FileName, "正在处理图片"));
                string? directory = Path.GetDirectoryName(item.OutputPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                string temp = item.OutputPath + ".tmp";
                try
                {
                    Process(item, options, temp);
                    if (File.Exists(item.OutputPath)) throw new IOException("目标文件在预览后出现：" + item.OutputPath);
                    File.Move(temp, item.OutputPath);
                }
                finally
                {
                    try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                }
                created.Add(item.OutputPath);
                journal.Operations[i].State = JournalOperationState.Completed;
                _repository.Save(journal);
                progress?.Report(new OperationProgress(i + 1, items.Count, item.FileName, "已输出"));
            }

            journal.Status = JournalStatus.Completed;
            journal.CompletedAt = DateTimeOffset.Now;
            _repository.Save(journal);
            return new ImageBatchResult
            {
                Success = true,
                Message = $"已处理 {items.Count} 张图片，可在回退中心删除本次输出。",
                Journal = journal
            };
        }
        catch (Exception ex)
        {
            foreach (string path in created.AsEnumerable().Reverse())
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }

            journal.Status = JournalStatus.RolledBack;
            journal.Error = ex.Message;
            try { _repository.Save(journal); } catch { }
            string message = ex is OperationCanceledException ? "图片处理已取消。" : "图片处理失败：" + ex.Message;
            return new ImageBatchResult { Success = false, Message = message + " 已清理本次未完成的输出。" };
        }
    }

    private static void Process(ImageBatchPlanItem item, ImageBatchOptions options, string outputPath)
    {
        BitmapDecoder decoder;
        using (var input = new FileStream(item.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        }

        if (decoder.Frames.Count == 0) throw new InvalidOperationException("图片没有可读取的帧。");
        BitmapSource source = decoder.Frames[0];

        BitmapSource scaled;
        if (item.TargetWidth != item.SourceWidth || item.TargetHeight != item.SourceHeight)
        {
            double scaleX = item.TargetWidth / (double)item.SourceWidth;
            double scaleY = item.TargetHeight / (double)item.SourceHeight;
            scaled = new TransformedBitmap(source, new ScaleTransform(scaleX, scaleY));
        }
        else
        {
            scaled = source;
        }

        BitmapSource rotated = scaled;
        if (options.Rotation is 90 or 180 or 270)
        {
            rotated = RotateBitmap(scaled, options.Rotation, item.TargetWidth, item.TargetHeight);
        }

        if (!string.IsNullOrWhiteSpace(options.WatermarkText))
        {
            rotated = AddWatermark(rotated, options.WatermarkText);
        }

        BitmapEncoder encoder = CreateEncoder(Path.GetExtension(outputPath), options);
        if (Path.GetExtension(outputPath).Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(outputPath).Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            encoder.Frames.Add(BitmapFrame.Create(FlattenForJpeg(rotated)));
        }
        else
        {
            encoder.Frames.Add(BitmapFrame.Create(rotated));
        }

        using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        encoder.Save(output);
    }

    private static BitmapEncoder CreateEncoder(string extension, ImageBatchOptions options)
    {
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = Math.Clamp(options.JpegQuality, 1, 100) },
            ".png" => new PngBitmapEncoder(),
            ".bmp" => new BmpBitmapEncoder(),
            ".tif" or ".tiff" => new TiffBitmapEncoder { Compression = TiffCompressOption.Zip },
            _ => new PngBitmapEncoder()
        };
    }

    private static BitmapSource RotateBitmap(BitmapSource source, int angle, int targetWidth, int targetHeight)
    {
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            double x = (targetWidth - source.PixelWidth) / 2.0;
            double y = (targetHeight - source.PixelHeight) / 2.0;
            dc.PushTransform(new RotateTransform(angle, targetWidth / 2.0, targetHeight / 2.0));
            dc.DrawImage(source, new Rect(x, y, source.PixelWidth, source.PixelHeight));
            dc.Pop();
        }

        var target = new RenderTargetBitmap(targetWidth, targetHeight, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    private static BitmapSource FlattenForJpeg(BitmapSource source)
    {
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
            dc.DrawImage(source, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
        }

        var target = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    private static BitmapSource AddWatermark(BitmapSource source, string text)
    {
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawImage(source, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
            double fontSize = Math.Max(12, Math.Min(source.PixelWidth, source.PixelHeight) * 0.045);
            var formatted = new FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface("Microsoft YaHei UI"),
                fontSize,
                Brushes.White,
                1.0)
            {
                MaxTextWidth = Math.Max(10, source.PixelWidth - 24),
                MaxTextHeight = Math.Max(10, source.PixelHeight - 24)
            };
            double x = Math.Max(12, source.PixelWidth - formatted.Width - 12);
            double y = Math.Max(12, source.PixelHeight - formatted.Height - 12);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), null,
                new Rect(x - 6, y - 4, formatted.Width + 12, formatted.Height + 8));
            dc.DrawText(formatted, new Point(x, y));
        }

        var target = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }
}
