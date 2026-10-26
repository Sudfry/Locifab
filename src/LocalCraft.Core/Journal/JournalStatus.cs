namespace LocalCraft.Core.Journal;

public enum JournalStatus
{
    /// <summary>已写入日志，尚未开始或正在执行。</summary>
    Prepared,

    /// <summary>全部操作成功，可以回退。</summary>
    Completed,

    /// <summary>正在执行回退。</summary>
    Undoing,

    /// <summary>已回退。</summary>
    Undone,

    /// <summary>执行失败后已自动回滚，无需再次回退。</summary>
    RolledBack,

    /// <summary>执行或回退失败，需要人工检查。</summary>
    Failed
}
