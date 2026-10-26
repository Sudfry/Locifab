namespace LocalCraft.Core.Journal;

public sealed record JournalLoadResult(
    IReadOnlyList<JournalEntry> Entries,
    IReadOnlyList<string> Warnings);
