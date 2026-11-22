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

    public ToolPresetStore()
    {
        Load();
        EnsureDemoPresets();
    }

    private void EnsureDemoPresets()
    {
        var demos = new[]
        {
            Demo("rename", "演示-交付前缀", new() { ["Prefix"]="交付_", ["Suffix"]="", ["StartNumberText"]="1", ["NumberPaddingText"]="3", ["NumberSeparator"]="_", ["UseNumbering"]="True" },
                Step(PipelineStepKind.Rename, text1:"交付_", text3:"_", n1:1, n2:3, b1:true)),
            Demo("imgcompress", "演示-图片500KB", new() { ["TargetKb"]="500" },
                Step(PipelineStepKind.ImageTarget, text1:"压缩图片", n1:500)),
            Demo("imgcheck", "演示-损坏隔离", new() { ["IntegrityFolder"]="" },
                Step(PipelineStepKind.ImageCheck, text1:"损坏图片隔离")),
            Demo("report", "演示-清单CSV", new() { ["IncludeHash"]="True" },
                Step(PipelineStepKind.Report, text1:"交付清单.csv", choice:"Csv", b1:true)),
            Demo("classify", "演示-按类型分类", new() { ["FolderPath"]="" },
                Step(PipelineStepKind.Classify, choice:"ByType")),
            Demo("archive", "演示-交付ZIP", new() { ["IsCompress"]="True" },
                Step(PipelineStepKind.Archive, text1:"交付包.zip", choice:"Compress", b1:true)),
        };
        bool changed = false;
        foreach (var preset in demos)
        {
            if (All.Any(x => x.ToolKey.Equals(preset.ToolKey, StringComparison.OrdinalIgnoreCase)
                          && x.Name.Equals(preset.Name, StringComparison.OrdinalIgnoreCase))) continue;
            All.Add(preset);
            changed = true;
        }
        if (changed) Save();
    }

    private static ToolPreset Demo(string tool, string name, Dictionary<string,string> values, PipelineStepData step)
        => new() { ToolKey = tool, Name = name, Values = values, Step = step };

    private static PipelineStepData Step(PipelineStepKind kind, string text1="", string text2="", string text3="",
        int n1=0, int n2=0, string choice="", bool b1=false, bool b2=false)
        => new() { Kind = kind.ToString(), Text1 = text1, Text2 = text2, Text3 = text3, Number1 = n1, Number2 = n2, Choice = choice, Bool1 = b1, Bool2 = b2 };

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
