using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LocalCraft.App.Services;
using LocalCraft.App.ViewModels;
using LocalCraft.Core.Journal;

internal sealed class Dialog : IDialogService
{
    public string FilePath = "";
    public bool Confirm(string title, string message) => true;
    public string? PickFolder(string? initialDirectory = null) => null;
    public IReadOnlyList<string> PickFiles(string filter, string? initialDirectory = null) => new[] { FilePath };
    public string? PickSaveFile(string filter, string suggestedFileName, string? initialDirectory = null) => null;
    public void ShowError(string title, string message) => Console.WriteLine("ERROR " + title + ": " + message);
    public void ShowInfo(string title, string message) => Console.WriteLine("INFO " + title + ": " + message);
}

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        string workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string template = Path.Combine(workspace, "视频演示数据-11.1.0", "05-模板与说明", "流水线模板", "交付整理-全流程.json");
        string data = Path.Combine(workspace, "tests", "import-harness-data");
        if (Directory.Exists(data)) Directory.Delete(data, true);
        Directory.CreateDirectory(data);
        Environment.SetEnvironmentVariable("LOCALCRAFT_DATA_DIR", data);

        var dialog = new Dialog { FilePath = template };
        var vm = new PipelineViewModel(new JournalRepository(data), dialog);
        int before = vm.Presets.Count;
        vm.ImportPresetFileCommand.Execute(null);
        int after = vm.Presets.Count;
        Console.WriteLine($"BEFORE={before} AFTER={after}");
        Console.WriteLine("HAS_DELIVERY=" + vm.Presets.Any(x => x.Name == "交付整理-全流程"));
        Console.WriteLine("HAS_DOC=" + vm.Presets.Any(x => x.Name == "文档隐私交付"));
        Console.WriteLine("HAS_ARCHIVE=" + vm.Presets.Any(x => x.Name == "客户资料归档"));
        Console.WriteLine("TEMPLATE_FILES=" + Directory.GetFiles(Path.Combine(data, "流水线模板"), "*.json").Length);
        Console.WriteLine("SELECTED=" + vm.SelectedPreset?.Name);
        var reloaded = new PipelineViewModel(new JournalRepository(data), dialog);
        Console.WriteLine("RELOADED=" + reloaded.Presets.Count);
        Console.WriteLine("RELOADED_DELIVERY=" + reloaded.Presets.Any(x => x.Name == "交付整理-全流程"));
        return (after >= before + 1 && vm.Presets.Any(x => x.Name == "交付整理-全流程")
                && reloaded.Presets.Any(x => x.Name == "交付整理-全流程")) ? 0 : 1;
    }
}
