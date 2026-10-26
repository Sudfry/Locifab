using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalCraft.App.Infrastructure;
using LocalCraft.App.Services;
using LocalCraft.Core.Journal;

namespace LocalCraft.App.ViewModels;

public sealed record DiskItem(string Path, string SizeText, string Type, long Bytes);
public sealed record ImageResult(string File, string Before, string After, string Status);
public sealed record BadImageItem(string Path, string Reason);
public sealed record TagItem(string FileName, string Tag, string Note);

public sealed class DirNode
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public long Bytes { get; set; }
    public string SizeText { get; set; } = string.Empty;
    public string FilesText { get; set; } = string.Empty;
    public ObservableCollection<DirNode> Children { get; set; } = new();
}

public sealed class ToolboxViewModel : ViewModelBase
{
    private readonly IDialogService _dialogService;
    private readonly JournalRepository _repository;
    private readonly List<TagItem> _allTags = new();
    private string _tagStorePath = string.Empty;

    private int _activeToolTab;
    private string _diskFolder = string.Empty;
    private string _diskSummary = "选择一个文件夹，只读分析空间占用。";
    private string _imageFolder = string.Empty;
    private string _imageOutput = string.Empty;
    private double _targetKb = 500;
    private string _imageSummary = "压缩到指定大小，输出 JPG 副本。";
    private string _integrityFolder = string.Empty;
    private string _integritySummary = "只读解码检查，不会修改原图。";
    private string _officeFolder = string.Empty;
    private string _officeOutput = string.Empty;
    private string _officeSummary = "提取 DOCX/XLSX/PPTX/PDF 内嵌图片。";
    private string _shortcutFolder = string.Empty;
    private string _shortcutOutput = string.Empty;
    private string _shortcutSummary = "为文件夹内每个项目创建 .lnk 快捷方式。";
    private string _tagFolder = string.Empty;
    private string _tagFilter = string.Empty;
    private TagItem? _selectedTag;
    private string _editTag = string.Empty;
    private string _editNote = string.Empty;
    private string _tagSummary = "标签保存为文件夹内 .locifab-tags.json。";

    public ToolboxViewModel(JournalRepository repository, IDialogService dialogService)
    {
        _repository = repository;
        _dialogService = dialogService;
        BrowseDiskCommand = new RelayCommand(() => PickFolderInto(ref _diskFolder, nameof(DiskFolder)));
        ScanDiskCommand = new AsyncRelayCommand(ScanDiskAsync);
        BrowseImageSourceCommand = new RelayCommand(() => PickFolderInto(ref _imageFolder, nameof(ImageFolder)));
        BrowseImageFileCommand = new RelayCommand(PickImageFile);
        BrowseImageOutputCommand = new RelayCommand(() => PickFolderInto(ref _imageOutput, nameof(ImageOutput)));
        CompressImagesCommand = new AsyncRelayCommand(CompressImagesAsync);
        BrowseIntegrityCommand = new RelayCommand(() => PickFolderInto(ref _integrityFolder, nameof(IntegrityFolder)));
        BrowseIntegrityFileCommand = new RelayCommand(PickIntegrityFile);
        CheckIntegrityCommand = new AsyncRelayCommand(CheckIntegrityAsync);
        MoveBadImagesCommand = new AsyncRelayCommand(MoveBadImagesAsync);
        BrowseOfficeCommand = new RelayCommand(() => PickFolderInto(ref _officeFolder, nameof(OfficeFolder)));
        BrowseOfficeFileCommand = new RelayCommand(PickOfficeFile);
        BrowseOfficeOutputCommand = new RelayCommand(() => PickFolderInto(ref _officeOutput, nameof(OfficeOutput)));
        ExtractOfficeCommand = new AsyncRelayCommand(ExtractOfficeAsync);
        BrowseShortcutCommand = new RelayCommand(() => PickFolderInto(ref _shortcutFolder, nameof(ShortcutFolder)));
        BrowseShortcutOutputCommand = new RelayCommand(() => PickFolderInto(ref _shortcutOutput, nameof(ShortcutOutput)));
        CreateShortcutsCommand = new AsyncRelayCommand(CreateShortcutsAsync);
        BrowseTagFolderCommand = new RelayCommand(() => PickFolderInto(ref _tagFolder, nameof(TagFolder)));
        BrowseTagFileCommand = new RelayCommand(PickTagFile);
        LoadTagsCommand = new AsyncRelayCommand(LoadTagsAsync);
        SaveTagCommand = new RelayCommand(SaveTag);
        OpenUndoCenterCommand = new RelayCommand(() => UndoCenterRequested?.Invoke(this, EventArgs.Empty));
    }

