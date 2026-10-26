namespace LocalCraft.Core.Archives;

public enum ArchiveMode
{
    Compress,
    Extract
}

public sealed class ArchiveOptions
{
    public ArchiveMode Mode { get; set; } = ArchiveMode.Compress;

    public string SourcePath { get; set; } = string.Empty;

    public string TargetPath { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}

public sealed class ArchivePlanEntry
{
    public string Name { get; init; } = string.Empty;

    public long Size { get; init; }

    public string TargetPath { get; init; } = string.Empty;
}

public sealed class ArchivePlan
{
    public ArchiveMode Mode { get; init; }

    public string SourcePath { get; init; } = string.Empty;

    public string TargetPath { get; init; } = string.Empty;

    public IReadOnlyList<ArchivePlanEntry> Entries { get; init; } = Array.Empty<ArchivePlanEntry>();

    public string? Error { get; init; }

    public string Password { get; init; } = string.Empty;

    public bool Success => string.IsNullOrEmpty(Error);

    public long TotalBytes => Entries.Sum(x => x.Size);
}
