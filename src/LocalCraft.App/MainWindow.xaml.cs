using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LocalCraft.App.Infrastructure;
using LocalCraft.App.ViewModels;

namespace LocalCraft.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private NavItem? _navContextItem;
    private DateTime _lastShellDrop = DateTime.MinValue;
    private readonly DispatcherTimer _dropHideTimer;

    private const int WM_DROPFILES = 0x0233;
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void DragAcceptFiles(IntPtr hWnd, bool accept);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(IntPtr hDrop, uint index, StringBuilder? file, uint cch);
    [DllImport("shell32.dll")]
    private static extern void DragFinish(IntPtr hDrop);

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        PageScroll.PreviewMouseWheel += PageScroll_PreviewMouseWheel;

        // 高 DPI / 小屏适配：窗口按可用工作区收口，内部 1320×820 画布等比缩放。
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(1320, Math.Max(MinWidth, workArea.Width * 0.94));
        Height = Math.Min(820, Math.Max(MinHeight, workArea.Height * 0.94));

        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) => viewModel.Watch.Dispose();

        _dropHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _dropHideTimer.Tick += (_, _) =>
        {
            _dropHideTimer.Stop();
            HideDropOverlay();
        };

        // 在窗口层用隧道事件捕获文件拖入，避免页面内部控制吃掉 DragEnter。
        AllowDrop = true;
        PreviewDragEnter += Window_PreviewDragEnter;
        PreviewDragOver += Window_PreviewDragOver;
        PreviewDragLeave += (_, _) => ScheduleHideDropOverlay();
        PreviewDrop += Window_PreviewDrop;
        Drop += Window_Drop;
        Loaded += (_, _) => EnableDropForVisualTree(this);
        PageHost.Loaded += (_, _) => EnableDropForVisualTree(PageHost);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentPage))
        {
            Dispatcher.BeginInvoke(AnimatePageTransition, DispatcherPriority.Render);
            Dispatcher.BeginInvoke(() => EnableDropForVisualTree(PageHost), DispatcherPriority.Loaded);
        }
    }

    private void Window_PreviewDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(NavItem)))
        {
            e.Effects = _viewModel.IsHomeActive ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
            return;
        }
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            ShowDropOverlay(e);
        }
    }

    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(NavItem)))
        {
            e.Effects = _viewModel.IsHomeActive ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
            return;
        }
        bool hasFiles = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        if (hasFiles)
        {
            e.Handled = true;
            ShowDropOverlay(e);
        }
    }

    private void Window_PreviewDrop(object sender, DragEventArgs e)
    {
        _dropHideTimer.Stop();
        HideDropOverlay();
        if (e.Data.GetData(typeof(NavItem)) is not NavItem nav) return;
        if (!_viewModel.IsHomeActive) return;
        _viewModel.Home.AddFavorite(nav);
        e.Handled = true;
    }

    private void EnableDropForVisualTree(DependencyObject root)
    {
        if (root is UIElement element) element.AllowDrop = true;
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
            EnableDropForVisualTree(VisualTreeHelper.GetChild(root, i));
    }

    private void ShowDropOverlay(DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            DropTargetText.Text = $"导入到：{_viewModel.PageTitle}";
            DropFileText.Text = paths.Length == 1
                ? (Directory.Exists(paths[0]) ? "1 个文件夹 · 自动识别当前工具" : "1 个文件 · 松开后自动识别")
                : $"{paths.Length} 个项目 · 松开后自动识别";
        }
        _dropHideTimer.Stop();
        if (DropOverlay.Visibility == Visibility.Visible) return;
        DropOverlay.Visibility = Visibility.Visible;
        DropOverlay.AllowDrop = true;
        DropOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
    }

    private void ScheduleHideDropOverlay()
    {
        _dropHideTimer.Stop();
        _dropHideTimer.Start();
    }

    private void HideDropOverlay()
    {
        _dropHideTimer.Stop();
        if (DropOverlay.Visibility != Visibility.Visible) return;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(100));
        fade.Completed += (_, _) => DropOverlay.Visibility = Visibility.Collapsed;
        DropOverlay.BeginAnimation(OpacityProperty, fade);
    }

    private void PageScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source)
        {
            ScrollViewer? inner = FindParentScrollViewer(source);
            if (inner is not null && !ReferenceEquals(inner, PageScroll))
            {
                bool canScroll = e.Delta < 0
                    ? inner.VerticalOffset < inner.ScrollableHeight
                    : inner.VerticalOffset > 0;
                if (canScroll) return;
            }
        }
        if (PageScroll.ScrollableHeight <= 0) return;
        PageScroll.ScrollToVerticalOffset(PageScroll.VerticalOffset - e.Delta / 3.0);
        e.Handled = true;
    }

    private static ScrollViewer? FindParentScrollViewer(DependencyObject source)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is ScrollViewer viewer) return viewer;
            try { current = VisualTreeHelper.GetParent(current); }
            catch { return null; }
        }
        return null;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        HideDropOverlay();
        if ((DateTime.UtcNow - _lastShellDrop).TotalMilliseconds < 600) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        await ProcessDroppedPathsAsync(paths);
        e.Handled = true;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr handle = new WindowInteropHelper(this).Handle;
        DragAcceptFiles(handle, true);
        HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_DROPFILES) return IntPtr.Zero;
        try
        {
            _lastShellDrop = DateTime.UtcNow;
            uint count = DragQueryFile(wParam, 0xFFFFFFFF, null, 0);
            var paths = new List<string>();
            for (uint i = 0; i < count; i++)
            {
                uint length = DragQueryFile(wParam, i, null, 0);
                var builder = new StringBuilder((int)length + 1);
                DragQueryFile(wParam, i, builder, (uint)builder.Capacity);
                if (builder.Length > 0) paths.Add(builder.ToString());
            }
            DragFinish(wParam);
            if (paths.Count > 0)
            {
                HideDropOverlay();
                _ = ProcessDroppedPathsAsync(paths.ToArray());
            }
            handled = true;
            return IntPtr.Zero;
        }
        catch
        {
            try { DragFinish(wParam); } catch { }
            return IntPtr.Zero;
        }
    }

    private async Task ProcessDroppedPathsAsync(string[] paths)
    {
        object? page = _viewModel.CurrentPage;
        if (page is null) return;
        try
        {
            MethodInfo? asyncAdd = page.GetType().GetMethod("AddDroppedPathsAsync",
                BindingFlags.Public | BindingFlags.Instance);
            if (asyncAdd is not null && asyncAdd.Invoke(page, new object[] { paths }) is Task task)
            {
                await task;
                return;
            }

            MethodInfo? fileAdd = page.GetType().GetMethod("AddDroppedFilesAsync",
                BindingFlags.Public | BindingFlags.Instance)
                ?? page.GetType().GetMethod("AddDroppedFiles", BindingFlags.Public | BindingFlags.Instance);
            if (fileAdd is not null)
            {
                object? result = fileAdd.Invoke(page, new object[] { paths });
                if (result is Task addTask) await addTask;
                return;
            }
        }
        catch
        {
            // 页面自己的拖入处理失败时退回路径填充。
        }

        string first = paths[0];
        bool isFolder = Directory.Exists(first);
        string folder = isFolder ? first : Path.GetDirectoryName(first) ?? string.Empty;
        if (isFolder)
        {
            SetFirst(page, folder, "Folder", "SourceFolder", "Root", "MergeFolder", "InputFolder");
        }
        else
        {
            bool setFile = SetFirst(page, first, "SourceFile", "FilePath", "SelectedFile");
            if (!setFile) SetFirst(page, folder, "Folder", "SourceFolder", "Root", "MergeFolder", "InputFolder");
        }
        TryAutoScan(page);
    }

    private static void TryAutoScan(object page)
    {
        foreach (string name in new[] { "ScanCommand", "PreviewCommand" })
        {
            PropertyInfo? property = page.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property?.GetValue(page) is ICommand command && command.CanExecute(null))
            {
                command.Execute(null);
                return;
            }
        }
    }

    private static bool SetFirst(object target, string value, params string[] propertyNames)
    {
        foreach (string name in propertyNames)
        {
            PropertyInfo? property = target.GetType().GetProperty(name,
                BindingFlags.Public | BindingFlags.Instance);
            if (property is null || !property.CanWrite || property.PropertyType != typeof(string)) continue;
            try
            {
                property.SetValue(target, value);
                return true;
            }
            catch
            {
                // 页面属性只读或当前状态不允许写入时继续尝试下一个。
            }
        }
        return false;
    }

    private void AnimatePageTransition()
    {
        if (!MotionPreferences.Enabled)
        {
            PageHost.BeginAnimation(OpacityProperty, null);
            PageTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            PageScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            PageScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            PageHost.Opacity = 1;
            PageTranslate.Y = 0;
            PageScale.ScaleX = 1;
            PageScale.ScaleY = 1;
            return;
        }

        int milliseconds = MotionPreferences.PageTransitionMilliseconds;
        var duration = new Duration(TimeSpan.FromMilliseconds(milliseconds));
        var easing = new QuarticEase { EasingMode = EasingMode.EaseOut };

        PageScroll.ScrollToTop();
        PageHost.Opacity = 0;
        PageTranslate.Y = MotionPreferences.Level == LocalCraft.Core.Settings.MotionLevel.Rich ? 14 : 8;
        PageScale.ScaleX = PageScale.ScaleY =
            MotionPreferences.Level == LocalCraft.Core.Settings.MotionLevel.Rich ? 0.988 : 0.994;

        var fade = new DoubleAnimation(0, 1, duration) { EasingFunction = easing };
        var slide = new DoubleAnimation(PageTranslate.Y, 0, duration) { EasingFunction = easing };
        var scale = new DoubleAnimation(PageScale.ScaleX, 1, duration) { EasingFunction = easing };
        PageHost.BeginAnimation(OpacityProperty, fade);
        PageTranslate.BeginAnimation(TranslateTransform.YProperty, slide);
        PageScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        PageScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
    }

    private void PinnedButton_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not Button button || _viewModel.IsSidebarCollapsed) return;
        button.Background = (Brush)FindResource("SidebarHoverBrush");
    }

    private void PinnedButton_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Button button) button.ClearValue(Control.BackgroundProperty);
    }

    private void OpenSettingsFromQuick_Click(object sender, RoutedEventArgs e)
    {
        QuickSettingsToggle.IsChecked = false;
    }

    private void NavList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _navContextItem = FindNavItem(e.OriginalSource as DependencyObject);
    }

    private void AddFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (_navContextItem is null) return;
        _viewModel.Home.AddFavorite(_navContextItem);
        _viewModel.OpenHomeCommand.Execute(null);
        _navContextItem = null;
    }

    private NavItem? FindNavItem(DependencyObject? source)
    {
        return FindNavContainer(source)?.DataContext as NavItem;
    }

    private ListBoxItem? FindNavContainer(DependencyObject? source)
    {
        if (source is null) return null;
        return ItemsControl.ContainerFromElement(NavList, source) as ListBoxItem;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInsideButton(e.OriginalSource as DependencyObject)) return;

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed && WindowState == WindowState.Normal)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // 拖动过程中窗口状态发生切换时忽略。
            }
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (MaximizeGlyph is null) return;
        MaximizeGlyph.Text = WindowState == WindowState.Maximized ? "❐" : "□";
        if (MaximizeButton is not null)
        {
            MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "还原" : "最大化";
        }
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private static bool IsInsideButton(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is ButtonBase) return true;

            element = element switch
            {
                Visual => VisualTreeHelper.GetParent(element),
                FrameworkContentElement contentElement => contentElement.Parent,
                _ => null
            };
        }

        return false;
    }
}
