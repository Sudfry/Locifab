namespace LocalCraft.Core.Journal;

public sealed record JournalEntry(string FilePath, OperationJournal Journal)
{
    public string FileName => Path.GetFileName(FilePath);
}
