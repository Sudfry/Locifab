namespace LocalCraft.Core.Checksums;

public sealed class ChecksumEntry
{
    public string RelativePath { get; init; } = string.Empty;

    public string Hash { get; init; } = string.Empty;

    public long Size { get; init; }
}

public sealed class ChecksumScanResult
{
    public IReadOnlyList<ChecksumEntry> Entries { get; init; } = Array.Empty<ChecksumEntry>();

    public int SkippedCount { get; init; }

    public string? Error { get; init; }

    public bool Success => string.IsNullOrEmpty(Error);
}

public enum ChecksumVerifyState
{
    Ok,
    Missing,
    Changed
}

public sealed class ChecksumVerifyItem
{
    public string RelativePath { get; init; } = string.Empty;

    public string ExpectedHash { get; init; } = string.Empty;

    public string ActualHash { get; init; } = string.Empty;

    public ChecksumVerifyState State { get; init; }

    public string StateText => State switch
    {
        ChecksumVerifyState.Ok => "一致",
        ChecksumVerifyState.Missing => "缺失",
        _ => "已变化"
    };
}

public sealed class ChecksumVerifyResult
{
    public IReadOnlyList<ChecksumVerifyItem> Items { get; init; } = Array.Empty<ChecksumVerifyItem>();

    public string? Error { get; init; }

    public bool Success => string.IsNullOrEmpty(Error);

    public int OkCount => Items.Count(x => x.State == ChecksumVerifyState.Ok);

    public int MissingCount => Items.Count(x => x.State == ChecksumVerifyState.Missing);

    public int ChangedCount => Items.Count(x => x.State == ChecksumVerifyState.Changed);
}
