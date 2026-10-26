using System.Text.Json;
using LocalCraft.Core.Journal;
using LocalCraft.Core.Operations;

namespace LocalCraft.Core.Files;

public enum TimestampMode
{
    Fixed,
    Offset
}

public sealed class TimestampOptions
{
    public string Folder { get; set; } = string.Empty;
    public bool IncludeSubfolders { get; set; } = true;
    public bool IncludeHidden { get; set; }
    public string Extensions { get; set; } = string.Empty;
    public TimestampMode Mode { get; set; } = TimestampMode.Fixed;
    public DateTime FixedTime { get; set; } = DateTime.Now;
    public double OffsetDays { get; set; }
    public bool ApplyCreated { get; set; } = true;
    public bool ApplyModified { get; set; } = true;
    public bool ApplyAccessed { get; set; }
}

public sealed class TimestampPlanItem
{
    public string Path { get; init; } = string.Empty;
    public string FileName => System.IO.Path.GetFileName(Path);
    public DateTime OldCreated { get; init; }
    public DateTime OldModified { get; init; }
    public DateTime OldAccessed { get; init; }
    public DateTime NewCreated { get; init; }
    public DateTime NewModified { get; init; }
    public DateTime NewAccessed { get; init; }
    public string OldText => OldModified.ToString("yyyy-MM-dd HH:mm:ss");
    public string NewText => NewModified.ToString("yyyy-MM-dd HH:mm:ss");
}

public sealed class TimestampPlan
{
    public IReadOnlyList<TimestampPlanItem> Items { get; init; } = Array.Empty<TimestampPlanItem>();
    public int SkippedCount { get; init; }
    public string? Error { get; init; }
    public bool Success => string.IsNullOrEmpty(Error);
}

public sealed class AttributeOptions
{
    public string Folder { get; set; } = string.Empty;
    public bool IncludeSubfolders { get; set; } = true;
    public bool IncludeHidden { get; set; } = true;
    public string Extensions { get; set; } = string.Empty;
    public bool SetReadOnly { get; set; }
    public bool SetHidden { get; set; }
    public bool SetArchive { get; set; }
    public bool Remove { get; set; }
}

public sealed class AttributePlanItem
{
    public string Path { get; init; } = string.Empty;
    public string FileName => System.IO.Path.GetFileName(Path);
    public FileAttributes OldAttributes { get; init; }
    public FileAttributes NewAttributes { get; init; }
    public string OldText => Describe(OldAttributes);
    public string NewText => Describe(NewAttributes);

    public static string Describe(FileAttributes attributes)
    {
        var parts = new List<string>();
        if (attributes.HasFlag(FileAttributes.ReadOnly)) parts.Add("只读");
        if (attributes.HasFlag(FileAttributes.Hidden)) parts.Add("隐藏");
        if (attributes.HasFlag(FileAttributes.Archive)) parts.Add("存档");
        if (parts.Count == 0) parts.Add("普通");
        return string.Join(" / ", parts);
    }
}

public sealed class AttributePlan
{
    public IReadOnlyList<AttributePlanItem> Items { get; init; } = Array.Empty<AttributePlanItem>();
    public int SkippedCount { get; init; }
    public string? Error { get; init; }
    public bool Success => string.IsNullOrEmpty(Error);
}

public sealed class FileStateService
{
    private readonly JournalRepository _repository;

    public FileStateService(JournalRepository repository)
    {
        _repository = repository;
    }

    public TimestampPlan PlanTimestamps(TimestampOptions options)
    {
        if (!Directory.Exists(options.Folder)) return new TimestampPlan { Error = "请选择存在的文件夹。" };
        if (!options.ApplyCreated && !options.ApplyModified && !options.ApplyAccessed)
            return new TimestampPlan { Error = "请至少选择一种时间。" };
        var files = ScanFiles(options.Folder, options.IncludeSubfolders, options.IncludeHidden, options.Extensions);
        var items = new List<TimestampPlanItem>();
        int skipped = 0;
        foreach (string path in files)
        {
            try
            {
                DateTime created = File.GetCreationTime(path);
                DateTime modified = File.GetLastWriteTime(path);
                DateTime accessed = File.GetLastAccessTime(path);
                DateTime New(DateTime old) => options.Mode == TimestampMode.Fixed
                    ? options.FixedTime
                    : old.AddDays(options.OffsetDays);
                items.Add(new TimestampPlanItem
                {
                    Path = path,
                    OldCreated = created,
                    OldModified = modified,
                    OldAccessed = accessed,
                    NewCreated = New(created),
                    NewModified = New(modified),
                    NewAccessed = New(accessed)
                });
            }
            catch
            {
                skipped++;
            }
        }

        return new TimestampPlan { Items = items, SkippedCount = skipped };
    }

