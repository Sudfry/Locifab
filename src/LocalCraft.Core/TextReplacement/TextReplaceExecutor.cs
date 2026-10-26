using LocalCraft.Core.Journal;
using LocalCraft.Core.Operations;

namespace LocalCraft.Core.TextReplacement;

/// <summary>
/// 先备份原文并写入日志，再执行内容改写；失败时自动用备份回滚。
/// </summary>
public sealed class TextReplaceExecutor
{
    private readonly JournalRepository _repository;

    public TextReplaceExecutor(JournalRepository repository)
    {
        _repository = repository;
    }

    public BatchExecutionResult Execute(
        IReadOnlyList<TextReplaceFilePlan> plans,
        TextReplaceOptions options,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, IReadOnlySet<int>>? selectedMatches = null)
    {
        if (plans.Count == 0)
        {
            return new BatchExecutionResult(false, null, "没有可执行的文件。");
        }

        string? preflight = Preflight(plans);
        if (preflight is not null)
        {
            return new BatchExecutionResult(false, null, preflight);
        }

        var journal = new OperationJournal
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = "replace",
            DisplayName = "文本替换",
            SourceDirectory = options.Folder,
            CreatedAt = DateTimeOffset.Now,
            Status = JournalStatus.Prepared,
            Operations = plans.Select((plan, index) => new JournalOperation
            {
                Sequence = index + 1,
                SourcePath = plan.FilePath,
                TargetPath = plan.FilePath,
                Note = $"替换 {plan.MatchCount} 处",
                State = JournalOperationState.Pending
            }).ToList()
        };

        try
        {
            _repository.Save(journal);
        }
        catch (Exception ex)
        {
            return new BatchExecutionResult(false, null, "无法写入操作日志，未修改文件：" + ex.Message);
        }

        var backups = new List<(JournalOperation Operation, string BackupPath, string SourcePath)>();
        try
        {
            Directory.CreateDirectory(_repository.BackupsDirectory);
            string backupDirectory = _repository.GetBackupDirectory(journal.Id);
            Directory.CreateDirectory(backupDirectory);

            for (int i = 0; i < plans.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var operation = journal.Operations[i];
                var plan = plans[i];
                string backupPath = Path.Combine(backupDirectory, $"{i + 1:D4}_{Path.GetFileName(plan.FilePath)}");
                File.Copy(plan.FilePath, backupPath, overwrite: true);
                operation.BackupPath = backupPath;
                operation.State = JournalOperationState.BackedUp;
                backups.Add((operation, backupPath, plan.FilePath));
                _repository.Save(journal);
            }

            for (int i = 0; i < plans.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var plan = plans[i];
                var operation = journal.Operations[i];
                progress?.Report(new OperationProgress(i, plans.Count, plan.FileName, "正在替换"));

                if (!TextReplacePlanner.TryReadText(plan.FilePath, out string text, out var encoding))
                {
                    throw new IOException("文件已变为不可读取的文本：" + plan.FilePath);
                }

                string updated = selectedMatches is not null
                    && selectedMatches.TryGetValue(plan.FilePath, out IReadOnlySet<int>? selected)
                    ? TextReplacePlanner.ApplySelected(text, options, selected)
                    : TextReplacePlanner.Apply(text, options);
                File.WriteAllText(plan.FilePath, updated, encoding);
                operation.State = JournalOperationState.Completed;
                _repository.Save(journal);
                progress?.Report(new OperationProgress(i + 1, plans.Count, plan.FileName, "已替换"));
            }

            journal.Status = JournalStatus.Completed;
            journal.CompletedAt = DateTimeOffset.Now;
            _repository.Save(journal);
            return new BatchExecutionResult(
                true,
                journal,
                $"已完成 {plans.Count} 个文件的文本替换，共 {plans.Sum(x => x.MatchCount)} 处。");
        }
        catch (Exception ex)
        {
            bool rollbackOk = TryRollback(backups, journal);
            string message = ex is OperationCanceledException
                ? "文本替换已取消。"
                : "文本替换失败：" + ex.Message;
            if (rollbackOk)
            {
                journal.Status = JournalStatus.RolledBack;
                message += " 已自动恢复所有已修改文件。";
            }
            else
            {
                journal.Status = JournalStatus.Failed;
                journal.Error = ex.Message;
                message += " 自动回滚未完全成功，请到回退中心检查。";
            }

            try { _repository.Save(journal); } catch { }
            return new BatchExecutionResult(false, journal, message);
        }
    }

    private bool TryRollback(
        IReadOnlyList<(JournalOperation Operation, string BackupPath, string SourcePath)> backups,
        OperationJournal journal)
    {
        bool ok = true;
        foreach (var item in backups.Reverse())
        {
            try
            {
                if (File.Exists(item.BackupPath))
                {
                    File.Copy(item.BackupPath, item.SourcePath, overwrite: true);
                    item.Operation.State = JournalOperationState.Undone;
                }
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

    private static string? Preflight(IReadOnlyList<TextReplaceFilePlan> plans)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in plans)
        {
            if (!File.Exists(plan.FilePath))
            {
                return "文件已不存在：" + plan.FilePath;
            }

            if (!seen.Add(plan.FilePath))
            {
                return "同一文件在预览中出现了多次：" + plan.FilePath;
            }

            var info = new FileInfo(plan.FilePath);
            if (Math.Abs((info.LastWriteTimeUtc - plan.LastWriteTimeUtc).TotalSeconds) > 2)
            {
                return "文件在生成预览后被修改，请重新扫描：" + plan.FilePath;
            }
        }

        return null;
    }
}
