using LocalCraft.Conversion.Formats;

namespace LocalCraft.Conversion.Operations;

public sealed record ConversionWorkItem(
    string SourcePath,
    string TargetPath,
    ConversionTargetFormat TargetFormat,
    string EngineId);
