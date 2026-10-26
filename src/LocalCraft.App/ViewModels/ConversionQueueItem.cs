using LocalCraft.Conversion.Formats;

namespace LocalCraft.App.ViewModels;

public sealed class ConversionQueueItem
{
    public ConversionQueueItem(string filePath)
    {
        FilePath = filePath;
        var info = new FileInfo(filePath);
        FileName = info.Name;
        Directory = info.DirectoryName ?? string.Empty;
        Extension = info.Extension;
        SizeBytes = info.Exists ? info.Length : 0;
        Category = SourceFormats.GetCategoryLabel(filePath);
    }

    public string FilePath { get; }

    public string FileName { get; }

    public string Directory { get; }

    public string Extension { get; }

    public string Category { get; }

    public long SizeBytes { get; }

    public string SizeText => FormatSize(SizeBytes);

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):0.#} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):0.##} GB";
    }
}
