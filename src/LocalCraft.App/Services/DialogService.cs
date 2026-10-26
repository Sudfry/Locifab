using System.Windows;
using LocalCraft.App.Views;
using Microsoft.Win32;

namespace LocalCraft.App.Services;

public sealed class DialogService : IDialogService
{
    public string? PickFolder(string? initialDirectory = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择要处理的文件夹",
            Multiselect = false
        };

        string? initial = ResolveInitial(initialDirectory);
        if (initial is not null) dialog.InitialDirectory = initial;

        Window? owner = Application.Current?.MainWindow;
        bool? result = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        if (result != true) return null;
        RecentFoldersService.Remember(dialog.FolderName);
        return dialog.FolderName;
    }

    public IReadOnlyList<string> PickFiles(string filter, string? initialDirectory = null)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择要转换的文件",
            Multiselect = true,
            CheckFileExists = true,
            Filter = filter
        };

        string? initial = ResolveInitial(initialDirectory);
        if (initial is not null) dialog.InitialDirectory = initial;

        Window? owner = Application.Current?.MainWindow;
        bool? result = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        if (result != true) return Array.Empty<string>();
        if (dialog.FileNames.Length > 0) RecentFoldersService.RememberFile(dialog.FileNames[0]);
        return dialog.FileNames;
    }

    public string? PickSaveFile(string filter, string suggestedFileName, string? initialDirectory = null)
    {
        var dialog = new SaveFileDialog
        {
            Title = "选择输出文件",
            Filter = filter,
            FileName = suggestedFileName,
            OverwritePrompt = true,
            AddExtension = true
        };

        string? initial = ResolveInitial(initialDirectory);
        if (initial is not null) dialog.InitialDirectory = initial;

        Window? owner = Application.Current?.MainWindow;
        bool? result = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        if (result != true) return null;
        RecentFoldersService.RememberFile(dialog.FileName);
        return dialog.FileName;
    }

    public bool Confirm(string title, string message)
    {
        Window? owner = Application.Current?.MainWindow;
        var dialog = new ConfirmDialog(title, message);
        if (owner is not null && !ReferenceEquals(owner, dialog)) dialog.Owner = owner;
        return dialog.ShowDialog() == true;
    }

    public void ShowError(string title, string message)
    {
        Window? owner = Application.Current?.MainWindow;
        if (owner is null)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        else
        {
            MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void ShowInfo(string title, string message)
    {
        Window? owner = Application.Current?.MainWindow;
        if (owner is null)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private static string? ResolveInitial(string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested) && Directory.Exists(requested)) return requested;
        return RecentFoldersService.LastExisting;
    }
}
