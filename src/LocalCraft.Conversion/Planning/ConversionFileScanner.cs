using LocalCraft.Conversion.Formats;
using LocalCraft.Core.Infrastructure;

namespace LocalCraft.Conversion.Planning;

public sealed record ConversionScanResult(
    IReadOnlyList<string> Files,
    int SkippedUnsupported,
    bool Truncated);

public static class ConversionFileScanner
{
    public const int DefaultMaxFiles = 5_000;

    public static ConversionScanResult Scan(
        string folder,
        bool includeSubdirectories,
        int maxFiles = DefaultMaxFiles)
    {
        if (!Directory.Exists(folder))
        {
            return new ConversionScanResult(Array.Empty<string>(), 0, false);
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = includeSubdirectories,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
        };

        var files = new List<string>();
        int skipped = 0;
        bool truncated = false;

        foreach (string path in Directory.EnumerateFiles(folder, "*", options))
        {
            if (!SourceFormats.IsSupported(path))
            {
                skipped++;
                continue;
            }

            if (files.Count >= maxFiles)
            {
                truncated = true;
                break;
            }

            files.Add(Path.GetFullPath(path));
        }

        files.Sort(static (x, y) =>
            NaturalStringComparer.Instance.Compare(Path.GetFileName(x), Path.GetFileName(y)));

        return new ConversionScanResult(files, skipped, truncated);
    }
}