    public int ActiveToolTab
    {
        get => _activeToolTab;
        set
        {
            if (!SetProperty(ref _activeToolTab, Math.Clamp(value, 0, 5))) return;
            OnPropertyChanged(nameof(IsSpaceActive));
            OnPropertyChanged(nameof(IsImageCompressActive));
            OnPropertyChanged(nameof(IsImageCheckActive));
            OnPropertyChanged(nameof(IsEmbeddedActive));
            OnPropertyChanged(nameof(IsShortcutActive));
            OnPropertyChanged(nameof(IsTagsActive));
        }
    }

    public bool IsSpaceActive => _activeToolTab == 0;
    public bool IsImageCompressActive => _activeToolTab == 1;
    public bool IsImageCheckActive => _activeToolTab == 2;
    public bool IsEmbeddedActive => _activeToolTab == 3;
    public bool IsShortcutActive => _activeToolTab == 4;
    public bool IsTagsActive => _activeToolTab == 5;

    public event EventHandler? UndoCenterRequested;

    public ObservableCollection<DiskItem> DiskItems { get; } = new();
    public ObservableCollection<DirNode> DirNodes { get; } = new();
    public ObservableCollection<ImageResult> ImageItems { get; } = new();
    public ObservableCollection<BadImageItem> BadImages { get; } = new();
    public ObservableCollection<string> OfficeResults { get; } = new();
    public ObservableCollection<string> ShortcutResults { get; } = new();
    public ObservableCollection<TagItem> TagItems { get; } = new();

    public RelayCommand BrowseDiskCommand { get; }
    public AsyncRelayCommand ScanDiskCommand { get; }
    public RelayCommand BrowseImageSourceCommand { get; }
    public RelayCommand BrowseImageFileCommand { get; }
    public RelayCommand BrowseImageOutputCommand { get; }
    public AsyncRelayCommand CompressImagesCommand { get; }
    public RelayCommand BrowseIntegrityCommand { get; }
    public RelayCommand BrowseIntegrityFileCommand { get; }
    public AsyncRelayCommand CheckIntegrityCommand { get; }
    public AsyncRelayCommand MoveBadImagesCommand { get; }
    public RelayCommand BrowseOfficeCommand { get; }
    public RelayCommand BrowseOfficeFileCommand { get; }
    public RelayCommand BrowseOfficeOutputCommand { get; }
    public AsyncRelayCommand ExtractOfficeCommand { get; }
    public RelayCommand BrowseShortcutCommand { get; }
    public RelayCommand BrowseShortcutOutputCommand { get; }
    public AsyncRelayCommand CreateShortcutsCommand { get; }
    public RelayCommand BrowseTagFolderCommand { get; }
    public RelayCommand BrowseTagFileCommand { get; }
    public AsyncRelayCommand LoadTagsCommand { get; }
    public RelayCommand SaveTagCommand { get; }
    public RelayCommand OpenUndoCenterCommand { get; }

