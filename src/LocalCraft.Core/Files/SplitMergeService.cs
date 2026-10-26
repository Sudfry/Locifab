using LocalCraft.Core.Journal;
using LocalCraft.Core.Operations;

namespace LocalCraft.Core.Files;

public enum SplitMode
{
    BySize,
    ByParts
}

public sealed class SplitOptions
{
    public string SourceFile { get; set; } = string.Empty;
    public string OutputFolder { get; set; } = string.Empty;
    public SplitMode Mode { get; set; } = SplitMode.BySize;
    public double SizeMb { get; set; } = 10;
    public int Parts { get; set; } = 2;
}

public sealed class SplitPart
{
    public int Index { get; init; }
    public string TargetPath { get; init; } = string.Empty;
    public long Length { get; init; }
    public string SizeText => FormatSize(Length);
    public string FileName => Path.GetFileName(TargetPath);

    internal static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = bytes;
        int index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }
        return $"{value:0.#} {units[index]}";
    }
}

public sealed class SplitPlan
{
    public string SourceFile { get; init; } = string.Empty;
    public string OutputFolder { get; init; } = string.Empty;
    public IReadOnlyList<SplitPart> Parts { get; init; } = Array.Empty<SplitPart>();
    public string? Error { get; init; }
    public bool Success => string.IsNullOrEmpty(Error);
    public long TotalBytes => Parts.Sum(x => x.Length);
}

public sealed class MergePlan
{
    public string OutputFile { get; init; } = string.Empty;
    public IReadOnlyList<string> Sources { get; init; } = Array.Empty<string>();
    public long TotalBytes { get; init; }
    public string? Error { get; init; }
    public bool Success => string.IsNullOrEmpty(Error);
}

public sealed class SplitMergeService
{
    private readonly JournalRepository _repository;

    public SplitMergeService(JournalRepository repository)
    {
        _repository = repository;
    }

    public SplitPlan PlanSplit(SplitOptions options)
    {
        if (!File.Exists(options.SourceFile))
        {
            return new SplitPlan { Error = "请选择存在的源文件。" };
        }

        string source = Path.GetFullPath(options.SourceFile);
        long length = new FileInfo(source).Length;
        if (length == 0)
        {
            return new SplitPlan { Error = "空文件不需要拆分。" };
        }

        long chunk = options.Mode == SplitMode.BySize
            ? (long)Math.Max(1, Math.Round(options.SizeMb * 1024 * 1024))
            : (long)Math.Ceiling(length / (double)Math.Clamp(options.Parts, 2, 9999));
        chunk = Math.Max(1, chunk);
        int count = (int)Math.Ceiling(length / (double)chunk);
        if (count > 9999)
        {
            return new SplitPlan { Error = "拆分数量超过 9999，请增大每块大小。" };
        }

        string output = string.IsNullOrWhiteSpace(options.OutputFolder)
            ? Path.Combine(Path.GetDirectoryName(source) ?? string.Empty, Path.GetFileName(source) + "_parts")
            : Path.GetFullPath(options.OutputFolder);
        var parts = new List<SplitPart>();
        for (int i = 0; i < count; i++)
        {
            long offset = i * chunk;
            long size = Math.Min(chunk, length - offset);
            string target = Path.Combine(output, $"{Path.GetFileName(source)}.part{i + 1:000}");
            if (File.Exists(target) || Directory.Exists(target))
            {
                return new SplitPlan { Error = "输出分片已存在，请换一个输出文件夹：" + target };
            }

            parts.Add(new SplitPart { Index = i + 1, TargetPath = target, Length = size });
        }

        return new SplitPlan { SourceFile = source, OutputFolder = output, Parts = parts };
    }

    public BatchExecutionResult ExecuteSplit(SplitPlan plan, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!plan.Success || plan.Parts.Count == 0)
        {
            return new BatchExecutionResult(false, null, plan.Error ?? "没有可执行的分片。");
        }

