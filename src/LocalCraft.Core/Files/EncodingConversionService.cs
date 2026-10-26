using System.Text;
using LocalCraft.Core.Journal;
using LocalCraft.Core.Operations;

namespace LocalCraft.Core.Files;

public enum TextEncodingTarget
{
    Utf8,
    Utf8Bom,
    Gbk
}

public enum NewlineTarget
{
    Keep,
    Crlf,
    Lf
}

public enum SourceEncodingMode
{
    Auto,
    Utf8,
    Gbk
}

public sealed class EncodingOptions
{
    public string Folder { get; set; } = string.Empty;
    public bool IncludeSubfolders { get; set; } = true;
    public string Extensions { get; set; } = ".txt,.md,.csv,.json,.xml,.html,.css,.js,.cs,.log,.ini,.yml,.yaml";
    public string OutputFolder { get; set; } = string.Empty;
    public SourceEncodingMode SourceEncoding { get; set; } = SourceEncodingMode.Auto;
    public TextEncodingTarget Target { get; set; } = TextEncodingTarget.Utf8;
    public NewlineTarget Newline { get; set; } = NewlineTarget.Keep;
}

public sealed class EncodingPlanItem
{
    public string SourcePath { get; init; } = string.Empty;
    public string OutputPath { get; init; } = string.Empty;
    public string EncodingName { get; init; } = string.Empty;
    public long Size { get; init; }
    public string FileName => Path.GetFileName(SourcePath);
    public string SizeText => SplitPart.FormatSize(Size);
}

public sealed class EncodingPlan
{
    public IReadOnlyList<EncodingPlanItem> Items { get; init; } = Array.Empty<EncodingPlanItem>();
    public int SkippedCount { get; init; }
    public string? Error { get; init; }
    public bool Success => string.IsNullOrEmpty(Error);
}

public sealed class EncodingConversionService
{
    private readonly JournalRepository _repository;

    static EncodingConversionService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public EncodingConversionService(JournalRepository repository)
    {
        _repository = repository;
    }

    public EncodingPlan Plan(EncodingOptions options)
    {
        if (!Directory.Exists(options.Folder)) return new EncodingPlan { Error = "请选择存在的文件夹。" };
        string root = Path.GetFullPath(options.Folder);
        string outputRoot = string.IsNullOrWhiteSpace(options.OutputFolder)
            ? Path.Combine(root, "_编码转换")
            : Path.GetFullPath(options.OutputFolder);
        if (string.Equals(root, outputRoot, StringComparison.OrdinalIgnoreCase))
            return new EncodingPlan { Error = "输出文件夹不能与源文件夹相同。" };

        var extensions = options.Extensions.Split(new[] { ',', ';', ' ', '，', '；' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim().TrimStart('*'))
            .Where(x => x.Length > 0)
            .Select(x => x.StartsWith('.') ? x : "." + x)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = options.IncludeSubfolders,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false
        };
        var items = new List<EncodingPlanItem>();
        int skipped = 0;

        foreach (string path in Directory.EnumerateFiles(root, "*", enumeration))
        {
            if (extensions.Count > 0 && !extensions.Contains(Path.GetExtension(path))) continue;
            try
            {
                if (IsUnder(path, outputRoot)) continue;
                byte[] bytes = File.ReadAllBytes(path);
                string text = Decode(bytes, options.SourceEncoding, out string encodingName);
                if (text.IndexOf('\0') >= 0) { skipped++; continue; }
                string relative = Path.GetRelativePath(root, path);
                string targetDir = Path.Combine(outputRoot, Path.GetDirectoryName(relative) ?? string.Empty);
                string target = Path.Combine(targetDir, Path.GetFileNameWithoutExtension(path) + "_编码" + Path.GetExtension(path));
                if (File.Exists(target)) target = Unique(target);
                items.Add(new EncodingPlanItem
                {
                    SourcePath = path,
                    OutputPath = target,
                    EncodingName = encodingName,
                    Size = bytes.LongLength
                });
            }
            catch
            {
                skipped++;
            }
        }

        return new EncodingPlan { Items = items, SkippedCount = skipped };
    }

