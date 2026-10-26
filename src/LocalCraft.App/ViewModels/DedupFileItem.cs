using LocalCraft.App.Infrastructure;
using LocalCraft.Core.Analysis;

namespace LocalCraft.App.ViewModels;

public sealed class DedupFileItem : ViewModelBase
{
    private bool _isIncludedForRemoval;

    public DedupFileItem(
        DedupDocument document,
        bool isRepresentative,
        double similarityToRepresentative,
        string potentialTargetPath)
    {
        Document = document;
        IsRepresentative = isRepresentative;
        SimilarityToRepresentative = similarityToRepresentative;
        PotentialTargetPath = potentialTargetPath;
        _isIncludedForRemoval = !isRepresentative;
    }

    public event EventHandler? RemovalChanged;

    public DedupDocument Document { get; }

    public bool IsRepresentative { get; }

    public double SimilarityToRepresentative { get; }

    public string PotentialTargetPath { get; }

    public string FileName => Document.FileName;

    public string FilePath => Document.Path;

    public string Directory => Path.GetDirectoryName(Document.Path) ?? string.Empty;

    public string SizeText => ConversionQueueItem.FormatSize(Document.Size);

    public string Preview => Document.Preview;

    public string SimilarityText => IsRepresentative
        ? "保留基准"
        : $"{SimilarityToRepresentative:P1}";

    public string TargetDisplay => IsRepresentative
        ? "保留"
        : IsIncludedForRemoval
            ? PotentialTargetPath
            : "保留";

    public bool CanToggle => !IsRepresentative;

    public bool IsIncludedForRemoval
    {
        get => _isIncludedForRemoval;
        set
        {
            if (!CanToggle) value = false;
            if (!SetProperty(ref _isIncludedForRemoval, value)) return;
            OnPropertyChanged(nameof(TargetDisplay));
            RemovalChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
