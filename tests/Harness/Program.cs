using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Text;
using LocalCraft.App.Services;
using LocalCraft.App.ViewModels;
using LocalCraft.Core.Journal;

internal sealed class NullDialog : IDialogService
{
    public bool Confirm(string title, string message) => true;
    public string? PickFolder(string? initialDirectory = null) => null;
    public IReadOnlyList<string> PickFiles(string filter, string? initialDirectory = null) => Array.Empty<string>();
    public string? PickSaveFile(string filter, string suggestedFileName, string? initialDirectory = null) => null;
    public void ShowError(string title, string message) => Console.WriteLine("ERROR " + title + ": " + message);
    public void ShowInfo(string title, string message) => Console.WriteLine("INFO " + title + ": " + message);
}

internal static class Program
{
    private static int _failed;

    [STAThread]
    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        string workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string templateDir = Path.Combine(workspace, "tests", "流水线测试模板-10.1.17");
        string dataDir = Path.Combine(workspace, "tests", "harness-data");
        string source = Path.Combine(workspace, "tests", "测试数据-10.1.17", "00-流水线输入");
        string workRoot = Path.Combine(workspace, "tests", "harness-work");
        if (Directory.Exists(workRoot)) Directory.Delete(workRoot, true);
        Directory.CreateDirectory(workRoot);
        Directory.CreateDirectory(dataDir);
        Environment.SetEnvironmentVariable("LOCALCRAFT_DATA_DIR", dataDir);
        string presetFile = Path.Combine(templateDir, "流水线模板.json");
        File.Copy(presetFile, Path.Combine(dataDir, "pipeline-presets.json"), true);

        var repo = new JournalRepository(dataDir);
        var vm = new PipelineViewModel(repo, new NullDialog());
        Console.WriteLine($"模板数量: {vm.Presets.Count}");
        if (vm.Presets.Count < 5) Fail("流水线模板数量少于 5");

        foreach (var preset in vm.Presets.ToList())
        {
            Console.WriteLine($"\n=== {preset.Name} ({preset.Steps.Count} 步) ===");
            vm.SelectedPreset = preset;
            string output = Path.Combine(workRoot, Sanitize(preset.Name));
            string sourceCopy = Path.Combine(output, "_源");
            CopyDirectory(source, sourceCopy);
            vm.SourceFolder = sourceCopy;
            vm.OutputFolder = output;
            vm.PreviewCommand.Execute(null);
            Wait(vm);
            Console.WriteLine($"预览: HasPreview={vm.HasPreview} {vm.SummaryText}");
            if (!vm.HasPreview) { Fail($"{preset.Name} 预览失败"); continue; }
            vm.ExecuteCommand.Execute(null);
            Wait(vm);
            Console.WriteLine($"执行: success={vm.ResultIsSuccess} {vm.ResultTitle} / {vm.ResultMessage}");
            if (!vm.ResultIsSuccess) Fail($"{preset.Name} 执行失败");
        }

        // 功能配置 JSON 解析
        string toolsFile = Path.Combine(templateDir, "功能配置模板.json");
        File.Copy(toolsFile, Path.Combine(dataDir, "tool-presets.json"), true);
        var store = new ToolPresetStore();
        Console.WriteLine($"\n功能配置数量: {store.All.Count}");
        if (store.All.Count != 19) Fail("功能配置数量不是 19");
        foreach (var preset in store.All)
        {
            if (preset.Step is null) Fail($"配置 {preset.Name} 没有步骤快照");
        }

        Console.WriteLine(_failed == 0 ? "\n全部通过" : $"\n失败 {_failed} 项");
        return _failed == 0 ? 0 : 1;
    }

    private static void Wait(PipelineViewModel vm)
    {
        for (int i = 0; i < 600; i++)
        {
            if (!vm.IsBusy) { Thread.Sleep(200); if (!vm.IsBusy) return; }
            Thread.Sleep(200);
        }
        Fail("等待流水线超时");
    }

    private static void Fail(string message)
    {
        _failed++;
        Console.WriteLine("FAIL: " + message);
    }

    private static string Sanitize(string value) =>
        string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(source, target));
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(source, target), true);
    }
}
