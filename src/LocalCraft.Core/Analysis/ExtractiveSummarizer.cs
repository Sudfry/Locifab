namespace LocalCraft.Core.Analysis;

public sealed record SummarySentence(
    string Text,
    double Score,
    int Offset,
    int LineNumber,
    int Rank);

public sealed record SummaryKeyword(string Text, double Weight, int Frequency);

public sealed record SummaryResult(
    IReadOnlyList<SummarySentence> Sentences,
    IReadOnlyList<SummaryKeyword> Keywords,
    int TotalSentences,
    int TotalCharacters);

/// <summary>
/// 纯本地抽取式摘要：TextRank 给句子排序，TF-IDF 给关键词排序。
/// 只输出原文句子，不生成新内容，并保留字符偏移供定位。
/// </summary>
public sealed class ExtractiveSummarizer
{
    public SummaryResult Summarize(string text, int maxSentences = 5, int maxKeywords = 10)
    {
        text ??= string.Empty;
        maxSentences = Math.Clamp(maxSentences, 1, 20);
        maxKeywords = Math.Clamp(maxKeywords, 1, 30);

        var spans = TextTokenizer.SplitSentences(text);
        if (spans.Count == 0)
        {
            return new SummaryResult(Array.Empty<SummarySentence>(), Array.Empty<SummaryKeyword>(), 0, text.Length);
        }

        var sentenceTokens = spans
            .Select(s => TextTokenizer.Tokenize(s.Text))
            .ToList();

        var tokenSets = sentenceTokens
            .Select(tokens => tokens.ToHashSet(StringComparer.Ordinal))
            .ToList();

        int count = spans.Count;
        var scores = Enumerable.Repeat(1.0, count).ToArray();
        var adjacency = new double[count, count];

        for (int i = 0; i < count; i++)
        {
            for (int j = i + 1; j < count; j++)
            {
                double similarity = Similarity(sentenceTokens[i], sentenceTokens[j], tokenSets[i], tokenSets[j]);
                adjacency[i, j] = similarity;
                adjacency[j, i] = similarity;
            }
        }

        // TextRank 迭代。
        for (int iteration = 0; iteration < 30; iteration++)
        {
            var next = new double[count];
            for (int i = 0; i < count; i++)
            {
                double sum = 0;
                for (int j = 0; j < count; j++)
                {
                    if (i == j) continue;
                    double weight = adjacency[j, i];
                    if (weight <= 0) continue;
                    double outgoing = 0;
                    for (int k = 0; k < count; k++)
                    {
                        if (k != j) outgoing += adjacency[j, k];
                    }
                    if (outgoing > 0) sum += weight / outgoing * scores[j];
                }
                next[i] = 0.15 + 0.85 * sum;
            }
            scores = next;
        }

        double maxScore = scores.Max();
        if (maxScore <= 0) maxScore = 1;

        var selected = Enumerable.Range(0, count)
            .Where(i => spans[i].Text.Length >= 4)
            .OrderByDescending(i => scores[i])
            .ThenBy(i => spans[i].Offset)
            .Take(maxSentences)
            .ToList();

        if (selected.Count == 0)
        {
            selected = Enumerable.Range(0, count).Take(maxSentences).ToList();
        }

        var ranked = selected
            .Select((index, rank) => new SummarySentence(
                spans[index].Text,
                scores[index] / maxScore,
                spans[index].Offset,
                spans[index].LineNumber,
                rank + 1))
            .ToList();

        var keywords = ExtractKeywords(sentenceTokens, count, maxKeywords);

        return new SummaryResult(ranked, keywords, count, text.Length);
    }

    private static double Similarity(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right,
        IReadOnlySet<string> leftSet,
        IReadOnlySet<string> rightSet)
    {
        if (left.Count == 0 || right.Count == 0) return 0;

        int overlap = leftSet.Count(rightSet.Contains);
        if (overlap == 0) return 0;

        double denominator = Math.Log(left.Count + 1) + Math.Log(right.Count + 1);
        return denominator <= 0 ? 0 : overlap / denominator;
    }

    private static IReadOnlyList<SummaryKeyword> ExtractKeywords(
        List<List<string>> sentenceTokens,
        int sentenceCount,
        int maxKeywords)
    {
        var termFrequency = new Dictionary<string, int>(StringComparer.Ordinal);
        var documentFrequency = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var tokens in sentenceTokens)
        {
            foreach (var token in tokens)
            {
                termFrequency.TryGetValue(token, out int frequency);
                termFrequency[token] = frequency + 1;
            }

            foreach (string token in tokens.Distinct(StringComparer.Ordinal))
            {
                documentFrequency.TryGetValue(token, out int df);
                documentFrequency[token] = df + 1;
            }
        }

        var ranked = termFrequency
            .Where(static pair => pair.Key.Length >= 2)
            .Select(pair =>
            {
                int df = documentFrequency.TryGetValue(pair.Key, out int value) ? value : 1;
                double idf = Math.Log((1.0 + sentenceCount) / (1.0 + df)) + 1.0;
                return new SummaryKeyword(pair.Key, pair.Value * idf, pair.Value);
            })
            .OrderByDescending(static x => x.Weight)
            .ThenByDescending(static x => x.Frequency)
            .Take(maxKeywords)
            .ToList();

        double maxWeight = ranked.Count > 0 ? ranked.Max(static x => x.Weight) : 1;
        if (maxWeight <= 0) maxWeight = 1;

        return ranked
            .Select(x => new SummaryKeyword(x.Text, x.Weight / maxWeight, x.Frequency))
            .ToList();
    }
}
