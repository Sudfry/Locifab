using System.Collections.ObjectModel;

namespace LocalCraft.App.ViewModels;

public sealed class DedupGroupItem
{
    public DedupGroupItem(
        int index,
        ObservableCollection<DedupFileItem> files,
        double similarity)
    {
        Index = index;
        Files = files;
        Similarity = similarity;
    }

    public int Index { get; }

    public ObservableCollection<DedupFileItem> Files { get; }

    public double Similarity { get; }

    public string Title => $"相似组 {Index:00}";

    public string Summary => $"{Files.Count} 个文件 · 最低相似度 {Similarity:P0}";

    public string RepresentativeName => Files.FirstOrDefault(static x => x.IsRepresentative)?.FileName ?? "—";

    public int MarkedCount => Files.Count(static x => x.IsIncludedForRemoval);
}
