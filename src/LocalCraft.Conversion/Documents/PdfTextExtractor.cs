using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace LocalCraft.Conversion.Documents;

/// <summary>
/// 轻量 PDF 文本层提取器：支持常见的 FlateDecode 内容流与 ToUnicode CMap。
/// 扫描版图片 PDF 没有文本层时会返回空字符串，不做 OCR。
/// </summary>
public static class PdfTextExtractor
{
    private static readonly Regex StreamRegex = new(
        @"<<(?<dict>[\s\S]{0,8000}?)>>\s*stream\r?\n(?<data>[\s\S]*?)\r?\nendstream",
        RegexOptions.Compiled);

    private static readonly Regex BfCharRegex = new(
        @"beginbfchar(?<body>[\s\S]*?)endbfchar",
        RegexOptions.Compiled);

    private static readonly Regex BfRangeRegex = new(
        @"beginbfrange(?<body>[\s\S]*?)endbfrange",
        RegexOptions.Compiled);

    private static readonly Regex PairRegex = new(
        @"<(?<src>[0-9A-Fa-f]+)>\s*<(?<dst>[0-9A-Fa-f]+)>",
        RegexOptions.Compiled);

    public static string ExtractText(string path, int maxCharacters = 4_000_000)
    {
        byte[] bytes = File.ReadAllBytes(path);
        string latin = Encoding.Latin1.GetString(bytes);
        var decodedStreams = new List<string>();

        foreach (Match match in StreamRegex.Matches(latin))
        {
            string dictionary = match.Groups["dict"].Value;
            string raw = match.Groups["data"].Value;
            byte[] data = Encoding.Latin1.GetBytes(raw);

            if (dictionary.Contains("/FlateDecode", StringComparison.Ordinal))
            {
                try
                {
                    data = Inflate(data);
                }
                catch
                {
                    continue;
                }
            }

            decodedStreams.Add(Encoding.Latin1.GetString(data));
        }

        var toUnicode = BuildToUnicodeMap(decodedStreams);
        var builder = new StringBuilder(Math.Min(maxCharacters, 64_000));

        foreach (string stream in decodedStreams)
        {
            if (!stream.Contains("BT", StringComparison.Ordinal)) continue;

            foreach (Match block in Regex.Matches(stream, @"BT(?<body>[\s\S]*?)ET", RegexOptions.Compiled))
            {
                string body = block.Groups["body"].Value;
                foreach (Match token in Regex.Matches(
                             body,
                             @"\((?:\\.|[^\\()])*\)|<[0-9A-Fa-f\s]+>|\bT[dD]\b|\bT\*",
                             RegexOptions.Compiled))
                {
                    string value = token.Value;
                    if (value is "Td" or "TD" or "T*")
                    {
                        builder.Append('\n');
                        continue;
                    }

                    string decoded = value[0] == '<'
                        ? DecodeHexString(value[1..^1], toUnicode)
                        : DecodeLiteralString(value[1..^1], toUnicode);

                    builder.Append(decoded);
                    if (builder.Length >= maxCharacters) break;
                }

                builder.Append('\n');
                if (builder.Length >= maxCharacters) break;
            }

            if (builder.Length >= maxCharacters) break;
        }

        return Clean(builder.ToString());
    }

