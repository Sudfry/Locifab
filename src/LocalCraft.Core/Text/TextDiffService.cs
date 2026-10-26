using LocalCraft.Core.Files;

namespace LocalCraft.Core.Text;

public enum DiffKind
{
    Same,
    Added,
    Removed
}

public sealed class DiffLine
{
    public DiffKind Kind { get; init; }
    public int? OldNumber { get; init; }
    public int? NewNumber { get; init; }
    public string Text { get; init; } = string.Empty;
    public string Marker => Kind == DiffKind.Added ? "+" : Kind == DiffKind.Removed ? "−" : " ";
}

public sealed class DiffResult
{
    public IReadOnlyList<DiffLine> Lines { get; init; } = Array.Empty<DiffLine>();
    public int AddedCount => Lines.Count(x => x.Kind == DiffKind.Added);
    public int RemovedCount => Lines.Count(x => x.Kind == DiffKind.Removed);
    public string? Error { get; init; }
    public bool Success => string.IsNullOrEmpty(Error);
}

public static class TextDiffService
{
    public static DiffResult Compare(string leftPath, string rightPath)
    {
        if (!File.Exists(leftPath) || !File.Exists(rightPath))
            return new DiffResult { Error = "请选择两个存在的文本文件。" };
        try
        {
            string leftText = EncodingConversionService.Decode(File.ReadAllBytes(leftPath), out _);
            string rightText = EncodingConversionService.Decode(File.ReadAllBytes(rightPath), out _);
            string[] left = Split(leftText);
            string[] right = Split(rightText);
            if (left.Length > 3000 || right.Length > 3000)
                return new DiffResult { Error = "文本超过 3000 行，暂不做逐行对比。" };

            int[,] lcs = new int[left.Length + 1, right.Length + 1];
            for (int i = left.Length - 1; i >= 0; i--)
            {
                for (int j = right.Length - 1; j >= 0; j--)
                {
                    lcs[i, j] = left[i] == right[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
                }
            }

            var result = new List<DiffLine>();
            int x = 0, y = 0;
            while (x < left.Length && y < right.Length)
            {
                if (left[x] == right[y])
                {
                    result.Add(new DiffLine { Kind = DiffKind.Same, OldNumber = x + 1, NewNumber = y + 1, Text = left[x] });
                    x++;
                    y++;
                }
                else if (lcs[x + 1, y] >= lcs[x, y + 1])
                {
                    result.Add(new DiffLine { Kind = DiffKind.Removed, OldNumber = x + 1, Text = left[x] });
                    x++;
                }
                else
                {
                    result.Add(new DiffLine { Kind = DiffKind.Added, NewNumber = y + 1, Text = right[y] });
                    y++;
                }
            }

            while (x < left.Length)
            {
                result.Add(new DiffLine { Kind = DiffKind.Removed, OldNumber = x + 1, Text = left[x] });
                x++;
            }
            while (y < right.Length)
            {
                result.Add(new DiffLine { Kind = DiffKind.Added, NewNumber = y + 1, Text = right[y] });
                y++;
            }

            return new DiffResult { Lines = result };
        }
        catch (Exception ex)
        {
            return new DiffResult { Error = "读取文本失败：" + ex.Message };
        }
    }

    private static string[] Split(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
}
