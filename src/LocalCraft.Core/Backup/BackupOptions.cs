namespace LocalCraft.Core.Backup;

public sealed class BackupOptions
{
    public string SourceFolder { get; set; } = string.Empty;

    public string TargetFolder { get; set; } = string.Empty;

    public bool IncludeSubfolders { get; set; } = true;

    public bool SkipHidden { get; set; } = true;

    public bool StrictCompare { get; set; }

    public string IncludeExtensions { get; set; } = string.Empty;

    public string ExcludeExtensions { get; set; } = string.Empty;

    public double MaxFileSizeMb { get; set; } = 8192;
}
