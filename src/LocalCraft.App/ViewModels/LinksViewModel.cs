using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using LocalCraft.App.Infrastructure;
using LocalCraft.App.Services;
using LocalCraft.Core.Journal;

namespace LocalCraft.App.ViewModels;

public sealed class LinkItem : ViewModelBase
{
    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsCustom { get; set; }
}

public sealed class LinksViewModel : ViewModelBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _customLinksPath;
    private readonly IDialogService _dialogService;
    private string _newName = string.Empty;
    private string _newUrl = string.Empty;
    private string _newDescription = string.Empty;
    private string _statusText = "内置链接由系统浏览器打开；自定义链接只保存在本机。";

    public LinksViewModel(JournalRepository repository, IDialogService dialogService)
    {
        _customLinksPath = Path.Combine(repository.DataRoot, "links.json");
        _dialogService = dialogService;
        OpenLinkCommand = new RelayCommand(OpenLink);
        AddLinkCommand = new RelayCommand(AddLink, () => !string.IsNullOrWhiteSpace(NewName) && !string.IsNullOrWhiteSpace(NewUrl));
        RemoveLinkCommand = new RelayCommand(RemoveLink);
        LoadCustomLinks();
    }

    public ObservableCollection<LinkItem> BuiltInLinks { get; } = new()
    {
        new LinkItem { Name = "iLovePDF", Url = "https://www.ilovepdf.com/zh-cn", Description = "PDF 合并、拆分、压缩与 Office 转换" },
        new LinkItem { Name = "CloudConvert", Url = "https://cloudconvert.com/", Description = "大文件与多种格式的在线转换" },
        new LinkItem { Name = "Squoosh", Url = "https://squoosh.app/", Description = "图片压缩与格式转换，可比较画质" },
        new LinkItem { Name = "OCR.Space", Url = "https://ocr.space/", Description = "扫描件和图片文字识别" },
        new LinkItem { Name = "Smallpdf", Url = "https://smallpdf.com/cn", Description = "PDF 工具集合" },
        new LinkItem { Name = "Diffchecker", Url = "https://www.diffchecker.com/", Description = "文本与文件差异对比" },
        new LinkItem { Name = "Regex101", Url = "https://regex101.com/", Description = "正则表达式调试与解释" },
        new LinkItem { Name = "深求在线工具", Url = "https://tool.lu/", Description = "编码、时间戳、格式化等常用小工具" }
    };

    public ObservableCollection<LinkItem> CustomLinks { get; } = new();

    public RelayCommand OpenLinkCommand { get; }
    public RelayCommand AddLinkCommand { get; }
    public RelayCommand RemoveLinkCommand { get; }

    public string NewName
    {
        get => _newName;
        set
        {
            if (!SetProperty(ref _newName, value)) return;
            AddLinkCommand.RaiseCanExecuteChanged();
        }
    }

    public string NewUrl
    {
        get => _newUrl;
        set
        {
            if (!SetProperty(ref _newUrl, value)) return;
            AddLinkCommand.RaiseCanExecuteChanged();
        }
    }

    public string NewDescription
    {
        get => _newDescription;
        set => SetProperty(ref _newDescription, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool HasCustomLinks => CustomLinks.Count > 0;

    private void OpenLink(object? parameter)
    {
        if (parameter is not LinkItem link) return;
        if (!Uri.TryCreate(link.Url, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            _dialogService.ShowError("无法打开链接", "只支持 http:// 或 https:// 地址。");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("无法打开链接", ex.Message);
        }
    }

    private void AddLink()
    {
        if (!Uri.TryCreate(NewUrl.Trim(), UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            _dialogService.ShowInfo("添加链接", "网址必须以 http:// 或 https:// 开头。");
            return;
        }

        var link = new LinkItem
        {
            Name = NewName.Trim(),
            Url = uri.AbsoluteUri,
            Description = string.IsNullOrWhiteSpace(NewDescription) ? "自定义链接" : NewDescription.Trim(),
            IsCustom = true
        };
        CustomLinks.Add(link);
        NewName = string.Empty;
        NewUrl = string.Empty;
        NewDescription = string.Empty;
        SaveCustomLinks();
        OnPropertyChanged(nameof(HasCustomLinks));
        StatusText = "已添加自定义链接。";
    }

    private void RemoveLink(object? parameter)
    {
        if (parameter is not LinkItem link) return;
        CustomLinks.Remove(link);
        SaveCustomLinks();
        OnPropertyChanged(nameof(HasCustomLinks));
        StatusText = "已移除自定义链接。";
    }

    private void LoadCustomLinks()
    {
        try
        {
            if (!File.Exists(_customLinksPath)) return;
            string json = File.ReadAllText(_customLinksPath);
            var links = JsonSerializer.Deserialize<List<LinkItem>>(json, JsonOptions) ?? new();
            foreach (var link in links)
            {
                if (string.IsNullOrWhiteSpace(link.Name) || string.IsNullOrWhiteSpace(link.Url)) continue;
                link.IsCustom = true;
                CustomLinks.Add(link);
            }

            OnPropertyChanged(nameof(HasCustomLinks));
        }
        catch
        {
            // 损坏的自定义链接不应阻止页面打开。
        }
    }

    private void SaveCustomLinks()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_customLinksPath) ?? string.Empty);
            string json = JsonSerializer.Serialize(CustomLinks.ToList(), JsonOptions);
            string temp = _customLinksPath + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            File.Move(temp, _customLinksPath, overwrite: true);
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("保存自定义链接失败", ex.Message);
        }
    }
}
