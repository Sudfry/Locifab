namespace LocalCraft.Core.Operations;

public sealed record OperationProgress(
    int Completed,
    int Total,
    string CurrentFile,
    string Stage);
