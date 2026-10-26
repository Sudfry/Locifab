using LocalCraft.App.ViewModels;

namespace LocalCraft.App.Services;

public static class ToolPresetMapper
{
    public static PipelineStepData? TryCreate(string toolKey, object page)
    {
        PipelineStepKind? kind = toolKey.ToLowerInvariant() switch
        {
            "rename" => PipelineStepKind.Rename,
            "imgcompress" => PipelineStepKind.ImageTarget,
            "imgcheck" => PipelineStepKind.ImageCheck,
            "embedimg" => PipelineStepKind.EmbeddedImages,
            "convert" => PipelineStepKind.Convert,
            "findrep" => PipelineStepKind.TextReplace,
            "archive" => PipelineStepKind.Archive,
            "metadata" => PipelineStepKind.Metadata,
            "checksum" => PipelineStepKind.Checksum,
            "classify" => PipelineStepKind.Classify,
            "template" => PipelineStepKind.Template,
            "report" => PipelineStepKind.Report,
            "filestate" => PipelineStepKind.FileState,
            "splitmerge" => PipelineStepKind.SplitMerge,
            "cleanup" => PipelineStepKind.Cleanup,
            "backup" => PipelineStepKind.Backup,
            "dedup" => PipelineStepKind.Dedup,
            "photos" => PipelineStepKind.PhotoDedup,
            "images" => PipelineStepKind.ImageBatch,
            _ => null
        };
        if (kind is null || page is null) return null;
        var step = new PipelineStep(kind.Value, _ => { }, (_, _) => { });
        switch (page)
        {
            case RenameViewModel rename when kind == PipelineStepKind.Rename:
                step.Text1 = rename.Prefix;
                step.Text2 = rename.Suffix;
                if (int.TryParse(rename.StartNumberText, out int start)) step.Number1 = start;
                if (int.TryParse(rename.NumberPaddingText, out int digits)) step.Number2 = digits;
                step.Text3 = rename.NumberSeparator;
                step.Bool1 = rename.UseNumbering;
                break;
            case ToolboxViewModel toolbox when kind == PipelineStepKind.ImageTarget && toolbox.ActiveToolTab == 1:
                step.Number1 = Math.Max(1, (int)Math.Round(toolbox.TargetKb));
                step.Text1 = SafeName(toolbox.ImageOutput);
                break;
            case ToolboxViewModel toolbox when kind == PipelineStepKind.ImageCheck && toolbox.ActiveToolTab == 2:
                step.Text1 = "损坏图片隔离";
                break;
            case ToolboxViewModel toolbox when kind == PipelineStepKind.EmbeddedImages && toolbox.ActiveToolTab == 3:
                step.Text1 = SafeName(toolbox.OfficeOutput);
                break;
            case ConvertViewModel convert when kind == PipelineStepKind.Convert:
                step.Text1 = SafeName(convert.OutputDirectory);
                string choice = convert.SelectedTarget.Extension.TrimStart('.').ToLowerInvariant();
                if (step.ChoiceOptions.Contains(choice)) step.Choice = choice;
                break;
            case TextReplaceViewModel text when kind == PipelineStepKind.TextReplace:
                step.Text1 = text.FindText;
                step.Text2 = text.ReplaceText;
                if (!text.AnyExtension) step.Choice = text.IncludeExtensions;
                break;
            case ArchiveViewModel archive when kind == PipelineStepKind.Archive:
                step.Choice = archive.IsCompress ? "Compress" : "Extract";
                step.Text1 = SafeName(archive.TargetPath);
                step.Text2 = archive.Password;
                break;
            case MetadataViewModel metadata when kind == PipelineStepKind.Metadata:
                step.Text1 = SafeName(metadata.OutputFolder);
                break;
            case ChecksumViewModel checksum when kind == PipelineStepKind.Checksum:
                step.Text1 = SafeName(checksum.ManifestPath);
                break;
            case ClassifyViewModel classify when kind == PipelineStepKind.Classify:
                string? mode = Nested(classify.SelectedMode, "Mode");
                if (mode is "ByType" or "ByYear" or "ByMonth" or "ByNamePrefix") step.Choice = mode;
                break;
            case TemplateViewModel template when kind == PipelineStepKind.Template:
                step.Text1 = template.Template;
                break;
            case ReportViewModel report when kind == PipelineStepKind.Report:
                step.Text1 = SafeName(report.OutputPath);
                step.Bool1 = report.IncludeHash;
                break;
            case FileStateViewModel state when kind == PipelineStepKind.FileState:
                step.Text1 = state.TimestampOffset ? state.OffsetDays.ToString("0.#") : state.FixedTime;
                step.Bool1 = state.SetReadOnly;
                step.Bool2 = state.SetHidden;
                break;
            case SplitMergeViewModel split when kind == PipelineStepKind.SplitMerge:
                step.Choice = split.IsSplit ? "Split" : "Merge";
                step.Text1 = SafeName(split.OutputFolder);
                step.Text2 = split.MergePattern;
                step.Number1 = (int)Math.Max(1, Math.Round(split.SizeMb));
                break;
            case CleanupViewModel cleanup when kind == PipelineStepKind.Cleanup:
                step.Bool1 = true;
                step.Bool2 = true;
                break;
            case BackupViewModel backup when kind == PipelineStepKind.Backup:
                step.Text1 = SafeName(backup.TargetFolder);
                break;
            case DedupViewModel dedup when kind == PipelineStepKind.Dedup:
                step.Text1 = SafeName(dedup.QuarantineDirectory);
                break;
            case PhotoDedupViewModel photos when kind == PipelineStepKind.PhotoDedup:
                step.Text1 = SafeName(photos.QuarantineDirectory);
                step.Number1 = (int)Math.Round(photos.ThresholdPercent);
                break;
            case ImageBatchViewModel images when kind == PipelineStepKind.ImageBatch:
                step.Text1 = SafeName(images.OutputFolder);
                step.Number1 = images.SizeValue;
                step.Number2 = images.JpegQuality;
                break;
        }
        return step.ToData();
    }

    private static string? Nested(object? instance, string property)
    {
        if (instance is null) return null;
        return instance.GetType().GetProperty(property)?.GetValue(instance)?.ToString();
    }

    private static string SafeName(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        return Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }
}
