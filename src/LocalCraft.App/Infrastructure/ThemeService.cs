using System.Windows;
using System.Windows.Media;
using LocalCraft.Core.Settings;
using Microsoft.Win32;

namespace LocalCraft.App.Infrastructure;

/// <summary>
/// 参考版的冷色外观系统：浅/深色、主题色、密度、侧栏宽度。
/// 颜色写入应用级资源，XAML 通过 DynamicResource 实时响应。
/// </summary>
public static class ThemeService
{
    private static readonly Dictionary<string, (string Light, string Dark)> Neutrals = new()
    {
        ["BgBrush"] = ("#F2F3F6", "#0F1115"),
        ["CardBrush"] = ("#FFFFFF", "#171A20"),
        ["SurfaceMutedBrush"] = ("#F8F9FB", "#1C2028"),
        ["InkBrush"] = ("#14171C", "#E9ECF1"),
        ["InkSoftBrush"] = ("#4D5563", "#A5ADBB"),
        ["MutedBrush"] = ("#828A99", "#7B8494"),
        ["BorderBrush"] = ("#E4E7EC", "#262B34"),
        ["BorderStrongBrush"] = ("#D0D5DD", "#363D47"),
        ["NeutralBadgeBrush"] = ("#EEF0F4", "#232832"),
        ["TopBarBrush"] = ("#FFFFFF", "#171A20"),
        ["TableRowHoverBrush"] = ("#F7F8FA", "#1C2028"),
        ["TableRowSelectedBrush"] = ("#EEF0FF", "#1D2340"),
        ["TableAlternateBrush"] = ("#FCFCFD", "#1A1D23"),
        ["SubtlePanelBrush"] = ("#F3F5F8", "#1D2129"),
        ["ProgressTrackBrush"] = ("#E8EBF0", "#2A303A"),
        ["SidebarIconBackgroundBrush"] = ("#F0F2F6", "#22272F"),
        ["SidebarIconForegroundBrush"] = ("#697386", "#98A3B5"),
        ["SidebarBadgeBackgroundBrush"] = ("#EEF0F4", "#232832"),
        ["SidebarFooterBrush"] = ("#F8F9FB", "#1C2028"),
        ["SidebarFooterLineBrush"] = ("#E4E7EC", "#262B34")
    };

    private static readonly Dictionary<string, (string Light, string Dark)> States = new()
    {
        ["SuccessBrush"] = ("#0F8A4D", "#46D18A"),
        ["SuccessSoftBrush"] = ("#E8F7EE", "#12261B"),
        ["WarningBrush"] = ("#B45309", "#E3A33D"),
        ["WarningSoftBrush"] = ("#FDF4E7", "#2A2113"),
        ["DangerBrush"] = ("#C62828", "#F0736A"),
        ["DangerSoftBrush"] = ("#FDECEB", "#2B1817")
    };

    private static readonly Dictionary<AccentPalette, (string Light, string Dark)> Accents = new()
    {
        [AccentPalette.Indigo] = ("#4F46E5", "#7C8CFF"),
        [AccentPalette.Blue] = ("#2563EB", "#5B9DFF"),
        [AccentPalette.Teal] = ("#0D9488", "#2DD4BF"),
        [AccentPalette.Green] = ("#16A34A", "#4ADE80"),
        [AccentPalette.Amber] = ("#D97706", "#FBBF24"),
        [AccentPalette.Rose] = ("#E11D48", "#FB7185"),
        [AccentPalette.Slate] = ("#475569", "#94A3B8")
    };

    public static bool IsDark { get; private set; }

