using System.Globalization;

namespace LocalCraft.Core.Tables;

public sealed class TableProcessOptions
{
    public int? FilterColumn { get; set; }

    public TableFilterMode FilterMode { get; set; } = TableFilterMode.Contains;

    public string FilterValue { get; set; } = string.Empty;

    public int? SortColumn { get; set; }

    public bool SortNumeric { get; set; }

    public bool SortDescending { get; set; }

    public IReadOnlyList<int>? KeepColumns { get; set; }
}

public static class TableProcessor
{
    public static TableData Build(IReadOnlyList<TableFileItem> files, TableProcessOptions options)
    {
        if (files.Count == 0) return new TableData();
        string[] headers = files[0].Data.Headers.ToArray();
        var rows = new List<string[]>();
        foreach (var file in files)
        {
            foreach (string[] row in file.Data.Rows)
            {
                if (Matches(row, options)) rows.Add(row.ToArray());
            }
        }

        if (options.SortColumn is int sortColumn && sortColumn >= 0 && sortColumn < headers.Length)
        {
            rows.Sort((a, b) => Compare(
                Get(a, sortColumn),
                Get(b, sortColumn),
                options.SortNumeric));
            if (options.SortDescending) rows.Reverse();
        }

        int[] keep = options.KeepColumns is { Count: > 0 }
            ? options.KeepColumns.Where(x => x >= 0 && x < headers.Length).Distinct().ToArray()
            : Enumerable.Range(0, headers.Length).ToArray();

        if (keep.Length == 0) keep = Enumerable.Range(0, headers.Length).ToArray();
        var selectedHeaders = keep.Select(x => headers[x]).ToArray();
        var selectedRows = rows.Select(row => keep.Select(x => Get(row, x)).ToArray()).ToList();
        return new TableData { Headers = selectedHeaders, Rows = selectedRows };
    }

    private static bool Matches(string[] row, TableProcessOptions options)
    {
        if (options.FilterColumn is not int column || column < 0) return true;
        string value = Get(row, column);
        string target = options.FilterValue;
        return options.FilterMode switch
        {
            TableFilterMode.Contains => value.Contains(target, StringComparison.OrdinalIgnoreCase),
            TableFilterMode.NotContains => !value.Contains(target, StringComparison.OrdinalIgnoreCase),
            TableFilterMode.Equals => string.Equals(value, target, StringComparison.OrdinalIgnoreCase),
            TableFilterMode.NotEquals => !string.Equals(value, target, StringComparison.OrdinalIgnoreCase),
            TableFilterMode.GreaterThan => TryNumber(value, out double left)
                                            && TryNumber(target, out double right) && left > right,
            TableFilterMode.LessThan => TryNumber(value, out double left)
                                         && TryNumber(target, out double right) && left < right,
            TableFilterMode.Empty => string.IsNullOrWhiteSpace(value),
            TableFilterMode.NotEmpty => !string.IsNullOrWhiteSpace(value),
            _ => true
        };
    }

    private static int Compare(string a, string b, bool numeric)
    {
        if (numeric && TryNumber(a, out double left) && TryNumber(b, out double right))
        {
            return left.CompareTo(right);
        }

        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryNumber(string value, out double number)
    {
        return double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out number)
               || double.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out number);
    }

    private static string Get(string[] row, int index)
    {
        return index >= 0 && index < row.Length ? row[index] : string.Empty;
    }
}
