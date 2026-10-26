using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalCraft.Core.Files;
using LocalCraft.Core.Journal;
using LocalCraft.Core.Operations;

namespace LocalCraft.Conversion.Images;

public sealed class StitchOptions
{
    public IReadOnlyList<string> Files { get; set; } = Array.Empty<string>();
    public string OutputPath { get; set; } = string.Empty;
    public bool Vertical { get; set; } = true;
    public int Gap { get; set; } = 8;
    public string BackgroundHex { get; set; } = "#FFFFFF";
    public int MaxWidth { get; set; } = 1600;
    public int JpegQuality { get; set; } = 90;
    public int Columns { get; set; }
    public int Rows { get; set; }
    public bool OuterBorder { get; set; }
    public string BorderHex { get; set; } = "#FFFFFF";
    public int BorderWidth { get; set; } = 8;
}

public sealed class StitchItem
{
    public string Path { get; init; } = string.Empty;
    public int Width { get; init; }
    public int Height { get; init; }
    public int DrawWidth { get; init; }
    public int DrawHeight { get; init; }
    public int Column { get; init; }
    public int Row { get; init; }
    public string FileName => System.IO.Path.GetFileName(Path);
    public string SizeText => $"{Width}×{Height} → {DrawWidth}×{DrawHeight}";
}

public sealed class StitchPlan
{
    public IReadOnlyList<StitchItem> Items { get; init; } = Array.Empty<StitchItem>();
    public int CanvasWidth { get; init; }
    public int CanvasHeight { get; init; }
    public int CellWidth { get; init; }
    public int CellHeight { get; init; }
    public int Columns { get; init; }
    public int Rows { get; init; }
    public string OutputPath { get; init; } = string.Empty;
    public string? Error { get; init; }
    public bool Success => string.IsNullOrEmpty(Error);
}

public sealed class ImageStitchService
{
    private readonly JournalRepository _repository;

    public ImageStitchService(JournalRepository repository)
    {
        _repository = repository;
    }

    public StitchPlan Plan(StitchOptions options)
    {
        var files = options.Files.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (files.Count < 2) return new StitchPlan { Error = "至少选择 2 张图片。" };
        if (string.IsNullOrWhiteSpace(options.OutputPath)) return new StitchPlan { Error = "请选择输出文件。" };
        string output = Path.GetFullPath(options.OutputPath);
        if (File.Exists(output)) return new StitchPlan { Error = "输出文件已存在，请换一个文件名。" };

        bool grid = options.Columns > 0 && options.Rows > 0;
        int columns = grid ? Math.Clamp(options.Columns, 1, 10) : 1;
        int rows = grid ? Math.Clamp(options.Rows, 1, 10) : 0;
        if (grid && files.Count > columns * rows)
            return new StitchPlan { Error = $"当前布局只能放 {columns * rows} 张图片，请减少图片或选择更大的布局。" };

        var items = new List<StitchItem>();
        int index = 0;
        foreach (string file in files)
        {
            try
            {
                using var stream = File.OpenRead(file);
                BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                BitmapFrame frame = decoder.Frames[0];
                int width = frame.PixelWidth;
                int height = frame.PixelHeight;
                int max = grid
                    ? Math.Max(64, options.MaxWidth / columns)
                    : Math.Max(64, options.MaxWidth);
                if (width > max)
                {
                    double scale = max / (double)width;
                    width = max;
                    height = Math.Max(1, (int)Math.Round(frame.PixelHeight * scale));
                }

                items.Add(new StitchItem
                {
                    Path = file,
                    Width = frame.PixelWidth,
                    Height = frame.PixelHeight,
                    DrawWidth = width,
                    DrawHeight = height,
                    Column = grid ? index % columns : 0,
                    Row = grid ? index / columns : 0
                });
                index++;
            }
            catch
            {
                return new StitchPlan { Error = "无法读取图片：" + file };
            }
        }

        int gap = Math.Max(0, options.Gap);
        int border = options.OuterBorder ? Math.Clamp(options.BorderWidth, 1, 80) : 0;
        int canvasWidth;
        int canvasHeight;
        int cellWidth = 0;
        int cellHeight = 0;
        if (grid)
        {
            cellWidth = items.Max(x => x.DrawWidth);
            cellHeight = items.Max(x => x.DrawHeight);
            canvasWidth = border * 2 + columns * cellWidth + gap * (columns - 1);
            canvasHeight = border * 2 + rows * cellHeight + gap * (rows - 1);
        }
        else
        {
            canvasWidth = options.Vertical
                ? items.Max(x => x.DrawWidth) + border * 2
                : items.Sum(x => x.DrawWidth) + gap * (items.Count - 1) + border * 2;
            canvasHeight = options.Vertical
                ? items.Sum(x => x.DrawHeight) + gap * (items.Count - 1) + border * 2
                : items.Max(x => x.DrawHeight) + border * 2;
        }

        if (canvasWidth > 16000 || canvasHeight > 16000)
            return new StitchPlan { Error = "拼接结果超过 16000 像素，请降低宽度或减少图片。" };

        return new StitchPlan
        {
            Items = items,
            CanvasWidth = canvasWidth,
            CanvasHeight = canvasHeight,
            CellWidth = cellWidth,
            CellHeight = cellHeight,
            Columns = columns,
            Rows = rows,
            OutputPath = output
        };
    }

