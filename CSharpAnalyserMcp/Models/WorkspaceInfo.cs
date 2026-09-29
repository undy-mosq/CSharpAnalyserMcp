namespace CSharpAnalyzerMcp.Models;

/// <summary>
/// 表示 Roslyn 工作区的当前状态，用于刷新快照 / 设置解析基准后的结果反馈。
/// </summary>
public class WorkspaceInfo
{
    /// <summary>解决方案文件的绝对路径。</summary>
    public string SolutionPath { get; set; } = string.Empty;

    /// <summary>相对路径的解析基准目录，默认为解决方案文件所在目录。</summary>
    public string ResolveBaseDirectory { get; set; } = string.Empty;

    /// <summary>当前快照中的项目数量。</summary>
    public int ProjectCount { get; set; }

    /// <summary>本次调用是否重新加载了解决方案。</summary>
    public bool Reloaded { get; set; }

    /// <summary>本次重新加载耗时（毫秒）；未重新加载时为 0。</summary>
    public long ElapsedMilliseconds { get; set; }
}
