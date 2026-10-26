using System.Windows;
using System.Windows.Input;

namespace LocalCraft.App.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { DialogResult = false; Close(); }
            else if (e.Key == Key.Enter) { DialogResult = true; Close(); }
        };
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
