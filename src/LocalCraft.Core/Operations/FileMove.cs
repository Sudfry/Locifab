namespace LocalCraft.Core.Operations;

internal static class FileMove
{
    public static bool IsCaseOnly(string sourcePath, string targetPath)
    {
        return !string.Equals(sourcePath, targetPath, StringComparison.Ordinal)
               && string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase);
    }

    public static string CreateUniqueTempPath(string directory)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            string candidate = Path.Combine(directory, $".localcraft-{Guid.NewGuid():N}.tmp");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("无法创建临时文件名。");
    }

    /// <summary>
    /// 移动文件。仅大小写变化的改名会经过临时文件名，避免 Windows 大小写不敏感带来的问题。
    /// </summary>
    public static void Move(string sourcePath, string targetPath)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("源文件不存在。", sourcePath);
        }

        if (string.Equals(sourcePath, targetPath, StringComparison.Ordinal))
        {
            return;
        }

        if (IsCaseOnly(sourcePath, targetPath))
        {
            string directory = Path.GetDirectoryName(sourcePath)
                               ?? throw new IOException("无法确定源文件目录。");
            string temp = CreateUniqueTempPath(directory);
            File.Move(sourcePath, temp, overwrite: false);
            try
            {
                File.Move(temp, targetPath, overwrite: false);
            }
            catch
            {
                try
                {
                    if (File.Exists(temp))
                    {
                        File.Move(temp, sourcePath, overwrite: false);
                    }
                }
                catch
                {
                    // 原异常优先返回；临时文件仍保留在磁盘上，便于人工恢复。
                }
                throw;
            }
            return;
        }

        File.Move(sourcePath, targetPath, overwrite: false);
    }
}
