using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using LocalCraft.Core.Journal;

namespace LocalCraft.App.Services;

/// <summary>
/// 记住最近使用过的文件夹。选择对话框优先打开最近一次确认的目录。
/// </summary>
public static class RecentFoldersService
{
    private const int Limit = 20;
    private static readonly string FilePath =
        Path.Combine(LocalCraftPaths.DataRoot, "recent-folders.json");
    private static readonly ObservableCollection<string> _folders = new();
    private static bool _loaded;

    public static ObservableCollection<string> Folders
    {
        get
        {
            EnsureLoaded();
            return _folders;
        }
    }

    public static string? LastExisting
    {
        get
        {
            EnsureLoaded();
            return _folders.FirstOrDefault(Directory.Exists);
        }
    }

    public static void Remember(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return;
        string full;
        try { full = Path.GetFullPath(folder); }
        catch { return; }
        if (!Directory.Exists(full)) return;

        EnsureLoaded();
        string? existing = _folders.FirstOrDefault(
            x => string.Equals(x, full, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) _folders.Remove(existing);
        _folders.Insert(0, full);
        while (_folders.Count > Limit) _folders.RemoveAt(_folders.Count - 1);
        Save();
    }

    public static void RememberFile(string? file)
    {
        if (!string.IsNullOrWhiteSpace(file)) Remember(Path.GetDirectoryName(file));
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!File.Exists(FilePath))
            {
                AddDefaultFolders();
                return;
            }
            List<string>? saved = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath));
            if (saved is null) return;
            foreach (string item in saved.Where(Directory.Exists).Take(Limit))
            {
                if (!_folders.Any(x => string.Equals(x, item, StringComparison.OrdinalIgnoreCase)))
                    _folders.Add(item);
            }
            if (_folders.Count == 0) AddDefaultFolders();
        }
        catch
        {
            // 最近目录读取失败不影响主流程。
        }
    }

    private static void AddDefaultFolders()
    {
        foreach (Environment.SpecialFolder folder in new[]
                 {
                     Environment.SpecialFolder.Desktop,
                     Environment.SpecialFolder.MyDocuments,
                     Environment.SpecialFolder.MyPictures,
                     Environment.SpecialFolder.UserProfile
                 })
        {
            string path = Environment.GetFolderPath(folder);
            if (Directory.Exists(path) && !_folders.Contains(path)) _folders.Add(path);
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath) ?? LocalCraftPaths.DataRoot);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_folders.ToList()));
        }
        catch
        {
            // 记录失败不影响文件操作。
        }
    }
}
