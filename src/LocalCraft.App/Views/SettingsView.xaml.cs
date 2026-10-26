using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using LocalCraft.App.Infrastructure;

namespace LocalCraft.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void PreviewMotion_Click(object sender, RoutedEventArgs e)
    {
        PlayPreview();
    }

    private void PlayPreview()
    {
        if (!MotionPreferences.Enabled) return;

        int duration = Math.Max(140, MotionPreferences.PageTransitionMilliseconds + 160);
        var animation = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(duration)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        StartAnimation(animation);
    }

    private void StartAnimation(DoubleAnimation animation)
    {
        PreviewMotionBar.BeginAnimation(OpacityProperty, animation);

        var translate = new System.Windows.Media.TranslateTransform(0, 0);
        PreviewMotionBar.RenderTransform = translate;
        var move = new DoubleAnimation(24, 0, new Duration(TimeSpan.FromMilliseconds(260)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        translate.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, move);
    }
}
