namespace LocalCraft.App.Services;

public interface IDialogService
{
    string? PickFolder(string? initialDirectory = null);

    IReadOnlyList<string> PickFiles(string filter, string? initialDirectory = null);

    string? PickSaveFile(string filter, string suggestedFileName, string? initialDirectory = null);

    bool Confirm(string title, string message);

    void ShowError(string title, string message);

    void ShowInfo(string title, string message);
}
