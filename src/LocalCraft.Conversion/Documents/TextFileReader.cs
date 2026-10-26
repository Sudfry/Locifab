using System.Text;

namespace LocalCraft.Conversion.Documents;

internal static class TextFileReader
{
    static TextFileReader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static string ReadAllText(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            try
            {
                // 兼容常见的中文 Windows 记事本 GBK 文本。
                return Encoding.GetEncoding(936).GetString(bytes);
            }
            catch
            {
                return Encoding.UTF8.GetString(bytes);
            }
        }
    }
}
