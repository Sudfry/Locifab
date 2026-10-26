using System.Windows;
using System.Windows.Controls;
using LocalCraft.App.Services;

namespace LocalCraft.App.Controls;

public partial class PathTextBox : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(PathTextBox),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextChanged));

    private bool _syncing;

    public PathTextBox()
    {
        InitializeComponent();
        SuggestionList.ItemsSource = RecentFoldersService.Folders;
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (PathTextBox)d;
        if (control._syncing) return;
        string value = e.NewValue as string ?? string.Empty;
        if (control.PathInput.Text == value) return;
        control._syncing = true;
        control.PathInput.Text = value;
        control._syncing = false;
    }

    private void PathInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        _syncing = true;
        SetCurrentValue(TextProperty, PathInput.Text);
        _syncing = false;
    }

    private void PathInput_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void PathInput_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        string? path = paths[0];
        if (File.Exists(path)) path = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(path))
        {
            Text = path;
            RecentFoldersService.Remember(path);
            if (DataContext is not null)
            {
                var command = DataContext.GetType().GetProperty("ScanCommand")?.GetValue(DataContext)
                    as System.Windows.Input.ICommand;
                if (command?.CanExecute(null) == true) command.Execute(null);
            }
        }
        e.Handled = true;
    }

    private void DropButton_Click(object sender, RoutedEventArgs e)
    {
        SuggestionList.ItemsSource = RecentFoldersService.Folders;
        Suggestions.IsOpen = !Suggestions.IsOpen;
    }

    private void SuggestionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SuggestionList.SelectedItem is not string path) return;
        Text = path;
        Suggestions.IsOpen = false;
        SuggestionList.SelectedItem = null;
    }
}