    public SplitResult ExecuteTimestamps(TimestampPlan plan, TimestampOptions options,
        IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!plan.Success || plan.Items.Count == 0) return new SplitResult(false, null, "没有可修改时间戳的文件。");
        var journal = NewJournal("timestamp", "批量修改时间戳", plan.Items[0].Path, plan.Items.Select((item, index) =>
        {
            var old = new StateNote
            {
                Created = options.ApplyCreated ? item.OldCreated : null,
                Modified = options.ApplyModified ? item.OldModified : null,
                Accessed = options.ApplyAccessed ? item.OldAccessed : null
            };
            return new JournalOperation
            {
                Sequence = index + 1,
                SourcePath = item.Path,
                TargetPath = item.Path,
                Note = JsonSerializer.Serialize(old),
                State = JournalOperationState.Pending
            };
        }).ToList());
        var actions = new List<(int Index, Action Apply)>();
        for (int i = 0; i < plan.Items.Count; i++)
        {
            var item = plan.Items[i];
            actions.Add((i, () =>
            {
                if (options.ApplyCreated) File.SetCreationTime(item.Path, item.NewCreated);
                if (options.ApplyModified) File.SetLastWriteTime(item.Path, item.NewModified);
                if (options.ApplyAccessed) File.SetLastAccessTime(item.Path, item.NewAccessed);
            }));
        }
        return ApplyState(journal, actions, progress, cancellationToken, "时间戳已更新。");
    }

    public AttributePlan PlanAttributes(AttributeOptions options)
    {
        if (!Directory.Exists(options.Folder)) return new AttributePlan { Error = "请选择存在的文件夹。" };
        var files = ScanFiles(options.Folder, options.IncludeSubfolders, options.IncludeHidden, options.Extensions);
        var items = new List<AttributePlanItem>();
        int skipped = 0;
        foreach (string path in files)
        {
            try
            {
                FileAttributes old = File.GetAttributes(path);
                FileAttributes flags = FileAttributes.Normal;
                if (options.SetReadOnly) flags |= FileAttributes.ReadOnly;
                if (options.SetHidden) flags |= FileAttributes.Hidden;
                if (options.SetArchive) flags |= FileAttributes.Archive;
                FileAttributes target = options.Remove
                    ? old & ~flags
                    : old | flags;
                items.Add(new AttributePlanItem { Path = path, OldAttributes = old, NewAttributes = target });
            }
            catch
            {
                skipped++;
            }
        }
        return new AttributePlan { Items = items, SkippedCount = skipped };
    }

    public SplitResult ExecuteAttributes(AttributePlan plan, AttributeOptions options,
        IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!plan.Success || plan.Items.Count == 0) return new SplitResult(false, null, "没有可修改属性的文件。");
        var journal = NewJournal("attributes", "批量修改文件属性", plan.Items[0].Path, plan.Items.Select((item, index) =>
            new JournalOperation
            {
                Sequence = index + 1,
                SourcePath = item.Path,
                TargetPath = item.Path,
                Note = ((int)item.OldAttributes).ToString(),
                State = JournalOperationState.Pending
            }).ToList());
        var actions = new List<(int Index, Action Apply)>();
        for (int i = 0; i < plan.Items.Count; i++)
        {
            var item = plan.Items[i];
            actions.Add((i, () => File.SetAttributes(item.Path, item.NewAttributes)));
        }
        return ApplyState(journal, actions, progress, cancellationToken, "文件属性已更新。");
    }

    private SplitResult ApplyState(OperationJournal journal, List<(int Index, Action Apply)> actions,
        IProgress<OperationProgress>? progress, CancellationToken cancellationToken, string successMessage)
    {
        try { _repository.Save(journal); }
        catch (Exception ex) { return new SplitResult(false, null, "无法写入操作日志，未修改文件：" + ex.Message); }

        int completed = 0;
        try
        {
            foreach (var (index, apply) in actions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var operation = journal.Operations[index];
                progress?.Report(new OperationProgress(completed, actions.Count, Path.GetFileName(operation.SourcePath), "正在修改"));
                if (!File.Exists(operation.SourcePath)) throw new IOException("文件已不存在：" + operation.SourcePath);
                apply();
                operation.State = JournalOperationState.Completed;
                completed++;
                if (completed % 20 == 0) _repository.Save(journal);
            }

            journal.Status = JournalStatus.Completed;
            journal.CompletedAt = DateTimeOffset.Now;
            _repository.Save(journal);
            return new SplitResult(true, journal, successMessage);
        }
        catch (Exception ex)
        {
            journal.Status = JournalStatus.Failed;
            journal.Error = ex.Message;
            try { _repository.Save(journal); } catch { }
            return new SplitResult(false, journal, "修改失败：" + ex.Message);
        }
    }

    private static OperationJournal NewJournal(string kind, string name, string sourceDirectory, List<JournalOperation> operations) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Kind = kind,
        DisplayName = name,
        SourceDirectory = Path.GetDirectoryName(sourceDirectory) ?? string.Empty,
        CreatedAt = DateTimeOffset.Now,
        Status = JournalStatus.Prepared,
        Operations = operations
    };

    private static List<string> ScanFiles(string folder, bool subfolders, bool hidden, string extensions)
    {
        var set = extensions.Split(new[] { ',', ';', ' ', '，', '；' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.StartsWith('.') ? x : "." + x)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = subfolders,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = hidden ? 0 : FileAttributes.Hidden | FileAttributes.System
        };
        return Directory.EnumerateFiles(folder, "*", enumeration)
            .Where(x => set.Count == 0 || set.Contains(Path.GetExtension(x)))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed class StateNote
    {
        public DateTime? Created { get; set; }
        public DateTime? Modified { get; set; }
        public DateTime? Accessed { get; set; }
    }
}

public sealed record SplitResult(bool Success, OperationJournal? Journal, string Message);
