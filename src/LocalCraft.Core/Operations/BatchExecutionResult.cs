using LocalCraft.Core.Journal;

namespace LocalCraft.Core.Operations;

public sealed record BatchExecutionResult(
    bool Success,
    OperationJournal? Journal,
    string Message)
{
    public string? JournalId => Journal?.Id;
}
