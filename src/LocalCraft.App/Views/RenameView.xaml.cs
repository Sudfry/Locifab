using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LocalCraft.App.ViewModels;

namespace LocalCraft.App.Views;

public partial class RenameView : UserControl
{
    public RenameView()
    {
        InitializeComponent();
    }

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Root_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not RenameViewModel viewModel) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;

        string? folder = paths.FirstOrDefault(Directory.Exists);
        if (folder is null)
        {
            string first = paths[0];
            folder = Directory.Exists(first)
                ? first
                : Path.GetDirectoryName(first);
        }

        if (!string.IsNullOrWhiteSpace(folder))
        {
            e.Handled = true;
            await viewModel.LoadFolderAsync(folder);
        }
    }
}
