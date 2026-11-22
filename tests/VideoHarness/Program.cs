using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using System.Threading;
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
    [STAThread]
    private static int Main()
    {
        string workspace = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string demo = Path.Combine(workspace, "视频演示数据-11.1.0");
        string dataDir = Path.Combine(workspace, "tests", "video-harness-data");
        string output = Path.Combine(workspace, "tests", "video-harness-output");
        if (Directory.Exists(dataDir)) Directory.Delete(dataDir, true);
        if (Directory.Exists(output)) Directory.Delete(output, true);
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(output);
        Environment.SetEnvironmentVariable("LOCALCRAFT_DATA_DIR", dataDir);
        Directory.CreateDirectory(Path.Combine(dataDir, "流水线模板"));
        foreach (var file in Directory.GetFiles(Path.Combine(demo, "05-模板与说明", "流水线模板"), "*.json"))
            File.Copy(file, Path.Combine(dataDir, "流水线模板", Path.GetFileName(file)), true);

        var cases = new[]
        {
            (Name: "交付整理-全流程", Source: Path.Combine(demo, "00-流水线-混乱素材"), Out: "full"),
            (Name: "客户资料归档", Source: Path.Combine(demo, "00-流水线-混乱素材"), Out: "archive"),
            (Name: "文档隐私交付", Source: Path.Combine(demo, "00-流水线-混乱素材", "办公"), Out: "doc"),
        };
        int failures = 0;
        foreach (var item in cases)
        {
            var vm = new PipelineViewModel(new JournalRepository(dataDir), new NullDialog());
            var preset = vm.Presets.FirstOrDefault(x => x.Name == item.Name);
            if (preset is null) { Console.WriteLine($"CASE={item.Name} MISSING"); failures++; continue; }
            vm.SelectedPreset = preset;
            vm.SourceFolder = item.Source;
            string outDir = Path.Combine(output, item.Out);
            Directory.CreateDirectory(outDir);
            vm.OutputFolder = outDir;
            vm.PreviewCommand.Execute(null);
            Wait(vm);
            Console.WriteLine($"CASE={item.Name} PREVIEW={vm.HasPreview}");
            if (!vm.HasPreview) { failures++; continue; }
            vm.ExecuteCommand.Execute(null);
            Wait(vm);
            int outputs = Directory.GetFiles(outDir, "*", SearchOption.AllDirectories).Length;
            Console.WriteLine($"CASE={item.Name} RESULT={vm.ResultIsSuccess} OUTPUT={outputs} MSG={vm.ResultMessage}");
            if (!vm.ResultIsSuccess) failures++;
        }
        return failures == 0 ? 0 : 1;
    }

    private static void Wait(PipelineViewModel vm)
    {
        for (int i = 0; i < 1200; i++)
        {
            if (!vm.IsBusy) { Thread.Sleep(250); if (!vm.IsBusy) return; }
            Thread.Sleep(250);
        }
        throw new TimeoutException("pipeline timeout");
    }
}