    public SplitResult Execute(StitchPlan plan, StitchOptions options, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!plan.Success || plan.Items.Count == 0) return new SplitResult(false, null, plan.Error ?? "没有可拼接的图片。");
        var journal = new OperationJournal
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = "stitch",
            DisplayName = "图片拼接",
            SourceDirectory = Path.GetDirectoryName(plan.Items[0].Path) ?? string.Empty,
            CreatedAt = DateTimeOffset.Now,
            Status = JournalStatus.Prepared,
            Operations =
            {
                new JournalOperation
                {
                    Sequence = 1,
                    SourcePath = plan.Items[0].Path,
                    TargetPath = plan.OutputPath,
                    State = JournalOperationState.Pending
                }
            }
        };

        try { _repository.Save(journal); }
        catch (Exception ex) { return new SplitResult(false, null, "无法写入操作日志，未生成拼接图片：" + ex.Message); }

        try
        {
            var background = ParseColor(options.BackgroundHex);
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(background), null,
                    new Rect(0, 0, plan.CanvasWidth, plan.CanvasHeight));
                int gap = Math.Max(0, options.Gap);
                int border = options.OuterBorder ? Math.Clamp(options.BorderWidth, 1, 80) : 0;
                int x = border;
                int y = border;
                for (int i = 0; i < plan.Items.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var item = plan.Items[i];
                    progress?.Report(new OperationProgress(i, plan.Items.Count, item.FileName, "正在拼接"));
                    using var stream = File.OpenRead(item.Path);
                    BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    BitmapSource frame = decoder.Frames[0];
                    Rect rect;
                    if (plan.Columns > 0)
                    {
                        int cellX = border + item.Column * (plan.CellWidth + gap);
                        int cellY = border + item.Row * (plan.CellHeight + gap);
                        rect = new Rect(
                            cellX + (plan.CellWidth - item.DrawWidth) / 2.0,
                            cellY + (plan.CellHeight - item.DrawHeight) / 2.0,
                            item.DrawWidth, item.DrawHeight);
                    }
                    else if (options.Vertical)
                    {
                        rect = new Rect((plan.CanvasWidth - item.DrawWidth) / 2.0, y, item.DrawWidth, item.DrawHeight);
                        y += item.DrawHeight + gap;
                    }
                    else
                    {
                        rect = new Rect(x, (plan.CanvasHeight - item.DrawHeight) / 2.0, item.DrawWidth, item.DrawHeight);
                        x += item.DrawWidth + gap;
                    }
                    dc.DrawImage(frame, rect);
                }

                if (border > 0)
                {
                    var borderBrush = new SolidColorBrush(ParseColor(options.BorderHex));
                    dc.DrawRectangle(null, new Pen(borderBrush, border),
                        new Rect(border / 2.0, border / 2.0,
                            Math.Max(1, plan.CanvasWidth - border),
                            Math.Max(1, plan.CanvasHeight - border)));
                }
            }

            var target = new RenderTargetBitmap(plan.CanvasWidth, plan.CanvasHeight, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);
            string extension = Path.GetExtension(plan.OutputPath).ToLowerInvariant();
            BitmapEncoder encoder = extension == ".jpg" || extension == ".jpeg"
                ? new JpegBitmapEncoder { QualityLevel = Math.Clamp(options.JpegQuality, 1, 100) }
                : new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(Flatten(target, extension)));
            Directory.CreateDirectory(Path.GetDirectoryName(plan.OutputPath) ?? string.Empty);
            string temp = plan.OutputPath + ".tmp";
            try
            {
                using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    encoder.Save(output);
                }
                File.Move(temp, plan.OutputPath);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }

            journal.Operations[0].State = JournalOperationState.Completed;
            journal.Status = JournalStatus.Completed;
            journal.CompletedAt = DateTimeOffset.Now;
            _repository.Save(journal);
            return new SplitResult(true, journal, $"已拼接 {plan.Items.Count} 张图片：{plan.CanvasWidth}×{plan.CanvasHeight}。");
        }
        catch (Exception ex)
        {
            try { if (File.Exists(plan.OutputPath)) File.Delete(plan.OutputPath); } catch { }
            journal.Status = JournalStatus.Failed;
            journal.Error = ex.Message;
            try { _repository.Save(journal); } catch { }
            return new SplitResult(false, journal, "拼接失败：" + ex.Message);
        }
    }

    private static BitmapSource Flatten(BitmapSource source, string extension)
    {
        if (extension != ".jpg" && extension != ".jpeg") return source;
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

    private static Color ParseColor(string hex)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(string.IsNullOrWhiteSpace(hex) ? "#FFFFFF" : hex);
        }
        catch
        {
            return Colors.White;
        }
    }
}
