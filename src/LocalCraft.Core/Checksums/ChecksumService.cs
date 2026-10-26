using System.Security.Cryptography;
using System.Text;

namespace LocalCraft.Core.Checksums;

public sealed class ChecksumService
{
    public ChecksumScanResult BuildManifest(string folder, bool includeSubfolders)
    {
        if (!Directory.Exists(folder))
        {
            return new ChecksumScanResult { Error = "请选择存在的文件夹。" };
        }

        string root = Path.GetFullPath(folder);
        var entries = new List<ChecksumEntry>();
        int skipped = 0;
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = includeSubfolders,
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false
            };
            foreach (string file in Directory.EnumerateFiles(root, "*", options))
            {
                var info = new FileInfo(file);
                if (info.Length > 8L * 1024 * 1024 * 1024)
                {
                    skipped++;
                    continue;
                }

                try
                {
                    entries.Add(new ChecksumEntry
                    {
                        RelativePath = Path.GetRelativePath(root, file).Replace('\\', '/'),
                        Hash = ComputeFileSha256(file),
                        Size = info.Length
                    });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    skipped++;
                }
            }
        }
        catch (Exception ex)
        {
            return new ChecksumScanResult { Error = "扫描文件失败：" + ex.Message };
        }

        return new ChecksumScanResult
        {
            Entries = entries.OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase).ToList(),
            SkippedCount = skipped
        };
    }

    public string WriteManifest(string path, IReadOnlyList<ChecksumEntry> entries)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var text = new StringBuilder();
        foreach (var entry in entries)
        {
            text.Append(entry.Hash).Append(" *").Append(entry.RelativePath).Append('\n');
        }

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
        return Path.GetFullPath(path);
    }

    public IReadOnlyList<ChecksumEntry> ReadManifest(string path)
    {
        var entries = new List<ChecksumEntry>();
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            int separator = line.IndexOf(" *", StringComparison.Ordinal);
            if (separator < 0) separator = line.IndexOf("  ", StringComparison.Ordinal);
            if (separator <= 0) continue;

            string hash = line[..separator].Trim();
            string relative = line[(separator + 1)..].TrimStart('*', ' ');
            if (hash.Length != 64 || relative.Length == 0) continue;
            entries.Add(new ChecksumEntry { RelativePath = relative.Replace('\\', '/'), Hash = hash });
        }

        return entries;
    }

    public ChecksumVerifyResult Verify(string root, string manifestPath)
    {
        if (!Directory.Exists(root))
        {
            return new ChecksumVerifyResult { Error = "校验根目录不存在。" };
        }

        IReadOnlyList<ChecksumEntry> entries;
        try
        {
            entries = ReadManifest(manifestPath);
        }
        catch (Exception ex)
        {
            return new ChecksumVerifyResult { Error = "读取清单失败：" + ex.Message };
        }

        if (entries.Count == 0)
        {
            return new ChecksumVerifyResult { Error = "清单为空或格式无法识别。" };
        }

        string rootFull = Path.GetFullPath(root);
        var items = new List<ChecksumVerifyItem>();
        foreach (var entry in entries)
        {
            string fullPath = Path.GetFullPath(Path.Combine(rootFull, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsSubPath(fullPath, rootFull) || !File.Exists(fullPath))
            {
                items.Add(new ChecksumVerifyItem
                {
                    RelativePath = entry.RelativePath,
                    ExpectedHash = entry.Hash,
                    State = ChecksumVerifyState.Missing
                });
                continue;
            }

            try
            {
                string actual = ComputeFileSha256(fullPath);
                items.Add(new ChecksumVerifyItem
                {
                    RelativePath = entry.RelativePath,
                    ExpectedHash = entry.Hash,
                    ActualHash = actual,
                    State = string.Equals(actual, entry.Hash, StringComparison.OrdinalIgnoreCase)
                        ? ChecksumVerifyState.Ok
                        : ChecksumVerifyState.Changed
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                items.Add(new ChecksumVerifyItem
                {
                    RelativePath = entry.RelativePath,
                    ExpectedHash = entry.Hash,
                    State = ChecksumVerifyState.Missing
                });
            }
        }

        return new ChecksumVerifyResult { Items = items };
    }

    public static string ComputeFileSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    private static bool IsSubPath(string child, string parent)
    {
        string c = Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string p = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return c.StartsWith(p, StringComparison.OrdinalIgnoreCase);
    }
}
