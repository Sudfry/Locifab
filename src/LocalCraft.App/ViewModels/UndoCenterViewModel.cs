using System.Collections.ObjectModel;
using LocalCraft.App.Infrastructure;
using LocalCraft.App.Services;
using LocalCraft.Core.Journal;
using LocalCraft.Core.Operations;

namespace LocalCraft.App.ViewModels;

public sealed class UndoCenterViewModel : ViewModelBase
{
    private readonly JournalRepository _repository;
    private readonly IDialogService _dialogService;

    private JournalListItem? _selectedJournal;
    private bool _isBusy;
    private bool _isProgressIndeterminate;
    private double _progressValue;
    private string _busyText = string.Empty;
    private string _warningsText = string.Empty;
    private bool _hasStatus;
    private bool _statusIsError;
    private string _statusMessage = string.Empty;

    public UndoCenterViewModel(JournalRepository repository, IDialogService dialogService)
    {
        _repository = repository;
        _dialogService = dialogService;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        UndoCommand = new AsyncRelayCommand(UndoAsync, () => CanUndoSelected);
        OpenJournalFolderCommand = new RelayCommand(OpenJournalFolder);
        RevealJournalFileCommand = new RelayCommand(RevealJournalFile, () => SelectedJournal is not null);
        DeleteJournalCommand = new RelayCommand(DeleteSelectedJournal, () => SelectedJournal is not null && !IsBusy);
        DeleteJournalItemCommand = new RelayCommand(parameter =>
        {
            if (parameter is JournalListItem item) DeleteJournal(item);
        }, parameter => parameter is JournalListItem && !IsBusy);
    }

    public ObservableCollection<JournalListItem> Journals { get; } = new();

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand UndoCommand { get; }
    public RelayCommand OpenJournalFolderCommand { get; }
    public RelayCommand RevealJournalFileCommand { get; }
    public RelayCommand DeleteJournalCommand { get; }
    public RelayCommand DeleteJournalItemCommand { get; }