    public static void Apply(AppSettings settings)
    {
        if (Application.Current is null) return;

        IsDark = settings.Theme == ThemeMode.Dark
                 || (settings.Theme == ThemeMode.Auto && IsSystemDark());

        var resources = Application.Current.Resources;
        foreach (var pair in Neutrals)
        {
            resources[pair.Key] = Brush(IsDark ? pair.Value.Dark : pair.Value.Light);
        }

        foreach (var pair in States)
        {
            resources[pair.Key] = Brush(IsDark ? pair.Value.Dark : pair.Value.Light);
        }

        (string accent, string accentHi, string accentSoft, string accentInk) = GetAccent(settings.Accent);
        resources["AccentBrush"] = Brush(accent);
        resources["AccentHoverBrush"] = Brush(accentHi);
        resources["AccentSoftBrush"] = Brush(accentSoft);
        resources["AccentInkBrush"] = Brush(accentInk);
        resources["WarmBrush"] = Brush(IsDark && settings.Accent == AccentPalette.Amber ? "#FBBF24" : accent);
        resources["WarmSoftBrush"] = Brush(accentSoft);

        resources["SidebarBrush"] = Brush(IsDark ? "#171A20" : "#FFFFFF");
        resources["SidebarHoverBrush"] = Brush(IsDark ? "#1C2028" : "#F3F5F8");
        resources["SidebarSelectedBrush"] = Brush(accentSoft);
        resources["SidebarTextBrush"] = Brush(IsDark ? "#E9ECF1" : "#14171C");
        resources["SidebarMutedBrush"] = Brush(IsDark ? "#7B8494" : "#828A99");
        resources["SidebarLineBrush"] = Brush(IsDark ? "#262B34" : "#E4E7EC");
        resources["SidebarTextActiveBrush"] = Brush(accentInk);
        resources["SidebarIconActiveBrush"] = Brush(accent);

        bool compact = settings.Density == UiDensity.Compact;
        resources["CardPadding"] = new Thickness(compact ? 14 : 18, compact ? 12 : 18, compact ? 14 : 18, compact ? 12 : 18);
        resources["GridRowHeight"] = compact ? 40.0 : 47.0;
        resources["GridHeaderHeight"] = compact ? 34.0 : 38.0;
        resources["InputMinHeight"] = compact ? 34.0 : 38.0;
        resources["NavRowHeight"] = compact ? 42.0 : 46.0;
        resources["PageGap"] = new Thickness(0, 0, 0, compact ? 8 : 10);
    }

    public static (string Accent, string AccentHi, string AccentSoft, string AccentInk) GetAccent(AccentPalette palette)
    {
        (string light, string dark) = Accents.TryGetValue(palette, out var pair) ? pair : Accents[AccentPalette.Indigo];
        string accent = IsDark ? dark : light;
        string soft = palette switch
        {
            AccentPalette.Indigo => IsDark ? "#1D2340" : "#EEF0FF",
            AccentPalette.Blue => IsDark ? "#16233A" : "#EFF4FF",
            AccentPalette.Teal => IsDark ? "#12332F" : "#E6F7F5",
            AccentPalette.Green => IsDark ? "#132A1B" : "#EAF8EF",
            AccentPalette.Amber => IsDark ? "#2E2415" : "#FEF6E7",
            AccentPalette.Rose => IsDark ? "#2F1620" : "#FEF0F2",
            AccentPalette.Slate => IsDark ? "#1C2028" : "#F1F4F8",
            _ => "#EEF0FF"
        };
        string ink = palette switch
        {
            AccentPalette.Indigo => IsDark ? "#B3BEFF" : "#3730A3",
            AccentPalette.Blue => IsDark ? "#A8C8FF" : "#1E40AF",
            AccentPalette.Teal => IsDark ? "#99F6E4" : "#115E59",
            AccentPalette.Green => IsDark ? "#BBF7D0" : "#166534",
            AccentPalette.Amber => IsDark ? "#FDE68A" : "#92400E",
            AccentPalette.Rose => IsDark ? "#FECDD3" : "#9F1239",
            AccentPalette.Slate => IsDark ? "#CBD5E1" : "#334155",
            _ => "#3730A3"
        };
        return (accent, accent, soft, ink);
    }

    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private static bool IsSystemDark()
    {
        try
        {
            object? value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                1);
            return Convert.ToInt32(value) == 0;
        }
        catch
        {
            return false;
        }
    }
}
