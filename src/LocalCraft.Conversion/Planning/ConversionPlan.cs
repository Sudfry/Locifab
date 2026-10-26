using LocalCraft.Conversion.Formats;

namespace LocalCraft.Conversion.Planning;

public sealed class ConversionPlan
{
    public ConversionPlan(
        IReadOnlyList<ConversionPlanItem> items,
        string outputDirectory,
        ConversionTargetFormat targetFormat,
        string? setupError = null)
    {
        Items = items;
        OutputDirectory = outputDirectory;
        TargetFormat = targetFormat;
        SetupError = setupError;
    }

    public IReadOnlyList<ConversionPlanItem> Items { get; }

    public string OutputDirectory { get; }

    public ConversionTargetFormat TargetFormat { get; }

    public string? SetupError { get; }

    public int ReadyCount => Items.Count(static x => x.Status == ConversionItemStatus.Ready);

    public int UnsupportedCount => Items.Count(static x => x.Status == ConversionItemStatus.Unsupported);

    public int MissingEngineCount => Items.Count(static x => x.Status == ConversionItemStatus.MissingEngine);

    public int InvalidCount => Items.Count(static x => x.Status == ConversionItemStatus.Invalid);

    public bool HasBlockingItems => UnsupportedCount + MissingEngineCount + InvalidCount > 0;

    public string SourceDirectory => Items.Count > 0
        ? Path.GetDirectoryName(Items[0].SourcePath) ?? string.Empty
        : string.Empty;
}