    public SplitResult Execute(EncodingPlan plan, EncodingOptions options,
        IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!plan.Success || plan.Items.Count == 0) return new SplitResult(false, null, "没有可转换的文本文件。");
        var journal = new OperationJournal
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = "encoding",
            DisplayName = "批量编码转换",
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

        try { _repository.Save(journal); }
        catch (Exception ex) { return new SplitResult(false, null, "无法写入操作日志，未转换文件：" + ex.Message); }

        var created = new List<string>();
        try
        {
            for (int i = 0; i < plan.Items.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = plan.Items[i];
                progress?.Report(new OperationProgress(i, plan.Items.Count, item.FileName, "正在转换编码"));
                Directory.CreateDirectory(Path.GetDirectoryName(item.OutputPath) ?? string.Empty);
                string text = Decode(File.ReadAllBytes(item.SourcePath), options.SourceEncoding, out _);
                text = NormalizeNewlines(text, options.Newline);
                Encoding encoding = options.Target switch
                {
                    TextEncodingTarget.Utf8Bom => new UTF8Encoding(true),
                    TextEncodingTarget.Gbk => Encoding.GetEncoding(936),
                    _ => new UTF8Encoding(false)
                };
                string temp = item.OutputPath + ".tmp";
                try
                {
                    File.WriteAllText(temp, text, encoding);
                    File.Move(temp, item.OutputPath);
                }
                finally
                {
                    try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                }

                created.Add(item.OutputPath);
                journal.Operations[i].State = JournalOperationState.Completed;
                _repository.Save(journal);
            }

            journal.Status = JournalStatus.Completed;
            journal.CompletedAt = DateTimeOffset.Now;
            _repository.Save(journal);
            return new SplitResult(true, journal, $"已转换 {plan.Items.Count} 个文本文件；原文件未修改。");
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
            return new SplitResult(false, journal, "编码转换失败：" + ex.Message + " 已清理未完成的输出。");
        }
    }

    public static string Decode(byte[] bytes, out string encodingName) =>
        Decode(bytes, SourceEncodingMode.Auto, out encodingName);

    public static string Decode(byte[] bytes, SourceEncodingMode source, out string encodingName)
    {
        if (source == SourceEncodingMode.Utf8)
        {
            encodingName = "UTF-8（指定）";
            return new UTF8Encoding(false).GetString(bytes).TrimStart('\uFEFF');
        }
        if (source == SourceEncodingMode.Gbk)
        {
            encodingName = "GBK（指定）";
            return Encoding.GetEncoding(936).GetString(bytes);
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            encodingName = "UTF-8 BOM";
            return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            encodingName = "UTF-16 LE";
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            encodingName = "UTF-16 BE";
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        try
        {
            var strict = new UTF8Encoding(false, true);
            string text = strict.GetString(bytes);
            encodingName = "UTF-8";
            return text;
        }
        catch (DecoderFallbackException)
        {
            encodingName = "GBK";
            return Encoding.GetEncoding(936).GetString(bytes);
        }
    }

    private static string NormalizeNewlines(string text, NewlineTarget target)
    {
        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        return target switch
        {
            NewlineTarget.Crlf => normalized.Replace("\n", "\r\n"),
            NewlineTarget.Lf => normalized,
            _ => text
        };
    }

    private static bool IsUnder(string path, string root)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static string Unique(string path)
    {
        string directory = Path.GetDirectoryName(path) ?? string.Empty;
        string name = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        for (int i = 2; i < 10_000; i++)
        {
            string candidate = Path.Combine(directory, $"{name} ({i}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }
        throw new IOException("无法生成不冲突的输出路径：" + path);
    }
}
