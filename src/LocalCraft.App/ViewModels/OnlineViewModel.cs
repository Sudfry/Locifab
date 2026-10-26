using System.Collections.ObjectModel;
using LocalCraft.App.Infrastructure;

namespace LocalCraft.App.ViewModels;

public sealed record OnlineFeatureItem(string Name, string Status, string Description);

public sealed class OnlineViewModel : ViewModelBase
{
    public OnlineViewModel()
    {
        OpenLinksCommand = new RelayCommand(() => OpenLinksRequested?.Invoke(this, EventArgs.Empty));
        Features = new ObservableCollection<OnlineFeatureItem>
        {
            new("在线翻译", "未启用", "会把文本发送到第三方接口，与完全离线承诺冲突。"),
            new("在线 OCR", "未启用", "需要上传图片或扫描件，可能包含敏感内容。"),
            new("检查更新", "未启用", "程序不会主动发起任何出站请求；更新由你手动替换 exe。"),
            new("OpenAI 兼容接口", "未启用", "不保存密钥，不调用外部模型，也不提供 API 配置入口。")
        };
    }

    public event EventHandler? OpenLinksRequested;

    public ObservableCollection<OnlineFeatureItem> Features { get; }

    public RelayCommand OpenLinksCommand { get; }
}
