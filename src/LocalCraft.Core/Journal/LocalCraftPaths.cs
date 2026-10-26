namespace LocalCraft.Core.Journal;

/// <summary>
/// 本地数据目录。可用 LOCALCRAFT_DATA_DIR 覆盖（自测和便携模式使用）。
/// 当默认目录不可写时自动回退到程序目录或临时目录，避免日志写入失败导致功能中断。
/// </summary>
public static class LocalCraftPaths
{
    private static string? _resolved;

    public static string DataRoot
    {
        get
        {
            string? overridden = Environment.GetEnvironmentVariable("LOCIFAB_DATA_DIR")
                ?? Environment.GetEnvironmentVariable("LOCALCRAFT_DATA_DIR");
            if (!string.IsNullOrWhiteSpace(overridden))
                return Path.GetFullPath(overridden);
            return _resolved ??= ResolveWritableRoot();
        }
    }

    public static string JournalsDirectory => Path.Combine(DataRoot, "journals");

    public static string BackupsDirectory => Path.Combine(DataRoot, "backups");

    private static string ResolveWritableRoot()
    {
        string local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Locifab");
        var candidates = new List<string>
        {
            local,
            Path.Combine(AppContext.BaseDirectory, "LocifabData"),
            Path.Combine(Path.GetTempPath(), "Locifab")
        };

        foreach (string candidate in candidates)
        {
            try
            {
                Directory.CreateDirectory(candidate);
                string probe = Path.Combine(candidate, ".write-test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return candidate;
            }
            catch
            {
                // 尝试下一个可写目录。
            }
        }

        return local;
    }
}
