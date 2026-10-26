using System.Collections.ObjectModel;
using LocalCraft.App.Infrastructure;
using LocalCraft.App.Services;
using LocalCraft.Core.Text;

namespace LocalCraft.App.ViewModels;

public sealed class DiffViewModel : ViewModelBase
{
    private readonly IDialogService _dialogService;
    private string _leftPath = string.Empty;
    private string _rightPath = string.Empty;
    private string _summary = "选择两个文本文件，逐行对比；不会修改文件。";
    private bool _hasResult;

    public DiffViewModel(IDialogService dialogService)
    {
        _dialogService = dialogService;
        BrowseLeftCommand = new RelayCommand(() => Browse(true));
        BrowseRightCommand = new RelayCommand(() => Browse(false));
        CompareCommand = new RelayCommand(Compare, () => File.Exists(LeftPath) && File.Exists(RightPath));
    }

    public ObservableCollection<DiffLine> Rows { get; } = new();
    public RelayCommand BrowseLeftCommand { get; }
    public RelayCommand BrowseRightCommand { get; }
    public RelayCommand CompareCommand { get; }

    public string LeftPath { get => _leftPath; set { if (SetProperty(ref _leftPath, value)) CompareCommand.RaiseCanExecuteChanged(); } }
    public string RightPath { get => _rightPath; set { if (SetProperty(ref _rightPath, value)) CompareCommand.RaiseCanExecuteChanged(); } }
    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
    public bool HasResult { get => _hasResult; private set => SetProperty(ref _hasResult, value); }

    private void Browse(bool left)
    {
        var files = _dialogService.PickFiles("文本文件|*.txt;*.md;*.csv;*.json;*.xml;*.log;*.cs;*.js;*.html;*.css;*.ini;*.yml;*.yaml|所有文件|*.*", null);
        if (files.Count == 0) return;
        if (left) LeftPath = files[0];
        else RightPath = files[0];
    }

    private void Compare()
    {
        var result = TextDiffService.Compare(LeftPath, RightPath);
        Rows.Clear();
        if (!result.Success)
        {
            Summary = result.Error ?? "对比失败。";
            HasResult = false;
            return;
        }
        foreach (var line in result.Lines) Rows.Add(line);
        Summary = $"新增 {result.AddedCount} 行 · 删除 {result.RemovedCount} 行 · 共 {result.Lines.Count} 行";
        HasResult = true;
    }
}
