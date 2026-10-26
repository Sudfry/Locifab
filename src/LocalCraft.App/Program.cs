using System.Runtime.InteropServices;

namespace LocalCraft.App;

public static class Program
{
    private static readonly IntPtr PerMonitorAwareV2 = new(-4);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

    [STAThread]
    public static void Main()
    {
        try
        {
            // 即使通过 dotnet 宿主启动，也尽量启用 Per-Monitor V2，避免高 DPI 下窗口被二次放大。
            SetProcessDpiAwarenessContext(PerMonitorAwareV2);
        }
        catch
        {
            // 老系统没有该 API 时由 WPF 默认 DPI 行为处理。
        }

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
