using LocalCraft.Core.Journal;

namespace LocalCraft.Conversion.Operations;

public sealed record ConversionBatchResult(
    bool Success,
    OperationJournal? Journal,
    string Message)
{
    public string? JournalId => Journal?.Id;
}
