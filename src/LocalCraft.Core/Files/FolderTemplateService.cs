using LocalCraft.Core.Journal;
using LocalCraft.Core.Operations;

namespace LocalCraft.Core.Files;

public sealed class TemplateItem
{
    public string RelativePath { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public bool IsDirectory { get; init; }
    public bool Exists { get; init; }
    public string KindText => IsDirectory ? "文件夹" : "文件";
    public string StatusText => Exists ? "已存在" : "将创建";
}

public sealed class TemplatePlan
{
    public string Root { get; init; } = string.Empty;
    public IReadOnlyList<TemplateItem> Items { get; init; } = Array.Empty<TemplateItem>();
    public string? Error { get; init; }
    public bool Success => string.IsNullOrEmpty(Error);
}

public sealed class FolderTemplateService
{
    private readonly JournalRepository _repository;

    public FolderTemplateService(JournalRepository repository)
    {
        _repository = repository;
    }

    public TemplatePlan Plan(string root, string template)
    {
        if (string.IsNullOrWhiteSpace(root)) return new TemplatePlan { Error = "请选择目标文件夹。" };
        string fullRoot = Path.GetFullPath(root);
        var items = new List<TemplateItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string raw in template.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            bool isDirectory = line.EndsWith('/') || line.EndsWith('\\');
            string relative = line.TrimEnd('/', '\\').Replace('\\', '/').Trim();
            if (relative.Length == 0) continue;
            if (relative.StartsWith('/') || relative.Contains(':'))
                return new TemplatePlan { Error = "模板只能使用相对路径：" + line };

            string full = Path.GetFullPath(Path.Combine(fullRoot, relative));
            string rootWithSeparator = fullRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                return new TemplatePlan { Error = "模板路径越出目标文件夹：" + line };
            if (!seen.Add(full)) continue;

            items.Add(new TemplateItem
            {
                RelativePath = relative,
                FullPath = full,
                IsDirectory = isDirectory,
                Exists = isDirectory ? Directory.Exists(full) : File.Exists(full)
            });
        }

        if (items.Count == 0) return new TemplatePlan { Error = "模板为空。" };
        return new TemplatePlan { Root = fullRoot, Items = items };
    }

    public SplitResult Execute(TemplatePlan plan, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!plan.Success || plan.Items.Count == 0) return new SplitResult(false, null, plan.Error ?? "没有可创建的目录。");
        var pending = plan.Items.Where(x => !x.Exists).ToList();
        if (pending.Count == 0) return new SplitResult(false, null, "所有目录和文件都已存在。");

        var journal = new OperationJournal
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = "template",
            DisplayName = "创建目录结构",
            SourceDirectory = plan.Root,
            CreatedAt = DateTimeOffset.Now,
            Status = JournalStatus.Prepared,
            Operations = pending.Select((item, index) => new JournalOperation
            {
                Sequence = index + 1,
                SourcePath = plan.Root,
                TargetPath = item.FullPath,
                Note = item.IsDirectory ? "dir" : "file",
                State = JournalOperationState.Pending
            }).ToList()
        };

        try { _repository.Save(journal); }
        catch (Exception ex) { return new SplitResult(false, null, "无法写入操作日志，未创建目录：" + ex.Message); }

        int created = 0;
        try
        {
            for (int i = 0; i < pending.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = pending[i];
                progress?.Report(new OperationProgress(i, pending.Count, item.RelativePath, item.IsDirectory ? "创建文件夹" : "创建文件"));
                if (item.IsDirectory)
                {
                    Directory.CreateDirectory(item.FullPath);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(item.FullPath) ?? plan.Root);
                    using (File.Create(item.FullPath)) { }
                }
                journal.Operations[i].State = JournalOperationState.Completed;
                created++;
                _repository.Save(journal);
            }

            journal.Status = JournalStatus.Completed;
            journal.CompletedAt = DateTimeOffset.Now;
            _repository.Save(journal);
            return new SplitResult(true, journal, $"已创建 {created} 个目录/文件。");
        }
        catch (Exception ex)
        {
            journal.Status = JournalStatus.Failed;
            journal.Error = ex.Message;
            try { _repository.Save(journal); } catch { }
            return new SplitResult(false, journal, "创建目录结构失败：" + ex.Message);
        }
    }
}