    public JournalListItem? SelectedJournal
    {
        get => _selectedJournal;
        set
        {
            if (!SetProperty(ref _selectedJournal, value)) return;
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(CanUndoSelected));
            OnPropertyChanged(nameof(SelectedUndoHint));
            UndoCommand.RaiseCanExecuteChanged();
            DeleteJournalCommand.RaiseCanExecuteChanged();
            RevealJournalFileCommand.RaiseCanExecuteChanged();
            DeleteJournalCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasSelection => SelectedJournal is not null;

    public bool HasJournals => Journals.Count > 0;

    public bool IsEmpty => !HasJournals && !IsBusy;

    public string JournalCountText => HasJournals ? $"{Journals.Count} 条本地操作记录" : "暂无操作记录";

    public bool CanUndoSelected => !IsBusy && SelectedJournal?.CanUndo == true;

    public string SelectedUndoHint => SelectedJournal?.UndoHint ?? "选择一条操作记录查看完整映射。";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(IsIdle));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(CanUndoSelected));
            RefreshCommand.RaiseCanExecuteChanged();
            UndoCommand.RaiseCanExecuteChanged();
            DeleteJournalCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsIdle => !IsBusy;

    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        private set => SetProperty(ref _isProgressIndeterminate, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public string BusyText
    {
        get => _busyText;
        private set => SetProperty(ref _busyText, value);
    }

    public string WarningsText
    {
        get => _warningsText;
        private set
        {
            if (SetProperty(ref _warningsText, value)) OnPropertyChanged(nameof(HasWarnings));
        }
    }

    public bool HasWarnings => !string.IsNullOrWhiteSpace(WarningsText);

    public bool HasStatus
    {
        get => _hasStatus;
        private set => SetProperty(ref _hasStatus, value);
    }

    public bool StatusIsError
    {
        get => _statusIsError;
        private set => SetProperty(ref _statusIsError, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public void OnActivated()
    {
        _ = RefreshAsync();
    }

    private void DeleteSelectedJournal()
    {
        if (SelectedJournal is not null) DeleteJournal(SelectedJournal);
    }

    private void DeleteJournal(JournalListItem item)
    {
        if (!_dialogService.Confirm("删除操作记录", "只删除这条本地日志及其备份，不会修改任何文件。是否继续？")) return;
        try
        {
            string path = item.Entry.FilePath;
            string id = item.Journal.Id;
            if (File.Exists(path)) File.Delete(path);
            _repository.DeleteBackupDirectory(id);
            Journals.Remove(item);
            if (ReferenceEquals(SelectedJournal, item)) SelectedJournal = null;
            OnPropertyChanged(nameof(HasJournals));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(JournalCountText));
            StatusMessage = "记录已删除。";
            HasStatus = true;
            StatusIsError = false;
        }
        catch (Exception ex)
        {
            StatusMessage = "删除失败：" + ex.Message;
            HasStatus = true;
            StatusIsError = true;
        }
    }

    private async Task RefreshAsync()
    {
        if (IsBusy) return;

        string? previousId = SelectedJournal?.Journal.Id;
        IsBusy = true;
        BusyText = "正在读取本地回退日志…";
        IsProgressIndeterminate = true;

        try
        {
            var load = await Task.Run(() => _repository.LoadAll());

            Journals.Clear();
            foreach (var entry in load.Entries)
            {
                Journals.Add(new JournalListItem(entry));
            }

            WarningsText = string.Join(Environment.NewLine, load.Warnings);
            OnPropertyChanged(nameof(HasJournals));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(JournalCountText));

            SelectedJournal = previousId is null
                ? Journals.FirstOrDefault()
                : Journals.FirstOrDefault(x => x.Journal.Id == previousId) ?? Journals.FirstOrDefault();
        }
        catch (Exception ex)
        {
            WarningsText = "读取日志失败：" + ex.Message;
            Journals.Clear();
            SelectedJournal = null;
            OnPropertyChanged(nameof(HasJournals));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(JournalCountText));
        }
        finally
        {
            IsBusy = false;
            IsProgressIndeterminate = false;
        }
    }

    private async Task UndoAsync()
    {
        var selected = SelectedJournal;
        if (selected is null || !selected.CanUndo || IsBusy) return;

        bool isConversion = selected.IsConversion;
        string confirmation = isConversion
            ? $"将删除本次转换生成的 {selected.Journal.OperationCount} 个输出文件。{Environment.NewLine}{Environment.NewLine}" +
              $"操作时间：{selected.CreatedText}{Environment.NewLine}" +
              $"源文件夹：{selected.SourceDirectory}{Environment.NewLine}{Environment.NewLine}" +
              "原始文件不会被修改或删除。"
            : $"将把 {selected.Journal.OperationCount} 个文件恢复到操作前状态。{Environment.NewLine}{Environment.NewLine}" +
              $"操作时间：{selected.CreatedText}{Environment.NewLine}" +
              $"文件夹：{selected.SourceDirectory}{Environment.NewLine}{Environment.NewLine}" +
              "回退会逆向执行日志中的完整映射；被覆盖的文件会从本地备份恢复。";

        if (!_dialogService.Confirm(isConversion ? "确认删除转换输出" : "确认一键回退", confirmation))
        {
            return;
        }

        HasStatus = false;
        IsBusy = true;
        IsProgressIndeterminate = false;
        ProgressValue = 0;
        BusyText = "正在准备回退…";

        var progress = new Progress<OperationProgress>(value =>
        {
            ProgressValue = value.Total == 0 ? 0 : value.Completed * 100.0 / value.Total;
            BusyText = string.IsNullOrEmpty(value.CurrentFile)
                ? value.Stage
                : $"{value.Stage} · {value.CurrentFile}（{value.Completed}/{value.Total}）";
        });

        UndoResult result;
        try
        {
            result = await Task.Run(() => new UndoService(_repository).Undo(selected.Entry, progress));
        }
        catch (Exception ex)
        {
            result = new UndoResult(false, null, "回退过程发生异常：" + ex.Message);
        }
        finally
        {
            IsBusy = false;
            IsProgressIndeterminate = false;
        }

        StatusIsError = !result.Success;
        StatusMessage = result.Message;
        HasStatus = true;
        await RefreshAsync();

        if (!result.Success)
        {
            _dialogService.ShowError("回退未完成", result.Message);
        }
    }

    private void OpenJournalFolder()
    {
        try
        {
            ShellService.OpenFolder(_repository.JournalsDirectory);
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("无法打开日志目录", ex.Message);
        }
    }

    private void RevealJournalFile()
    {
        if (SelectedJournal is null) return;
        try
        {
            ShellService.RevealFile(SelectedJournal.Entry.FilePath);
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("无法打开日志文件", ex.Message);
        }
    }
}