    public string DiskFolder { get => _diskFolder; set { if (SetProperty(ref _diskFolder, value)) { } } }
    public string DiskSummary { get => _diskSummary; private set => SetProperty(ref _diskSummary, value); }
    public string ImageFolder { get => _imageFolder; set => SetProperty(ref _imageFolder, value); }
    public string ImageOutput { get => _imageOutput; set => SetProperty(ref _imageOutput, value); }
    public double TargetKb { get => _targetKb; set => SetProperty(ref _targetKb, Math.Clamp(value, 0.1, 20000)); }
    public string ImageSummary { get => _imageSummary; private set => SetProperty(ref _imageSummary, value); }
    public string IntegrityFolder { get => _integrityFolder; set => SetProperty(ref _integrityFolder, value); }
    public string IntegritySummary { get => _integritySummary; private set => SetProperty(ref _integritySummary, value); }
    public string OfficeFolder { get => _officeFolder; set => SetProperty(ref _officeFolder, value); }
    public string OfficeOutput { get => _officeOutput; set => SetProperty(ref _officeOutput, value); }
    public string OfficeSummary { get => _officeSummary; private set => SetProperty(ref _officeSummary, value); }
    public string ShortcutFolder { get => _shortcutFolder; set => SetProperty(ref _shortcutFolder, value); }
    public string ShortcutOutput { get => _shortcutOutput; set => SetProperty(ref _shortcutOutput, value); }
    public string ShortcutSummary { get => _shortcutSummary; private set => SetProperty(ref _shortcutSummary, value); }
    public string TagFolder { get => _tagFolder; set => SetProperty(ref _tagFolder, value); }
    public string TagFilter { get => _tagFilter; set { if (SetProperty(ref _tagFilter, value)) FilterTags(); } }
    public TagItem? SelectedTag
    {
        get => _selectedTag;
        set
        {
            if (!SetProperty(ref _selectedTag, value)) return;
            EditTag = value?.Tag ?? string.Empty;
            EditNote = value?.Note ?? string.Empty;
        }
    }
    public string EditTag { get => _editTag; set => SetProperty(ref _editTag, value); }
    public string EditNote { get => _editNote; set => SetProperty(ref _editNote, value); }
    public string TagSummary { get => _tagSummary; private set => SetProperty(ref _tagSummary, value); }

    private void PickFolderInto(ref string field, string property)
    {
        string? folder = _dialogService.PickFolder(string.IsNullOrWhiteSpace(field) ? null : field);
        if (folder is null) return;
        field = folder;
        OnPropertyChanged(property);
    }

    public Task AddDroppedPathsAsync(string[] paths)
    {
        if (paths.Length == 0) return Task.CompletedTask;
        string path = paths[0];
        switch (ActiveToolTab)
        {
            case 0: DiskFolder = path; break;
            case 1: ImageFolder = path; break;
            case 2: IntegrityFolder = path; break;
            case 3: OfficeFolder = path; break;
            case 4: ShortcutFolder = Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path; break;
            case 5: TagFolder = path; break;
        }
        return Task.CompletedTask;
    }

    private void PickImageFile()
    {
        var files = _dialogService.PickFiles("图片|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.webp|所有文件|*.*", null);
        if (files.Count > 0) { ImageFolder = files[0]; OnPropertyChanged(nameof(ImageFolder)); }
    }

    private void PickIntegrityFile()
    {
        var files = _dialogService.PickFiles("图片|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.webp|所有文件|*.*", null);
        if (files.Count > 0) { IntegrityFolder = files[0]; OnPropertyChanged(nameof(IntegrityFolder)); }
    }

    private void PickOfficeFile()
    {
        var files = _dialogService.PickFiles("文档|*.docx;*.xlsx;*.pptx;*.pdf|所有文件|*.*", null);
        if (files.Count > 0) { OfficeFolder = files[0]; OnPropertyChanged(nameof(OfficeFolder)); }
    }

    private void PickTagFile()
    {
        var files = _dialogService.PickFiles("所有文件|*.*", null);
        if (files.Count > 0) { TagFolder = files[0]; OnPropertyChanged(nameof(TagFolder)); }
    }