    private static Dictionary<int, string> BuildToUnicodeMap(IEnumerable<string> streams)
    {
        var map = new Dictionary<int, string>();

        foreach (string stream in streams)
        {
            if (!stream.Contains("beginbfchar", StringComparison.Ordinal)
                && !stream.Contains("beginbfrange", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match block in BfCharRegex.Matches(stream))
            {
                foreach (Match pair in PairRegex.Matches(block.Groups["body"].Value))
                {
                    int code = Convert.ToInt32(pair.Groups["src"].Value, 16);
                    string value = DecodeUtf16Hex(pair.Groups["dst"].Value);
                    if (value.Length > 0) map[code] = value;
                }
            }

            foreach (Match block in BfRangeRegex.Matches(stream))
            {
                foreach (Match range in Regex.Matches(
                             block.Groups["body"].Value,
                             @"<(?<start>[0-9A-Fa-f]+)>\s*<(?<end>[0-9A-Fa-f]+)>\s*(?<dest><[0-9A-Fa-f]+>|\[[^\]]*\])",
                             RegexOptions.Compiled))
                {
                    int start = Convert.ToInt32(range.Groups["start"].Value, 16);
                    int end = Convert.ToInt32(range.Groups["end"].Value, 16);
                    string dest = range.Groups["dest"].Value;

                    if (dest.StartsWith('['))
                    {
                        MatchCollection items = Regex.Matches(dest, "<([0-9A-Fa-f]+)>");
                        for (int code = start; code <= end && code - start < items.Count; code++)
                        {
                            string value = DecodeUtf16Hex(items[code - start].Groups[1].Value);
                            if (value.Length > 0) map[code] = value;
                        }
                    }
                    else
                    {
                        string baseHex = dest[1..^1];
                        if (baseHex.Length < 4) continue;
                        int baseCode = Convert.ToInt32(baseHex, 16);
                        for (int code = start; code <= end && code - start < 512; code++)
                        {
                            int value = baseCode + (code - start);
                            map[code] = char.ConvertFromUtf32(value);
                        }
                    }
                }
            }
        }

        return map;
    }

    private static string DecodeHexString(string hex, Dictionary<int, string> map)
    {
        hex = Regex.Replace(hex, @"\s", string.Empty);
        if (hex.Length == 0) return string.Empty;
        if (hex.Length % 2 != 0) hex += "0";

        byte[] bytes = Convert.FromHexString(hex);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        return DecodeBytes(bytes, map);
    }

    private static string DecodeLiteralString(string escaped, Dictionary<int, string> map)
    {
        var bytes = new List<byte>(escaped.Length);
        for (int i = 0; i < escaped.Length; i++)
        {
            char c = escaped[i];
            if (c != '\\' || i + 1 >= escaped.Length)
            {
                bytes.Add((byte)c);
                continue;
            }

            char next = escaped[++i];
            switch (next)
            {
                case 'n': bytes.Add((byte)'\n'); break;
                case 'r': bytes.Add((byte)'\r'); break;
                case 't': bytes.Add((byte)'\t'); break;
                case 'b': bytes.Add((byte)'\b'); break;
                case 'f': bytes.Add((byte)'\f'); break;
                case '(':
                case ')':
                case '\\': bytes.Add((byte)next); break;
                case '\r':
                    if (i + 1 < escaped.Length && escaped[i + 1] == '\n') i++;
                    break;
                case '\n': break;
                default:
                    if (char.IsDigit(next) && i + 2 < escaped.Length
                        && char.IsDigit(escaped[i + 1]) && char.IsDigit(escaped[i + 2]))
                    {
                        int value = (next - '0') * 64 + (escaped[i + 1] - '0') * 8 + (escaped[i + 2] - '0');
                        bytes.Add((byte)value);
                        i += 2;
                    }
                    else
                    {
                        bytes.Add((byte)next);
                    }
                    break;
            }
        }

        return DecodeBytes(bytes.ToArray(), map);
    }

    private static string DecodeBytes(byte[] bytes, Dictionary<int, string> map)
    {
        if (bytes.Length == 0) return string.Empty;

        if (map.Count > 0)
        {
            int twoByteMapped = CountMapped(bytes, map, 2);
            int oneByteMapped = CountMapped(bytes, map, 1);
            int codeLength = twoByteMapped >= oneByteMapped ? 2 : 1;

            var builder = new StringBuilder();
            for (int i = 0; i + codeLength <= bytes.Length; i += codeLength)
            {
                int code = codeLength == 2
                    ? (bytes[i] << 8) | bytes[i + 1]
                    : bytes[i];

                if (map.TryGetValue(code, out string? value))
                {
                    builder.Append(value);
                }
                else if (codeLength == 2)
                {
                    char[] chars = Encoding.BigEndianUnicode.GetChars(bytes, i, 2);
                    foreach (char ch in chars)
                    {
                        if (!char.IsControl(ch) || ch is '\n' or '\t') builder.Append(ch);
                    }
                }
                else if (bytes[i] >= 32 || bytes[i] is (byte)'\n' or (byte)'\t')
                {
                    builder.Append((char)bytes[i]);
                }
            }

            if (builder.Length > 0) return builder.ToString();
        }

        string latin = Encoding.Latin1.GetString(bytes);
        return new string(latin.Where(static c => !char.IsControl(c) || c is '\n' or '\t').ToArray());
    }

    private static int CountMapped(byte[] bytes, Dictionary<int, string> map, int codeLength)
    {
        int count = 0;
        for (int i = 0; i + codeLength <= bytes.Length; i += codeLength)
        {
            int code = codeLength == 2 ? (bytes[i] << 8) | bytes[i + 1] : bytes[i];
            if (map.ContainsKey(code)) count++;
        }
        return count;
    }

    private static string DecodeUtf16Hex(string hex)
    {
        try
        {
            if (hex.Length % 4 != 0) return string.Empty;
            byte[] bytes = Convert.FromHexString(hex);
            return Encoding.BigEndianUnicode.GetString(bytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static byte[] Inflate(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    private static string Clean(string text)
    {
        text = text.Replace("\u0000", string.Empty);
        text = Regex.Replace(text, @"[ \t]+\n", "\n");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        text = Regex.Replace(text, @"[ \t]{2,}", " ");
        return text.Trim();
    }
}
