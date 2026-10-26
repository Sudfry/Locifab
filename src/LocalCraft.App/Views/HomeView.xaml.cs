using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LocalCraft.App.Infrastructure;
using LocalCraft.App.ViewModels;

namespace LocalCraft.App.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
    }

    private void HomeView_Loaded(object sender, RoutedEventArgs e)
    {
        if (!MotionPreferences.Enabled) return;
        var cards = new FrameworkElement[] { HeroCard, WelcomeCard, ActionList, SafetyCard };
        for (int i = 0; i < cards.Length; i++)
        {
            FrameworkElement card = cards[i];
            if (card.Visibility != Visibility.Visible) continue;
            var translate = new TranslateTransform(0, 10);
            card.RenderTransform = translate;
            card.Opacity = 0;
            var delay = TimeSpan.FromMilliseconds(i * 45);
            var duration = new Duration(TimeSpan.FromMilliseconds(260));
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            card.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, duration) { BeginTime = delay, EasingFunction = easing });
            translate.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(10, 0, duration) { BeginTime = delay, EasingFunction = easing });
        }
    }

    private void HomeView_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(NavItem)) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void HomeView_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not HomeViewModel home) return;
        if (e.Data.GetDataPresent(typeof(NavItem)) && e.Data.GetData(typeof(NavItem)) is NavItem nav)
        {
            home.AddFavorite(nav);
            e.Handled = true;
        }
    }
}
