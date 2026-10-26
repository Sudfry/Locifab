using LocalCraft.Conversion.Engines;
using LocalCraft.Conversion.Formats;
using LocalCraft.Core.Infrastructure;

namespace LocalCraft.Conversion.Planning;

/// <summary>
/// 只读转换规划：计算输出路径、检查引擎与冲突，不创建任何文件或目录。
/// </summary>
public sealed class ConversionPlanner
{
    private readonly ConversionEngineCatalog _engines;

    public ConversionPlanner(ConversionEngineCatalog engines)
    {
        _engines = engines ?? throw new ArgumentNullException(nameof(engines));
    }

    public ConversionPlan CreatePlan(
        IEnumerable<string> sourcePaths,
        string outputDirectory,
        ConversionTargetFormat targetFormat)
    {
        var sources = sourcePaths
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        sources.Sort(static (x, y) =>
            NaturalStringComparer.Instance.Compare(Path.GetFileName(x), Path.GetFileName(y)));

        string normalizedOutput = string.Empty;
        string? setupError = null;
        try
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                setupError = "请选择输出文件夹";
            }
            else
            {
                normalizedOutput = Path.GetFullPath(outputDirectory.Trim().Trim('"'));
                if (File.Exists(normalizedOutput))
                {
                    setupError = "输出路径是一个文件，请选择文件夹";
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            setupError = "输出文件夹路径无效";
        }

        var plannedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<ConversionPlanItem>(sources.Count);

        for (int i = 0; i < sources.Count; i++)
        {
            string source = sources[i];
            string sourceExtension = SourceFormats.NormalizeExtension(source);
            ConversionKind kind = SourceFormats.GetKind(source) ?? ConversionKind.Document;
            string baseName = Path.GetFileNameWithoutExtension(source);

            string targetPath = string.IsNullOrEmpty(normalizedOutput)
                ? string.Empty
                : Path.Combine(normalizedOutput, baseName + targetFormat.Extension);

            ConversionItemStatus status;
            string note;
            string? engineId = null;
            string? engineName = null;

            if (!SourceFormats.IsSupported(sourceExtension))
            {
                status = ConversionItemStatus.Unsupported;
                note = "不支持的文件类型";
            }
            else if (setupError is not null)
            {
                status = ConversionItemStatus.Invalid;
                note = setupError;
            }
            else if (!File.Exists(source))
            {
                status = ConversionItemStatus.Invalid;
                note = "源文件不存在或已不可访问";
            }
            else
            {
                ConversionEngineMatch match = _engines.Resolve(source, targetFormat);
                if (!match.IsSupported || match.Engine is null)
                {
                    status = IsMissingEngineReason(match.Note)
                        ? ConversionItemStatus.MissingEngine
                        : ConversionItemStatus.Unsupported;
                    note = match.Note;
                }
                else
                {
                    engineId = match.Engine.Id;
                    engineName = match.Engine.DisplayName;
                    status = ConversionItemStatus.Ready;
                    note = match.Note;
                }
            }

            long size = 0;
            try
            {
                if (File.Exists(source)) size = new FileInfo(source).Length;
            }
            catch
            {
                // 大小读取失败不阻断规划。
            }

            if (status == ConversionItemStatus.Ready)
            {
                try
                {
                    targetPath = EnsureUniqueTarget(
                        normalizedOutput,
                        baseName,
                        targetFormat.Extension,
                        plannedTargets,
                        source);

                    bool avoidedExisting = !string.Equals(targetPath, Path.Combine(normalizedOutput, baseName + targetFormat.Extension), StringComparison.OrdinalIgnoreCase);
                    if (avoidedExisting)
                    {
                        note += " · 输出已自动避开同名文件";
                    }

                    if (targetPath.Length > 259)
                    {
                        status = ConversionItemStatus.Invalid;
                        note = "输出完整路径超过 259 个字符";
                    }
                }
                catch (Exception ex) when (ex is IOException or ArgumentException or PathTooLongException)
                {
                    status = ConversionItemStatus.Invalid;
                    note = "无法生成输出路径：" + ex.Message;
                }
            }

            plannedTargets.Add(targetPath);
            items.Add(new ConversionPlanItem
            {
                Index = i + 1,
                SourcePath = source,
                TargetPath = targetPath,
                TargetFormat = targetFormat,
                SourceKind = kind,
                Status = status,
                Note = note,
                EngineId = engineId,
                EngineName = engineName,
                SourceSize = size
            });
        }

        return new ConversionPlan(items, normalizedOutput, targetFormat, setupError);
    }

    private static string EnsureUniqueTarget(
        string outputDirectory,
        string baseName,
        string extension,
        HashSet<string> plannedTargets,
        string sourcePath)
    {
        string candidate = Path.Combine(outputDirectory, baseName + extension);
        int suffix = 1;
        while (plannedTargets.Contains(candidate)
               || File.Exists(candidate)
               || Directory.Exists(candidate)
               || string.Equals(candidate, sourcePath, StringComparison.OrdinalIgnoreCase))
        {
            candidate = Path.Combine(outputDirectory, $"{baseName}_{suffix}{extension}");
            suffix++;
            if (suffix > 10_000)
            {
                throw new IOException("无法分配唯一的输出文件名。");
            }
        }

        return candidate;
    }

    private static bool IsMissingEngineReason(string note)
    {
        return note.Contains("引擎", StringComparison.Ordinal)
               || note.Contains("解码器", StringComparison.Ordinal)
               || note.Contains("图像扩展", StringComparison.Ordinal)
               || note.Contains("PDF", StringComparison.Ordinal)
               || note.Contains("旧版 Office", StringComparison.Ordinal);
    }
}
