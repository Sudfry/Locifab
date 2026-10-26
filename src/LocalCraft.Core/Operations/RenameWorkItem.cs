namespace LocalCraft.Core.Operations;

public sealed record RenameWorkItem(
    string SourcePath,
    string TargetPath,
    bool AllowOverwrite);
