using System.Globalization;

namespace LocalCraft.Core.Infrastructure;

/// <summary>
/// 对文件名做自然排序，让 "第2章" 排在 "第10章" 前面。
/// </summary>
public sealed class NaturalStringComparer : IComparer<string>
{
    public static readonly NaturalStringComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                int startX = i, startY = j;
                while (i < x.Length && char.IsDigit(x[i])) i++;
                while (j < y.Length && char.IsDigit(y[j])) j++;

                var spanX = x.AsSpan(startX, i - startX).TrimStart('0');
                var spanY = y.AsSpan(startY, j - startY).TrimStart('0');
                int lengthCompare = spanX.Length.CompareTo(spanY.Length);
                if (lengthCompare != 0) return lengthCompare;

                int numericCompare = spanX.SequenceCompareTo(spanY);
                if (numericCompare != 0) return numericCompare;

                continue;
            }

            int charCompare = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
            if (charCompare != 0) return charCompare;
            i++;
            j++;
        }

        return (x.Length - i).CompareTo(y.Length - j);
    }
}
