using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.InteropServices;
using LocalCraft.App.Infrastructure;
using LocalCraft.App.Services;
using LocalCraft.Conversion.Engines;
using LocalCraft.Core.Journal;

namespace LocalCraft.App.ViewModels;

public sealed record EnvironmentInfoItem(string Label, string Value);
public sealed record EngineStatusItem(string Name, string Status, bool Available, string Description);

public sealed class EnvironmentViewModel : ViewModelBase
{
    private readonly JournalRepository _repository;
    private readonly IDialogService _dialogService;
    private string _versionText = string.Empty;
    private string _runtimeText = string.Empty;
    private string _osText = string.Empty;
    private string _memoryText = string.Empty;

    public EnvironmentViewModel(JournalRepository repository, IDialogService dialogService)
    {
        _repository = repository;
        _dialogService = dialogService;
        OpenDataFolderCommand = new RelayCommand(OpenDataFolder);
        OpenLogsFolderCommand = new RelayCommand(OpenLogsFolder);
        RefreshCommand = new RelayCommand(Refresh);
        Refresh();
    }

    public ObservableCollection<EnvironmentInfoItem> SystemInfo { get; } = new();

    public ObservableCollection<EngineStatusItem> Engines { get; } = new();

    public ObservableCollection<string> Capabilities { get; } = new();

    public RelayCommand OpenDataFolderCommand { get; }
    public RelayCommand OpenLogsFolderCommand { get; }
    public RelayCommand RefreshCommand { get; }

    public string VersionText
    {
        get => _versionText;
        private set => SetProperty(ref _versionText, value);
    }

    public string RuntimeText
    {
        get => _runtimeText;
        private set => SetProperty(ref _runtimeText, value);
    }

    public string OsText
    {
        get => _osText;
        private set => SetProperty(ref _osText, value);
    }

    public string MemoryText
    {
        get => _memoryText;
        private set => SetProperty(ref _memoryText, value);
    }

    public string DataRoot => _repository.DataRoot;

    public string SettingsPath => Path.Combine(_repository.DataRoot, "settings.json");

    private void Refresh()
    {
        Version appVersion = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 5, 0);
        VersionText = "v" + appVersion.ToString(3);
        RuntimeText = $".NET {Environment.Version}";
        OsText = RuntimeInformation.OSDescription;
        MemoryText = FormatBytes(Environment.WorkingSet);

        SystemInfo.Clear();
        SystemInfo.Add(new EnvironmentInfoItem("发行商", "Sudfry"));
        SystemInfo.Add(new EnvironmentInfoItem("运行模式", "完全离线 · 自包含单文件"));
        SystemInfo.Add(new EnvironmentInfoItem("进程架构", RuntimeInformation.ProcessArchitecture.ToString()));
        SystemInfo.Add(new EnvironmentInfoItem("操作系统", OsText));
        SystemInfo.Add(new EnvironmentInfoItem("运行环境", RuntimeText));
        SystemInfo.Add(new EnvironmentInfoItem("当前内存占用", MemoryText));
        SystemInfo.Add(new EnvironmentInfoItem("数据目录", DataRoot));
        SystemInfo.Add(new EnvironmentInfoItem("操作日志", _repository.JournalsDirectory));
        SystemInfo.Add(new EnvironmentInfoItem("设置文件", SettingsPath));

        Engines.Clear();
        try
        {
            var catalog = new ConversionEngineCatalog();
            foreach (var status in catalog.Statuses)
            {
                Engines.Add(new EngineStatusItem(
                    status.Name,
                    status.Available ? "已就绪" : "未检测到",
                    status.Available,
                    status.Description));
            }
        }
        catch (Exception ex)
        {
            Engines.Add(new EngineStatusItem("引擎检测", "检测失败", false, ex.Message));
        }

        Capabilities.Clear();
        Capabilities.Add("批量重命名：模板、预览、冲突阻断、日志与回退");
        Capabilities.Add("格式转换：内置文档/图片引擎，支持多格式导出");
        Capabilities.Add("内容去重：中文文本指纹与相似分组");
        Capabilities.Add("抽取式摘要：TextRank 关键句与 TF-IDF 关键词");
        Capabilities.Add("导出排版：Markdown / DOCX / PDF");
        Capabilities.Add("扫描分类：按类型、年份、年月、名称前缀");
        Capabilities.Add("文本替换：正则/字面量、备份与回退");
        Capabilities.Add("备份同步：只增不删、严格比对、回退");
        Capabilities.Add("实时监控：只记录变化，不自动处理");
    }

    private void OpenDataFolder()
    {
        try { ShellService.OpenFolder(_repository.DataRoot); }
        catch (Exception ex) { _dialogService.ShowError("无法打开数据目录", ex.Message); }
    }

    private void OpenLogsFolder()
    {
        try { ShellService.OpenFolder(_repository.JournalsDirectory); }
        catch (Exception ex) { _dialogService.ShowError("无法打开日志目录", ex.Message); }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = bytes;
        int index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return $"{value:0.#} {units[index]}";
    }
}
