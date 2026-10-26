namespace LocalCraft.Core.Rename;

public sealed record RenameReplaceRule(string FindText, string ReplaceText);

/// <summary>
/// 批量重命名规则。所有规则均为纯文本处理，不做正则解析，结果可预测。
/// </summary>
public sealed record RenameOptions
{
    public string Prefix { get; init; } = string.Empty;

    public string Suffix { get; init; } = string.Empty;

    public string FindText { get; init; } = string.Empty;

    public string ReplaceText { get; init; } = string.Empty;

    /// <summary>按顺序执行的多组查找替换；可与 FindText/ReplaceText 同时使用。</summary>
    public IReadOnlyList<RenameReplaceRule> ReplaceRules { get; init; } = Array.Empty<RenameReplaceRule>();

    public bool MatchCase { get; init; }

    /// <summary>查找替换是否作用于扩展名。</summary>
    public bool IncludeExtension { get; init; }

    public bool UseNumbering { get; init; }

    public int StartNumber { get; init; } = 1;

    public int NumberPadding { get; init; } = 3;

    public string NumberSeparator { get; init; } = "_";

    /// <summary>编号置于文件名之前；false 时置于文件名之后。</summary>
    public bool NumberBeforeName { get; init; }

    /// <summary>目标已存在时是否允许覆盖（执行器会先备份原文件）。</summary>
    public bool AllowOverwrite { get; init; }

    public static RenameOptions Default { get; } = new();
}
