namespace LocalCraft.Core.Analysis;

public static class SimHash
{
    public static ulong Compute(IEnumerable<string> tokens)
    {
        var frequencies = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string token in tokens)
        {
            if (string.IsNullOrEmpty(token)) continue;
            frequencies.TryGetValue(token, out int count);
            frequencies[token] = count + 1;
        }

        if (frequencies.Count == 0) return 0;

        var vector = new int[64];
        foreach (var pair in frequencies)
        {
            ulong hash = Fnv1a64(pair.Key);
            int weight = Math.Min(pair.Value, 16);
            for (int bit = 0; bit < 64; bit++)
            {
                vector[bit] += (hash & (1UL << bit)) != 0 ? weight : -weight;
            }
        }

        ulong fingerprint = 0;
        for (int bit = 0; bit < 64; bit++)
        {
            if (vector[bit] > 0) fingerprint |= 1UL << bit;
        }

        return fingerprint;
    }

    public static int HammingDistance(ulong left, ulong right)
    {
        return System.Numerics.BitOperations.PopCount(left ^ right);
    }

    public static double Similarity(ulong left, ulong right)
    {
        return 1.0 - HammingDistance(left, right) / 64.0;
    }

    private static ulong Fnv1a64(string value)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;

        foreach (char c in value)
        {
            hash ^= char.ToLowerInvariant(c);
            hash *= prime;
        }

        return hash;
    }
}
