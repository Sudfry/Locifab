namespace LocalCraft.Conversion.Engines;

internal static class ToolLocator
{
    public static string? Find(string engineRoot, string fileName, params string[] relativeCandidates)
    {
        foreach (string relative in relativeCandidates)
        {
            string candidate = Path.IsPathRooted(relative)
                ? relative
                : Path.Combine(engineRoot, relative);
            if (File.Exists(candidate)) return candidate;
        }

        string? pathValue = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathValue))
        {
            foreach (string directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    string candidate = Path.Combine(directory.Trim(), fileName);
                    if (File.Exists(candidate)) return candidate;
                }
                catch
                {
                    // 忽略非法 PATH 项。
                }
            }
        }

        return null;
    }
}
