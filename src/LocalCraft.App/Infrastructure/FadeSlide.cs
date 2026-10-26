using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LocalCraft.Core.Settings;

namespace LocalCraft.App.Infrastructure;

/// <summary>
/// 为动态出现的提示条、结果卡加入淡入 + 上移动画。
/// 用法：infra:FadeSlide.IsEnabled="True"。
/// </summary>
public static class FadeSlide
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(FadeSlide),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;

        if (e.NewValue is true)
        {
            element.IsVisibleChanged += OnVisibleChanged;
            element.Loaded += OnLoaded;
        }
        else
        {
            element.IsVisibleChanged -= OnVisibleChanged;
            element.Loaded -= OnLoaded;
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.IsVisible)
        {
            Animate(element);
        }
    }

    private static void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        if (e.NewValue is true) Animate(element);
    }

    private static void Animate(FrameworkElement element)
    {
        if (!MotionPreferences.EntranceAnimationsEnabled) return;

        int durationMs = MotionPreferences.Level == MotionLevel.Rich ? 300 : 200;
        var duration = new Duration(TimeSpan.FromMilliseconds(durationMs));
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        element.Opacity = 0;
        var translate = new TranslateTransform(0, 8);
        element.RenderTransform = translate;

        var fade = new DoubleAnimation(0, 1, duration) { EasingFunction = easing };
        var slide = new DoubleAnimation(8, 0, duration) { EasingFunction = easing };
        element.BeginAnimation(UIElement.OpacityProperty, fade);
        translate.BeginAnimation(TranslateTransform.YProperty, slide);
    }
}
