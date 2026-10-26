using LocalCraft.Core.Journal;

namespace LocalCraft.Core.Operations;

/// <summary>
/// 批量重命名执行器。执行前先落盘日志；任意步骤失败时自动逆向回滚已完成的步骤。
/// </summary>
public sealed class BatchRenameExecutor
{
    private const int CheckpointInterval = 25;

    private readonly JournalRepository _repository;

    public BatchRenameExecutor(JournalRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public BatchExecutionResult Execute(
        IReadOnlyList<RenameWorkItem> requests,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default,
        string kind = "rename",
        string displayName = "批量重命名",
        bool createTargetDirectories = false)
    {
        if (requests is null || requests.Count == 0)
        {
            return new BatchExecutionResult(false, null, "没有可执行的重命名操作。");
        }

        var normalized = requests
            .Select(static x => new RenameWorkItem(
                Path.GetFullPath(x.SourcePath),
                Path.GetFullPath(x.TargetPath),
                x.AllowOverwrite))
            .ToList();

        string? preflightError = Preflight(normalized, createTargetDirectories);
        if (preflightError is not null)
        {
            return new BatchExecutionResult(false, null, preflightError);
        }

        var journal = new OperationJournal
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = string.IsNullOrWhiteSpace(kind) ? "rename" : kind,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "批量重命名" : displayName,
            SourceDirectory = Path.GetDirectoryName(normalized[0].SourcePath) ?? string.Empty,
            CreatedAt = DateTimeOffset.Now,
            Status = JournalStatus.Prepared,
            Operations = normalized.Select(static (item, index) => new JournalOperation
            {
                Sequence = index + 1,
                SourcePath = item.SourcePath,
                TargetPath = item.TargetPath,
                OverwroteTarget = File.Exists(item.TargetPath) && !FileMove.IsCaseOnly(item.SourcePath, item.TargetPath),
                State = JournalOperationState.Pending
            }).ToList()
        };

        string journalPath;
        try
        {
            journalPath = _repository.Save(journal);
        }
        catch (Exception ex)
        {
            return new BatchExecutionResult(
                false,
                null,
                "无法写入本地操作日志，已取消执行，未修改任何文件。" + Environment.NewLine + ex.Message);
        }

        int completed = 0;
        try
        {
            Directory.CreateDirectory(_repository.BackupsDirectory);

            foreach (var operation in journal.Operations)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string fileName = Path.GetFileName(operation.SourcePath);
                bool caseOnly = FileMove.IsCaseOnly(operation.SourcePath, operation.TargetPath);
                bool targetExistsNow = File.Exists(operation.TargetPath) && !caseOnly;

                if (!File.Exists(operation.SourcePath))
                {
                    throw new IOException($"源文件已不存在：{operation.SourcePath}");
                }

                if (targetExistsNow != operation.OverwroteTarget)
                {
                    throw new IOException($"目标文件状态在预览后发生变化，请重新生成预览：{operation.TargetPath}");
                }

                if (targetExistsNow)
                {
                    string backupDirectory = _repository.GetBackupDirectory(journal.Id);
                    Directory.CreateDirectory(backupDirectory);
                    string backupPath = CreateBackupPath(backupDirectory, operation.Sequence, operation.TargetPath);
                    FileMove.Move(operation.TargetPath, backupPath);
                    operation.BackupPath = backupPath;
                    operation.State = JournalOperationState.BackedUp;
                    _repository.Save(journal);
                }

                if (createTargetDirectories)
                {
                    string? targetDirectory = Path.GetDirectoryName(operation.TargetPath);
                    if (!string.IsNullOrEmpty(targetDirectory))
                    {
                        Directory.CreateDirectory(targetDirectory);
                    }
                }

                progress?.Report(new OperationProgress(
                    completed,
                    journal.Operations.Count,
                    fileName,
                    kind == "move" ? "正在移动" : "正在重命名"));

                try
                {
                    FileMove.Move(operation.SourcePath, operation.TargetPath);
                }
                catch
                {
                    // FileMove 对仅大小写改名会自行恢复临时文件；外层统一进入自动回滚。
                    throw;
                }

                operation.State = JournalOperationState.Completed;
                completed++;

                if (completed % CheckpointInterval == 0)
                {
                    _repository.Save(journal);
                }

                progress?.Report(new OperationProgress(completed, journal.Operations.Count, fileName, "已完成"));
            }

            journal.Status = JournalStatus.Completed;
            journal.CompletedAt = DateTimeOffset.Now;
            journal.Error = null;
            _repository.Save(journal);

            string successText = kind == "move"
                ? $"已完成 {journal.OperationCount} 个文件的分类移动，操作日志已保存。"
                : $"已完成 {journal.OperationCount} 个文件的重命名，操作日志已保存。";

            return new BatchExecutionResult(true, journal, successText);
        }
        catch (Exception ex)
        {
            string originalError = ex is OperationCanceledException
                ? "操作已取消，正在自动回滚。"
                : ex.Message;

            var rollbackErrors = Rollback(journal);
            bool rollbackSucceeded = rollbackErrors.Count == 0;
            journal.Status = rollbackSucceeded ? JournalStatus.RolledBack : JournalStatus.Failed;
            journal.Error = rollbackSucceeded
                ? originalError
                : originalError + "；回滚异常：" + string.Join("；", rollbackErrors);

            try
            {
                _repository.Save(journal);
            }
            catch (Exception saveEx)
            {
                journal.Error += "；日志状态保存失败：" + saveEx.Message;
            }

            if (rollbackSucceeded)
            {
                TryDeleteBackupDirectory(journal.Id);
            }

            string message = rollbackSucceeded
                ? $"执行失败，已自动恢复到操作前状态。{Environment.NewLine}{originalError}"
                : $"执行失败，且自动回滚未能全部完成，请检查日志。{Environment.NewLine}{originalError}";

            return new BatchExecutionResult(false, journal, message);
        }
        finally
        {
            progress?.Report(new OperationProgress(completed, journal.Operations.Count, string.Empty, "结束"));
        }
    }

    private static string? Preflight(List<RenameWorkItem> items, bool allowMissingTargetDirectories)
    {
        var errors = new List<string>();
        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            if (string.Equals(item.SourcePath, item.TargetPath, StringComparison.Ordinal))
            {
                errors.Add($"源路径与目标路径相同：{item.SourcePath}");
                continue;
            }

            if (!sources.Add(item.SourcePath))
            {
                errors.Add($"同一源文件出现多次：{item.SourcePath}");
            }

            if (!targets.Add(item.TargetPath))
            {
                errors.Add($"多个文件指向同一目标：{item.TargetPath}");
            }

            if (!File.Exists(item.SourcePath))
            {
                errors.Add($"源文件不存在：{item.SourcePath}");
                continue;
            }

            if (Directory.Exists(item.TargetPath))
            {
                errors.Add($"目标位置存在同名文件夹：{item.TargetPath}");
                continue;
            }

            string? targetDirectory = Path.GetDirectoryName(item.TargetPath);
            if (string.IsNullOrEmpty(targetDirectory))
            {
                errors.Add("目标文件夹路径为空。");
                continue;
            }

            if (!allowMissingTargetDirectories && !Directory.Exists(targetDirectory))
            {
                errors.Add($"目标文件夹不存在：{targetDirectory}");
                continue;
            }

            bool caseOnly = FileMove.IsCaseOnly(item.SourcePath, item.TargetPath);

            if (File.Exists(item.TargetPath) && !caseOnly)
            {
                if (sources.Contains(item.TargetPath))
                {
                    errors.Add($"目标文件属于本批次源文件，不能覆盖：{item.TargetPath}");
                }
                else if (!item.AllowOverwrite)
                {
                    errors.Add($"目标已存在且未允许覆盖：{item.TargetPath}");
                }
            }
        }

        if (errors.Count == 0) return null;

        const int maxShown = 4;
        string joined = string.Join(Environment.NewLine, errors.Take(maxShown).Select(static x => "• " + x));
        if (errors.Count > maxShown)
        {
            joined += Environment.NewLine + $"• 另有 {errors.Count - maxShown} 个问题";
        }

        return "执行前检查未通过，未修改任何文件：" + Environment.NewLine + joined;
    }

    private List<string> Rollback(OperationJournal journal)
    {
        var errors = new List<string>();

        foreach (var operation in journal.Operations.AsEnumerable().Reverse())
        {
            try
            {
                switch (operation.State)
                {
                    case JournalOperationState.Pending:
                        break;

                    case JournalOperationState.BackedUp:
                        RestoreBackup(operation);
                        operation.State = JournalOperationState.Undone;
                        break;

                    case JournalOperationState.Staging:
                        if (!string.IsNullOrEmpty(operation.StagingPath) && File.Exists(operation.StagingPath))
                        {
                            FileMove.Move(operation.StagingPath, operation.SourcePath);
                        }
                        RestoreBackup(operation);
                        operation.State = JournalOperationState.Undone;
                        break;

                    case JournalOperationState.Completed:
                        if (File.Exists(operation.TargetPath))
                        {
                            FileMove.Move(operation.TargetPath, operation.SourcePath);
                        }
                        else if (!File.Exists(operation.SourcePath))
                        {
                            throw new IOException("新文件与旧文件均不存在，无法回滚。");
                        }

                        RestoreBackup(operation);
                        operation.State = JournalOperationState.Undone;
                        break;

                    case JournalOperationState.Undone:
                        break;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"恢复「{Path.GetFileName(operation.TargetPath)}」失败：{ex.Message}");
            }
        }

        return errors;
    }

    private static void RestoreBackup(JournalOperation operation)
    {
        if (!operation.OverwroteTarget) return;
        if (string.IsNullOrWhiteSpace(operation.BackupPath) || !File.Exists(operation.BackupPath))
        {
            throw new IOException("被覆盖文件的备份丢失。");
        }

        if (File.Exists(operation.TargetPath))
        {
            throw new IOException("无法恢复被覆盖文件：目标位置已被占用。");
        }

        FileMove.Move(operation.BackupPath, operation.TargetPath);
        operation.BackupPath = null;
    }

    private static string CreateBackupPath(string backupDirectory, int sequence, string targetPath)
    {
        string extension = Path.GetExtension(targetPath);
        for (int attempt = 0; attempt < 20; attempt++)
        {
            string candidate = Path.Combine(
                backupDirectory,
                $"{sequence:D5}_{Guid.NewGuid():N}{extension}");
            if (!File.Exists(candidate)) return candidate;
        }

        throw new IOException("无法分配备份文件路径。");
    }

    private void TryDeleteBackupDirectory(string journalId)
    {
        try
        {
            _repository.DeleteBackupDirectory(journalId);
        }
        catch
        {
            // 清理失败不影响回滚结果。
        }
    }
}
