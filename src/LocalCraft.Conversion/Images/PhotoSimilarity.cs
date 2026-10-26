using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LocalCraft.Conversion.Images;

public sealed class PhotoHashEntry
{
    public string Path { get; init; } = string.Empty;

    public string FileName => System.IO.Path.GetFileName(Path);

    public ulong Hash { get; init; }

    public string HashText => Hash.ToString("X16");

    public int Width { get; init; }

    public int Height { get; init; }

    public long Size { get; init; }

    public string Dimensions => $"{Width}×{Height}";
}

public sealed class PhotoSimilarGroup
{
    public string Id { get; init; } = string.Empty;

    public PhotoHashEntry Keeper { get; init; } = new();

    public IReadOnlyList<PhotoHashEntry> Members { get; init; } = Array.Empty<PhotoHashEntry>();

    public int MaxDistance { get; init; }

    public int SimilarityPercent => 100 - (int)Math.Round(MaxDistance * 100.0 / 64);
}

public sealed class PhotoScanResult
{
    public IReadOnlyList<PhotoSimilarGroup> Groups { get; init; } = Array.Empty<PhotoSimilarGroup>();

    public int ScannedCount { get; init; }

    public int SkippedCount { get; init; }

    public string? Error { get; init; }

    public bool Success => string.IsNullOrEmpty(Error);
}

public static class PerceptualHash
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp"
    };

    public static PhotoScanResult Scan(string folder, bool includeSubfolders, double thresholdPercent)
    {
        if (!Directory.Exists(folder))
        {
            return new PhotoScanResult { Error = "请选择存在的文件夹。" };
        }

        var hashes = new List<PhotoHashEntry>();
        int skipped = 0;
        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = includeSubfolders,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false
        };

        try
        {
            foreach (string path in Directory.EnumerateFiles(folder, "*", enumeration)
                         .Where(x => Extensions.Contains(Path.GetExtension(x))))
            {
                try
                {
                    ulong hash = ComputeDHash(path, out int width, out int height);
                    var info = new FileInfo(path);
                    hashes.Add(new PhotoHashEntry
                    {
                        Path = info.FullName,
                        Hash = hash,
                        Width = width,
                        Height = height,
                        Size = info.Length
                    });
                }
                catch
                {
                    skipped++;
                }
            }
        }
        catch (Exception ex)
        {
            return new PhotoScanResult { Error = "扫描图片失败：" + ex.Message };
        }

        int maxDistance = Math.Clamp((int)Math.Round((100 - Math.Clamp(thresholdPercent, 70, 100)) / 100 * 64), 0, 64);
        var groups = new List<(PhotoHashEntry Keeper, List<PhotoHashEntry> Members, int MaxDistance)>();
        foreach (var entry in hashes.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase))
        {
            var match = groups.FirstOrDefault(group => Hamming(group.Keeper.Hash, entry.Hash) <= maxDistance);
            if (match.Members is null)
            {
                groups.Add((entry, new List<PhotoHashEntry> { entry }, 0));
            }
            else
            {
                int distance = Hamming(match.Keeper.Hash, entry.Hash);
                match.Members.Add(entry);
                int index = groups.IndexOf(match);
                groups[index] = (match.Keeper, match.Members, Math.Max(match.MaxDistance, distance));
            }
        }

        var result = new List<PhotoSimilarGroup>();
        int groupNumber = 1;
        foreach (var group in groups.Where(x => x.Members.Count > 1))
        {
            var ordered = group.Members.OrderByDescending(x => x.Size).ToList();
            result.Add(new PhotoSimilarGroup
            {
                Id = $"组 {groupNumber++}",
                Keeper = ordered[0],
                Members = ordered,
                MaxDistance = ordered.Skip(1).Max(x => Hamming(ordered[0].Hash, x.Hash))
            });
        }

        return new PhotoScanResult
        {
            Groups = result,
            ScannedCount = hashes.Count,
            SkippedCount = skipped
        };
    }

    public static ulong ComputeDHash(string path, out int width, out int height)
    {
        using var stream = File.OpenRead(path);
        BitmapDecoder decoder = BitmapDecoder.Create(
            stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0) throw new InvalidOperationException("图片没有可读取的帧。");

        BitmapSource source = decoder.Frames[0];
        width = source.PixelWidth;
        height = source.PixelHeight;
        var scaled = new TransformedBitmap(source, new ScaleTransform(9.0 / width, 8.0 / height));
        var converted = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
        int stride = 9 * 4;
        byte[] pixels = new byte[stride * 8];
        converted.CopyPixels(pixels, stride, 0);

        Span<byte> gray = stackalloc byte[9 * 8];
        for (int i = 0, p = 0; i < gray.Length; i++, p += 4)
        {
            gray[i] = (byte)((pixels[p + 2] * 299 + pixels[p + 1] * 587 + pixels[p] * 114) / 1000);
        }

        ulong hash = 0;
        int bit = 0;
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                if (gray[y * 9 + x] > gray[y * 9 + x + 1]) hash |= 1UL << bit;
                bit++;
            }
        }

        return hash;
    }

    public static int Hamming(ulong left, ulong right) => BitOperations.PopCount(left ^ right);
}
