using LocalCraft.Core.Journal;

namespace LocalCraft.Core.Operations;

public sealed record UndoResult(
    bool Success,
    OperationJournal? Journal,
    string Message);
