using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalCraft.Conversion.Documents;
using LocalCraft.Conversion.Engines;
using LocalCraft.Conversion.Formats;
using LocalCraft.Conversion.Images;
using LocalCraft.Conversion.Metadata;
using LocalCraft.Conversion.Operations;
using LocalCraft.Conversion.Planning;
using LocalCraft.Core.Analysis;
using LocalCraft.Core.Classify;
using LocalCraft.Core.Journal;
using LocalCraft.Core.Operations;
using LocalCraft.Core.Rename;
using LocalCraft.Core.Archives;
using LocalCraft.Core.Backup;
using LocalCraft.Core.Checksums;
using LocalCraft.Core.Cleanup;
using LocalCraft.Core.Files;
using LocalCraft.Core.Reports;
using LocalCraft.Core.Text;
using LocalCraft.Core.Search;
using LocalCraft.Core.Settings;
using LocalCraft.Core.Tables;
using LocalCraft.Core.TextReplacement;

namespace LocalCraft.SelfTest;

internal static class Program
{
    private static readonly List<string> Failures = new();
    private static int _assertions;

    [STAThread]
    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("LocalCraft 核心自测");
        Console.WriteLine("==================");

        string root = Path.Combine(Path.GetTempPath(), "LocalCraftSelfTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            TestPreviewIsReadOnly(root);
            TestRenameAndUndo(root);
            TestOverwriteAndUndo(root);
            TestCaseOnlyRenameAndUndo(root);
            TestPlannerConflicts(root);
            TestInvalidNameRejected(root);
            TestJournalPersistence(root);
            TestDocumentConversionAndUndo(root);
            TestImageConversionAndUndo(root);
            TestConversionPlanning(root);
            TestPdfExport(root);
            TestMultiFormatExport(root);
            TestDedupGroupingAndSummary(root);
            TestClassificationAndUndo(root);
            TestSettingsPersistence(root);
            TestTextReplacement(root);
            TestBackupSync(root);
            TestTableProcessing(root);
            TestArchiveOperations(root);
            TestCleanup(root);
            TestChecksums(root);
            TestImageBatch(root);
            TestPhotoSimilarity(root);
            TestMetadataCleanup(root);
            TestFileSearch(root);
            TestFileReport(root);
            TestFileTools(root);
        }
        catch (Exception ex)
        {
            Failures.Add("未捕获异常：" + ex);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }

        Console.WriteLine();
        Console.WriteLine($"断言：{_assertions}，失败：{Failures.Count}");
        if (Failures.Count == 0)
        {
            Console.WriteLine("全部通过 ✓");
            return 0;
        }

        foreach (string failure in Failures)
        {
            Console.WriteLine("✗ " + failure);
        }