        var journal = new OperationJournal
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = "split",
            DisplayName = "文件拆分",
            SourceDirectory = Path.GetDirectoryName(plan.SourceFile) ?? string.Empty,
            CreatedAt = DateTimeOffset.Now,
            Status = JournalStatus.Prepared,
            Operations = plan.Parts.Select((part, index) => new JournalOperation
            {
                Sequence = index + 1,
                SourcePath = plan.SourceFile,
                TargetPath = part.TargetPath,
                State = JournalOperationState.Pending
            }).ToList()
        };

        try
        {
            _repository.Save(journal);
        }
        catch (Exception ex)
        {
            return new BatchExecutionResult(false, null, "无法写入操作日志，未拆分文件：" + ex.Message);
        }

        var created = new List<string>();
        try
        {
            Directory.CreateDirectory(plan.OutputFolder);
            using var source = new FileStream(plan.SourceFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            foreach (var part in plan.Parts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new OperationProgress(part.Index - 1, plan.Parts.Count, part.FileName, "正在拆分"));
                using (var output = new FileStream(part.TargetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] buffer = new byte[128 * 1024];
                    long remaining = part.Length;
                    while (remaining > 0)
                    {
                        int read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                        if (read <= 0) throw new IOException("源文件读取提前结束。");
                        output.Write(buffer, 0, read);
                        remaining -= read;
                    }
                }

                created.Add(part.TargetPath);
                journal.Operations[part.Index - 1].State = JournalOperationState.Completed;
                _repository.Save(journal);
            }

            journal.Status = JournalStatus.Completed;
            journal.CompletedAt = DateTimeOffset.Now;
            _repository.Save(journal);
            return new BatchExecutionResult(true, journal, $"已拆分为 {plan.Parts.Count} 个分片；原文件未修改。");
        }
        catch (Exception ex)
        {
            foreach (string path in created.AsEnumerable().Reverse())
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }

            journal.Status = JournalStatus.Failed;
            journal.Error = ex.Message;
            try { _repository.Save(journal); } catch { }
            return new BatchExecutionResult(false, journal, "拆分失败：" + ex.Message + " 已清理本次生成的分片。");
        }
    }

    public MergePlan PlanMerge(string folder, string pattern, string outputFile)
    {
        if (!Directory.Exists(folder))
        {
            return new MergePlan { Error = "请选择分片所在文件夹。" };
        }

        string filter = string.IsNullOrWhiteSpace(pattern) ? "*.part*" : pattern.Trim();
        List<string> sources;
        try
        {
            sources = Directory.EnumerateFiles(folder, filter, SearchOption.TopDirectoryOnly)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            return new MergePlan { Error = "扫描分片失败：" + ex.Message };
        }

        if (sources.Count == 0)
        {
            return new MergePlan { Error = "没有找到匹配的分片文件。" };
        }

        string first = Path.GetFileName(sources[0]);
        int partIndex = first.LastIndexOf(".part", StringComparison.OrdinalIgnoreCase);
        string originalName = partIndex > 0 ? first[..partIndex] : first;
        string output = string.IsNullOrWhiteSpace(outputFile)
            ? Path.Combine(folder, "merged" + Path.GetExtension(originalName))
            : Path.GetFullPath(outputFile);
        if (File.Exists(output))
        {
            return new MergePlan { Error = "输出文件已存在，请换一个文件名。" };
        }

        long total = sources.Sum(x => new FileInfo(x).Length);
        return new MergePlan { OutputFile = output, Sources = sources, TotalBytes = total };
    }

    public BatchExecutionResult ExecuteMerge(MergePlan plan, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!plan.Success || plan.Sources.Count == 0)
        {
            return new BatchExecutionResult(false, null, plan.Error ?? "没有可合并的分片。");
        }

        var journal = new OperationJournal
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = "merge",
            DisplayName = "文件合并",
            SourceDirectory = Path.GetDirectoryName(plan.Sources[0]) ?? string.Empty,
            CreatedAt = DateTimeOffset.Now,
            Status = JournalStatus.Prepared,
            Operations =
            {
                new JournalOperation
                {
                    Sequence = 1,
                    SourcePath = plan.Sources[0],
                    TargetPath = plan.OutputFile,
                    State = JournalOperationState.Pending
                }
            }
        };

        try
        {
            _repository.Save(journal);
        }
        catch (Exception ex)
        {
            return new BatchExecutionResult(false, null, "无法写入操作日志，未合并文件：" + ex.Message);
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(plan.OutputFile) ?? string.Empty);
            using var output = new FileStream(plan.OutputFile, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            for (int i = 0; i < plan.Sources.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new OperationProgress(i, plan.Sources.Count, Path.GetFileName(plan.Sources[i]), "正在合并"));
                using var input = new FileStream(plan.Sources[i], FileMode.Open, FileAccess.Read, FileShare.Read);
                input.CopyTo(output, 128 * 1024);
            }

            journal.Operations[0].State = JournalOperationState.Completed;
            journal.Status = JournalStatus.Completed;
            journal.CompletedAt = DateTimeOffset.Now;
            _repository.Save(journal);
            return new BatchExecutionResult(true, journal, $"已合并 {plan.Sources.Count} 个分片；分片文件未删除。");
        }
        catch (Exception ex)
        {
            try { if (File.Exists(plan.OutputFile)) File.Delete(plan.OutputFile); } catch { }
            journal.Status = JournalStatus.Failed;
            journal.Error = ex.Message;
            try { _repository.Save(journal); } catch { }
            return new BatchExecutionResult(false, journal, "合并失败：" + ex.Message + " 已清理未完成的输出。");
        }
    }
}
