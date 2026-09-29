namespace CSharpAnalyzerMcp.Models;

/// <summary>
/// 表示一个 C# 类型（类 / 结构体 / 接口 / 枚举 / 委托）的摘要信息，用于 MCP 工具返回。
/// </summary>
public class ClassInfo
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

    /// <summary>是否存在多个声明片段（partial）。</summary>
    public bool IsPartial { get; set; }

    /// <summary>声明片段数量。</summary>
    public int DeclarationCount { get; set; }

    public string? DocumentationSummary { get; set; }
}
