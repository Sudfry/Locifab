using System.Text.Json;
using LocalCraft.Core.Journal;

namespace LocalCraft.Core.Operations;

/// <summary>
/// 基于日志的逆向执行。回退不依赖系统撤销，也不受操作时间限制。
/// </summary>
public sealed class UndoService
{
    private readonly JournalRepository _repository;

    public UndoService(JournalRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public UndoResult Undo(
        JournalEntry entry,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        OperationJournal journal;
        try
        {
            journal = _repository.Load(entry);
        }
        catch (Exception ex)
        {
            return new UndoResult(false, null, "无法读取操作日志：" + ex.Message);
        }

        if (string.Equals(journal.Kind, "tags", StringComparison.OrdinalIgnoreCase))
        {
            return UndoTags(journal, progress, cancellationToken);
        }

        if (string.Equals(journal.Kind, "convert", StringComparison.OrdinalIgnoreCase)
            || string.Equals(journal.Kind, "table", StringComparison.OrdinalIgnoreCase)
            || string.Equals(journal.Kind, "zip", StringComparison.OrdinalIgnoreCase)
            || string.Equals(journal.Kind, "checksum", StringComparison.OrdinalIgnoreCase)
            || string.Equals(journal.Kind, "image", StringComparison.OrdinalIgnoreCase)
            || string.Equals(journal.Kind, "metadata", StringComparison.OrdinalIgnoreCase)
            || string.Equals(journal.Kind, "report", StringComparison.OrdinalIgnoreCase)
            || string.Equals(journal.Kind, "split", StringComparison.OrdinalIgnoreCase)
            || string.Equals(journal.Kind, "merge", StringComparison.OrdinalIgnoreCase)
            || string.Equals(journal.Kind, "encoding", StringComparison.OrdinalIgnoreCase)
            || string.Equals(journal.Kind, "stitch", StringComparison.OrdinalIgnoreCase))
        {
            return UndoConversion(journal, progress, cancellationToken);
        }

        if (string.Equals(journal.Kind, "timestamp", StringComparison.OrdinalIgnoreCase)
            || string.Equals(journal.Kind, "attributes", StringComparison.OrdinalIgnoreCase))
        {
            return UndoFileState(journal, progress, cancellationToken);
        }

        if (string.Equals(journal.Kind, "template", StringComparison.OrdinalIgnoreCase))
        {
            return UndoTemplate(journal, progress, cancellationToken);
        }

        if (string.Equals(journal.Kind, "replace", StringComparison.OrdinalIgnoreCase))
        {
            return UndoReplace(journal, progress, cancellationToken);
        }

        if (string.Equals(journal.Kind, "backup", StringComparison.OrdinalIgnoreCase))
        {
            return UndoBackup(journal, progress, cancellationToken);
        }

        if (string.Equals(journal.Kind, "cleanup", StringComparison.OrdinalIgnoreCase))
        {
            return UndoCleanup(journal, progress, cancellationToken);
        }

        string? preflightError = Preflight(journal);
        if (preflightError is not null)
        {
            return new UndoResult(false, journal, preflightError);
        }

        try
        {
            journal.Status = JournalStatus.Undoing;
            journal.Error = null;
            _repository.Save(journal);
        }
        catch (Exception ex)
        {
            return new UndoResult(false, journal, "无法更新日志状态，未修改任何文件：" + ex.Message);
        }

        var undoneThisRun = new List<JournalOperation>();
        var ordered = journal.Operations.OrderByDescending(static x => x.Sequence).ToList();

        try
        {
            int completed = 0;
            foreach (var operation in ordered)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new OperationProgress(
                    completed,
                    ordered.Count,
                    Path.GetFileName(operation.TargetPath),
                    "正在回退"));

                if (!File.Exists(operation.TargetPath))
                {
                    throw new IOException($"当前文件不存在，无法回退：{operation.TargetPath}");
                }

                FileMove.Move(operation.TargetPath, operation.SourcePath);
                // 从这一刻起若后续步骤失败，补偿流程需要把这个文件放回新路径。
                undoneThisRun.Add(operation);

                if (operation.OverwroteTarget)
                {
                    if (string.IsNullOrWhiteSpace(operation.BackupPath) || !File.Exists(operation.BackupPath))
                    {
                        throw new IOException("被覆盖文件的备份丢失，无法恢复。");
                    }

                    FileMove.Move(operation.BackupPath, operation.TargetPath);
                }

                operation.State = JournalOperationState.Undone;
                completed++;
                progress?.Report(new OperationProgress(
                    completed,
                    ordered.Count,
                    Path.GetFileName(operation.SourcePath),
                    "已回退"));
            }

            journal.Status = JournalStatus.Undone;
            journal.UndoneAt = DateTimeOffset.Now;
            journal.Error = null;
            _repository.Save(journal);

            try
            {
                _repository.DeleteBackupDirectory(journal.Id);
            }
            catch
            {
                // 回退已经完成，备份清理失败不影响结果。
            }

            return new UndoResult(
                true,
                journal,
                $"已将 {journal.OperationCount} 个文件恢复到操作前状态。");
        }
        catch (Exception ex)
        {
            string originalError = ex is OperationCanceledException
                ? "回退已取消。"
                : ex.Message;

            var compensationErrors = new List<string>();
            bool compensated = TryCompensate(undoneThisRun, compensationErrors);

            journal.Status = compensated ? JournalStatus.Completed : JournalStatus.Failed;
            journal.Error = compensated
                ? originalError + "；已恢复到回退前状态。"
                : originalError + "；恢复失败：" + string.Join("；", compensationErrors);

            try
            {
                _repository.Save(journal);
            }
            catch (Exception saveEx)
            {
                journal.Error += "；日志状态保存失败：" + saveEx.Message;
            }

            string message = compensated
                ? $"回退未完成，文件已保持在操作后的状态。{Environment.NewLine}{originalError}"
                : $"回退失败，部分文件可能处于中间状态，请检查日志。{Environment.NewLine}{originalError}";

            return new UndoResult(false, journal, message);
        }
        finally
        {
            progress?.Report(new OperationProgress(0, ordered.Count, string.Empty, "结束"));
        }
    }

    /// <summary>
    /// 格式转换只新增输出，不改动原文件；回退即删除本批次生成的输出文件。
    /// </summary>
    private UndoResult UndoTags(
        OperationJournal journal,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (journal.Status != JournalStatus.Completed)
            return new UndoResult(false, journal, "该标签日志当前不可回退。");

        try
        {
            journal.Status = JournalStatus.Undoing;
            _repository.Save(journal);
            foreach (var operation in journal.Operations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrWhiteSpace(operation.BackupPath) && File.Exists(operation.BackupPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(operation.TargetPath) ?? string.Empty);
                    File.Copy(operation.BackupPath, operation.TargetPath, true);
                }
                else if (File.Exists(operation.TargetPath))
                {
                    File.Delete(operation.TargetPath);
                }
                operation.State = JournalOperationState.Undone;
            }
            journal.Status = JournalStatus.Undone;
            journal.UndoneAt = DateTimeOffset.Now;
            _repository.Save(journal);
            return new UndoResult(true, journal, "已恢复本次标签备注修改。");
        }
        catch (Exception ex)
        {
            journal.Status = JournalStatus.Failed;
            journal.Error = ex.Message;
            try { _repository.Save(journal); } catch { }
            return new UndoResult(false, journal, "标签回退失败：" + ex.Message);
        }
    }

    private UndoResult UndoConversion(
        OperationJournal journal,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (journal.Status != JournalStatus.Completed)
        {
            return new UndoResult(false, journal, "该转换日志当前不可回退。");
        }

        if (journal.Operations.Count == 0)
        {
            return new UndoResult(false, journal, "日志中没有可回退的转换输出。");
        }

        var errors = new List<string>();
        foreach (var operation in journal.Operations)
        {
            if (operation.State != JournalOperationState.Completed)
            {
                errors.Add($"操作状态异常：{Path.GetFileName(operation.TargetPath)}");
            }

            if (string.Equals(operation.SourcePath, operation.TargetPath, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"输出路径与源文件相同，拒绝删除：{operation.TargetPath}");
            }
        }

        if (errors.Count > 0)
        {
            return new UndoResult(
                false,
                journal,
                "回退前检查未通过，未删除任何文件：" + Environment.NewLine +
                string.Join(Environment.NewLine, errors.Take(6).Select(static x => "• " + x)));
        }

        try
        {
            journal.Status = JournalStatus.Undoing;
            journal.Error = null;
            _repository.Save(journal);
        }
        catch (Exception ex)
        {
            return new UndoResult(false, journal, "无法更新日志状态，未删除任何文件：" + ex.Message);
        }

        int deleted = 0;
        try
        {
            var ordered = journal.Operations.OrderByDescending(static x => x.Sequence).ToList();
            int processed = 0;

            foreach (var operation in ordered)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new OperationProgress(
                    processed,
                    ordered.Count,
                    Path.GetFileName(operation.TargetPath),
                    "正在删除转换输出"));

                if (File.Exists(operation.TargetPath))
                {
                    File.Delete(operation.TargetPath);
                    deleted++;
                }

                operation.State = JournalOperationState.Undone;
                processed++;
                progress?.Report(new OperationProgress(
                    processed,
                    ordered.Count,
                    Path.GetFileName(operation.TargetPath),
                    "已删除"));
            }

            journal.Status = JournalStatus.Undone;
            journal.UndoneAt = DateTimeOffset.Now;
            journal.Error = null;
            _repository.Save(journal);

            return new UndoResult(
                true,
                journal,
                $"已删除本次转换生成的 {deleted} 个输出文件；原始文件未改动。");
        }
        catch (Exception ex)
        {
            journal.Status = JournalStatus.Failed;
            journal.Error = ex is OperationCanceledException
                ? "回退已取消，部分输出文件可能已被删除。"
                : ex.Message;

            try
            {
                _repository.Save(journal);
            }
            catch (Exception saveEx)
            {
                journal.Error += "；日志状态保存失败：" + saveEx.Message;
            }

            return new UndoResult(
                false,
                journal,
                "删除转换输出时发生错误：" + Environment.NewLine + journal.Error);
        }
        finally
        {
            progress?.Report(new OperationProgress(0, journal.OperationCount, string.Empty, "结束"));
        }
    }

    private UndoResult UndoFileState(
        OperationJournal journal,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (journal.Status != JournalStatus.Completed)
        {
            return new UndoResult(false, journal, "该文件状态日志当前不可回退。");
        }

        try
        {
            journal.Status = JournalStatus.Undoing;
            journal.Error = null;
            _repository.Save(journal);
        }
        catch (Exception ex)
        {
            return new UndoResult(false, journal, "无法更新日志状态，未修改任何文件：" + ex.Message);
        }

        int restored = 0;
        try
        {
            var ordered = journal.Operations.OrderByDescending(x => x.Sequence).ToList();
            foreach (var operation in ordered)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(operation.SourcePath))
                {
                    throw new IOException("文件已不存在，无法恢复状态：" + operation.SourcePath);
                }

                if (string.Equals(journal.Kind, "timestamp", StringComparison.OrdinalIgnoreCase))
                {
                    var note = JsonSerializer.Deserialize<UndoStateNote>(operation.Note ?? "{}") ?? new UndoStateNote();
                    if (note.Created.HasValue) File.SetCreationTime(operation.SourcePath, note.Created.Value);
                    if (note.Modified.HasValue) File.SetLastWriteTime(operation.SourcePath, note.Modified.Value);
                    if (note.Accessed.HasValue) File.SetLastAccessTime(operation.SourcePath, note.Accessed.Value);
                }
                else if (int.TryParse(operation.Note, out int attributes))
                {
                    File.SetAttributes(operation.SourcePath, (FileAttributes)attributes);
                }

                operation.State = JournalOperationState.Undone;
                restored++;
                progress?.Report(new OperationProgress(restored, ordered.Count,
                    Path.GetFileName(operation.SourcePath), "已恢复"));
            }

            journal.Status = JournalStatus.Undone;
            journal.UndoneAt = DateTimeOffset.Now;
            _repository.Save(journal);
            return new UndoResult(true, journal, $"已恢复 {restored} 个文件的原始状态。");
        }
        catch (Exception ex)
        {
            journal.Status = JournalStatus.Failed;
            journal.Error = ex.Message;
            try { _repository.Save(journal); } catch { }
            return new UndoResult(false, journal, "恢复文件状态失败：" + ex.Message);
        }
    }

    private UndoResult UndoTemplate(
        OperationJournal journal,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (journal.Status != JournalStatus.Completed)
        {
            return new UndoResult(false, journal, "该模板日志当前不可回退。");
        }

        try
        {
            journal.Status = JournalStatus.Undoing;
            journal.Error = null;
            _repository.Save(journal);
        }
        catch (Exception ex)
        {
            return new UndoResult(false, journal, "无法更新日志状态，未修改任何文件：" + ex.Message);
        }

        int removed = 0;
        try
        {
            var ordered = journal.Operations
                .OrderByDescending(x => x.TargetPath.Count(c => c == Path.DirectorySeparatorChar))
                .ThenByDescending(x => x.Sequence)
                .ToList();
            foreach (var operation in ordered)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.Equals(operation.Note, "dir", StringComparison.OrdinalIgnoreCase))
                {
                    if (Directory.Exists(operation.TargetPath) && !Directory.EnumerateFileSystemEntries(operation.TargetPath).Any())
                    {
                        Directory.Delete(operation.TargetPath);
                        removed++;
                    }
                }
                else if (File.Exists(operation.TargetPath))
                {
                    var info = new FileInfo(operation.TargetPath);
                    if (info.Length == 0)
                    {
                        File.Delete(operation.TargetPath);
                        removed++;
                    }
                }

                operation.State = JournalOperationState.Undone;
            }

            journal.Status = JournalStatus.Undone;
            journal.UndoneAt = DateTimeOffset.Now;
            _repository.Save(journal);
            return new UndoResult(true, journal, $"已删除 {removed} 个本次创建的目录/空文件；已有内容未被删除。");
        }
        catch (Exception ex)
        {
            journal.Status = JournalStatus.Failed;
            journal.Error = ex.Message;
            try { _repository.Save(journal); } catch { }
            return new UndoResult(false, journal, "回退目录结构失败：" + ex.Message);
        }
    }

    private sealed class UndoStateNote
    {
        public DateTime? Created { get; set; }
        public DateTime? Modified { get; set; }
        public DateTime? Accessed { get; set; }
    }

    private static string? Preflight(OperationJournal journal)
    {
        if (journal.Status != JournalStatus.Completed)
        {
            return "该日志当前不可回退（只有已完成的批次可以回退）。";
        }

        if (journal.Operations.Count == 0)
        {
            return "日志中没有可回退的操作。";
        }

        var errors = new List<string>();

        foreach (var operation in journal.Operations)
        {
            if (operation.State != JournalOperationState.Completed)
            {
                errors.Add($"操作状态异常：{Path.GetFileName(operation.TargetPath)}");
                continue;
            }

            if (!File.Exists(operation.TargetPath))
            {
                errors.Add($"文件已被移动或删除：{operation.TargetPath}");
                continue;
            }

            bool caseOnly = FileMove.IsCaseOnly(operation.SourcePath, operation.TargetPath);
            if (!caseOnly && File.Exists(operation.SourcePath))
            {
                errors.Add($"原路径已被其他文件占用：{operation.SourcePath}");
                continue;
            }

            if (operation.OverwroteTarget
                && (string.IsNullOrWhiteSpace(operation.BackupPath) || !File.Exists(operation.BackupPath)))
            {
                errors.Add($"被覆盖文件的备份丢失：{Path.GetFileName(operation.TargetPath)}");
            }
        }

        if (errors.Count == 0) return null;

        string joined = string.Join(Environment.NewLine, errors.Take(6).Select(static x => "• " + x));
        if (errors.Count > 6)
        {
            joined += Environment.NewLine + $"• 另有 {errors.Count - 6} 个问题";
        }

        return "回退前检查未通过，未修改任何文件：" + Environment.NewLine + joined;
    }

    private UndoResult UndoReplace(
        OperationJournal journal,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        foreach (var operation in journal.Operations)
        {
            if (string.IsNullOrWhiteSpace(operation.BackupPath) || !File.Exists(operation.BackupPath))
            {
                return new UndoResult(false, journal, $"原文备份丢失，无法回退：{Path.GetFileName(operation.SourcePath)}");
            }
        }

        try
        {
            journal.Status = JournalStatus.Undoing;
            journal.Error = null;
            _repository.Save(journal);
        }
        catch (Exception ex)
        {
            return new UndoResult(false, journal, "无法更新日志状态，未修改文件：" + ex.Message);
        }

        var restored = new List<JournalOperation>();
        try
        {
            var ordered = journal.Operations.OrderByDescending(static x => x.Sequence).ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var operation = ordered[i];
                progress?.Report(new OperationProgress(i, ordered.Count, Path.GetFileName(operation.SourcePath), "正在恢复原文"));
                File.Copy(operation.BackupPath!, operation.SourcePath, overwrite: true);
                operation.State = JournalOperationState.Undone;
                restored.Add(operation);
            }

            journal.Status = JournalStatus.Undone;
            journal.UndoneAt = DateTimeOffset.Now;
            journal.Error = null;
            _repository.Save(journal);
            try { _repository.DeleteBackupDirectory(journal.Id); } catch { }
            return new UndoResult(true, journal, $"已将 {journal.OperationCount} 个文件的原文恢复。");
        }
        catch (Exception ex)
        {
            journal.Status = JournalStatus.Failed;
            journal.Error = ex.Message;
            try { _repository.Save(journal); } catch { }
            return new UndoResult(
                false,
                journal,
                $"恢复原文失败：{ex.Message}。已恢复 {restored.Count} 个文件，请人工检查。");
        }
    }

    private UndoResult UndoBackup(
        OperationJournal journal,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        foreach (var operation in journal.Operations)
        {
            if (operation.OverwroteTarget
                && (string.IsNullOrWhiteSpace(operation.BackupPath) || !File.Exists(operation.BackupPath)))
            {
                return new UndoResult(false, journal, $"目标文件备份丢失，无法回退：{Path.GetFileName(operation.TargetPath)}");
            }
        }

        try
        {
            journal.Status = JournalStatus.Undoing;
            journal.Error = null;
            _repository.Save(journal);
        }
        catch (Exception ex)
        {
            return new UndoResult(false, journal, "无法更新日志状态，未修改任何文件：" + ex.Message);
        }

        var ordered = journal.Operations.OrderByDescending(static x => x.Sequence).ToList();
        try
        {
            for (int i = 0; i < ordered.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var operation = ordered[i];
                progress?.Report(new OperationProgress(i, ordered.Count, Path.GetFileName(operation.TargetPath), "正在回退备份"));
                if (operation.OverwroteTarget && !string.IsNullOrWhiteSpace(operation.BackupPath))
                {
                    File.Copy(operation.BackupPath, operation.TargetPath, overwrite: true);
                }
                else if (File.Exists(operation.TargetPath))
                {
                    File.Delete(operation.TargetPath);
                }

                operation.State = JournalOperationState.Undone;
            }

            journal.Status = JournalStatus.Undone;
            journal.UndoneAt = DateTimeOffset.Now;
            journal.Error = null;
            _repository.Save(journal);
            try { _repository.DeleteBackupDirectory(journal.Id); } catch { }
            return new UndoResult(true, journal, $"已回退 {journal.OperationCount} 个备份目标文件。");
        }
        catch (Exception ex)
        {
            journal.Status = JournalStatus.Failed;
            journal.Error = ex.Message;
            try { _repository.Save(journal); } catch { }
            return new UndoResult(false, journal, "回退备份失败：" + ex.Message);
        }
    }

    private UndoResult UndoCleanup(
        OperationJournal journal,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            journal.Status = JournalStatus.Undoing;
            journal.Error = null;
            _repository.Save(journal);
        }
        catch (Exception ex)
        {
            return new UndoResult(false, journal, "无法更新日志状态，未修改任何文件：" + ex.Message);
        }

        var ordered = journal.Operations.OrderByDescending(static x => x.Sequence).ToList();
        try
        {
            for (int i = 0; i < ordered.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var operation = ordered[i];
                progress?.Report(new OperationProgress(i, ordered.Count, Path.GetFileName(operation.SourcePath), "正在恢复清理项"));
                string path = operation.SourcePath;

                if (string.Equals(operation.Note, "directory", StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(path))
                    {
                        throw new IOException("原路径已被文件占用，无法恢复目录：" + path);
                    }

                    Directory.CreateDirectory(path);
                }
                else
                {
                    if (Directory.Exists(path))
                    {
                        throw new IOException("原路径已被目录占用，无法恢复文件：" + path);
                    }

                    if (!File.Exists(path))
                    {
                        string? parent = Path.GetDirectoryName(path);
                        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                        File.WriteAllBytes(path, Array.Empty<byte>());
                    }
                }

                operation.State = JournalOperationState.Undone;
            }

            journal.Status = JournalStatus.Undone;
            journal.UndoneAt = DateTimeOffset.Now;
            journal.Error = null;
            _repository.Save(journal);
            return new UndoResult(true, journal, $"已恢复 {journal.OperationCount} 个目录/零字节文件。");
        }
        catch (Exception ex)
        {
            journal.Status = JournalStatus.Failed;
            journal.Error = ex.Message;
            try { _repository.Save(journal); } catch { }
            return new UndoResult(false, journal, "恢复清理项失败：" + ex.Message);
        }
    }

    private static bool TryCompensate(List<JournalOperation> undone, List<string> errors)
    {
        if (undone.Count == 0) return true;

        foreach (var operation in undone.OrderBy(static x => x.Sequence))
        {
            try
            {
                if (!File.Exists(operation.SourcePath))
                {
                    if (File.Exists(operation.TargetPath)) continue;
                    throw new IOException("新文件不存在，无法恢复。");
                }

                if (operation.OverwroteTarget)
                {
                    if (string.IsNullOrWhiteSpace(operation.BackupPath))
                    {
                        throw new IOException("缺少备份路径。");
                    }

                    string? backupDirectory = Path.GetDirectoryName(operation.BackupPath);
                    if (!string.IsNullOrEmpty(backupDirectory))
                    {
                        Directory.CreateDirectory(backupDirectory);
                    }

                    if (File.Exists(operation.TargetPath))
                    {
                        FileMove.Move(operation.TargetPath, operation.BackupPath);
                    }
                }

                FileMove.Move(operation.SourcePath, operation.TargetPath);
                operation.State = JournalOperationState.Completed;
            }
            catch (Exception ex)
            {
                errors.Add($"恢复「{Path.GetFileName(operation.TargetPath)}」失败：{ex.Message}");
            }
        }

        return errors.Count == 0;
    }
}
