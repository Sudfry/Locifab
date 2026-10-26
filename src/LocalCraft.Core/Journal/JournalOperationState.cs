namespace LocalCraft.Core.Journal;

public enum JournalOperationState
{
    Pending,
    BackedUp,
    Staging,
    Completed,
    Undone
}
