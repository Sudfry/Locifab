namespace LocalCraft.Core.TextReplacement;

public sealed class TextReplaceOptions
{
    public string Folder { get; set; } = string.Empty;

    public string FindText { get; set; } = string.Empty;

    public string ReplaceText { get; set; } = string.Empty;

    public bool UseRegex { get; set; }

    public bool IgnoreCase { get; set; }

    public bool IncludeSubfolders { get; set; }

    public bool AnyExtension { get; set; }

    public string IncludeExtensions { get; set; } = string.Empty;

    public string ExcludeExtensions { get; set; } = string.Empty;

    public double MaxFileSizeMb { get; set; } = 64;
}
