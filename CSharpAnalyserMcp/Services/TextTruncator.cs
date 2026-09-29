namespace CSharpAnalyzerMcp.Services;

/// <summary>
/// 统一的文本截断工具：避免单次工具返回超大文案（方法体、XML 文档注释、摘要）造成 token 浪费。
/// 所有阈值集中在此处，便于统一调整。
/// </summary>
internal static class TextTruncator
{
    /// <summary>列表结果中类型摘要的上限（需要完整文档时改用 get_class_documentation）。</summary>
    public const int SummaryCharsInList = 300;

    /// <summary>方法体默认上限；get_method 的 maxBodyChars 可放大，传 0 表示不限制。</summary>
    public const int MethodBodyChars = 8000;

    /// <summary>XML 文档注释默认上限；get_class_documentation 的 maxXmlChars 可放大，传 0 表示不限制。</summary>
    public const int DocumentationXmlChars = 2000;

    /// <summary>摘要（summary）上限。</summary>
    public const int SummaryChars = 500;

    /// <summary>
    /// 按字符数截断文本：保留头部并追加英文标记；maxChars 小于等于 0 表示不截断。
    /// 截断点落在 UTF-16 代理对中间时自动回退一个字符，避免产生非法字符。
    /// </summary>
    public static string? Truncate(string? text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || maxChars <= 0 || text.Length <= maxChars) return text;

        var cut = maxChars;
        if (char.IsHighSurrogate(text[cut - 1])) cut--;

        return $"{text[..cut]}\n... [truncated: showing {cut} of {text.Length} chars]";
    }
}
