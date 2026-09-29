using System.ComponentModel;
using CSharpAnalyzerMcp.Models;
using CSharpAnalyzerMcp.Services;
using ModelContextProtocol.Server;

namespace CSharpAnalyzerMcp.Tools;

/// <summary>
/// 暴露给 AI 助手的 C# 代码分析工具集。
/// </summary>
[McpServerToolType]
public class CSharpAnalyzerTools
{
    private readonly RoslynWorkspaceService _workspace;

    public CSharpAnalyzerTools(RoslynWorkspaceService workspace)
    {
        _workspace = workspace;
    }

    [McpServerTool(UseStructuredContent = true, ReadOnly = true), Description("List C# types (classes, structs, interfaces, records, enums, delegates) in a folder and its subfolders.")]
    public async Task<List<ClassInfo>> ListClassesInFolder(
        [Description("Folder path: absolute, or relative to the resolve base directory (default: the solution directory). Example: 'src/Services'")] string folderPath,
        [Description("Type kinds to include, case-insensitive: class | static class | record | struct | record struct | interface | enum | delegate | all. Default: class (see server instructions).")] string[]? kinds = null)
    {
        return await _workspace.ListClassesInFolderAsync(folderPath, kinds);
    }

    [McpServerTool(UseStructuredContent = true, ReadOnly = true), Description("List C# types declared in a namespace.")]
    public async Task<List<ClassInfo>> ListClassesInNamespace(
        [Description("Namespace name, e.g. 'MyApp.Services'. Empty or 'global' means the global namespace.")] string? namespaceName = null,
        [Description("Include types from sub-namespaces. Default: true.")] bool includeSubNamespaces = true,
        [Description("Type kinds to include, case-insensitive: class | static class | record | struct | record struct | interface | enum | delegate | all. Default: class (see server instructions).")] string[]? kinds = null)
    {
        return await _workspace.ListClassesInNamespaceAsync(namespaceName, includeSubNamespaces, kinds);
    }

    [McpServerTool(UseStructuredContent = true, ReadOnly = true), Description("Get the XML documentation of a type by name. Returns every matching type in the solution. Long XML is truncated unless maxXmlChars is 0.")]
    public async Task<List<ClassDocumentationInfo>> GetClassDocumentation(
        [Description("Type name, e.g. 'UserService'.")] string className,
        [Description("Maximum characters of the XML documentation to return (default 2000). Pass 0 for no limit.")] int maxXmlChars = TextTruncator.DocumentationXmlChars)
    {
        return await _workspace.GetClassDocumentationAsync(className, maxXmlChars);
    }

    [McpServerTool(UseStructuredContent = true, ReadOnly = true), Description("Get a method by type, method name and parameter types: returns signature, XML docs, source location and method body. When nothing matches, candidate signatures are returned. Long method bodies are truncated unless maxBodyChars is 0.")]
    public async Task<MethodQueryResult> GetMethod(
        [Description("Type name; may be namespace-qualified, e.g. 'DuelModel.RoleAttributes'.")] string className,
        [Description("Method name, e.g. 'SetEffect'.")] string methodName,
        [Description("Parameter type names in declaration order, e.g. ['RoleAttributes','int']. Namespace prefixes and C# aliases are ignored. Leave empty to match all overloads by name.")] string[]? parameterTypes = null,
        [Description("Namespace of the type, used when the name is ambiguous. Empty string means the global namespace.")] string? namespaceName = null,
        [Description("Maximum characters of the method body to return (default 8000). Pass 0 for no limit.")] int maxBodyChars = TextTruncator.MethodBodyChars)
    {
        return await _workspace.GetMethodAsync(className, namespaceName, methodName, parameterTypes, maxBodyChars);
    }

    [McpServerTool(UseStructuredContent = true, Idempotent = true), Description("Reload the solution snapshot (projects and source). Call it after source files change, otherwise queries use a stale snapshot.")]
    public async Task<WorkspaceInfo> ReloadSolution()
    {
        return await _workspace.ReloadAsync();
    }

    [McpServerTool(UseStructuredContent = true, Idempotent = true), Description("Set the base directory used to resolve relative paths (default: the directory of the solution file).")]
    public WorkspaceInfo SetResolveBasePath(
        [Description("Base directory: absolute, or relative to the current base directory. Leave empty to restore the default (solution directory).")] string? basePath = null)
    {
        return _workspace.SetResolveBaseDirectory(basePath);
    }
}
