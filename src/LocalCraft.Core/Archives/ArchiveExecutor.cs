using System.IO.Compression;
using LocalCraft.Core.Journal;
using LocalCraft.Core.Operations;

namespace LocalCraft.Core.Archives;

public sealed class ArchiveExecutor
{
    private readonly JournalRepository _repository;

    public ArchiveExecutor(JournalRepository repository)
    {
        _repository = repository;
    }

    public BatchExecutionResult Execute(
        ArchivePlan plan,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!plan.Success || plan.Entries.Count == 0)
        {
            return new BatchExecutionResult(false, null, plan.Error ?? "没有可执行的归档操作。");
        }

        var journal = new OperationJournal
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = "zip",
            DisplayName = plan.Mode == ArchiveMode.Compress ? "ZIP 压缩" : "ZIP 解压",
            SourceDirectory = plan.Mode == ArchiveMode.Compress
                ? plan.SourcePath
                : Path.GetDirectoryName(plan.SourcePath) ?? string.Empty,
            CreatedAt = DateTimeOffset.Now,
            Status = JournalStatus.Prepared,
            Operations = plan.Mode == ArchiveMode.Compress
                ? new List<JournalOperation>
                {
                    new()
                    {
                        Sequence = 1,
                        SourcePath = plan.SourcePath,
                        TargetPath = plan.TargetPath,
                        State = JournalOperationState.Pending
                    }
                }
                : plan.Entries.Select((entry, index) => new JournalOperation
                {
                    Sequence = index + 1,
                    SourcePath = plan.SourcePath,
                    TargetPath = entry.TargetPath,
                    State = JournalOperationState.Pending
                }).ToList()
        };

        try
        {
            _repository.Save(journal);
        }
        catch (Exception ex)
        {
            return new BatchExecutionResult(false, null, "无法写入归档日志，未修改文件：" + ex.Message);
        }

        var created = new List<string>();
        try
        {
            if (plan.Mode == ArchiveMode.Compress)
            {
                Compress(plan, journal, created, progress, cancellationToken);
            }
            else
            {
                Extract(plan, journal, created, progress, cancellationToken);
            }

            journal.Status = JournalStatus.Completed;
            journal.CompletedAt = DateTimeOffset.Now;
            _repository.Save(journal);
            string action = plan.Mode == ArchiveMode.Compress ? "压缩" : "解压";
            return new BatchExecutionResult(
                true,
                journal,
                $"已{action} {plan.Entries.Count} 个文件，可在回退中心删除本次输出。");
        }
        catch (Exception ex)
        {
            foreach (string path in created.AsEnumerable().Reverse())
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch { }
            }

            journal.Status = JournalStatus.RolledBack;
            journal.Error = ex.Message;
            try { _repository.Save(journal); } catch { }
            string message = ex is OperationCanceledException ? "归档操作已取消。" : "归档失败：" + ex.Message;
            return new BatchExecutionResult(false, journal, message + " 已清理本次未完成的输出。");
        }
    }

    private static void Compress(
        ArchivePlan plan,
        OperationJournal journal,
        List<string> created,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        string temp = plan.TargetPath + ".tmp";
        if (File.Exists(temp)) File.Delete(temp);

        try
        {
            if (!string.IsNullOrEmpty(plan.Password))
            {
                ZipCrypto.Create(plan.Entries, plan.SourcePath, temp, plan.Password,
                    (current, total, name) =>
                    {
                        progress?.Report(new OperationProgress(current, total, name,
                            current < total ? "正在加密压缩" : "已加入加密压缩包"));
                    }, cancellationToken);
            }
            else
            {
                using var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
                for (int i = 0; i < plan.Entries.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = plan.Entries[i];
                    progress?.Report(new OperationProgress(i, plan.Entries.Count, entry.Name, "正在压缩"));
                    string fullSource = Path.Combine(plan.SourcePath, entry.Name.Replace('/', Path.DirectorySeparatorChar));
                    archive.CreateEntryFromFile(fullSource, entry.Name, CompressionLevel.Optimal);
                    progress?.Report(new OperationProgress(i + 1, plan.Entries.Count, entry.Name, "已加入压缩包"));
                }
            }

            if (File.Exists(plan.TargetPath))
            {
                throw new IOException("目标 ZIP 已存在，拒绝覆盖。");
            }

            File.Move(temp, plan.TargetPath);
            created.Add(plan.TargetPath);
            journal.Operations[0].State = JournalOperationState.Completed;
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            throw;
        }
    }

    private static void Extract(
        ArchivePlan plan,
        OperationJournal journal,
        List<string> created,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(plan.Password))
        {
            for (int i = 0; i < plan.Entries.Count; i++)
            {
                ArchivePlanEntry planned = plan.Entries[i];
                if (File.Exists(planned.TargetPath) || Directory.Exists(planned.TargetPath))
                    throw new IOException("目标文件在预览后出现冲突：" + planned.TargetPath);
            }
            ZipCrypto.Extract(plan, plan.Password, (current, total, name) =>
            {
                progress?.Report(new OperationProgress(current, total, name,
                    current < total ? "正在解密解压" : "已解密解压"));
            }, cancellationToken);
            for (int i = 0; i < plan.Entries.Count; i++)
            {
                created.Add(plan.Entries[i].TargetPath);
                journal.Operations[i].TargetPath = plan.Entries[i].TargetPath;
                journal.Operations[i].State = JournalOperationState.Completed;
            }
            return;
        }

        using ZipArchive archive = ZipFile.OpenRead(plan.SourcePath);
        var entriesByPath = archive.Entries
            .Where(x => !string.IsNullOrEmpty(x.Name))
            .GroupBy(x => ArchivePlanner.NormalizeEntry(x.FullName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < plan.Entries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var planned = plan.Entries[i];
            if (!entriesByPath.TryGetValue(
                    planned.Name.Replace('/', Path.DirectorySeparatorChar),
                    out ZipArchiveEntry? entry))
            {
                throw new IOException("ZIP 条目已变化：" + planned.Name);
            }

            if (File.Exists(planned.TargetPath) || Directory.Exists(planned.TargetPath))
            {
                throw new IOException("目标文件在预览后出现冲突：" + planned.TargetPath);
            }

            progress?.Report(new OperationProgress(i, plan.Entries.Count, planned.Name, "正在解压"));
            string? directory = Path.GetDirectoryName(planned.TargetPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            entry.ExtractToFile(planned.TargetPath, overwrite: false);
            created.Add(planned.TargetPath);
            journal.Operations[i].TargetPath = planned.TargetPath;
            journal.Operations[i].State = JournalOperationState.Completed;
            progress?.Report(new OperationProgress(i + 1, plan.Entries.Count, planned.Name, "已解压"));
        }
    }
}
