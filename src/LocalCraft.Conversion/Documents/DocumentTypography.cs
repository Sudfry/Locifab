namespace LocalCraft.Conversion.Documents;

/// <summary>导出排版参数。由导出页在本地设置，PDF/Word 渲染时读取。</summary>
public static class DocumentTypography
{
    public static string FontFamily { get; set; } = "Microsoft YaHei, SimSun, Segoe UI";
    public static double BodyFontSize { get; set; } = 12.5;
    public static double LineHeightFactor { get; set; } = 1.7;
    public static double PageMargin { get; set; } = 72;

    public static void Reset()
    {
        FontFamily = "Microsoft YaHei, SimSun, Segoe UI";
        BodyFontSize = 12.5;
        LineHeightFactor = 1.7;
        PageMargin = 72;
    }
}
