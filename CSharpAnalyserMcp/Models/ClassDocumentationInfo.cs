namespace CSharpAnalyzerMcp.Models;

/// <summary>
/// 表示一个类型的 XML 文档注释查询结果，用于 MCP 工具返回。
/// </summary>
public class ClassDocumentationInfo
{
    public string ClassName { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;

    /// <summary>类型种类：class / static class / record / struct / record struct / interface / enum / delegate。</summary>
    public string Kind { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    /// <summary>类型声明起始行（1 基）。</summary>
    public int Line { get; set; }

    /// <summary>类型声明结束行（1 基）；partial 类型只表示第一个声明片段。</summary>
    public int EndLine { get; set; }

    /// <summary>原始 XML 文档注释；类型上没有文档注释时为 null。</summary>
    public string? DocumentationXml { get; set; }

    /// <summary>从 XML 文档注释中提取的 summary 文本。</summary>
    public string? Summary { get; set; }
}