    private async Task ScanDiskAsync()
    {
        if (!Directory.Exists(DiskFolder)) { DiskSummary = "请选择有效文件夹。"; return; }
        DiskSummary = "正在扫描…";
        DiskItems.Clear();
        DirNodes.Clear();
        var result = await Task.Run(() =>
        {
            var files = Directory.EnumerateFiles(DiskFolder, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true, IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
            }).Select(x => new FileInfo(x)).Where(x => x.Exists).ToList();
            return files;
        });
        long total = result.Sum(x => x.Length);
        foreach (var f in result.OrderByDescending(x => x.Length).Take(200))
            DiskItems.Add(new DiskItem(f.FullName, FormatSize(f.Length), f.Extension.ToLowerInvariant(), f.Length));
        await Task.Run(() =>
        {
            foreach (string dir in Directory.EnumerateDirectories(DiskFolder))
            {
                var node = BuildDirNode(dir);
                if (node.Bytes > 0) DirNodes.Add(node);
            }
        });
        DiskSummary = $"共 {result.Count} 个文件，合计 {FormatSize(total)}；上方按目录显示占用，下方显示最大 200 个文件。";
    }

    private static DirNode BuildDirNode(string path)
    {
        long bytes = 0;
        int fileCount = 0;
        var node = new DirNode
        {
            Name = string.IsNullOrEmpty(Path.GetFileName(path)) ? path : Path.GetFileName(path),
            FullPath = path
        };
        try
        {
            foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly))
            {
                try { bytes += new FileInfo(file).Length; fileCount++; } catch { }
            }
            foreach (string dir in Directory.EnumerateDirectories(path))
            {
                var child = BuildDirNode(dir);
                if (child.Bytes > 0) { node.Children.Add(child); bytes += child.Bytes; fileCount += child.Children.Count; }
            }
        }
        catch { }
        var ordered = node.Children.OrderByDescending(x => x.Bytes).ToList();
        node.Children.Clear();
        foreach (var child in ordered) node.Children.Add(child);
        node.Bytes = bytes;
        node.SizeText = FormatSize(bytes);
        node.FilesText = $"{fileCount} 项";
        return node;
    }

    private async Task CompressImagesAsync()
    {
        bool singleImage = File.Exists(ImageFolder);
        if (!singleImage && !Directory.Exists(ImageFolder)) { ImageSummary = "请选择图片文件或文件夹。"; return; }
        string imageRoot = singleImage ? Path.GetDirectoryName(ImageFolder) ?? ImageFolder : ImageFolder;
        string output = string.IsNullOrWhiteSpace(ImageOutput) ? Path.Combine(imageRoot, "压缩结果") : ImageOutput;
        Directory.CreateDirectory(output);
        ImageItems.Clear();
        var files = singleImage
            ? new List<string> { ImageFolder }
            : Directory.EnumerateFiles(ImageFolder, "*", SearchOption.TopDirectoryOnly)
                .Where(x => IsImage(Path.GetExtension(x))).ToList();
        long target = Math.Max(102, (long)Math.Round(TargetKb * 1024));
        var outputs = new List<(string Source, string Target)>();
        int ok = 0;
        foreach (string file in files)
        {
            try
            {
                byte[] data = await Task.Run(() => CompressToTarget(file, target));
                string targetPath = UniquePath(Path.Combine(output, Path.GetFileNameWithoutExtension(file) + ".jpg"));
                await File.WriteAllBytesAsync(targetPath, data);
                outputs.Add((file, targetPath));
                ImageItems.Add(new ImageResult(Path.GetFileName(file), FormatSize(new FileInfo(file).Length), FormatSize(data.Length), "已压缩"));
                ok++;
            }
            catch (Exception ex)
            {
                ImageItems.Add(new ImageResult(Path.GetFileName(file), "", "", "失败：" + ex.Message));
            }
        }
        if (outputs.Count > 0) SaveOutputJournal("image", "图片压缩", imageRoot, outputs);
        ImageSummary = $"完成 {ok}/{files.Count} 张，目标不超过 {TargetKb} KB，输出：{output}";
    }

    private static byte[] CompressToTarget(string path, long targetBytes)
    {
        using var stream = File.OpenRead(path);
        BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        BitmapSource source = decoder.Frames[0];
        byte[] best = Array.Empty<byte>();
        for (double scale = 1.0; scale >= 0.08; scale *= 0.8)
        {
            BitmapSource current = scale >= 0.999
                ? source
                : new TransformedBitmap(source, new ScaleTransform(scale, scale));
            if (current.PixelWidth < 8 || current.PixelHeight < 8) break;
            for (int quality = 90; quality >= 25; quality -= 5)
            {
                var encoder = new JpegBitmapEncoder { QualityLevel = quality };
                encoder.Frames.Add(BitmapFrame.Create(current));
                using var ms = new MemoryStream();
                encoder.Save(ms);
                byte[] data = ms.ToArray();
                if (best.Length == 0 || data.Length < best.Length) best = data;
                if (data.Length <= targetBytes) return data;
            }
        }
        return best;
    }

    private async Task CheckIntegrityAsync()
    {
        bool singleCheck = File.Exists(IntegrityFolder);
        if (!singleCheck && !Directory.Exists(IntegrityFolder)) { IntegritySummary = "请选择图片文件或文件夹。"; return; }
        IntegritySummary = "正在检查…";
        BadImages.Clear();
        var files = singleCheck
            ? new List<string> { IntegrityFolder }
            : Directory.EnumerateFiles(IntegrityFolder, "*", SearchOption.AllDirectories)
                .Where(x => IsImage(Path.GetExtension(x))).ToList();
        await Task.Run(() =>
        {
            foreach (string file in files)
            {
                try
                {
                    using var stream = File.OpenRead(file);
                    BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                }
                catch (Exception ex)
                {
                    BadImages.Add(new BadImageItem(file, ex.Message));
                }
            }
        });
        IntegritySummary = $"检查 {files.Count} 张，发现问题 {BadImages.Count} 张。";
    }

    private async Task MoveBadImagesAsync()
    {
        if (BadImages.Count == 0) return;
        string checkRoot = File.Exists(IntegrityFolder) ? Path.GetDirectoryName(IntegrityFolder) ?? IntegrityFolder : IntegrityFolder;
        string quarantine = UniqueDirectory(Path.Combine(checkRoot, "损坏隔离"));
        var moves = new List<(string Source, string Target)>();
        foreach (var item in BadImages.ToList())
        {
            if (!File.Exists(item.Path)) continue;
            string target = UniquePath(Path.Combine(quarantine, Path.GetFileName(item.Path)));
            Directory.CreateDirectory(quarantine);
            File.Move(item.Path, target);
            moves.Add((item.Path, target));
        }
        if (moves.Count > 0) SaveMoveJournal("move", "图片检查隔离", checkRoot, moves);
        BadImages.Clear();
        IntegritySummary = "损坏图片已移到隔离目录。";
        await Task.CompletedTask;
    }

    private async Task ExtractOfficeAsync()
    {
        bool singleFile = File.Exists(OfficeFolder);
        if (!singleFile && !Directory.Exists(OfficeFolder)) { OfficeSummary = "请选择文件或文件夹。"; return; }
        string root = singleFile ? Path.GetDirectoryName(OfficeFolder) ?? OfficeFolder : OfficeFolder;
        string output = string.IsNullOrWhiteSpace(OfficeOutput) ? Path.Combine(root, "内嵌图片") : OfficeOutput;
        Directory.CreateDirectory(output);
        OfficeResults.Clear();
        var files = singleFile
            ? new List<string> { OfficeFolder }
            : Directory.EnumerateFiles(OfficeFolder, "*", SearchOption.AllDirectories)
                .Where(x => Path.GetExtension(x).ToLowerInvariant() is ".docx" or ".xlsx" or ".pptx" or ".pdf").ToList();
        var outputs = new List<(string Source, string Target)>();
        int count = 0;
        foreach (string file in files)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is not (".docx" or ".xlsx" or ".pptx" or ".pdf")) continue;
            try
            {
                if (ext == ".pdf")
                {
                    var extracted = ExtractPdfJpegs(file, output);
                    foreach (string item in extracted) outputs.Add((file, item));
                    count += extracted.Count;
                    OfficeResults.Add($"{Path.GetFileName(file)}：{extracted.Count} 张");
                }
                else
                {
                    using var zip = ZipFile.OpenRead(file);
                    int n = 0;
                    foreach (var entry in zip.Entries.Where(x => x.FullName.Contains("/media/", StringComparison.OrdinalIgnoreCase)))
                    {
                        string target = UniquePath(Path.Combine(output, Path.GetFileNameWithoutExtension(file) + "_" + Path.GetFileName(entry.FullName)));
                        entry.ExtractToFile(target, true);
                        outputs.Add((file, target));
                        n++;
                    }
                    count += n;
                    OfficeResults.Add($"{Path.GetFileName(file)}：已提取 {n} 张");
                }
            }
            catch (Exception ex) { OfficeResults.Add($"{Path.GetFileName(file)}：失败 {ex.Message}"); }
        }
        if (outputs.Count > 0) SaveOutputJournal("convert", "内嵌图片提取", root, outputs);
        OfficeSummary = $"共提取 {count} 张内嵌图片到 {output}";
        await Task.CompletedTask;
    }

    private static List<string> ExtractPdfJpegs(string path, string output)
    {
        byte[] bytes = File.ReadAllBytes(path);
        var results = new List<string>();
        int start = 0;
        while (true)
        {
            int soi = Find(bytes, new byte[] { 0xFF, 0xD8, 0xFF }, start);
            if (soi < 0) break;
            int eoi = Find(bytes, new byte[] { 0xFF, 0xD9 }, soi + 3);
            if (eoi < 0) break;
            int length = eoi + 2 - soi;
            if (length > 2048)
            {
                string target = UniquePath(Path.Combine(output, Path.GetFileNameWithoutExtension(path) + $"_img{results.Count + 1}.jpg"));
                File.WriteAllBytes(target, bytes.AsSpan(soi, length).ToArray());
                results.Add(target);
            }
            start = eoi + 2;
        }
        return results;
    }

    private static int Find(byte[] data, byte[] pattern, int start)
    {
        for (int i = Math.Max(0, start); i <= data.Length - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
                if (data[i + j] != pattern[j]) { match = false; break; }
            if (match) return i;
        }
        return -1;
    }

    private async Task CreateShortcutsAsync()
    {
        if (!Directory.Exists(ShortcutFolder)) { ShortcutSummary = "请选择文件夹。"; return; }
        string output = string.IsNullOrWhiteSpace(ShortcutOutput) ? ShortcutFolder : ShortcutOutput;
        try { Directory.CreateDirectory(output); }
        catch (Exception ex) { ShortcutSummary = "无法创建输出目录：" + ex.Message; return; }
        ShortcutResults.Clear();
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) { ShortcutSummary = "当前系统不支持创建快捷方式。"; return; }
        dynamic shell = Activator.CreateInstance(shellType)!;
        var outputs = new List<(string Source, string Target)>();
        int count = 0;
        int failed = 0;
        foreach (string item in Directory.EnumerateFileSystemEntries(ShortcutFolder))
        {
            try
            {
                string name = Path.GetFileNameWithoutExtension(item);
                string target = UniquePath(Path.Combine(output, name + ".lnk"));
                dynamic link = shell.CreateShortcut(target);
                link.TargetPath = item;
                link.WorkingDirectory = Directory.Exists(item) ? item : Path.GetDirectoryName(item) ?? ShortcutFolder;
                link.Save();
                outputs.Add((item, target));
                ShortcutResults.Add(Path.GetFileName(target));
                count++;
            }
            catch (Exception ex)
            {
                failed++;
                ShortcutResults.Add($"{Path.GetFileName(item)}：失败 {ex.Message}");
            }
        }
        if (outputs.Count > 0) SaveOutputJournal("convert", "批量快捷方式", ShortcutFolder, outputs);
        ShortcutSummary = failed == 0
            ? $"已创建 {count} 个快捷方式到 {output}"
            : $"已创建 {count} 个，失败 {failed} 个；失败原因见列表。";
        await Task.CompletedTask;
    }

    private async Task LoadTagsAsync()
    {
        bool singleFile = File.Exists(TagFolder);
        if (!singleFile && !Directory.Exists(TagFolder)) { TagSummary = "请选择文件或文件夹。"; return; }
        string root = singleFile ? Path.GetDirectoryName(TagFolder) ?? TagFolder : TagFolder;
        _tagStorePath = Path.Combine(root, ".locifab-tags.json");
        _allTags.Clear();
        Dictionary<string, TagItem> map = new(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(_tagStorePath))
        {
            try
            {
                var loaded = await JsonSerializer.DeserializeAsync<List<TagItem>>(File.OpenRead(_tagStorePath));
                if (loaded is not null) foreach (var item in loaded) map[item.FileName] = item;
            }
            catch { }
        }
        var files = singleFile
            ? new List<string> { TagFolder }
            : Directory.EnumerateFiles(TagFolder, "*", SearchOption.TopDirectoryOnly).ToList();
        foreach (string file in files)
        {
            string name = Path.GetFileName(file);
            _allTags.Add(map.TryGetValue(name, out var tag) ? tag : new TagItem(name, "", ""));
        }
        FilterTags();
        TagSummary = $"已载入 {_allTags.Count} 个文件。";
    }

    private void FilterTags()
    {
        string filter = TagFilter.Trim();
        TagItems.Clear();
        foreach (var item in _allTags)
        {
            if (filter.Length > 0 &&
                !item.Tag.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                !item.Note.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                !item.FileName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;
            TagItems.Add(item);
        }
    }

    private void SaveTag()
    {
        if (SelectedTag is null || string.IsNullOrWhiteSpace(_tagStorePath)) return;
        int index = _allTags.FindIndex(x => string.Equals(x.FileName, SelectedTag.FileName, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return;
        var updated = new TagItem(SelectedTag.FileName, EditTag.Trim(), EditNote.Trim());
        _allTags[index] = updated;
        FilterTags();
        SelectedTag = TagItems.FirstOrDefault(x => string.Equals(x.FileName, updated.FileName, StringComparison.OrdinalIgnoreCase));
        try
        {
            string journalId = Guid.NewGuid().ToString("N");
            string? backupPath = null;
            if (File.Exists(_tagStorePath))
            {
                string backupDir = _repository.GetBackupDirectory(journalId);
                Directory.CreateDirectory(backupDir);
                backupPath = Path.Combine(backupDir, "tags.json.bak");
                File.Copy(_tagStorePath, backupPath, true);
            }
            File.WriteAllText(_tagStorePath,
                JsonSerializer.Serialize(_allTags, new JsonSerializerOptions { WriteIndented = true }));
            SaveTagJournal(journalId, _tagStorePath, backupPath);
            TagSummary = "标签与备注已保存。";
        }
        catch (Exception ex) { TagSummary = "保存失败：" + ex.Message; }
    }

    private void SaveOutputJournal(string kind, string display, string sourceDirectory,
        IReadOnlyList<(string Source, string Target)> outputs)
    {
        try
        {
            var journal = new OperationJournal
            {
                Id = Guid.NewGuid().ToString("N"), Kind = kind, DisplayName = display,
                SourceDirectory = sourceDirectory, CreatedAt = DateTimeOffset.Now,
                CompletedAt = DateTimeOffset.Now, Status = JournalStatus.Completed
            };
            int seq = 0;
            foreach (var (source, target) in outputs)
                journal.Operations.Add(new JournalOperation
                {
                    Sequence = seq++, SourcePath = source, TargetPath = target,
                    State = JournalOperationState.Completed, Note = display
                });
            _repository.Save(journal);
        }
        catch { }
    }

    private void SaveMoveJournal(string kind, string display, string sourceDirectory,
        IReadOnlyList<(string Source, string Target)> moves)
    {
        try
        {
            var journal = new OperationJournal
            {
                Id = Guid.NewGuid().ToString("N"), Kind = kind, DisplayName = display,
                SourceDirectory = sourceDirectory, CreatedAt = DateTimeOffset.Now,
                CompletedAt = DateTimeOffset.Now, Status = JournalStatus.Completed
            };
            int seq = 0;
            foreach (var (source, target) in moves)
                journal.Operations.Add(new JournalOperation
                {
                    Sequence = seq++, SourcePath = source, TargetPath = target,
                    State = JournalOperationState.Completed, Note = display
                });
            _repository.Save(journal);
        }
        catch { }
    }

    private void SaveTagJournal(string? id, string store, string? backupPath)
    {
        if (id is null) return;
        try
        {
            var journal = new OperationJournal
            {
                Id = id, Kind = "tags", DisplayName = "标签备注",
                SourceDirectory = Path.GetDirectoryName(store) ?? store,
                CreatedAt = DateTimeOffset.Now, CompletedAt = DateTimeOffset.Now,
                Status = JournalStatus.Completed
            };
            journal.Operations.Add(new JournalOperation
            {
                Sequence = 0, SourcePath = store, TargetPath = store,
                BackupPath = backupPath, State = JournalOperationState.Completed,
                Note = "标签备注"
            });
            _repository.Save(journal);
        }
        catch { }
    }

    private static bool IsImage(string extension) =>
        extension.ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".tif" or ".tiff" or ".webp";

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = bytes;
        int index = 0;
        while (value >= 1024 && index < units.Length - 1) { value /= 1024; index++; }
        return $"{value:0.#} {units[index]}";
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        string directory = Path.GetDirectoryName(path) ?? string.Empty;
        string name = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            string candidate = Path.Combine(directory, $"{name} ({i}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
    }

    private static string UniqueDirectory(string path)
    {
        if (!Directory.Exists(path)) return path;
        for (int i = 2; ; i++)
        {
            string candidate = path + $" ({i})";
            if (!Directory.Exists(candidate)) return candidate;
        }
    }
}
