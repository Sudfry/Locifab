using System.Windows.Media;
using LocalCraft.App.Infrastructure;

namespace LocalCraft.App.ViewModels;

public enum NavCategory
{
    File,
    Text,
    Media,
    Workflow,
    System
}

public sealed class NavItem : ViewModelBase
{
    private Brush _categoryBrush = Brushes.SlateGray;
    private Brush _categorySoftBrush = new SolidColorBrush(Color.FromArgb(36, 100, 116, 139));
    private Brush _categoryHighlightBrush = new SolidColorBrush(Color.FromArgb(62, 100, 116, 139));
    private string _categoryColor = "#64748B";
    private string _badge = "";

    public required string Key { get; init; }

    public required string Title { get; init; }

    public required string Subtitle { get; init; }

    public required string IconData { get; init; }

    public required object Page { get; init; }

    public required string Badge
    {
        get => _badge;
        set
        {
            if (!SetProperty(ref _badge, value ?? string.Empty)) return;
        }
    }

    public string DefaultBadge { get; set; } = string.Empty;

    public bool IsAvailable { get; init; } = true;

    public Action? OnSelected { get; init; }

    public NavCategory Category { get; set; } = NavCategory.System;

    public string CategoryColor => _categoryColor;

    public Brush CategoryBrush => _categoryBrush;

    public Brush CategorySoftBrush => _categorySoftBrush;

    public Brush CategoryHighlightBrush => _categoryHighlightBrush;

    public void SetCategoryColor(string hex)
    {
        try
        {
            string value = string.IsNullOrWhiteSpace(hex) ? "#64748B" : hex.Trim();
            if (!value.StartsWith('#')) value = "#" + value;
            Color color = (Color)ColorConverter.ConvertFromString(value);
            _categoryColor = value.ToUpperInvariant();
            _categoryBrush = Freeze(new SolidColorBrush(color));
            _categorySoftBrush = Freeze(new SolidColorBrush(Color.FromArgb(38, color.R, color.G, color.B)));
            _categoryHighlightBrush = Freeze(new SolidColorBrush(Color.FromArgb(66, color.R, color.G, color.B)));
            OnPropertyChanged(nameof(CategoryColor));
            OnPropertyChanged(nameof(CategoryBrush));
            OnPropertyChanged(nameof(CategorySoftBrush));
            OnPropertyChanged(nameof(CategoryHighlightBrush));
        }
        catch
        {
            // 非法颜色保持上一次值。
        }
    }

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
