namespace LocalCraft.Core.Analysis;

public sealed record DedupDocument(
    string Path,
    string Text,
    ulong Fingerprint,
    int TokenCount,
    long Size)
{
    public string FileName => System.IO.Path.GetFileName(Path);

    public string Preview
    {
        get
        {
            string text = Text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return text.Length <= 120 ? text : text[..120] + "…";
        }
    }
}

public sealed record DedupGroup(
    IReadOnlyList<DedupDocument> Documents,
    double Similarity,
    int RepresentativeIndex)
{
    public DedupDocument Representative => Documents[RepresentativeIndex];
}

public sealed class DedupEngine
{
    public const int MinimumTokenCount = 12;

    public IReadOnlyList<DedupGroup> Group(IEnumerable<DedupDocument> documents, double threshold)
    {
        threshold = Math.Clamp(threshold, 0.50, 1.0);
        var candidates = documents
            .Where(static x => x.TokenCount >= MinimumTokenCount)
            .OrderByDescending(static x => x.Text.Length)
            .ThenBy(static x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int n = candidates.Count;
        if (n < 2) return Array.Empty<DedupGroup>();

        var parent = Enumerable.Range(0, n).ToArray();
        var pairSimilarity = new Dictionary<(int, int), double>();

        int Find(int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }
            return x;
        }

        void Union(int a, int b)
        {
            int rootA = Find(a);
            int rootB = Find(b);
            if (rootA != rootB) parent[rootB] = rootA;
        }

        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                double similarity = SimHash.Similarity(candidates[i].Fingerprint, candidates[j].Fingerprint);
                if (similarity < threshold) continue;

                pairSimilarity[(i, j)] = similarity;
                Union(i, j);
            }
        }

        var groups = new List<DedupGroup>();
        foreach (var cluster in Enumerable.Range(0, n).GroupBy(Find))
        {
            var indexes = cluster.OrderBy(static x => x).ToList();
            if (indexes.Count < 2) continue;

            double minimum = 1.0;
            for (int a = 0; a < indexes.Count; a++)
            {
                for (int b = a + 1; b < indexes.Count; b++)
                {
                    if (pairSimilarity.TryGetValue((indexes[a], indexes[b]), out double similarity))
                    {
                        minimum = Math.Min(minimum, similarity);
                    }
                    else
                    {
                        // 传递分组时，取组内文档与代表文档的相似度作为近似下限。
                        minimum = Math.Min(minimum, SimHash.Similarity(
                            candidates[indexes[a]].Fingerprint,
                            candidates[indexes[0]].Fingerprint));
                    }
                }
            }

            var docs = indexes.Select(x => candidates[x]).ToList();
            groups.Add(new DedupGroup(docs, minimum, 0));
        }

        return groups
            .OrderByDescending(static g => g.Documents.Count)
            .ThenByDescending(static g => g.Similarity)
            .ThenBy(static g => g.Documents[0].FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
