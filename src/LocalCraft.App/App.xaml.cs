using System.IO;
using System.Windows;
using System.Windows.Threading;
using LocalCraft.App.Services;
using LocalCraft.App.ViewModels;
using LocalCraft.Core.Journal;
using LocalCraft.Core.Settings;

namespace LocalCraft.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        try
        {
            var repository = new JournalRepository();
            var dialogService = new DialogService();
            var settingsService = new AppSettingsService();
            LocalCraft.App.Infrastructure.MotionPreferences.Apply(settingsService.Current);
            LocalCraft.App.Infrastructure.ThemeService.Apply(settingsService.Current);
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, _) =>
            {
                if (settingsService.Current.Theme == ThemeMode.Auto)
                {
                    LocalCraft.App.Infrastructure.ThemeService.Apply(settingsService.Current);
                    LocalCraft.App.Infrastructure.MotionPreferences.Apply(settingsService.Current);
                }
            };
            var mainViewModel = new MainViewModel(repository, dialogService, settingsService);
            var window = new MainWindow(mainViewModel);
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            WriteDiagnosticLog("启动失败", ex);
            MessageBox.Show(
                "Locifab 启动失败：" + Environment.NewLine + ex.Message,
                "Locifab",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteDiagnosticLog("未处理异常", e.Exception);
        MessageBox.Show(
            "发生未预期的错误：" + Environment.NewLine + e.Exception.Message,
            "Locifab",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void WriteDiagnosticLog(string context, Exception exception)
    {
        try
        {
            string logDirectory = LocalCraftPaths.DataRoot;
            Directory.CreateDirectory(logDirectory);
            File.AppendAllText(
                Path.Combine(logDirectory, "locifab-error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // 错误日志写入失败时仍然向用户显示提示。
        }
    }
}
