using System.Text.Json;
using LocalCraft.App.ViewModels;
using LocalCraft.Core.Journal;

namespace LocalCraft.App.Services;

public sealed class ToolPreset
{
    public string ToolKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public PipelineStepData? Step { get; set; }
}

public sealed class ToolPresetStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path = Path.Combine(LocalCraftPaths.DataRoot, "tool-presets.json");
    public List<ToolPreset> All { get; private set; } = new();

    public ToolPresetStore() { Load(); }

    public void Load()
    {
        try
        {
            All = File.Exists(_path)
                ? JsonSerializer.Deserialize<List<ToolPreset>>(File.ReadAllText(_path), Options) ?? new()
                : new();
        }
        catch { All = new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? string.Empty);
            File.WriteAllText(_path, JsonSerializer.Serialize(All, Options));
        }
        catch { }
    }

    public void Upsert(ToolPreset preset)
    {
        All.RemoveAll(x => x.ToolKey.Equals(preset.ToolKey, StringComparison.OrdinalIgnoreCase)
                        && x.Name.Equals(preset.Name, StringComparison.OrdinalIgnoreCase));
        All.Add(preset);
        Save();
    }

    public void Remove(ToolPreset preset)
    {
        All.Remove(preset);
        Save();
    }

    public void Import(string file, out int count)
    {
        count = 0;
        try
        {
            var imported = JsonSerializer.Deserialize<List<ToolPreset>>(File.ReadAllText(file), Options) ?? new();
            foreach (var preset in imported.Where(x => !string.IsNullOrWhiteSpace(x.Name)))
            {
                Upsert(preset);
                count++;
            }
        }
        catch { }
    }

    public void Export(ToolPreset preset, string file)
    {
        try
        {
            File.WriteAllText(file, JsonSerializer.Serialize(new[] { preset }, Options));
        }
        catch { }
    }
}
