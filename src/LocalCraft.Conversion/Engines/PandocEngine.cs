using LocalCraft.Conversion.Formats;

namespace LocalCraft.Conversion.Engines;

public sealed class PandocEngine : IConversionEngine
{
    private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".markdown", ".html", ".htm", ".txt", ".docx", ".pptx"
    };

    private static readonly HashSet<string> TargetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".html", ".docx", ".txt"
    };

    private readonly string? _executable;

    public PandocEngine(string engineRoot)
    {
        _executable = ToolLocator.Find(
            engineRoot,
            "pandoc.exe",
            "pandoc\\pandoc.exe",
            "pandoc\\bin\\pandoc.exe",
            @"C:\Program Files\Pandoc\pandoc.exe",
            @"C:\Program Files (x86)\Pandoc\pandoc.exe");
    }

    public string Id => "pandoc";

    public string DisplayName => "Pandoc";

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_executable) && File.Exists(_executable);

    public string? ExecutablePath => _executable;

    public bool CanConvert(string sourcePath, ConversionTargetFormat target)
    {
        if (!IsAvailable) return false;
        string sourceExtension = SourceFormats.NormalizeExtension(sourcePath);
        string targetExtension = SourceFormats.NormalizeExtension(target.Extension);
        if (!SourceExtensions.Contains(sourceExtension)) return false;
        if (!TargetExtensions.Contains(targetExtension)) return false;
        return sourceExtension != targetExtension;
    }

    public async Task ConvertAsync(
        string sourcePath,
        string targetTempPath,
        ConversionTargetFormat target,
        CancellationToken cancellationToken)
    {
        if (!IsAvailable || _executable is null)
        {
            throw new InvalidOperationException("Pandoc 未安装或未随应用一起部署。");
        }

        string from = SourceFormats.NormalizeExtension(sourcePath) switch
        {
            ".html" or ".htm" => "html",
            ".docx" => "docx",
            ".pptx" => "pptx",
            _ => "markdown"
        };

        string to = SourceFormats.NormalizeExtension(target.Extension) switch
        {
            ".html" => "html5",
            ".docx" => "docx",
            ".txt" => "plain",
            _ => "gfm"
        };

        string? outputDirectory = Path.GetDirectoryName(targetTempPath);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        await ProcessRunner.RunAsync(
            _executable,
            new[]
            {
                "--from", from,
                "--to", to,
                "--output", targetTempPath,
                sourcePath
            },
            Path.GetDirectoryName(_executable),
            TimeSpan.FromMinutes(2),
            cancellationToken).ConfigureAwait(false);

        if (!File.Exists(targetTempPath))
        {
            throw new InvalidOperationException("Pandoc 未生成输出文件。");
        }
    }
}