        return 1;
    }

    private static void TestPreviewIsReadOnly(string root)
    {
        string dir = NewDirectory(root, nameof(TestPreviewIsReadOnly));
        CreateFile(dir, "a.txt");
        CreateFile(dir, "b.txt");
        string[] before = Directory.GetFiles(dir);

        var plan = new RenamePlanner().CreatePlan(before, new RenameOptions { Prefix = "预览_" });
        Assert(plan.Items.Count == 2, "预览应包含两个文件");
        Assert(plan.ReadyCount == 2, "预览两行应为可执行");
        Assert(Directory.GetFiles(dir).OrderBy(x => x).SequenceEqual(before.OrderBy(x => x)),
            "只读预览不能修改文件");
        Assert(!Directory.GetFiles(dir).Any(x => Path.GetFileName(x).StartsWith("预览_")),
            "只读预览不能产生新文件");
    }

    private static void TestRenameAndUndo(string root)
    {
        string dir = NewDirectory(root, nameof(TestRenameAndUndo));
        CreateFile(dir, "发票_01.txt", "第一张");
        CreateFile(dir, "发票_02.txt", "第二张");

        var repository = NewRepository(root, nameof(TestRenameAndUndo));
        var plan = new RenamePlanner().CreatePlan(
            Directory.GetFiles(dir),
            new RenameOptions { FindText = "发票", ReplaceText = "凭据" });

        Assert(plan.ConflictCount == 0, "正常改名不应有冲突");
        var work = plan.Items.Where(x => x.IsExecutable)
            .Select(x => new RenameWorkItem(x.SourcePath, x.TargetPath, false)).ToList();

        var result = new BatchRenameExecutor(repository).Execute(work);
        Assert(result.Success, "重命名执行应成功");
        Assert(File.Exists(Path.Combine(dir, "凭据_01.txt")), "应生成新文件名");
        Assert(!File.Exists(Path.Combine(dir, "发票_01.txt")), "旧文件名应消失");
        Assert(File.ReadAllText(Path.Combine(dir, "凭据_02.txt")) == "第二张", "改名不能丢失内容");
        Assert(result.Journal?.Status == JournalStatus.Completed, "日志状态应为 Completed");

        var loaded = repository.LoadAll();
        Assert(loaded.Entries.Count == 1, "应写入一条操作日志");
        var undoResult = new UndoService(repository).Undo(loaded.Entries[0]);
        Assert(undoResult.Success, "一键回退应成功");
        Assert(File.Exists(Path.Combine(dir, "发票_01.txt")), "回退应恢复旧文件名");
        Assert(!File.Exists(Path.Combine(dir, "凭据_01.txt")), "回退应移除新文件名");
        Assert(File.ReadAllText(Path.Combine(dir, "发票_01.txt")) == "第一张", "回退应保留原内容");

        var afterUndo = repository.LoadAll();
        Assert(afterUndo.Entries[0].Journal.Status == JournalStatus.Undone, "回退后日志应为 Undone");
        Assert(!DirectoriesUnder(repository.BackupsDirectory).Any(), "回退后应清理备份");
    }

    private static void TestOverwriteAndUndo(string root)
    {
        string dir = NewDirectory(root, nameof(TestOverwriteAndUndo));
        string source = CreateFile(dir, "source.txt", "新内容");
        string target = CreateFile(dir, "target.txt", "被覆盖的旧内容");

        var repository = NewRepository(root, nameof(TestOverwriteAndUndo));
        var plan = new RenamePlanner().CreatePlan(
            new[] { source },
            new RenameOptions
            {
                FindText = "source",
                ReplaceText = "target",
                AllowOverwrite = true
            });

        Assert(plan.Items[0].Status == RenameItemStatus.Overwrite, "已存在目标应标记为覆盖");
        Assert(File.ReadAllText(target) == "被覆盖的旧内容", "预览阶段不能覆盖目标");

        var result = new BatchRenameExecutor(repository).Execute(
            new[] { new RenameWorkItem(source, target, true) });
        Assert(result.Success, "允许覆盖时执行应成功");
        Assert(File.ReadAllText(target) == "新内容", "目标应变为源文件内容");

        var journal = repository.LoadAll().Entries.Single().Journal;
        string? backup = journal.Operations[0].BackupPath;
        Assert(backup is not null && File.Exists(backup), "覆盖前应创建备份");

        var undoResult = new UndoService(repository).Undo(repository.LoadAll().Entries.Single());
        Assert(undoResult.Success, "覆盖操作应可回退");
        Assert(File.Exists(source), "回退应恢复源文件");
        Assert(File.ReadAllText(source) == "新内容", "回退后源文件内容应正确");
        Assert(File.Exists(target), "回退应重建被覆盖的目标文件");
        Assert(File.ReadAllText(target) == "被覆盖的旧内容", "回退应还原被覆盖文件内容");
    }

    private static void TestCaseOnlyRenameAndUndo(string root)
    {
        string dir = NewDirectory(root, nameof(TestCaseOnlyRenameAndUndo));
        string source = CreateFile(dir, "Report.TXT", "case");

        var repository = NewRepository(root, nameof(TestCaseOnlyRenameAndUndo));
        var plan = new RenamePlanner().CreatePlan(
            new[] { source },
            new RenameOptions { FindText = "Report", ReplaceText = "report" });

        Assert(plan.Items[0].Status == RenameItemStatus.Ready, "仅大小写改名应可执行");
        Assert(plan.Items[0].IsCaseOnlyChange, "应标记为仅大小写变化");

        var result = new BatchRenameExecutor(repository).Execute(
            new[] { new RenameWorkItem(source, plan.Items[0].TargetPath, false) });
        Assert(result.Success, "仅大小写改名应成功");
        Assert(Directory.GetFiles(dir).Single().EndsWith("report.TXT", StringComparison.Ordinal),
            "文件系统应反映新的名称大小写");

        var undo = new UndoService(repository).Undo(repository.LoadAll().Entries.Single());
        Assert(undo.Success, "仅大小写改名应可回退");
        Assert(Directory.GetFiles(dir).Single().EndsWith("Report.TXT", StringComparison.Ordinal),
            "回退应恢复原始大小写");
    }

    private static void TestPlannerConflicts(string root)
    {
        string dir = NewDirectory(root, nameof(TestPlannerConflicts));
        string first = CreateFile(dir, "x1.txt");
        CreateFile(dir, "x2.txt");
        CreateFile(dir, "EXISTS.txt");

        var duplicatePlan = new RenamePlanner().CreatePlan(
            new[] { first, Path.Combine(dir, "x2.txt") },
            new RenameOptions { FindText = "1", ReplaceText = "2" });
        Assert(duplicatePlan.ConflictCount == 2, "多个文件指向同一目标时应全部标记冲突");
        Assert(duplicatePlan.Items.All(x => x.Note.Contains("同一名称", StringComparison.Ordinal)),
            "重复目标应说明原因");

        string source = CreateFile(dir, "a.txt");
        var conflictPlan = new RenamePlanner().CreatePlan(
            new[] { source },
            new RenameOptions { FindText = "a", ReplaceText = "EXISTS" });
        Assert(conflictPlan.Items[0].Status == RenameItemStatus.Conflict,
            "目标已存在且未允许覆盖时应标记冲突");
        Assert(conflictPlan.Items[0].Note.Contains("目标已存在", StringComparison.Ordinal),
            "冲突行应说明原因");
    }

    private static void TestInvalidNameRejected(string root)
    {
        string dir = NewDirectory(root, nameof(TestInvalidNameRejected));
        string source = CreateFile(dir, "normal.txt");

        var plan = new RenamePlanner().CreatePlan(
            new[] { source },
            new RenameOptions { Prefix = "note_" });
        Assert(plan.Items[0].Status == RenameItemStatus.Ready, "普通前缀应可执行");

        var emptyPlan = new RenamePlanner().CreatePlan(
            new[] { source },
            new RenameOptions { FindText = "normal", ReplaceText = "" });
        Assert(emptyPlan.Items[0].Status == RenameItemStatus.Invalid, "空文件名应被拒绝");
    }

    private static void TestJournalPersistence(string root)
    {
        string dir = NewDirectory(root, nameof(TestJournalPersistence));
        string source = CreateFile(dir, "persist.txt", "persist");
        var repository = NewRepository(root, nameof(TestJournalPersistence));

        var result = new BatchRenameExecutor(repository).Execute(
            new[] { new RenameWorkItem(source, Path.Combine(dir, "persist_renamed.txt"), false) });
        Assert(result.Success, "持久化测试执行应成功");

        var secondRepository = new JournalRepository(repository.DataRoot);
        var loaded = secondRepository.LoadAll();
        Assert(loaded.Entries.Count == 1, "新实例应能读取既有日志");
        Assert(loaded.Entries[0].Journal.Kind == "rename", "日志应记录操作类型");
        Assert(loaded.Entries[0].Journal.Operations[0].TargetPath.EndsWith("persist_renamed.txt"),
            "日志应保存完整新旧映射");
    }

    private static void TestDocumentConversionAndUndo(string root)
    {
        string dir = NewDirectory(root, nameof(TestDocumentConversionAndUndo));
        string source = CreateFile(
            dir,
            "会议记录.md",
            "# 测试标题\n\n这是一个中文段落，包含 **重点** 内容。\n\n- 第一项\n- 第二项\n");
        var repository = NewRepository(root, nameof(TestDocumentConversionAndUndo));
        var catalog = NewConversionCatalog(root, nameof(TestDocumentConversionAndUndo));
        var planner = new ConversionPlanner(catalog);
        var docxTarget = ConversionTargetFormat.All.Single(static x => x.Id == "docx");
        string output = Path.Combine(dir, "输出_docx");

        var plan = planner.CreatePlan(new[] { source }, output, docxTarget);
        Assert(plan.ReadyCount == 1, "Markdown → DOCX 预览应为可执行");
        Assert(plan.Items[0].EngineId == "builtin-doc", "文档转换应使用内置文档引擎");
        Assert(!Directory.Exists(output), "预览阶段不能创建输出文件夹");

        var result = new ConversionExecutor(repository, catalog)
            .ExecuteAsync(ToWorkItems(plan)).GetAwaiter().GetResult();
        Assert(result.Success, "Markdown → DOCX 应执行成功");
        string docxPath = plan.Items[0].TargetPath;
        Assert(File.Exists(docxPath), "应生成 DOCX 输出文件");
        using (var zip = System.IO.Compression.ZipFile.OpenRead(docxPath))
        {
            Assert(zip.GetEntry("word/document.xml") is not null, "DOCX 应包含 document.xml");
            Assert(zip.GetEntry("word/styles.xml") is not null, "DOCX 应包含中文标题样式");
        }

        Assert(File.Exists(source), "转换后原文件必须保留");
        var journal = repository.LoadAll().Entries.Single();
        Assert(journal.Journal.Kind == "convert", "转换日志类型应为 convert");

        var markdownTarget = ConversionTargetFormat.All.Single(static x => x.Id == "markdown");
        string readbackDirectory = Path.Combine(dir, "回读_md");
        var readbackPlan = planner.CreatePlan(new[] { docxPath }, readbackDirectory, markdownTarget);
        Assert(readbackPlan.ReadyCount == 1, "DOCX → Markdown 回读预览应为可执行");
        var readbackResult = new ConversionExecutor(repository, catalog)
            .ExecuteAsync(ToWorkItems(readbackPlan)).GetAwaiter().GetResult();
        Assert(readbackResult.Success, "DOCX → Markdown 回读应成功");
        string readbackText = File.ReadAllText(readbackPlan.Items[0].TargetPath);
        Assert(readbackText.Contains("# 测试标题", StringComparison.Ordinal), "回读后应保留标题层级");
        Assert(readbackText.Contains("中文段落", StringComparison.Ordinal), "回读后应保留中文正文");

        var docxJournal = repository.LoadAll().Entries.Single(
            entry => entry.Journal.Operations[0].TargetPath.EndsWith(".docx", StringComparison.OrdinalIgnoreCase));
        var undo = new UndoService(repository).Undo(docxJournal);
        Assert(undo.Success, "转换日志应可回退");
        Assert(!File.Exists(docxPath), "回退应删除转换生成的 DOCX");
        Assert(File.Exists(source), "回退转换输出时不能删除原文件");
    }

    private static void TestImageConversionAndUndo(string root)
    {
        string dir = NewDirectory(root, nameof(TestImageConversionAndUndo));
        string source = Path.Combine(dir, "像素.png");
        WriteTestPng(source, 6, 4);

        var repository = NewRepository(root, nameof(TestImageConversionAndUndo));
        var catalog = NewConversionCatalog(root, nameof(TestImageConversionAndUndo));
        var planner = new ConversionPlanner(catalog);
        var jpgTarget = ConversionTargetFormat.All.Single(static x => x.Id == "jpg");
        string output = Path.Combine(dir, "输出_jpg");

        var plan = planner.CreatePlan(new[] { source }, output, jpgTarget);
        Assert(plan.ReadyCount == 1, "PNG → JPG 预览应为可执行");
        Assert(plan.Items[0].EngineId == "builtin-image", "图片转换应使用内置图像引擎");

        var result = new ConversionExecutor(repository, catalog)
            .ExecuteAsync(ToWorkItems(plan)).GetAwaiter().GetResult();
        Assert(result.Success, "PNG → JPG 应执行成功");
        string jpgPath = plan.Items[0].TargetPath;
        Assert(File.Exists(jpgPath), "应生成 JPG 输出文件");
        using (var stream = File.OpenRead(jpgPath))
        {
            var decoder = new JpegBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Assert(decoder.Frames[0].PixelWidth == 6 && decoder.Frames[0].PixelHeight == 4,
                "输出 JPG 尺寸应与源图一致");
        }

        var undo = new UndoService(repository).Undo(repository.LoadAll().Entries.Single());
        Assert(undo.Success, "图片转换日志应可回退");
        Assert(!File.Exists(jpgPath), "回退应删除生成的 JPG");
        Assert(File.Exists(source), "回退不能删除原 PNG");
    }

    private static void TestConversionPlanning(string root)
    {
        string dir = NewDirectory(root, nameof(TestConversionPlanning));
        string source = CreateFile(dir, "普通.txt", "文档内容");
        string output = Directory.CreateDirectory(Path.Combine(dir, "输出")).FullName;
        var catalog = NewConversionCatalog(root, nameof(TestConversionPlanning));
        var planner = new ConversionPlanner(catalog);

        var wrongKind = planner.CreatePlan(
            new[] { source },
            output,
            ConversionTargetFormat.All.Single(static x => x.Id == "png"));
        Assert(wrongKind.Items[0].Status == ConversionItemStatus.Unsupported,
            "文档转图片目标应标记为不支持");

        string image = Path.Combine(dir, "照片.png");
        WriteTestPng(image, 2, 2);
        File.WriteAllText(Path.Combine(output, "照片.jpg"), "occupied");
        var collisionPlan = planner.CreatePlan(
            new[] { image },
            output,
            ConversionTargetFormat.All.Single(static x => x.Id == "jpg"));
        Assert(collisionPlan.Items[0].Status == ConversionItemStatus.Ready,
            "同名输出存在时应自动改名而不是覆盖");
        Assert(collisionPlan.Items[0].TargetPath.EndsWith("照片_1.jpg", StringComparison.Ordinal),
            "同名输出应自动追加序号");
        Assert(File.ReadAllText(Path.Combine(output, "照片.jpg")) == "occupied",
            "规划阶段不能覆盖已有文件");
    }

    private static void TestPdfExport(string root)
    {
        string dir = NewDirectory(root, nameof(TestPdfExport));
        string source = CreateFile(
            dir,
            "中文导出.md",
            "# 中文标题\n\n这是用于验证 PDF 导出的中文正文。English mixed 123。\n\n## 二级标题\n\n- 项目一\n- 项目二\n");
        var repository = NewRepository(root, nameof(TestPdfExport));
        var catalog = NewConversionCatalog(root, nameof(TestPdfExport));
        var planner = new ConversionPlanner(catalog);
        var pdfTarget = ConversionTargetFormat.All.Single(static x => x.Id == "pdf");
        string output = Path.Combine(dir, "pdf");

        var plan = planner.CreatePlan(new[] { source }, output, pdfTarget);
        Assert(plan.ReadyCount == 1, "Markdown → PDF 应使用内置排版引擎");
        var result = new ConversionExecutor(repository, catalog)
            .ExecuteAsync(ToWorkItems(plan)).GetAwaiter().GetResult();
        Assert(result.Success, "中文 PDF 导出应成功");
        string pdfPath = plan.Items[0].TargetPath;
        Assert(File.Exists(pdfPath), "应生成 PDF 文件");
        byte[] header = File.ReadAllBytes(pdfPath).Take(5).ToArray();
        Assert(System.Text.Encoding.ASCII.GetString(header) == "%PDF-", "PDF 文件头应正确");
        Assert(new FileInfo(pdfPath).Length > 8_000, "PDF 应包含排版页面图像");
    }

    private static void TestMultiFormatExport(string root)
    {
        string dir = NewDirectory(root, nameof(TestMultiFormatExport));
        string source = CreateFile(dir, "多格式.md", "# 多格式导出\n\n同一源文件同时导出 Word 和 PDF。");
        var repository = NewRepository(root, nameof(TestMultiFormatExport));
        var catalog = NewConversionCatalog(root, nameof(TestMultiFormatExport));
        var planner = new ConversionPlanner(catalog);
        string output = Path.Combine(dir, "out");

        var docxPlan = planner.CreatePlan(new[] { source }, output,
            ConversionTargetFormat.All.Single(static x => x.Id == "docx"));
        var pdfPlan = planner.CreatePlan(new[] { source }, output,
            ConversionTargetFormat.All.Single(static x => x.Id == "pdf"));

        var work = ToWorkItems(docxPlan).Concat(ToWorkItems(pdfPlan)).ToList();
        Assert(work.Count == 2, "同一源文件应能同时生成两种格式");
        var result = new ConversionExecutor(repository, catalog)
            .ExecuteAsync(work).GetAwaiter().GetResult();
        Assert(result.Success, "多格式导出应成功");
        Assert(File.Exists(docxPlan.Items[0].TargetPath), "应生成 DOCX");
        Assert(File.Exists(pdfPlan.Items[0].TargetPath), "应生成 PDF");
    }

    private static void TestDedupGroupingAndSummary(string root)
    {
        string dir = NewDirectory(root, nameof(TestDedupGroupingAndSummary));
        string common =
            "本地文件处理工具应该先预览再执行，所有批量操作都应该能够回退。" +
            "内容去重通过文本指纹识别相似文档，摘要通过关键词和关键句帮助阅读。" +
            "所有处理都在本机完成，不依赖网络，也不上传用户文件。" +
            "界面需要展示进度，日志需要记录完整映射，方便用户恢复。";
        string first = CreateFile(dir, "原文.txt", common);
        string second = CreateFile(dir, "副本.txt", common.Replace("帮助阅读", "辅助阅读"));
        string third = CreateFile(dir, "无关.txt",
            "今天的天气很好。公园里有很多花。孩子们在草地上玩耍，阳光温暖而明亮。");

        var docs = new List<DedupDocument>();
        foreach (string path in new[] { first, second, third })
        {
            string text = DocumentTextService.ExtractText(path);
            var tokens = TextTokenizer.Tokenize(text);
            docs.Add(new DedupDocument(path, text, SimHash.Compute(tokens), tokens.Count, new FileInfo(path).Length));
        }

        var groups = new DedupEngine().Group(docs, 0.80);
        Assert(groups.Count == 1, "应识别出一组内容相似文档");
        Assert(groups[0].Documents.Count == 2, "相似组应包含原文与副本");
        Assert(groups[0].Documents.All(d => d.Path != third), "无关文档不能进入相似组");

        string longText =
            "LocalCraft 是一个完全离线的本地文件处理工具。它先预览再执行，支持批量重命名。" +
            "格式转换可以处理文档和图片，所有输出都会记录到本地日志。" +
            "内容去重使用 SimHash 算法识别相似文档，不依赖云端。" +
            "抽取式摘要使用 TextRank 和 TF-IDF，不生成原文之外的内容。" +
            "用户可以在回退中心恢复重命名、转换和分类操作。" +
            "整个软件不需要联网，文件不会离开本机。";
        SummaryResult summary = new ExtractiveSummarizer().Summarize(longText, 4, 8);
        Assert(summary.Sentences.Count > 0 && summary.Sentences.Count <= 4, "关键句数量应受设置限制");
        Assert(summary.Keywords.Count > 0, "应提取出关键词");
        Assert(summary.Sentences.All(s => s.Offset >= 0 && s.Offset < longText.Length), "关键句偏移应位于原文中");
        Assert(summary.Sentences.All(s => longText.Substring(s.Offset, s.Text.Length) == s.Text), "关键句应可从偏移定位");
    }

    private static void TestClassificationAndUndo(string root)
    {
        string dir = NewDirectory(root, nameof(TestClassificationAndUndo));
        CreateFile(dir, "报告.txt", "文档");
        CreateFile(dir, "照片.png", "fake image");
        CreateFile(dir, "压缩包.zip", "fake archive");
        var repository = NewRepository(root, nameof(TestClassificationAndUndo));
        var planner = new ClassifyPlanner();
        var plan = planner.CreatePlan(dir, new ClassifyOptions { Mode = ClassificationMode.ByType });

        Assert(plan.TotalCount == 3, "分类规划应包含三个文件");
        Assert(plan.ReadyCount == 3, "所有文件应可分类");
        Assert(plan.Items.Any(i => i.Category == "文档"), "TXT 应归入文档分类");
        Assert(plan.Items.Any(i => i.Category == "图片"), "PNG 应归入图片分类");
        Assert(plan.Items.Any(i => i.Category == "压缩包"), "ZIP 应归入压缩包分类");
        Assert(!Directory.Exists(Path.Combine(dir, "文档")), "预览阶段不能创建分类目录");

        var work = plan.Items.Where(static x => x.IsExecutable)
            .Select(x => new RenameWorkItem(x.SourcePath, x.TargetPath, false)).ToList();
        var result = new BatchRenameExecutor(repository).Execute(
            work,
            cancellationToken: CancellationToken.None,
            kind: "move",
            displayName: "扫描分类",
            createTargetDirectories: true);

        Assert(result.Success, "分类移动应成功");
        Assert(File.Exists(Path.Combine(dir, "文档", "报告.txt")), "文件应移动到文档分类");
        Assert(File.Exists(Path.Combine(dir, "图片", "照片.png")), "文件应移动到图片分类");

        var undo = new UndoService(repository).Undo(repository.LoadAll().Entries.Single());
        Assert(undo.Success, "分类移动应可一键回退");
        Assert(File.Exists(Path.Combine(dir, "报告.txt")), "回退应恢复文件原位置");
        Assert(!File.Exists(Path.Combine(dir, "文档", "报告.txt")), "回退后分类目录中的文件应消失");
    }

    private static void TestSettingsPersistence(string root)
    {
        string dataRoot = Path.Combine(root, "_settings", nameof(TestSettingsPersistence));
        Directory.CreateDirectory(dataRoot);
        var settings = new AppSettingsService(dataRoot);
        Assert(settings.Current.AnimationsEnabled, "默认应启用动画");
        Assert(settings.Current.SummarySentenceCount == 5, "默认摘要句数应为 5");

        settings.Update(s =>
        {
            s.AnimationsEnabled = false;
            s.MotionLevel = MotionLevel.Low;
            s.DedupThreshold = 91;
            s.SummarySentenceCount = 8;
            s.DefaultClassificationMode = "ByMonth";
            s.DefaultIncludeSubfolders = true;
            s.RememberLastPage = true;
            s.LastPageKey = "dedup";
            s.Theme = ThemeMode.Dark;
            s.Accent = AccentPalette.Rose;
            s.Density = UiDensity.Compact;
            s.SidebarWidth = 268;
            s.NavOrder = new List<string> { "settings", "rename", "dedup" };
        });

        var reloaded = new AppSettingsService(dataRoot);
        Assert(!reloaded.Current.AnimationsEnabled, "动画设置应持久化");
        Assert(reloaded.Current.MotionLevel == MotionLevel.Low, "动效等级应持久化");
        Assert(Math.Abs(reloaded.Current.DedupThreshold - 91) < 0.001, "去重阈值应持久化");
        Assert(reloaded.Current.SummarySentenceCount == 8, "摘要句数应持久化");
        Assert(reloaded.Current.DefaultClassificationMode == "ByMonth", "分类默认方式应持久化");
        Assert(reloaded.Current.DefaultIncludeSubfolders, "子文件夹默认值应持久化");
        Assert(reloaded.Current.Theme == ThemeMode.Dark, "主题应持久化");
        Assert(reloaded.Current.Accent == AccentPalette.Rose, "主题色应持久化");
        Assert(reloaded.Current.Density == UiDensity.Compact, "界面密度应持久化");
        Assert(reloaded.Current.SidebarWidth == 268, "侧栏宽度应持久化");
        Assert(reloaded.Current.NavOrder.SequenceEqual(new[] { "settings", "rename", "dedup" }),
            "自定义导航排序应持久化");

        reloaded.ResetToDefaults();
        Assert(reloaded.Current.AnimationsEnabled, "恢复默认后动画应重新启用");
        Assert(reloaded.Current.SummarySentenceCount == 5, "恢复默认后摘要句数应重置");
    }

    private static void TestTextReplacement(string root)
    {
        string dir = NewDirectory(root, nameof(TestTextReplacement));
        string file = Path.Combine(dir, "sample.txt");
        File.WriteAllText(file, "alpha beta alpha\ngamma alpha", new System.Text.UTF8Encoding(false));
        File.WriteAllBytes(Path.Combine(dir, "binary.bin"), new byte[] { 0, 1, 2, 3, 0, 5 });

        var options = new TextReplaceOptions
        {
            Folder = dir,
            FindText = "alpha",
            ReplaceText = "omega",
            IncludeSubfolders = true
        };
        var scan = new TextReplacePlanner().Scan(options);
        Assert(scan.Success && scan.Files.Count == 1, "文本替换应只命中文本文件");
        Assert(scan.TotalMatches == 3, "文本替换应统计全部 3 处命中");

        var repository = NewRepository(root, nameof(TestTextReplacement));
        var result = new TextReplaceExecutor(repository).Execute(scan.Files, options);
        Assert(result.Success, "文本替换执行应成功");
        Assert(File.ReadAllText(file).Contains("omega beta omega"), "替换后的内容应写回原文件");

        var entry = repository.LoadAll().Entries.Single();
        Assert(entry.Journal.Kind == "replace", "文本替换应写入 replace 日志");
        var undo = new UndoService(repository).Undo(entry);
        Assert(undo.Success, "文本替换应可回退");
        Assert(File.ReadAllText(file).Contains("alpha beta alpha"), "回退后应恢复原文");
    }

    private static void TestBackupSync(string root)
    {
        string source = NewDirectory(root, nameof(TestBackupSync) + "_src");
        string target = NewDirectory(root, nameof(TestBackupSync) + "_dst");
        Directory.CreateDirectory(Path.Combine(source, "sub"));
        File.WriteAllText(Path.Combine(source, "a.txt"), "alpha");
        File.WriteAllText(Path.Combine(source, "sub", "b.txt"), "beta");

        var options = new BackupOptions
        {
            SourceFolder = source,
            TargetFolder = target,
            IncludeSubfolders = true,
            SkipHidden = false
        };
        var plan = new BackupPlanner().Scan(options);
        Assert(plan.Success && plan.NewCount == 2, "备份分析应识别 2 个新增文件");

        var repository = NewRepository(root, nameof(TestBackupSync));
        var execute = new BackupExecutor(repository).Execute(plan);
        Assert(execute.Success, "备份执行应成功");
        Assert(File.Exists(Path.Combine(target, "sub", "b.txt")), "备份应保留子目录结构");

        var undo = new UndoService(repository).Undo(repository.LoadAll().Entries.Single());
        Assert(undo.Success, "备份应可回退");
        Assert(!File.Exists(Path.Combine(target, "a.txt")), "回退新增备份应删除目标文件");
        Assert(File.Exists(Path.Combine(source, "a.txt")), "回退不应影响源文件");
    }

    private static void TestTableProcessing(string root)
    {
        string dir = NewDirectory(root, nameof(TestTableProcessing));
        string csv = Path.Combine(dir, "people.csv");
        File.WriteAllText(csv, "name,score,dept\nAlice,90,Eng\nBob,70,Ops\nCarol,95,Eng\n", new System.Text.UTF8Encoding(false));

        var scan = new TableReader().Scan(dir, false);
        Assert(scan.Success && scan.Files.Count == 1, "表格扫描应找到一个 CSV");
        Assert(scan.Files[0].Headers.Count == 3 && scan.Files[0].RowCount == 3, "表格应读取表头和 3 行数据");

        var data = TableProcessor.Build(scan.Files, new TableProcessOptions
        {
            FilterColumn = 2,
            FilterMode = TableFilterMode.Contains,
            FilterValue = "Eng",
            SortColumn = 1,
            SortNumeric = true,
            SortDescending = true,
            KeepColumns = new[] { 0, 1 }
        });
        Assert(data.Rows.Count == 2 && data.Rows[0][0] == "Carol", "筛选、排序与选列应正确");

        var repository = NewRepository(root, nameof(TestTableProcessing));
        string output = Path.Combine(dir, "result.csv");
        var result = new TableExporter(repository).Export(data, output, csv);
        Assert(result.Success && File.Exists(output), "表格应导出为 CSV");
        Assert(File.ReadAllText(output).Contains("Carol,95"), "导出的 CSV 应包含处理后的数据");

        var journal = repository.LoadAll().Entries.Single();
        var undo = new UndoService(repository).Undo(journal);
        Assert(undo.Success && !File.Exists(output), "表格导出的 CSV 应可回退删除");
    }

    private static void TestArchiveOperations(string root)
    {
        string dir = NewDirectory(root, nameof(TestArchiveOperations));
        string source = Path.Combine(dir, "source");
        Directory.CreateDirectory(Path.Combine(source, "sub"));
        File.WriteAllText(Path.Combine(source, "a.txt"), "alpha");
        File.WriteAllText(Path.Combine(source, "sub", "b.txt"), "beta");
        string zipPath = Path.Combine(dir, "bundle.zip");

        var plan = new ArchivePlanner().Plan(new ArchiveOptions
        {
            Mode = ArchiveMode.Compress,
            SourcePath = source,
            TargetPath = zipPath
        });
        Assert(plan.Success && plan.Entries.Count == 2, "ZIP 规划应包含 2 个文件");

        var repository = NewRepository(root, nameof(TestArchiveOperations) + "_zip");
        var result = new ArchiveExecutor(repository).Execute(plan);
        Assert(result.Success && File.Exists(zipPath), "ZIP 压缩应生成文件");

        string extractTarget = Path.Combine(dir, "extract");
        var extractPlan = new ArchivePlanner().Plan(new ArchiveOptions
        {
            Mode = ArchiveMode.Extract,
            SourcePath = zipPath,
            TargetPath = extractTarget
        });
        Assert(extractPlan.Success && extractPlan.Entries.Count == 2, "ZIP 解压规划应包含 2 个条目");

        var extractRepository = NewRepository(root, nameof(TestArchiveOperations) + "_extract");
        var extractResult = new ArchiveExecutor(extractRepository).Execute(extractPlan);
        Assert(extractResult.Success && File.Exists(Path.Combine(extractTarget, "sub", "b.txt")),
            "ZIP 解压应保留目录结构");
        var extractUndo = new UndoService(extractRepository).Undo(extractRepository.LoadAll().Entries.Single());
        Assert(extractUndo.Success && !File.Exists(Path.Combine(extractTarget, "a.txt")),
            "ZIP 解压应可回退删除输出");

        var undo = new UndoService(repository).Undo(repository.LoadAll().Entries.Single());
        Assert(undo.Success && !File.Exists(zipPath), "ZIP 压缩应可回退删除输出");
    }

    private static void TestCleanup(string root)
    {
        string dir = NewDirectory(root, nameof(TestCleanup));
        string empty = Path.Combine(dir, "empty");
        string nested = Path.Combine(empty, "nested");
        Directory.CreateDirectory(nested);
        string zero = Path.Combine(dir, "placeholder.txt");
        File.WriteAllBytes(zero, Array.Empty<byte>());
        File.WriteAllText(Path.Combine(dir, "keep.txt"), "keep");

        var plan = new CleanupPlanner().Scan(new CleanupOptions
        {
            Folder = dir,
            IncludeSubfolders = true,
            RemoveEmptyDirectories = true,
            RemoveZeroByteFiles = true
        });
        Assert(plan.Success && plan.Items.Count == 3, "清理扫描应找到 2 个空目录和 1 个零字节文件");

        var repository = NewRepository(root, nameof(TestCleanup));
        var execute = new CleanupExecutor(repository).Execute(plan);
        Assert(execute.Success, "清理执行应成功");
        Assert(!File.Exists(zero) && !Directory.Exists(nested), "清理应移除零字节文件和空目录");
        Assert(File.Exists(Path.Combine(dir, "keep.txt")), "清理不应修改非空文件");

        var undo = new UndoService(repository).Undo(repository.LoadAll().Entries.Single());
        Assert(undo.Success && Directory.Exists(nested) && File.Exists(zero), "清理应可恢复空目录与零字节文件");
    }

    private static void TestChecksums(string root)
    {
        string dir = NewDirectory(root, nameof(TestChecksums));
        string file = Path.Combine(dir, "a.txt");
        File.WriteAllText(file, "checksum");
        string manifest = Path.Combine(dir, "checksums.sha256");
        var service = new ChecksumService();

        var scan = service.BuildManifest(dir, false);
        Assert(scan.Success && scan.Entries.Count == 1 && scan.Entries[0].Hash.Length == 64,
            "SHA-256 清单应包含 64 位摘要");
        service.WriteManifest(manifest, scan.Entries);
        var verify = service.Verify(dir, manifest);
        Assert(verify.Success && verify.OkCount == 1, "未修改文件应校验一致");

        File.AppendAllText(file, " changed");
        verify = service.Verify(dir, manifest);
        Assert(verify.ChangedCount == 1, "修改后应识别为内容变化");

        File.Delete(file);
        verify = service.Verify(dir, manifest);
        Assert(verify.MissingCount == 1, "删除后应识别为缺失");
    }

    private static void TestImageBatch(string root)
    {
        string dir = NewDirectory(root, nameof(TestImageBatch));
        string source = Path.Combine(dir, "source.png");
        WriteTestPng(source, 200, 100);
        string output = Path.Combine(dir, "out");
        Directory.CreateDirectory(output);

        var options = new ImageBatchOptions
        {
            OutputFolder = output,
            Suffix = "_small",
            ResizeMode = ImageResizeMode.LongEdge,
            SizeValue = 100,
            Rotation = 90,
            Format = ImageOutputFormat.Png,
            JpegQuality = 85
        };
        var planner = new ImageBatchPlanner();
        var sources = planner.FindImages(dir, false);
        var plan = planner.Plan(sources, options);
        Assert(plan.Success && plan.Items.Count == 1, "图片规划应包含 1 张图片");
        Assert(plan.Items[0].TargetWidth == 50 && plan.Items[0].TargetHeight == 100,
            "缩放 100 长边并旋转 90° 后尺寸应为 50×100");

        var repository = NewRepository(root, nameof(TestImageBatch));
        var result = new ImageBatchExecutor(repository).Execute(plan, options);
        Assert(result.Success && File.Exists(plan.Items[0].OutputPath), "图片处理应生成输出");
        using (var stream = File.OpenRead(plan.Items[0].OutputPath))
        {
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            Assert(decoder.Frames[0].PixelWidth == 50 && decoder.Frames[0].PixelHeight == 100,
                "输出图片实际尺寸应为 50×100");
        }

        var undo = new UndoService(repository).Undo(repository.LoadAll().Entries.Single());
        Assert(undo.Success && !File.Exists(plan.Items[0].OutputPath), "图片处理输出应可回退删除");
    }

    private static void TestPhotoSimilarity(string root)
    {
        string dir = NewDirectory(root, nameof(TestPhotoSimilarity));
        string a = Path.Combine(dir, "a.png");
        string copy = Path.Combine(dir, "a-copy.png");
        string b = Path.Combine(dir, "b.png");
        WriteTestPatternPng(a, horizontal: true);
        File.Copy(a, copy);
        WriteTestPatternPng(b, horizontal: false);

        var result = PerceptualHash.Scan(dir, false, 90);
        Assert(result.Success && result.ScannedCount == 3, "相似照片扫描应读取 3 张图片");
        Assert(result.Groups.Count == 1 && result.Groups[0].Members.Count == 2,
            "相同图片应归入同一相似组，不同结构图片应分开");
        Assert(result.Groups[0].Keeper.Size > 0, "相似组应选出基准图片");
    }

    private static void WriteTestPatternPng(string path, bool horizontal)
    {
        const int width = 80;
        const int height = 60;
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * stride + x * 4;
                byte value = (byte)((horizontal ? x : y) * 255 / Math.Max(1, (horizontal ? width : height) - 1));
                pixels[i] = value;
                pixels[i + 1] = (byte)(255 - value);
                pixels[i + 2] = (byte)(value / 2);
                pixels[i + 3] = 255;
            }
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void TestMetadataCleanup(string root)
    {
        string dir = NewDirectory(root, nameof(TestMetadataCleanup));
        string source = Path.Combine(dir, "sample.docx");
        using (var archive = System.IO.Compression.ZipFile.Open(source, System.IO.Compression.ZipArchiveMode.Create))
        {
            var core = archive.CreateEntry("docProps/core.xml");
            using (var writer = new StreamWriter(core.Open(), new UTF8Encoding(false)))
            {
                writer.Write("""<?xml version="1.0" encoding="UTF-8"?><cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:creator>Alice</dc:creator><cp:lastModifiedBy>Alice</cp:lastModifiedBy></cp:coreProperties>""");
            }

            var app = archive.CreateEntry("docProps/app.xml");
            using var appWriter = new StreamWriter(app.Open(), new UTF8Encoding(false));
            appWriter.Write("""<?xml version="1.0" encoding="UTF-8"?><Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"><Company>ACME</Company><Manager>Alice</Manager></Properties>""");
        }

        string output = Path.Combine(dir, "out");
        var cleaner = new MetadataCleaner(NewRepository(root, nameof(TestMetadataCleanup)));
        var plan = cleaner.Plan(dir, false, output);
        Assert(plan.Success && plan.Items.Count == 1, "元数据扫描应找到一个 DOCX");
        var result = cleaner.Execute(plan);
        Assert(result.Success && File.Exists(plan.Items[0].OutputPath), "元数据清理应生成副本");
        using (var archive = System.IO.Compression.ZipFile.OpenRead(plan.Items[0].OutputPath))
        {
            using var core = new StreamReader(archive.GetEntry("docProps/core.xml")!.Open());
            using var app = new StreamReader(archive.GetEntry("docProps/app.xml")!.Open());
            string coreText = core.ReadToEnd();
            string appText = app.ReadToEnd();
            Assert(!coreText.Contains(">Alice<") && !appText.Contains(">ACME<") && !appText.Contains(">Alice<"),
                "作者/公司/修订信息应被清空");
        }
    }

    private static void TestFileSearch(string root)
    {
        string dir = NewDirectory(root, nameof(TestFileSearch));
        File.WriteAllText(Path.Combine(dir, "报告.pdf"), "pdf");
        File.WriteAllText(Path.Combine(dir, "说明.txt"), "txt");
        File.WriteAllText(Path.Combine(dir, "大文件.pdf"), new string('x', 2048));

        var result = FileSearchService.Search(new FileSearchOptions
        {
            Folder = dir,
            Pattern = "*.pdf",
            IncludeSubfolders = true
        });
        Assert(result.Success && result.Items.Count == 2, "通配符搜索应找到 2 个 PDF");
        Assert(result.Items.Any(x => x.Name == "报告.pdf"), "搜索应保留中文文件名");

        var filtered = FileSearchService.Search(new FileSearchOptions
        {
            Folder = dir,
            Pattern = "*.pdf",
            MinSizeMb = 0.001,
            MaxSizeMb = 1
        });
        Assert(filtered.Success && filtered.Items.Count == 1 && filtered.Items[0].Name == "大文件.pdf",
            "大小过滤应只保留大文件");
    }

    private static void TestFileReport(string root)
    {
        string dir = NewDirectory(root, nameof(TestFileReport));
        File.WriteAllText(Path.Combine(dir, "报告.txt"), "hello", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(dir, "data.csv"), "a,b\n1,2", new UTF8Encoding(false));

        var result = FileReportService.Scan(new FileReportOptions
        {
            Folder = dir,
            IncludeSubfolders = true,
            IncludeHash = true
        });
        Assert(result.Success && result.Rows.Count == 2, "清单扫描应包含 2 个文件");
        Assert(result.Rows.All(x => x.Hash.Length == 64), "清单应包含 SHA-256");

        string csv = Path.Combine(dir, "report.csv");
        FileReportService.WriteCsv(csv, result.Rows);
        string html = Path.Combine(dir, "report.html");
        FileReportService.WriteHtml(html, dir, result.Rows);
        Assert(File.ReadAllText(csv).Contains("报告.txt") && File.ReadAllText(html).Contains("LocalCraft 文件夹清单"),
            "CSV/HTML 清单应包含文件与标题");
    }

    private static void TestFileTools(string root)
    {
        string dir = NewDirectory(root, nameof(TestFileTools));
        string splitDir = Path.Combine(dir, "split");
        Directory.CreateDirectory(splitDir);
        string original = Path.Combine(splitDir, "data.bin");
        byte[] content = Enumerable.Range(0, 3000).Select(i => (byte)(i % 251)).ToArray();
        File.WriteAllBytes(original, content);
        var repository = NewRepository(root, nameof(TestFileTools));
        var splitService = new SplitMergeService(repository);
        var splitPlan = splitService.PlanSplit(new SplitOptions
        {
            SourceFile = original,
            OutputFolder = Path.Combine(splitDir, "parts"),
            Mode = SplitMode.BySize,
            SizeMb = 0.001
        });
        Assert(splitPlan.Success && splitPlan.Parts.Count >= 2, "大文件应拆分为多个分片");
        var splitResult = splitService.ExecuteSplit(splitPlan);
        Assert(splitResult.Success && splitPlan.Parts.All(x => File.Exists(x.TargetPath)), "拆分应生成分片");
        string merged = Path.Combine(splitDir, "merged.bin");
        var mergePlan = splitService.PlanMerge(Path.Combine(splitDir, "parts"), "*.part*", merged);
        var mergeResult = splitService.ExecuteMerge(mergePlan);
        Assert(mergeResult.Success && File.Exists(merged) && File.ReadAllBytes(merged).SequenceEqual(content),
            "合并结果应与原文件完全一致");
        var mergeUndo = new UndoService(repository).Undo(repository.LoadAll().Entries.Single(x => x.Journal.Kind == "merge"));
        Assert(mergeUndo.Success && !File.Exists(merged), "合并输出应可回退删除");

        string stateFile = Path.Combine(dir, "state.txt");
        File.WriteAllText(stateFile, "state");
        DateTime oldTime = new(2020, 1, 2, 3, 4, 5);
        File.SetLastWriteTime(stateFile, oldTime);
        var stateService = new FileStateService(repository);
        var timestampPlan = stateService.PlanTimestamps(new TimestampOptions
        {
            Folder = dir,
            IncludeSubfolders = false,
            FixedTime = new DateTime(2021, 5, 6, 7, 8, 9),
            ApplyCreated = false,
            ApplyModified = true
        });
        Assert(timestampPlan.Success && timestampPlan.Items.Count >= 1, "时间戳扫描应找到文件");
        var timestampResult = stateService.ExecuteTimestamps(timestampPlan, new TimestampOptions
        {
            Folder = dir,
            FixedTime = new DateTime(2021, 5, 6, 7, 8, 9),
            ApplyCreated = false,
            ApplyModified = true
        });
        Assert(timestampResult.Success && File.GetLastWriteTime(stateFile).Year == 2021, "时间戳应被修改");
        var timestampUndo = new UndoService(repository).Undo(repository.LoadAll().Entries.Single(x => x.Journal.Kind == "timestamp"));
        Assert(timestampUndo.Success && File.GetLastWriteTime(stateFile).Year == 2020, "时间戳应可回退");

        var attributePlan = stateService.PlanAttributes(new AttributeOptions
        {
            Folder = dir,
            IncludeSubfolders = false,
            SetReadOnly = true
        });
        var attributeResult = stateService.ExecuteAttributes(attributePlan, new AttributeOptions
        {
            Folder = dir,
            IncludeSubfolders = false,
            SetReadOnly = true
        });
        Assert(attributeResult.Success && File.GetAttributes(stateFile).HasFlag(FileAttributes.ReadOnly), "只读属性应被设置");
        var attributeUndo = new UndoService(repository).Undo(repository.LoadAll().Entries.Single(x => x.Journal.Kind == "attributes"));
        Assert(attributeUndo.Success && !File.GetAttributes(stateFile).HasFlag(FileAttributes.ReadOnly), "文件属性应可回退");

        string encodingSource = Path.Combine(dir, "utf8bom.txt");
        File.WriteAllText(encodingSource, "第一行\r\n第二行\r\n", new UTF8Encoding(true));
        string encodingOut = Path.Combine(dir, "encoded");
        var encoding = new EncodingConversionService(repository);
        var encodingPlan = encoding.Plan(new EncodingOptions
        {
            Folder = dir,
            IncludeSubfolders = false,
            Extensions = ".txt",
            OutputFolder = encodingOut,
            Target = TextEncodingTarget.Utf8,
            Newline = NewlineTarget.Lf
        });
        Assert(encodingPlan.Success && encodingPlan.Items.Count >= 1, "编码扫描应找到文本文件");
        var encodingResult = encoding.Execute(encodingPlan, new EncodingOptions
        {
            Folder = dir,
            OutputFolder = encodingOut,
            Target = TextEncodingTarget.Utf8,
            Newline = NewlineTarget.Lf
        });
        string converted = encodingPlan.Items.First(x => x.SourcePath == encodingSource).OutputPath;
        byte[] convertedBytes = File.ReadAllBytes(converted);
        Assert(encodingResult.Success && convertedBytes.Length >= 3 && !(convertedBytes[0] == 0xEF && convertedBytes[1] == 0xBB && convertedBytes[2] == 0xBF),
            "UTF-8 输出不应带 BOM");
        Assert(!Encoding.UTF8.GetString(convertedBytes).Contains('\r'), "换行应统一为 LF");

        string templateRoot = Path.Combine(dir, "template");
        Directory.CreateDirectory(templateRoot);
        var template = new FolderTemplateService(repository);
        var templatePlan = template.Plan(templateRoot, "项目/\n项目/文档/\n项目/说明.md");
        var templateResult = template.Execute(templatePlan);
        Assert(templateResult.Success && Directory.Exists(Path.Combine(templateRoot, "项目", "文档"))
               && File.Exists(Path.Combine(templateRoot, "项目", "说明.md")), "目录模板应创建目录与空文件");
        var templateUndo = new UndoService(repository).Undo(repository.LoadAll().Entries.Single(x => x.Journal.Kind == "template"));
        Assert(templateUndo.Success && !Directory.Exists(Path.Combine(templateRoot, "项目")), "目录模板应可回退");

        string left = Path.Combine(dir, "left.txt");
        string right = Path.Combine(dir, "right.txt");
        File.WriteAllText(left, "a\nb\nc\n");
        File.WriteAllText(right, "a\nx\nc\n");
        var diff = TextDiffService.Compare(left, right);
        Assert(diff.Success && diff.AddedCount == 1 && diff.RemovedCount == 1, "文本差异应识别新增和删除行");

        string img1 = Path.Combine(dir, "a.png");
        string img2 = Path.Combine(dir, "b.png");
        WriteTestPng(img1, 40, 20);
        WriteTestPng(img2, 30, 10);
        string stitched = Path.Combine(dir, "stitched.png");
        var stitch = new ImageStitchService(repository);
        var stitchPlan = stitch.Plan(new StitchOptions
        {
            Files = new[] { img1, img2 },
            OutputPath = stitched,
            Vertical = true,
            Gap = 4,
            MaxWidth = 100
        });
        Assert(stitchPlan.Success && stitchPlan.CanvasWidth == 40 && stitchPlan.CanvasHeight == 34,
            "纵向拼接画布尺寸应正确");
        var stitchResult = stitch.Execute(stitchPlan, new StitchOptions { Files = new[] { img1, img2 }, OutputPath = stitched, Vertical = true, Gap = 4 });
        Assert(stitchResult.Success && File.Exists(stitched), "拼接输出应存在");
    }

    private static ConversionEngineCatalog NewConversionCatalog(string root, string testName)
    {
        string engineRoot = Path.Combine(root, "_engines", testName);
        Directory.CreateDirectory(engineRoot);
        return new ConversionEngineCatalog(engineRoot);
    }

    private static List<ConversionWorkItem> ToWorkItems(ConversionPlan plan)
    {
        return plan.Items
            .Where(static x => x.IsExecutable)
            .Select(static x => new ConversionWorkItem(
                x.SourcePath,
                x.TargetPath,
                x.TargetFormat,
                x.EngineId ?? string.Empty))
            .ToList();
    }

    private static void WriteTestPng(string path, int width, int height)
    {
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 80;
            pixels[i + 1] = 150;
            pixels[i + 2] = 210;
            pixels[i + 3] = 255;
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static JournalRepository NewRepository(string root, string testName)
    {
        string dataRoot = Path.Combine(root, "_data", testName);
        Directory.CreateDirectory(dataRoot);
        return new JournalRepository(dataRoot);
    }

    private static string NewDirectory(string root, string testName)
    {
        string dir = Path.Combine(root, testName);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string CreateFile(string dir, string name, string content = "test")
    {
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private static IEnumerable<string> DirectoriesUnder(string directory)
    {
        return Directory.Exists(directory)
            ? Directory.EnumerateDirectories(directory)
            : Enumerable.Empty<string>();
    }

    private static void Assert(bool condition, string message)
    {
        _assertions++;
        if (!condition) Failures.Add(message);
    }
}
