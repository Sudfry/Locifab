using System.Collections.ObjectModel;

namespace LocalCraft.App.ViewModels;

public sealed class PlaceholderViewModel
{
    public required string ModuleNumber { get; init; }

    public required string Title { get; init; }

    public required string Description { get; init; }

    public required string PreviewLabel { get; init; }

    public required ObservableCollection<PlaceholderFeature> Features { get; init; }

    public required ObservableCollection<string> TechnicalNotes { get; init; }
}

public sealed record PlaceholderFeature(string Title, string Description);
