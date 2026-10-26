using LocalCraft.Core.Journal;
using LocalCraft.Core.Operations;

namespace LocalCraft.Core.Cleanup;

public sealed class CleanupExecutor
{
    private readonly JournalRepository _repository;

    public CleanupExecutor(JournalRepository repository)
    {
        _repository = repository;
    }

    public BatchExecutionResult Execute(
        CleanupPlan plan,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!plan.Success || plan.Items.Count == 0)
        {
            return new BatchExecutionResult(false, null, plan.Error ?? "没有可清理的项目。");
        }

        var journal = new OperationJournal
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = "cleanup",
            DisplayName = "空目录与零字节清理",
            SourceDirectory = Path.GetDirectoryName(plan.Items[0].Path) ?? string.Empty,
            CreatedAt = DateTimeOffset.Now,
            Status = JournalStatus.Prepared,
            Operations = plan.Items.Select((item, index) => new JournalOperation
            {
                Sequence = index + 1,
                SourcePath = item.Path,
                TargetPath = item.Path,
                Note = item.IsDirectory ? "directory" : "zero-file",
                State = JournalOperationState.Pending
            }).ToList()
        };

        try
        {
            _repository.Save(journal);
        }
        catch (Exception ex)
        {
            return new BatchExecutionResult(false, null, "无法写入清理日志，未删除任何内容：" + ex.Message);
        }

        int deleted = 0;
        try
        {
            for (int i = 0; i < plan.Items.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = plan.Items[i];
                var operation = journal.Operations[i];
                progress?.Report(new OperationProgress(i, plan.Items.Count, item.Name, "正在清理"));

                if (item.IsDirectory)
                {
                    if (Directory.Exists(item.Path) && !Directory.EnumerateFileSystemEntries(item.Path).Any())
                    {
                        Directory.Delete(item.Path, false);
                    }
                    else if (Directory.Exists(item.Path))
                    {
                        throw new IOException("目录已不再为空，已停止：" + item.Path);
                    }
                }
                else
                {
                    if (File.Exists(item.Path))
                    {
                        if (new FileInfo(item.Path).Length != 0)
                        {
                            throw new IOException("文件已不再为 0 字节，已停止：" + item.Path);
                        }

                        File.Delete(item.Path);
                    }
                }

                operation.State = JournalOperationState.Completed;
                deleted++;
                _repository.Save(journal);
                progress?.Report(new OperationProgress(i + 1, plan.Items.Count, item.Name, "已清理"));
            }

            journal.Status = JournalStatus.Completed;
            journal.CompletedAt = DateTimeOffset.Now;
            _repository.Save(journal);
            return new BatchExecutionResult(true, journal, $"已清理 {deleted} 个空目录/零字节文件，可在回退中心恢复。");
        }
        catch (Exception ex)
        {
            journal.Status = JournalStatus.Failed;
            journal.Error = ex.Message;
            try { _repository.Save(journal); } catch { }
            string message = ex is OperationCanceledException ? "清理已取消。" : "清理失败：" + ex.Message;
            message += " 已完成的清理项可在回退中心恢复。";
            return new BatchExecutionResult(false, journal, message);
        }
    }
}
