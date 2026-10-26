namespace LocalCraft.Core.Rename;

public enum RenameItemStatus
{
    /// <summary>可以安全执行。</summary>
    Ready,

    /// <summary>目标已存在，执行时会先备份原目标。</summary>
    Overwrite,

    /// <summary>目标冲突，不能执行。</summary>
    Conflict,

    /// <summary>规则未改变文件名。</summary>
    Unchanged,

    /// <summary>文件名或路径不合法。</summary>
    Invalid
}
