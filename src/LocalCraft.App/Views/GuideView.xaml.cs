using System.Windows;
using System.Windows.Controls;

namespace LocalCraft.App.Views;

public partial class GuideView : UserControl
{
    public GuideView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Window? window = Window.GetWindow(this);
            if (window is not null) DetailPopup.PlacementTarget = window;
            DetailPopup.Opened += (_, _) => DetailScroll.ScrollToTop();
        };
    }
}
