using System.Text;
using System.Text.RegularExpressions;

namespace LocalCraft.Core.Analysis;

public static class TextTokenizer
{
    private static readonly HashSet<char> StopCharacters = new()
    {
        '的', '了', '是', '在', '和', '我', '你', '他', '她', '它', '这', '那', '有', '就', '不',
        '也', '都', '很', '与', '及', '等', '中', '上', '下', '为', '对', '以', '于', '而', '其',
        '或', '将', '把', '被', '从', '到', '由', '并', '但', '则', '让', '使', '给', '着', '过',
        '吗', '呢', '吧', '啊', '之', '与', '一', '个'
    };

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "that", "this", "from", "are", "was", "were", "have", "has",
        "had", "not", "but", "you", "your", "our", "they", "their", "its", "into", "about",
        "which", "will", "would", "can", "could", "there", "here", "then", "than", "also"
    };

    public static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return tokens;

        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];

            if (IsCjk(c))
            {
                int start = i;
                while (i < text.Length && IsCjk(text[i])) i++;
                AddCjkTokens(text.AsSpan(start, i - start), tokens);
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                int start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '-')) i++;
                string word = text[start..i].ToLowerInvariant();
                if (word.Length > 1 && !StopWords.Contains(word))
                {
                    tokens.Add(word);
                }
                continue;
            }

            i++;
        }

        return tokens;
    }

    public static IReadOnlyList<SentenceSpan> SplitSentences(string text)
    {
        var result = new List<SentenceSpan>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        int line = 1;
        int lineStart = 0;
        foreach (string rawLine in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            int lineOffset = lineStart;
            foreach (Match match in Regex.Matches(rawLine, @"[^。！？!?；;]+(?:[。！？!?；;]+|$)"))
            {
                string raw = match.Value;
                string sentence = raw.Trim();
                if (sentence.Length < 2) continue;

                int leading = raw.Length - raw.TrimStart().Length;
                int offset = lineOffset + match.Index + leading;
                result.Add(new SentenceSpan(sentence, offset, line));
            }

            lineStart += rawLine.Length + 1;
            line++;
        }

        if (result.Count == 0)
        {
            string trimmed = text.Trim();
            if (trimmed.Length > 0)
            {
                int offset = text.IndexOf(trimmed, StringComparison.Ordinal);
                result.Add(new SentenceSpan(trimmed, Math.Max(0, offset), 1));
            }
        }

        return result;
    }

    private static void AddCjkTokens(ReadOnlySpan<char> run, List<string> tokens)
    {
        var filtered = new StringBuilder(run.Length);
        foreach (char c in run)
        {
            if (!StopCharacters.Contains(c))
            {
                filtered.Append(c);
            }
            else
            {
                FlushBigrams(filtered, tokens);
            }
        }

        FlushBigrams(filtered, tokens);

        // 对短句保留单字，避免极短文本没有任何 token。
        if (tokens.Count == 0 && run.Length > 0)
        {
            tokens.Add(run.ToString());
        }
    }

    private static void FlushBigrams(StringBuilder buffer, List<string> tokens)
    {
        if (buffer.Length == 0) return;

        if (buffer.Length == 1)
        {
            tokens.Add(buffer.ToString());
        }
        else
        {
            for (int i = 0; i < buffer.Length - 1; i++)
            {
                tokens.Add(buffer.ToString(i, 2));
            }
        }

        buffer.Clear();
    }

    private static bool IsCjk(char c)
    {
        return c is >= '\u3400' and <= '\u4DBF'
            or >= '\u4E00' and <= '\u9FFF'
            or >= '\uF900' and <= '\uFAFF';
    }
}

public sealed record SentenceSpan(string Text, int Offset, int LineNumber);
