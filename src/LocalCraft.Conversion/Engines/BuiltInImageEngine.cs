using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalCraft.Conversion.Formats;

namespace LocalCraft.Conversion.Engines;

/// <summary>
/// 基于 Windows Imaging Component 的本地图像转换，不依赖网络或第三方包。
/// 支持 JPG / PNG / BMP / GIF / TIFF；HEIC 与 RAW 取决于系统是否装有对应 WIC 解码器。
/// </summary>
public sealed class BuiltInImageEngine : IConversionEngine
{
    private static readonly HashSet<string> TargetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff"
    };

    public string Id => "builtin-image";

    public string DisplayName => "Windows WIC 图像引擎";

    public bool IsAvailable => true;

    public bool CanConvert(string sourcePath, ConversionTargetFormat target)
    {
        if (target.Kind != ConversionKind.Image) return false;
        if (!TargetExtensions.Contains(target.Extension)) return false;

        string sourceExtension = SourceFormats.NormalizeExtension(sourcePath);
        if (!SourceFormats.Images.Contains(sourceExtension)) return false;

        return !IsSameFormat(sourceExtension, target.Extension);
    }

    public async Task ConvertAsync(
        string sourcePath,
        string targetTempPath,
        ConversionTargetFormat target,
        CancellationToken cancellationToken)
    {
        await Task.Run(() => ConvertCore(sourcePath, targetTempPath, target, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
    }

    private static void ConvertCore(
        string sourcePath,
        string targetTempPath,
        ConversionTargetFormat target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        BitmapDecoder decoder;
        try
        {
            using var input = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 128 * 1024,
                FileOptions.SequentialScan);
            decoder = BitmapDecoder.Create(
                input,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException)
        {
            string extension = SourceFormats.NormalizeExtension(sourcePath);
            throw new InvalidOperationException(
                $"无法解码 {extension} 文件。HEIC/RAW 需要系统已安装对应图像扩展，或后续接入 LibRaw 引擎。",
                ex);
        }

        if (decoder.Frames.Count == 0)
        {
            throw new InvalidOperationException("图像中没有可读取的帧。");
        }

        cancellationToken.ThrowIfCancellationRequested();
        BitmapSource source = decoder.Frames[0];
        BitmapSource encoded = PrepareForEncoder(source, IsLossyTarget(target.Extension));

        BitmapEncoder encoder = CreateEncoder(target.Extension);
        encoder.Frames.Add(BitmapFrame.Create(encoded));

        string? directory = Path.GetDirectoryName(targetTempPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var output = new FileStream(targetTempPath, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(output);
    }

    private static BitmapEncoder CreateEncoder(string extension)
    {
        return SourceFormats.NormalizeExtension(extension) switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 92 },
            ".png" => new PngBitmapEncoder(),
            ".bmp" => new BmpBitmapEncoder(),
            ".gif" => new GifBitmapEncoder(),
            ".tif" or ".tiff" => new TiffBitmapEncoder { Compression = TiffCompressOption.Zip },
            _ => throw new NotSupportedException($"不支持的图像输出格式：{extension}")
        };
    }

    private static BitmapSource PrepareForEncoder(BitmapSource source, bool flattenTransparency)
    {
        BitmapSource bgra;
        try
        {
            bgra = source.Format == PixelFormats.Bgra32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            bgra.Freeze();
        }
        catch
        {
            bgra = source;
        }

        if (!flattenTransparency) return bgra;

        int width = bgra.PixelWidth;
        int height = bgra.PixelHeight;
        int sourceStride = width * 4;
        byte[] sourcePixels = new byte[sourceStride * height];
        bgra.CopyPixels(sourcePixels, sourceStride, 0);

        int targetStride = width * 3;
        byte[] bgrPixels = new byte[targetStride * height];

        for (int y = 0; y < height; y++)
        {
            int sourceRow = y * sourceStride;
            int targetRow = y * targetStride;
            for (int x = 0; x < width; x++)
            {
                int sourceIndex = sourceRow + x * 4;
                int targetIndex = targetRow + x * 3;

                int alpha = sourcePixels[sourceIndex + 3];
                int blue = sourcePixels[sourceIndex];
                int green = sourcePixels[sourceIndex + 1];
                int red = sourcePixels[sourceIndex + 2];

                bgrPixels[targetIndex] = (byte)((blue * alpha + 255 * (255 - alpha) + 127) / 255);
                bgrPixels[targetIndex + 1] = (byte)((green * alpha + 255 * (255 - alpha) + 127) / 255);
                bgrPixels[targetIndex + 2] = (byte)((red * alpha + 255 * (255 - alpha) + 127) / 255);
            }
        }

        BitmapSource result = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgr24,
            null,
            bgrPixels,
            targetStride);
        result.Freeze();
        return result;
    }

    private static bool IsLossyTarget(string extension)
    {
        string normalized = SourceFormats.NormalizeExtension(extension);
        return normalized is ".jpg" or ".jpeg" or ".gif";
    }

    private static bool IsSameFormat(string sourceExtension, string targetExtension)
    {
        return NormalizeImageFamily(sourceExtension) == NormalizeImageFamily(targetExtension);
    }

    private static string NormalizeImageFamily(string extension)
    {
        return SourceFormats.NormalizeExtension(extension) switch
        {
            ".jpeg" => ".jpg",
            ".tiff" => ".tif",
            _ => SourceFormats.NormalizeExtension(extension)
        };
    }
}
