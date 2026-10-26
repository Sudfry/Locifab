namespace LocalCraft.Core.Tables;

public sealed class TableData
{
    public IReadOnlyList<string> Headers { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string[]> Rows { get; init; } = Array.Empty<string[]>();
}

public sealed class TableFileItem
{
    public string FilePath { get; init; } = string.Empty;

    public string FileName => Path.GetFileName(FilePath);

    public string Extension => Path.GetExtension(FilePath).TrimStart('.').ToUpperInvariant();

    public int RowCount { get; init; }

    public IReadOnlyList<string> Headers { get; init; } = Array.Empty<string>();

    public TableData Data { get; init; } = new();

    public string HeaderText => Headers.Count == 0 ? "（无表头）" : string.Join(" · ", Headers.Take(6));
}

public sealed class TableScanResult
{
    public IReadOnlyList<TableFileItem> Files { get; init; } = Array.Empty<TableFileItem>();

    public int SkippedCount { get; init; }

    public string? Error { get; init; }

    public bool Success => string.IsNullOrEmpty(Error);
}

public enum TableFilterMode
{
    Contains,
    NotContains,
    Equals,
    NotEquals,
    GreaterThan,
    LessThan,
    Empty,
    NotEmpty
}
