namespace CSharpAnalyzerMcp.Models;

/// <summary>
/// 方法查询结果：命中列表；未命中时给出候选签名以便纠正查询条件。
/// </summary>
public class MethodQueryResult
{
    /// <summary>匹配到的方法（可能多个重载）。</summary>
    public List<MethodInfo> Matches { get; set; } = [];

    /// <summary>未命中时给出的候选签名：优先同类同名方法，其次同类全部方法。</summary>
    public List<string> Candidates { get; set; } = [];
}
