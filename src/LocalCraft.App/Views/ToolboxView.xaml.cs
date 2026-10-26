using System.Windows;
using System.Windows.Controls;
using LocalCraft.App.ViewModels;

namespace LocalCraft.App.Views;

public partial class ToolboxView : UserControl
{
    public ToolboxView()
    {
        InitializeComponent();
    }

    private void ToolboxRoot_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void ToolboxRoot_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not ToolboxViewModel vm) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        string path = paths[0];
        switch (vm.ActiveToolTab)
        {
            case 1: vm.ImageFolder = path; break;
            case 2: vm.IntegrityFolder = path; break;
            case 3: vm.OfficeFolder = path; break;
            case 4: vm.ShortcutFolder = path; break;
            case 5: vm.TagFolder = path; break;
        }
        e.Handled = true;
    }
}
