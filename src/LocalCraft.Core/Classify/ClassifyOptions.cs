namespace LocalCraft.Core.Classify;

public sealed record ClassifyOptions
{
    public ClassificationMode Mode { get; init; } = ClassificationMode.ByType;

    public int NamePrefixLength { get; init; } = 1;

    public bool IncludeSubdirectories { get; init; }
}
