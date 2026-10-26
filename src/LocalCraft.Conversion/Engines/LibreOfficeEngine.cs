using LocalCraft.Conversion.Formats;

namespace LocalCraft.Conversion.Engines;

public sealed class LibreOfficeEngine : IConversionEngine
{
    private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".ppt", ".pptx", ".rtf", ".odt", ".odp", ".pdf", ".html", ".htm", ".txt", ".csv"
    };

    private static readonly HashSet<string> TargetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx", ".pptx", ".pdf", ".html", ".txt", ".odt"
    };

    private readonly string? _executable;

    public LibreOfficeEngine(string engineRoot)
    {
        _executable = ToolLocator.Find(
            engineRoot,
            "soffice.exe",
            "libreoffice\\program\\soffice.exe",
            "LibreOfficePortable\\App\\libreoffice\\program\\soffice.exe",
            @"C:\Program Files\LibreOffice\program\soffice.exe",
            @"C:\Program Files (x86)\LibreOffice\program\soffice.exe");
    }

    public string Id => "libreoffice";

    public string DisplayName => "LibreOffice";

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
            throw new InvalidOperationException("LibreOffice 未安装或未随应用一起部署。");
        }

        string tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "LocalCraftLO_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            string filter = SourceFormats.NormalizeExtension(target.Extension) switch
            {
                ".docx" => "docx:Office Open XML Text",
                ".pptx" => "pptx:Impress MS PowerPoint 2007 XML",
                ".pdf" => "pdf",
                ".html" => "html",
                ".odt" => "odt",
                _ => "txt:Text"
            };

            await ProcessRunner.RunAsync(
                _executable,
                new[]
                {
                    "--headless",
                    "--nologo",
                    "--nofirststartwizard",
                    "--norestore",
                    "--convert-to", filter,
                    "--outdir", tempDirectory,
                    sourcePath
                },
                Path.GetDirectoryName(_executable),
                TimeSpan.FromMinutes(3),
                cancellationToken).ConfigureAwait(false);

            string expectedName = Path.GetFileNameWithoutExtension(sourcePath) + target.Extension;
            string? produced = Directory.EnumerateFiles(tempDirectory, "*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(file =>
                    string.Equals(Path.GetFileName(file), expectedName, StringComparison.OrdinalIgnoreCase));

            produced ??= Directory.EnumerateFiles(tempDirectory, "*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(file =>
                    string.Equals(Path.GetExtension(file), target.Extension, StringComparison.OrdinalIgnoreCase));

            if (produced is null || !File.Exists(produced))
            {
                throw new InvalidOperationException("LibreOffice 未生成预期的输出文件。");
            }

            string? targetDirectory = Path.GetDirectoryName(targetTempPath);
            if (!string.IsNullOrEmpty(targetDirectory)) Directory.CreateDirectory(targetDirectory);
            File.Move(produced, targetTempPath, overwrite: false);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, recursive: true);
            }
            catch
            {
                // 临时目录清理失败不影响结果。
            }
        }
    }
}
