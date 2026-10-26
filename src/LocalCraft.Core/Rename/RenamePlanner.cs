using LocalCraft.Core.Infrastructure;

namespace LocalCraft.Core.Rename;

/// <summary>
/// 只读规划器：只生成“旧状态 → 新状态”的预览，不写入文件系统。
/// </summary>
public sealed class RenamePlanner
{
    public RenamePlan CreatePlan(IEnumerable<string> sourcePaths, RenameOptions? options = null)
    {
        options ??= RenameOptions.Default;

        var normalized = sourcePaths
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        normalized.Sort(static (x, y) =>
            NaturalStringComparer.Instance.Compare(Path.GetFileName(x), Path.GetFileName(y)));

        var sourceMap = normalized.ToDictionary(static x => x, StringComparer.OrdinalIgnoreCase);
        var drafts = new List<Draft>(normalized.Count);

        for (int i = 0; i < normalized.Count; i++)
        {
            string source = normalized[i];
            string originalName = Path.GetFileName(source);
            string proposedName = string.Empty;
            string targetPath = source;
            RenameItemStatus status;
            string note = string.Empty;
            bool caseOnly = false;

            try
            {
                proposedName = FileNameRules.BuildName(originalName, options.StartNumber + i, options);
                targetPath = Path.Combine(Path.GetDirectoryName(source) ?? string.Empty, proposedName);

                if (!File.Exists(source))
                {
                    status = RenameItemStatus.Invalid;
                    note = "源文件不存在或已不可访问";
                }
                else if (!FileNameRules.TryValidate(proposedName, source, out string validationError))
                {
                    status = RenameItemStatus.Invalid;
                    note = validationError;
                }
                else if (string.Equals(originalName, proposedName, StringComparison.Ordinal))
                {
                    status = RenameItemStatus.Unchanged;
                    note = "规则未改变文件名";
                }
                else
                {
                    status = RenameItemStatus.Ready;
                    note = "可以执行";
                    caseOnly = string.Equals(source, targetPath, StringComparison.OrdinalIgnoreCase);
                    if (caseOnly)
                    {
                        note = "仅修改文件名大小写";
                    }
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                status = RenameItemStatus.Invalid;
                note = "无法生成合法路径";
            }

            drafts.Add(new Draft
            {
                Index = i + 1,
                SourcePath = source,
                TargetPath = targetPath,
                OriginalName = originalName,
                ProposedName = proposedName,
                Status = status,
                Note = note,
                CaseOnly = caseOnly
            });
        }

        MarkDuplicateTargets(drafts);

        foreach (var draft in drafts)
        {
            if (draft.Status is not (RenameItemStatus.Ready or RenameItemStatus.Unchanged)) continue;

            if (string.Equals(draft.SourcePath, draft.TargetPath, StringComparison.Ordinal))
            {
                draft.Status = RenameItemStatus.Unchanged;
                draft.Note = "规则未改变文件名";
                continue;
            }

            bool targetExists = File.Exists(draft.TargetPath) || Directory.Exists(draft.TargetPath);

            if (Directory.Exists(draft.TargetPath))
            {
                draft.Status = RenameItemStatus.Conflict;
                draft.Note = "目标位置存在同名文件夹";
                continue;
            }

            if (draft.CaseOnly)
            {
                draft.Status = RenameItemStatus.Ready;
                draft.Note = "仅修改文件名大小写";
                continue;
            }

            if (sourceMap.ContainsKey(draft.TargetPath))
            {
                draft.Status = RenameItemStatus.Conflict;
                draft.Note = "目标名称与批次中的另一个源文件相同";
                continue;
            }

            if (targetExists)
            {
                if (options.AllowOverwrite)
                {
                    draft.Status = RenameItemStatus.Overwrite;
                    draft.Note = "目标已存在，执行前会先备份原文件";
                }
                else
                {
                    draft.Status = RenameItemStatus.Conflict;
                    draft.Note = "目标已存在；可勾选「允许覆盖」或调整规则";
                }
            }
        }

        var items = drafts.Select(static d => new RenamePlanItem
        {
            Index = d.Index,
            SourcePath = d.SourcePath,
            TargetPath = d.TargetPath,
            OriginalName = d.OriginalName,
            ProposedName = d.ProposedName,
            Status = d.Status,
            Note = d.Note,
            IsCaseOnlyChange = d.CaseOnly
        }).ToList();

        return new RenamePlan(items, options, DateTimeOffset.Now);
    }

    private static void MarkDuplicateTargets(List<Draft> drafts)
    {
        foreach (var group in drafts
                     .Where(static d => d.Status is RenameItemStatus.Ready or RenameItemStatus.Unchanged)
                     .GroupBy(static d => d.TargetPath, StringComparer.OrdinalIgnoreCase)
                     .Where(static g => g.Count() > 1))
        {
            var groupList = group.ToList();
            foreach (var draft in groupList)
            {
                draft.Status = RenameItemStatus.Conflict;
                draft.Note = $"{groupList.Count} 个文件将得到同一名称";
            }
        }
    }

    private sealed class Draft
    {
        public int Index { get; init; }
        public required string SourcePath { get; init; }
        public required string TargetPath { get; set; }
        public required string OriginalName { get; init; }
        public required string ProposedName { get; set; }
        public RenameItemStatus Status { get; set; }
        public string Note { get; set; } = string.Empty;
        public bool CaseOnly { get; set; }
    }
}
