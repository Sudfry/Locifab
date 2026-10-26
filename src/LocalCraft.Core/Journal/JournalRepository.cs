using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocalCraft.Core.Journal;

public sealed class JournalRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _dataRoot;
    private readonly string _journalsDirectory;

    public JournalRepository(string? dataRoot = null)
    {
        _dataRoot = Path.GetFullPath(dataRoot ?? LocalCraftPaths.DataRoot);
        _journalsDirectory = Path.Combine(_dataRoot, "journals");
        BackupsDirectory = Path.Combine(_dataRoot, "backups");
    }

    public string DataRoot => _dataRoot;

    public string JournalsDirectory => _journalsDirectory;

    public string BackupsDirectory { get; }

    public string GetBackupDirectory(string journalId)
    {
        if (!Guid.TryParseExact(journalId, "N", out _))
        {
            throw new ArgumentException("日志 ID 格式无效。", nameof(journalId));
        }

        return Path.Combine(BackupsDirectory, journalId);
    }

    public string Save(OperationJournal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        if (string.IsNullOrWhiteSpace(journal.Id) || !Guid.TryParseExact(journal.Id, "N", out _))
        {
            throw new ArgumentException("日志 ID 必须是 32 位 Guid。", nameof(journal));
        }

        if (journal.CreatedAt == default)
        {
            journal.CreatedAt = DateTimeOffset.Now;
        }

        Directory.CreateDirectory(_journalsDirectory);
        string path = Path.Combine(
            _journalsDirectory,
            $"{journal.CreatedAt:yyyyMMdd-HHmmss}_{journal.Id[..8]}.json");

        string json = JsonSerializer.Serialize(journal, SerializerOptions);
        string tempPath = path + ".tmp";
        File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        try
        {
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* 忽略清理失败 */ }
            }
            throw;
        }

        return path;
    }

    public JournalLoadResult LoadAll()
    {
        var warnings = new List<string>();
        var entries = new List<JournalEntry>();

        if (!Directory.Exists(_journalsDirectory))
        {
            return new JournalLoadResult(entries, warnings);
        }

        foreach (string path in Directory.EnumerateFiles(_journalsDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                string json = File.ReadAllText(path);
                var journal = JsonSerializer.Deserialize<OperationJournal>(json, SerializerOptions);
                if (journal is null || journal.FormatVersion > 1)
                {
                    warnings.Add($"跳过无法识别的日志：{Path.GetFileName(path)}");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(journal.Id))
                {
                    warnings.Add($"跳过缺少 ID 的日志：{Path.GetFileName(path)}");
                    continue;
                }

                entries.Add(new JournalEntry(path, journal));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                warnings.Add($"日志读取失败：{Path.GetFileName(path)}（{ex.Message}）");
            }
        }

        entries.Sort(static (x, y) => y.Journal.CreatedAt.CompareTo(x.Journal.CreatedAt));
        return new JournalLoadResult(entries, warnings);
    }

    public OperationJournal Load(JournalEntry entry)
    {
        string json = File.ReadAllText(entry.FilePath);
        var journal = JsonSerializer.Deserialize<OperationJournal>(json, SerializerOptions)
                      ?? throw new InvalidOperationException("日志内容为空。");
        return journal;
    }

    public void DeleteBackupDirectory(string journalId)
    {
        string directory = GetBackupDirectory(journalId);
        if (!Directory.Exists(directory)) return;

        string fullBackupRoot = Path.GetFullPath(BackupsDirectory);
        string fullDirectory = Path.GetFullPath(directory);
        if (!fullDirectory.StartsWith(fullBackupRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("拒绝删除备份目录之外的路径。");
        }

        Directory.Delete(fullDirectory, recursive: true);
    }
}
