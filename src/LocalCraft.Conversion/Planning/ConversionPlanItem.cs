using LocalCraft.Conversion.Formats;

namespace LocalCraft.Conversion.Planning;

public sealed class ConversionPlanItem
{
    public required int Index { get; init; }

    public required string SourcePath { get; init; }

    public required string TargetPath { get; init; }

    public required ConversionTargetFormat TargetFormat { get; init; }

    public required ConversionKind SourceKind { get; init; }

    public required ConversionItemStatus Status { get; init; }

    public required string Note { get; init; }

    public string? EngineId { get; init; }

    public string? EngineName { get; init; }

    public long SourceSize { get; init; }

    public string SourceName => Path.GetFileName(SourcePath);

    public string TargetName => Path.GetFileName(TargetPath);

    public bool IsExecutable => Status == ConversionItemStatus.Ready;
}
