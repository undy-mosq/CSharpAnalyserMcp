namespace CSharpAnalyzerMcp.Models;

/// <summary>
/// 表示一个方法的完整信息（签名、注释、位置、方法体），用于 MCP 工具返回。
/// </summary>
public class MethodInfo
{
    public string ClassName { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public string MethodName { get; set; } = string.Empty;

    /// <summary>人类可读签名，例如 "public static void SetEffect(DuelModel.RoleAttributes owner, int stack)"。</summary>
    public string Signature { get; set; } = string.Empty;

    public string ReturnType { get; set; } = string.Empty;

    /// <summary>按声明顺序的参数类型显示名。</summary>
    public List<string> ParameterTypes { get; set; } = [];

    public string FilePath { get; set; } = string.Empty;

    /// <summary>方法声明起始行（1 基）。</summary>
    public int StartLine { get; set; }

    /// <summary>方法声明结束行（1 基）。</summary>
    public int EndLine { get; set; }

    /// <summary>是否有方法体（抽象方法、接口方法、extern 方法为 false）。</summary>
    public bool HasBody { get; set; }

    public bool IsAbstract { get; set; }
    public bool IsOverride { get; set; }

    /// <summary>是否为扩展方法（第一个参数带 this 修饰符）。</summary>
    public bool IsExtension { get; set; }

    /// <summary>原始 XML 文档注释；方法上没有文档注释时为 null。</summary>
    public string? DocumentationXml { get; set; }

    /// <summary>从 XML 文档注释中提取的 summary 文本。</summary>
    public string? Summary { get; set; }

    /// <summary>方法体源码（含大括号）或表达式体源码（含 "=>"）；无方法体时为 null。</summary>
    public string? Body { get; set; }
}
