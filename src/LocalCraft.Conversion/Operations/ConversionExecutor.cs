using LocalCraft.Conversion.Engines;
using LocalCraft.Conversion.Formats;
using LocalCraft.Core.Journal;
using LocalCraft.Core.Operations;

namespace LocalCraft.Conversion.Operations;

/// <summary>
/// 离线转换执行器。输出先写临时文件，成功后原子改名；全部输出写入同一日志，回退时删除。
/// </summary>
public sealed class ConversionExecutor
{
    private const int CheckpointInterval = 20;

    private readonly JournalRepository _repository;
    private readonly ConversionEngineCatalog _engines;

    public ConversionExecutor(JournalRepository repository, ConversionEngineCatalog engines)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _engines = engines ?? throw new ArgumentNullException(nameof(engines));
    }

    public async Task<ConversionBatchResult> ExecuteAsync(
        IReadOnlyList<ConversionWorkItem> requests,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (requests is null || requests.Count == 0)
        {
            return new ConversionBatchResult(false, null, "没有可执行的转换任务。");
        }

        var normalized = requests.Select(static x => new ConversionWorkItem(
            Path.GetFullPath(x.SourcePath),
            Path.GetFullPath(x.TargetPath),
            x.TargetFormat,
            x.EngineId)).ToList();

        string? preflightError = Preflight(normalized);
        if (preflightError is not null)
        {
            return new ConversionBatchResult(false, null, preflightError);
        }

        var journal = new OperationJournal
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = "convert",
            DisplayName = "批量格式转换",
            SourceDirectory = CommonSourceDirectory(normalized),
            CreatedAt = DateTimeOffset.Now,
            Status = JournalStatus.Prepared,
            Operations = normalized.Select(static (item, index) => new JournalOperation
            {
                Sequence = index + 1,
                SourcePath = item.SourcePath,
                TargetPath = item.TargetPath,
                State = JournalOperationState.Pending,
                Note = $"转换为 {item.TargetFormat.DisplayName} · {item.EngineId}"
            }).ToList()
        };

        try
        {
            _repository.Save(journal);
        }
        catch (Exception ex)
        {
            return new ConversionBatchResult(
                false,
                null,
                "无法写入本地操作日志，已取消转换，未创建任何输出文件。" + Environment.NewLine + ex.Message);
        }

        int completed = 0;
        var createdTargets = new List<string>();
        try
        {
            foreach (var item in normalized)
            {
                cancellationToken.ThrowIfCancellationRequested();

                IConversionEngine engine = _engines.GetEngine(item.EngineId)
                    ?? throw new InvalidOperationException($"转换引擎不可用：{item.EngineId}");
                if (!engine.IsAvailable)
                {
                    throw new InvalidOperationException($"转换引擎当前不可用：{engine.DisplayName}");
                }

                var operation = journal.Operations[completed];
                string fileName = Path.GetFileName(item.SourcePath);
                progress?.Report(new OperationProgress(completed, journal.OperationCount, fileName, "正在转换"));

                string? outputDirectory = Path.GetDirectoryName(item.TargetPath);
                if (string.IsNullOrWhiteSpace(outputDirectory))
                {
                    throw new IOException("无法确定输出目录。");
                }

                Directory.CreateDirectory(outputDirectory);
                if (File.Exists(item.TargetPath))
                {
                    throw new IOException($"输出文件已存在，请重新生成预览：{item.TargetPath}");
                }

                string tempPath = Path.Combine(
                    outputDirectory,
                    $".localcraft-{Guid.NewGuid():N}-{Path.GetFileNameWithoutExtension(item.TargetPath)}{item.TargetFormat.Extension}");

                try
                {
                    await engine.ConvertAsync(item.SourcePath, tempPath, item.TargetFormat, cancellationToken)
                        .ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!File.Exists(tempPath))
                    {
                        throw new IOException("转换引擎没有生成输出文件。");
                    }

                    File.Move(tempPath, item.TargetPath, overwrite: false);
                    createdTargets.Add(item.TargetPath);

                    operation.State = JournalOperationState.Completed;
                    operation.Note = $"已转换为 {item.TargetFormat.DisplayName} · {engine.DisplayName}";
                    completed++;

                    if (completed % CheckpointInterval == 0)
                    {
                        _repository.Save(journal);
                    }

                    progress?.Report(new OperationProgress(completed, journal.OperationCount, fileName, "已完成"));
                }
                finally
                {
                    try
                    {
                        if (File.Exists(tempPath)) File.Delete(tempPath);
                    }
                    catch
                    {
                        // 临时文件清理失败不影响批次结果。
                    }
                }
            }

            journal.Status = JournalStatus.Completed;
            journal.CompletedAt = DateTimeOffset.Now;
            journal.Error = null;
            _repository.Save(journal);

            return new ConversionBatchResult(
                true,
                journal,
                $"已完成 {journal.OperationCount} 个文件的格式转换。原文件保留，输出文件已写入回退日志。");
        }
        catch (Exception ex)
        {
            string originalError = ex is OperationCanceledException
                ? "转换已取消，正在清理本次输出。"
                : ex.Message;

            var cleanupErrors = new List<string>();
            foreach (string target in createdTargets.AsEnumerable().Reverse())
            {
                try
                {
                    if (File.Exists(target)) File.Delete(target);
                }
                catch (Exception cleanupEx)
                {
                    cleanupErrors.Add($"{Path.GetFileName(target)}：{cleanupEx.Message}");
                }
            }

            bool rolledBack = cleanupErrors.Count == 0;
            foreach (var operation in journal.Operations)
            {
                if (operation.State == JournalOperationState.Completed && rolledBack)
                {
                    operation.State = JournalOperationState.Undone;
                }
            }

            journal.Status = rolledBack ? JournalStatus.RolledBack : JournalStatus.Failed;
            journal.Error = rolledBack
                ? originalError
                : originalError + "；清理输出失败：" + string.Join("；", cleanupErrors);

            try
            {
                _repository.Save(journal);
            }
            catch (Exception saveEx)
            {
                journal.Error += "；日志状态保存失败：" + saveEx.Message;
            }

            string message = rolledBack
                ? $"转换失败，已清理本次生成的输出文件。原文件未改动。{Environment.NewLine}{originalError}"
                : $"转换失败，部分输出文件可能残留，请检查日志。{Environment.NewLine}{originalError}";

            return new ConversionBatchResult(false, journal, message);
        }
        finally
        {
            progress?.Report(new OperationProgress(completed, journal.OperationCount, string.Empty, "结束"));
        }
    }

    private string? Preflight(List<ConversionWorkItem> items)
    {
        var errors = new List<string>();
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            if (!targets.Add(item.TargetPath))
            {
                errors.Add($"多个任务指向同一输出：{item.TargetPath}");
            }

            if (!File.Exists(item.SourcePath))
            {
                errors.Add($"源文件不存在：{item.SourcePath}");
                continue;
            }

            if (File.Exists(item.TargetPath) || Directory.Exists(item.TargetPath))
            {
                errors.Add($"输出文件已存在：{item.TargetPath}");
            }

            string? directory = Path.GetDirectoryName(item.TargetPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                errors.Add($"输出目录无效：{item.TargetPath}");
            }

            IConversionEngine? engine = _engines.GetEngine(item.EngineId);
            if (engine is null || !engine.IsAvailable)
            {
                errors.Add($"转换引擎不可用：{item.EngineId}");
            }
            else if (!engine.CanConvert(item.SourcePath, item.TargetFormat))
            {
                errors.Add($"引擎不支持该转换：{Path.GetFileName(item.SourcePath)} → {item.TargetFormat.Extension}");
            }
        }

        if (errors.Count == 0) return null;

        const int maxShown = 4;
        string joined = string.Join(Environment.NewLine, errors.Take(maxShown).Select(static x => "• " + x));
        if (errors.Count > maxShown)
        {
            joined += Environment.NewLine + $"• 另有 {errors.Count - maxShown} 个问题";
        }

        return "转换前检查未通过，未创建任何文件：" + Environment.NewLine + joined;
    }

    private static string CommonSourceDirectory(IReadOnlyList<ConversionWorkItem> items)
    {
        try
        {
            return Path.GetDirectoryName(items[0].SourcePath) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
