using System.Diagnostics;

namespace LocalCraft.App.Services;

public static class ShellService
{
    public static void OpenFile(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("文件不存在。", path);
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    public static void OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("文件夹路径为空。", nameof(path));
        Directory.CreateDirectory(path);
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
        catch
        {
            // 某些精简系统没有可用的 explorer 关联，退回 ShellExecute 目录。
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
    }

    public static void RevealFile(string path)
    {
        if (!File.Exists(path))
        {
            OpenFolder(Path.GetDirectoryName(path) ?? string.Empty);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{path}\"",
            UseShellExecute = true
        });
    }
}
