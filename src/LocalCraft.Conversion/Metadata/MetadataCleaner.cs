using System.IO.Compression;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using LocalCraft.Core.Journal;
using LocalCraft.Core.Operations;

namespace LocalCraft.Conversion.Metadata;

public sealed class MetadataPlanItem
{
    public string SourcePath { get; init; } = string.Empty;

    public string OutputPath { get; init; } = string.Empty;

    public bool IsOffice { get; init; }

    public string FileName => Path.GetFileName(SourcePath);

    public string KindText => IsOffice ? "Office 文档" : "图片";
}

public sealed class MetadataPlan
{
    public IReadOnlyList<MetadataPlanItem> Items { get; init; } = Array.Empty<MetadataPlanItem>();

    public int SkippedCount { get; init; }

    public string? Error { get; init; }

    public bool Success => string.IsNullOrEmpty(Error);
}

public sealed class MetadataResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public OperationJournal? Journal { get; init; }
}

public sealed class MetadataCleaner
{
    private static readonly HashSet<string> OfficeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx", ".pptx", ".xlsx"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff"
    };

    private readonly JournalRepository _repository;

    public MetadataCleaner(JournalRepository repository)
    {
        _repository = repository;
    }

    public MetadataPlan Plan(string folder, bool includeSubfolders, string outputFolder)
    {
        if (!Directory.Exists(folder))
        {
            return new MetadataPlan { Error = "请选择存在的文件夹。" };
        }

        string root = Path.GetFullPath(folder);
        string outputRoot = string.IsNullOrWhiteSpace(outputFolder)
            ? Path.Combine(root, "_去元数据")
            : Path.GetFullPath(outputFolder);
        if (string.Equals(root, outputRoot, StringComparison.OrdinalIgnoreCase))
        {
            return new MetadataPlan { Error = "输出文件夹不能与源文件夹相同。" };
        }

        var items = new List<MetadataPlanItem>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int skipped = 0;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = includeSubfolders,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false
        };

        try
        {
            foreach (string path in Directory.EnumerateFiles(root, "*", options))
            {
                string extension = Path.GetExtension(path);
                bool isOffice = OfficeExtensions.Contains(extension);
                bool isImage = ImageExtensions.Contains(extension);
                if (!isOffice && !isImage) continue;

                try
                {
                    string relative = Path.GetRelativePath(root, path);
                    string targetDirectory = Path.Combine(outputRoot, Path.GetDirectoryName(relative) ?? string.Empty);
                    string baseName = Path.GetFileNameWithoutExtension(path) + "_去元数据" + Path.GetExtension(path);
                    string output = UniquePath(Path.Combine(targetDirectory, baseName), used);
                    items.Add(new MetadataPlanItem
                    {
                        SourcePath = Path.GetFullPath(path),
                        OutputPath = output,
                        IsOffice = isOffice
                    });
                }
                catch
                {
                    skipped++;
                }
            }
        }
        catch (Exception ex)
        {
            return new MetadataPlan { Error = "扫描失败：" + ex.Message };
        }

        return new MetadataPlan { Items = items, SkippedCount = skipped };
    }

    public MetadataResult Execute(
        MetadataPlan plan,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!plan.Success || plan.Items.Count == 0)
        {
            return new MetadataResult { Success = false, Message = plan.Error ?? "没有可清理的文件。" };
        }

        var journal = new OperationJournal
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = "metadata",
            DisplayName = "隐私元数据清理",
            SourceDirectory = Path.GetDirectoryName(plan.Items[0].SourcePath) ?? string.Empty,
            CreatedAt = DateTimeOffset.Now,
            Status = JournalStatus.Prepared,
            Operations = plan.Items.Select((item, index) => new JournalOperation
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
            return new MetadataResult { Success = false, Message = "无法写入操作日志，未处理文件：" + ex.Message };
        }

        var created = new List<string>();
        var failures = new List<string>();
        try
        {
            for (int i = 0; i < plan.Items.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = plan.Items[i];
                progress?.Report(new OperationProgress(i, plan.Items.Count, item.FileName, "正在清理元数据"));
                Directory.CreateDirectory(Path.GetDirectoryName(item.OutputPath) ?? string.Empty);
                string temp = item.OutputPath + ".tmp";
                try
                {
                    if (item.IsOffice) CleanOffice(item.SourcePath, temp);
                    else ReencodeImage(item.SourcePath, temp, Path.GetExtension(item.OutputPath));
                    File.Move(temp, item.OutputPath);
                    created.Add(item.OutputPath);
                    journal.Operations[i].State = JournalOperationState.Completed;
                    _repository.Save(journal);
                }
                catch (Exception ex)
                {
                    failures.Add($"{item.FileName}：{ex.Message}");
                }
                finally
                {
                    try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                }
            }

            if (created.Count == 0)
            {
                journal.Status = JournalStatus.RolledBack;
                journal.Error = failures.FirstOrDefault() ?? "没有可处理的文件。";
                _repository.Save(journal);
                return new MetadataResult { Success = false, Message = "清理失败：没有文件可处理。" + (failures.Count > 0 ? " " + failures[0] : string.Empty) };
            }
            journal.Operations = journal.Operations
                .Where(x => created.Contains(x.TargetPath))
                .Select((x, index) => { x.Sequence = index; return x; })
                .ToList();
            journal.Status = JournalStatus.Completed;
            journal.CompletedAt = DateTimeOffset.Now;
            _repository.Save(journal);
            return new MetadataResult
            {
                Success = true,
                Message = $"已生成 {created.Count} 个去元数据副本，跳过 {failures.Count} 个无法处理的文件；原文件未修改。",
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
            return new MetadataResult
            {
                Success = false,
                Message = (ex is OperationCanceledException ? "清理已取消。" : "清理失败：" + ex.Message) +
                          " 已清理本次未完成的输出。"
            };
        }
    }

    private static void CleanOffice(string source, string destination)
    {
        File.Copy(source, destination, overwrite: true);
        using ZipArchive archive = ZipFile.Open(destination, ZipArchiveMode.Update);
        CleanXml(archive.GetEntry("docProps/core.xml"),
            new[] { "creator", "lastModifiedBy", "title", "subject", "description", "keywords", "created", "modified", "category", "contentStatus", "language" });
        CleanXml(archive.GetEntry("docProps/app.xml"),
            new[] { "Company", "Manager", "Template", "HyperlinkBase" });
        CleanXml(archive.GetEntry("docProps/custom.xml"), Array.Empty<string>(), blankAllValues: true);
    }

    private static void CleanXml(ZipArchiveEntry? entry, string[] names, bool blankAllValues = false)
    {
        if (entry is null) return;
        XDocument document;
        using (Stream read = entry.Open())
        {
            document = XDocument.Load(read);
        }

        foreach (XElement element in document.Descendants())
        {
            if (blankAllValues && element.HasElements == false)
            {
                element.Value = string.Empty;
            }
            else if (names.Contains(element.Name.LocalName, StringComparer.OrdinalIgnoreCase))
            {
                element.Value = string.Empty;
            }
        }

        using Stream write = entry.Open();
        write.SetLength(0);
        document.Save(write, SaveOptions.DisableFormatting);
    }

    private static void ReencodeImage(string source, string destination, string outputExtension)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        BitmapDecoder decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0) throw new InvalidOperationException("图片没有可读取的帧。");
        BitmapSource frame = decoder.Frames[0];
        string extension = outputExtension.ToLowerInvariant();
        BitmapEncoder encoder = extension switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 92 },
            ".png" => new PngBitmapEncoder(),
            ".bmp" => new BmpBitmapEncoder(),
            ".tif" or ".tiff" => new TiffBitmapEncoder { Compression = TiffCompressOption.Zip },
            _ => new PngBitmapEncoder()
        };
        BitmapSource clean = extension is ".jpg" or ".jpeg" ? Flatten(frame) : frame;
        encoder.Frames.Add(BitmapFrame.Create(clean));
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        encoder.Save(output);
    }

    private static BitmapSource Flatten(BitmapSource source)
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

    private static string UniquePath(string path, HashSet<string> used)
    {
        if (!File.Exists(path) && !Directory.Exists(path) && used.Add(path)) return path;
        string directory = Path.GetDirectoryName(path) ?? string.Empty;
        string name = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        for (int i = 2; i < 10_000; i++)
        {
            string candidate = Path.Combine(directory, $"{name} ({i}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate) && used.Add(candidate)) return candidate;
        }

        throw new IOException("无法生成不冲突的输出路径：" + path);
    }
}
