namespace LocalCraft.Core.Settings;

public sealed class AppSettings
{
    public int Version { get; set; } = 1;

    public bool AnimationsEnabled { get; set; } = true;

    public MotionLevel MotionLevel { get; set; } = MotionLevel.Normal;

    public ThemeMode Theme { get; set; } = ThemeMode.Auto;

    public AccentPalette Accent { get; set; } = AccentPalette.Indigo;

    public UiDensity Density { get; set; } = UiDensity.Cozy;

    public int SidebarWidth { get; set; } = 236;

    public bool DefaultIncludeSubfolders { get; set; }

    public bool RememberLastPage { get; set; } = true;

    public string LastPageKey { get; set; } = "home";

    public List<string> NavOrder { get; set; } = new();

    public double DedupThreshold { get; set; } = 88;

    public int SummarySentenceCount { get; set; } = 5;

    public string DefaultClassificationMode { get; set; } = "ByType";

    public string FileCategoryColor { get; set; } = "#3B82F6";

    public string TextCategoryColor { get; set; } = "#8B5CF6";

    public string MediaCategoryColor { get; set; } = "#F59E0B";

    public string WorkflowCategoryColor { get; set; } = "#10B981";

    public string SystemCategoryColor { get; set; } = "#64748B";

    public Dictionary<string, string> NavBadges { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool ShowSidebarBadges { get; set; } = true;

    public bool ShowSidebarColors { get; set; } = true;

    public double SidebarFontSize { get; set; } = 12.5;

    public bool StartOnGuide { get; set; }

    public bool HasSeenWelcome { get; set; }

    public bool SidebarCollapsed { get; set; }

    public string BackgroundMode { get; set; } = "Solid";

    public string BackgroundColor { get; set; } = "#F4F6FB";

    public string BackgroundImagePath { get; set; } = string.Empty;

    public string BackgroundImageStretch { get; set; } = "UniformToFill";

    public double BackgroundImageOpacity { get; set; } = 0.35;

    public double PanelOpacity { get; set; } = 0.96;

    public int NavOrderVersion { get; set; }

    public List<string> HomeFavoriteKeys { get; set; } = new();

    public bool HasCustomHomeFavorites { get; set; }

    public void Normalize()
    {
        DedupThreshold = Math.Clamp(DedupThreshold, 50, 100);
        SummarySentenceCount = SummarySentenceCount switch
        {
            <= 3 => 3,
            <= 5 => 5,
            <= 8 => 8,
            _ => 10
        };
        LastPageKey = string.IsNullOrWhiteSpace(LastPageKey) ? "home" : LastPageKey.Trim();
        BackgroundMode = BackgroundMode is "Image" ? "Image" : "Solid";
        BackgroundColor = string.IsNullOrWhiteSpace(BackgroundColor) ? "#F4F6FB" : BackgroundColor.Trim();
        BackgroundImagePath = (BackgroundImagePath ?? string.Empty).Trim();
        BackgroundImageStretch = string.IsNullOrWhiteSpace(BackgroundImageStretch) ? "UniformToFill" : BackgroundImageStretch.Trim();
        BackgroundImageOpacity = Math.Clamp(BackgroundImageOpacity, 0.05, 1.0);
        PanelOpacity = Math.Clamp(PanelOpacity, 0.6, 1.0);
        NavOrder = (NavOrder ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(32)
            .ToList();
        DefaultClassificationMode = string.IsNullOrWhiteSpace(DefaultClassificationMode)
            ? "ByType"
            : DefaultClassificationMode.Trim();
        if (!Enum.IsDefined(typeof(MotionLevel), MotionLevel)) MotionLevel = MotionLevel.Normal;
        if (!Enum.IsDefined(typeof(ThemeMode), Theme)) Theme = ThemeMode.Auto;
        if (!Enum.IsDefined(typeof(AccentPalette), Accent)) Accent = AccentPalette.Indigo;
        if (!Enum.IsDefined(typeof(UiDensity), Density)) Density = UiDensity.Cozy;
        SidebarWidth = Math.Clamp(SidebarWidth, 200, 300);
        FileCategoryColor = NormalizeColor(FileCategoryColor, "#3B82F6");
        TextCategoryColor = NormalizeColor(TextCategoryColor, "#8B5CF6");
        MediaCategoryColor = NormalizeColor(MediaCategoryColor, "#F59E0B");
        WorkflowCategoryColor = NormalizeColor(WorkflowCategoryColor, "#10B981");
        SystemCategoryColor = NormalizeColor(SystemCategoryColor, "#64748B");
        HomeFavoriteKeys = (HomeFavoriteKeys ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(40)
            .ToList();
        NavBadges = new Dictionary<string, string>(
            (NavBadges ?? new Dictionary<string, string>())
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .Select(x => new KeyValuePair<string, string>(x.Key.Trim(), (x.Value ?? string.Empty).Trim()))
                .Where(x => x.Value.Length <= 18),
            StringComparer.OrdinalIgnoreCase);
        SidebarFontSize = Math.Clamp(SidebarFontSize, 10, 15);
    }

    private static string NormalizeColor(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        string color = value.Trim();
        if (!color.StartsWith('#')) color = "#" + color;
        return System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}$")
            ? color.ToUpperInvariant()
            : fallback;
    }
}
