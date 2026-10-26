using LocalCraft.Core.Journal;
using LocalCraft.Core.Operations;

namespace LocalCraft.Core.Backup;

/// <summary>
/// 只增不删的备份同步。会备份被更新文件的原目标版本，供回退使用。
/// </summary>
public sealed class BackupExecutor
{
    private readonly JournalRepository _repository;

    public BackupExecutor(JournalRepository repository)
    {
        _repository = repository;
    }

    public BatchExecutionResult Execute(
        BackupPlan plan,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var pending = plan.PendingItems;
        if (pending.Count == 0)
        {
            return new BatchExecutionResult(false, null, "没有需要新增或更新的文件。");
        }

        foreach (var item in pending)
        {
            if (!File.Exists(item.SourcePath))
            {
                return new BatchExecutionResult(false, null, "源文件已不存在：" + item.SourcePath);
            }
        }

        var journal = new OperationJournal
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = "backup",
            DisplayName = "备份同步",
            SourceDirectory = Path.GetDirectoryName(pending[0].SourcePath) ?? string.Empty,
            CreatedAt = DateTimeOffset.Now,
            Status = JournalStatus.Prepared,
            Operations = pending.Select((item, index) => new JournalOperation
            {
                Sequence = index + 1,
                SourcePath = item.SourcePath,
                TargetPath = item.TargetPath,
                OverwroteTarget = File.Exists(item.TargetPath),
                State = JournalOperationState.Pending
            }).ToList()
        };

        try
        {
            _repository.Save(journal);
        }
        catch (Exception ex)
        {
            return new BatchExecutionResult(false, null, "无法写入操作日志，未复制任何文件：" + ex.Message);
        }

        var completed = new List<JournalOperation>();
        try
        {
            Directory.CreateDirectory(_repository.BackupsDirectory);
            string backupDirectory = _repository.GetBackupDirectory(journal.Id);
            Directory.CreateDirectory(backupDirectory);

            for (int i = 0; i < pending.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = pending[i];
                var operation = journal.Operations[i];
                progress?.Report(new OperationProgress(i, pending.Count, item.RelativePath, "正在备份"));

                if (File.Exists(item.TargetPath))
                {
                    string backupPath = Path.Combine(
                        backupDirectory,
                        $"{i + 1:D4}_{Path.GetFileName(item.TargetPath)}");
                    File.Copy(item.TargetPath, backupPath, overwrite: true);
                    operation.BackupPath = backupPath;
                    operation.State = JournalOperationState.BackedUp;
                    _repository.Save(journal);
                }

                string? targetDirectory = Path.GetDirectoryName(item.TargetPath);
                if (!string.IsNullOrEmpty(targetDirectory)) Directory.CreateDirectory(targetDirectory);
                File.Copy(item.SourcePath, item.TargetPath, overwrite: true);
                operation.State = JournalOperationState.Completed;
                completed.Add(operation);
                _repository.Save(journal);
                progress?.Report(new OperationProgress(i + 1, pending.Count, item.RelativePath, "已备份"));
            }

            journal.Status = JournalStatus.Completed;
            journal.CompletedAt = DateTimeOffset.Now;
            _repository.Save(journal);
            return new BatchExecutionResult(
                true,
                journal,
                $"已备份 {pending.Count} 个文件（新增 {plan.NewCount}、更新 {plan.UpdateCount}），未删除任何目标文件。");
        }
        catch (Exception ex)
        {
            bool rollbackOk = TryRollback(completed, journal);
            journal.Status = rollbackOk ? JournalStatus.RolledBack : JournalStatus.Failed;
            journal.Error = ex.Message;
            try { _repository.Save(journal); } catch { }
            string message = ex is OperationCanceledException ? "备份已取消。" : "备份失败：" + ex.Message;
            message += rollbackOk ? " 已恢复目标文件夹。" : " 自动回滚未完全成功，请到回退中心检查。";
            return new BatchExecutionResult(false, journal, message);
        }
    }

    private bool TryRollback(IReadOnlyList<JournalOperation> operations, OperationJournal journal)
    {
        bool ok = true;
        foreach (var operation in operations.Reverse())
        {
            try
            {
                if (operation.OverwroteTarget && !string.IsNullOrWhiteSpace(operation.BackupPath) && File.Exists(operation.BackupPath))
                {
                    File.Copy(operation.BackupPath, operation.TargetPath, overwrite: true);
                }
                else if (!operation.OverwroteTarget && File.Exists(operation.TargetPath))
                {
                    File.Delete(operation.TargetPath);
                }

                operation.State = JournalOperationState.Undone;
            }
            catch
            {
                ok = false;
            }
        }

        if (ok)
        {
            try { _repository.DeleteBackupDirectory(journal.Id); } catch { }
        }

        return ok;
    }
}
