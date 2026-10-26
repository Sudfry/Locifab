namespace LocalCraft.Core.TextReplacement;

public sealed record TextReplaceMatch(int LineNumber, string Before, string After, int Index = 0);

public sealed class TextReplaceFilePlan
{
    public string FilePath { get; init; } = string.Empty;

    public int MatchCount { get; init; }

    public DateTime LastWriteTimeUtc { get; init; }

    public IReadOnlyList<TextReplaceMatch> Matches { get; init; } = Array.Empty<TextReplaceMatch>();

    public string FileName => Path.GetFileName(FilePath);

    public string Preview => Matches.Count > 0 ? Matches[0].After : string.Empty;
}

public sealed class TextReplaceScanResult
{
    public IReadOnlyList<TextReplaceFilePlan> Files { get; init; } = Array.Empty<TextReplaceFilePlan>();

    public int TotalMatches { get; init; }

    public int SkippedFiles { get; init; }

    public string? Error { get; init; }

    public bool Success => string.IsNullOrEmpty(Error);
}
