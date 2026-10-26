using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using LocalCraft.Core.Journal;

namespace LocalCraft.Core.Settings;

/// <summary>
/// 本机设置存储。设置只写入本机 settings.json，不包含任何同步或网络逻辑。
/// </summary>
public sealed class AppSettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly object _sync = new();

    public AppSettingsService(string? dataRoot = null)
    {
        string root = Path.GetFullPath(dataRoot ?? LocalCraftPaths.DataRoot);
        FilePath = Path.Combine(root, "settings.json");
        Current = Load();
    }

    public AppSettings Current { get; private set; }

    public string FilePath { get; }

    public string DataRoot => Path.GetDirectoryName(FilePath) ?? string.Empty;

    public event EventHandler? Changed;

    public void Save()
    {
        lock (_sync)
        {
            Current.Normalize();
            string? directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            string json = JsonSerializer.Serialize(Current, SerializerOptions);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temp, FilePath, overwrite: true);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Update(Action<AppSettings> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        lock (_sync)
        {
            update(Current);
            Current.Normalize();
        }

        Save();
    }

    public void ResetToDefaults()
    {
        Current = new AppSettings();
        Save();
    }

    private AppSettings Load()
    {
        if (!File.Exists(FilePath)) return new AppSettings();

        try
        {
            string json = File.ReadAllText(FilePath);
            AppSettings settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions)
                                   ?? new AppSettings();
            settings.Normalize();
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }
}
