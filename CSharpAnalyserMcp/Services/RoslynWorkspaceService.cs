using System.Diagnostics;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using CSharpAnalyzerMcp.Models;
using Microsoft.Extensions.Logging;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpAnalyzerMcp.Services;

/// <summary>
/// 封装 MSBuildWorkspace 的加载与 Roslyn 符号查询逻辑。
/// </summary>
public class RoslynWorkspaceService : IDisposable
{
    /// <summary>全局命名空间的别名（大小写不敏感），命中任一别名即视为全局命名空间。</summary>
    private static readonly string[] GlobalNamespaceAliases =
        [string.Empty, "global", "global namespace", "<global namespace>"];

    /// <summary>C# 类型关键字到元数据类型名的映射，用于方法参数类型匹配。</summary>
    private static readonly Dictionary<string, string> TypeAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["int"] = "Int32",
        ["uint"] = "UInt32",
        ["long"] = "Int64",
        ["ulong"] = "UInt64",
        ["short"] = "Int16",
        ["ushort"] = "UInt16",
        ["byte"] = "Byte",
        ["sbyte"] = "SByte",
        ["bool"] = "Boolean",
        ["string"] = "String",
        ["char"] = "Char",
        ["float"] = "Single",
        ["double"] = "Double",
        ["decimal"] = "Decimal",
        ["object"] = "Object",
        ["dynamic"] = "Object",
        ["nint"] = "IntPtr",
        ["nuint"] = "UIntPtr"
    };

    private readonly ILogger<RoslynWorkspaceService> _logger;
    private MSBuildWorkspace? _workspace;
    private Solution? _solution;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public RoslynWorkspaceService(ILogger<RoslynWorkspaceService> logger)
    {
        _logger = logger;
    }

    /// <summary>解决方案文件的绝对路径；未初始化时为 null。</summary>
    public string? SolutionPath { get; private set; }

    /// <summary>相对路径的解析基准目录，默认是解决方案文件所在目录。</summary>
    public string? ResolveBaseDirectory { get; private set; }

    /// <summary>
    /// 初始化工作区。必须在任何查询之前调用一次。
    /// </summary>
    public async Task InitializeAsync(string workspacePath)
    {
        if (_initialized) return;

        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;

            EnsureMSBuildRegistered();

            SolutionPath = Path.GetFullPath(workspacePath);
            ResolveBaseDirectory = Path.GetDirectoryName(SolutionPath);

            _workspace = CreateWorkspace();

            _logger.LogInformation("正在加载解决方案：{Path}", SolutionPath);
            _solution = await _workspace.OpenSolutionAsync(SolutionPath);
            _logger.LogInformation("解决方案加载完成，共 {Count} 个项目", _solution.Projects.Count());

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// 丢弃当前快照并重新加载解决方案。源码被外部修改后必须调用，否则查询结果仍是旧快照。
    /// </summary>
    public async Task<WorkspaceInfo> ReloadAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            EnsureInitialized();

            var stopwatch = Stopwatch.StartNew();

            // 重建工作区，避免复用基于旧源码的项目缓存
            _workspace?.Dispose();
            _workspace = CreateWorkspace();

            _logger.LogInformation("正在刷新解决方案：{Path}", SolutionPath);
            _solution = await _workspace.OpenSolutionAsync(SolutionPath!);

            stopwatch.Stop();
            _logger.LogInformation("刷新完成，共 {Count} 个项目，耗时 {Elapsed} 毫秒",
                _solution.Projects.Count(), stopwatch.ElapsedMilliseconds);

            return CreateWorkspaceInfo(reloaded: true, stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// 设置相对路径的解析基准目录；传 null 或空白则恢复为解决方案文件所在目录。
    /// </summary>
    public WorkspaceInfo SetResolveBaseDirectory(string? baseDirectory)
    {
        EnsureInitialized();

        ResolveBaseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? Path.GetDirectoryName(SolutionPath)
            : ResolveAbsolutePath(baseDirectory);

        _logger.LogInformation("解析基准目录已设置为：{Directory}", ResolveBaseDirectory);

        return CreateWorkspaceInfo(reloaded: false, elapsedMilliseconds: 0);
    }

    /// <summary>
    /// 把绝对路径或相对于解析基准目录的路径解析为绝对路径。
    /// </summary>
    public string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("路径不能为空。", nameof(path));

        return Path.IsPathRooted(path) ? Path.GetFullPath(path) : ResolveAbsolutePath(path);
    }

    /// <summary>
    /// 列出指定文件夹下的所有类型（含子文件夹），可按 kinds 过滤。
    /// folderPath 可为绝对路径，或相对于解析基准目录（默认解决方案所在目录）的路径。
    /// </summary>
    public async Task<List<ClassInfo>> ListClassesInFolderAsync(string folderPath, IReadOnlyList<string>? kinds = null)
    {
        var solution = GetSolutionSnapshot();

        var folderAbsolute = Path.TrimEndingDirectorySeparator(ResolvePath(folderPath));
        var results = new List<ClassInfo>();

        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;

            // 筛选出位于该文件夹（含子文件夹）下、且不是生成文件（obj/bin）的语法树
            var treesInFolder = compilation.SyntaxTrees
                .Where(t => IsFileInDirectory(t.FilePath, folderAbsolute) && !IsGeneratedPath(t.FilePath));

            foreach (var tree in treesInFolder)
            {
                var root = await tree.GetRootAsync();
                var semanticModel = compilation.GetSemanticModel(tree);

                // 类、结构体、接口、record（TypeDeclarationSyntax）以及 enum、delegate
                foreach (var declaration in root.DescendantNodes().Where(IsTypeDeclaration))
                {
                    if (semanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol symbol) continue;
                    if (!MatchesKindFilter(GetTypeKind(symbol), kinds)) continue;

                    results.Add(CreateClassInfo(symbol, declaration));
                }
            }
        }

        return results;
    }

    /// <summary>
    /// 列出指定命名空间下的类型（可按 kinds 过滤）。
    /// 命名空间名为空或为全局命名空间别名时，查询全局命名空间；
    /// includeSubNamespaces 为 true 时递归包含子命名空间中的类型。
    /// </summary>
    public async Task<List<ClassInfo>> ListClassesInNamespaceAsync(string? namespaceName, bool includeSubNamespaces = true, IReadOnlyList<string>? kinds = null)
    {
        var solution = GetSolutionSnapshot();

        var results = new List<ClassInfo>();

        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;

            // 从全局命名空间递归查找目标命名空间（支持全局命名空间别名）
            var targetNs = ResolveNamespace(compilation.GlobalNamespace, namespaceName);
            if (targetNs == null) continue;

            // 递归收集该命名空间（可选含子命名空间）下匹配 kinds 的类型
            CollectClasses(targetNs, results, includeSubNamespaces, kinds);
        }

        return results;
    }

    /// <summary>
    /// 根据类名获取类上声明的 XML 文档注释，返回解决方案中所有同名类的结果。
    /// maxXmlChars 为 XML 文档注释的截断上限，传 0 表示不截断。
    /// </summary>
    public async Task<List<ClassDocumentationInfo>> GetClassDocumentationAsync(string className, int maxXmlChars = TextTruncator.DocumentationXmlChars)
    {
        var solution = GetSolutionSnapshot();

        var results = new List<ClassDocumentationInfo>();

        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;

            // 遍历所有命名空间（含全局命名空间），收集全部同名类
            CollectClassDocumentation(compilation.GlobalNamespace, className, results, maxXmlChars);
        }

        return results;
    }

    /// <summary>
    /// 按类名（可含命名空间限定）、方法名与参数类型查询方法：
    /// 返回签名、XML 文档、源码位置与方法体；未命中时返回候选签名。
    /// maxBodyChars 为方法体截断上限，传 0 表示不截断。
    /// </summary>
    public async Task<MethodQueryResult> GetMethodAsync(string className, string? namespaceName, string methodName, IReadOnlyList<string>? parameterTypes, int maxBodyChars = TextTruncator.MethodBodyChars)
    {
        var solution = GetSolutionSnapshot();

        var result = new MethodQueryResult();
        var shortName = GetShortTypeName(className);
        var qualifier = ResolveQualifier(className, namespaceName);

        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;

            foreach (var type in FindTypes(compilation.GlobalNamespace, shortName, qualifier))
            {
                var matches = FindMethodSymbols(type, methodName, parameterTypes);

                if (matches.Count == 0)
                {
                    result.Candidates.AddRange(CollectCandidateSignatures(type, methodName));
                    continue;
                }

                foreach (var method in matches)
                {
                    var declaration = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
                    result.Matches.Add(CreateMethodInfo(method, declaration, maxBodyChars));
                }
            }
        }

        result.Candidates = result.Candidates.Distinct().ToList();
        return result;
    }

    // ==================== 私有辅助方法 ====================

    private void EnsureMSBuildRegistered()
    {
        // 关键：先注册 MSBuild 定位器，再创建 Workspace
        if (MSBuildLocator.IsRegistered) return;

        MSBuildLocator.RegisterDefaults();
        _logger.LogInformation("MSBuildLocator 已注册");
    }

    private MSBuildWorkspace CreateWorkspace()
    {
        var workspace = MSBuildWorkspace.Create();

        // 订阅失败事件，便于排查项目加载问题
        workspace.WorkspaceFailed += (sender, args) =>
        {
            // 输出到 stderr，避免污染 MCP 的 stdio 通信
            Console.Error.WriteLine($"[WorkspaceFailed] {args.Diagnostic.Kind}: {args.Diagnostic.Message}");
        };

        return workspace;
    }

    private void EnsureInitialized()
    {
        if (!_initialized || _solution == null)
            throw new InvalidOperationException("工作区尚未初始化，请先调用 InitializeAsync。");
    }

    /// <summary>
    /// 获取当前快照；未初始化时抛出异常。
    /// </summary>
    private Solution GetSolutionSnapshot()
    {
        EnsureInitialized();
        return _solution!;
    }

    /// <summary>
    /// 把相对于解析基准目录的路径解析为绝对路径。
    /// </summary>
    private string ResolveAbsolutePath(string relativePath)
    {
        var baseDirectory = ResolveBaseDirectory
            ?? throw new InvalidOperationException("工作区尚未初始化，请先调用 InitializeAsync。");

        return Path.GetFullPath(Path.Combine(baseDirectory, relativePath));
    }

    private WorkspaceInfo CreateWorkspaceInfo(bool reloaded, long elapsedMilliseconds) => new()
    {
        SolutionPath = SolutionPath ?? string.Empty,
        ResolveBaseDirectory = ResolveBaseDirectory ?? string.Empty,
        ProjectCount = _solution?.Projects.Count() ?? 0,
        Reloaded = reloaded,
        ElapsedMilliseconds = elapsedMilliseconds
    };

    /// <summary>
    /// 判断文件是否位于指定目录（含子目录）内。
    /// </summary>
    private static bool IsFileInDirectory(string filePath, string directory)
    {
        if (string.IsNullOrEmpty(filePath) ||
            !filePath.StartsWith(directory, StringComparison.OrdinalIgnoreCase) ||
            filePath.Length == directory.Length)
        {
            return false;
        }

        var nextChar = filePath[directory.Length];
        return nextChar == Path.DirectorySeparatorChar || nextChar == Path.AltDirectorySeparatorChar;
    }

    /// <summary>
    /// 判断命名空间名是否表示全局命名空间（空、global、global namespace、&lt;global namespace&gt;）。
    /// </summary>
    private static bool IsGlobalNamespaceName(string? namespaceName)
    {
        if (namespaceName == null) return true;

        var trimmed = namespaceName.Trim();
        return GlobalNamespaceAliases.Any(alias =>
            string.Equals(alias, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 解析目标命名空间符号；传入全局命名空间别名时直接返回全局命名空间。
    /// </summary>
    private static INamespaceSymbol? ResolveNamespace(INamespaceSymbol globalNamespace, string? namespaceName)
    {
        if (IsGlobalNamespaceName(namespaceName)) return globalNamespace;

        return FindNamespace(globalNamespace, namespaceName!.Trim());
    }

    /// <summary>
    /// 递归查找指定名称的命名空间符号。
    /// </summary>
    private static INamespaceSymbol? FindNamespace(INamespaceSymbol root, string targetName)
    {
        if (root.ToDisplayString() == targetName) return root;

        foreach (var child in root.GetNamespaceMembers())
        {
            var found = FindNamespace(child, targetName);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>
    /// 递归收集命名空间（含子命名空间）中所有与指定类型名匹配的类型的文档注释。
    /// </summary>
    private static void CollectClassDocumentation(INamespaceSymbol ns, string className, List<ClassDocumentationInfo> results, int maxXmlChars)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            if (!string.Equals(type.Name, className, StringComparison.Ordinal)) continue;

            var declaration = GetSourceDeclaration(type);
            if (declaration == null) continue;

            var (startLine, endLine) = GetLineRange(declaration);

            results.Add(new ClassDocumentationInfo
            {
                ClassName = type.Name,
                Namespace = type.ContainingNamespace.ToDisplayString(),
                Kind = GetTypeKind(type),
                FilePath = declaration.SyntaxTree.FilePath,
                Line = startLine,
                EndLine = endLine,
                DocumentationXml = TextTruncator.Truncate(type.GetDocumentationCommentXml(), maxXmlChars),
                Summary = TextTruncator.Truncate(GetDocumentationSummary(type), TextTruncator.SummaryChars)
            });
        }

        foreach (var child in ns.GetNamespaceMembers())
        {
            CollectClassDocumentation(child, className, results, maxXmlChars);
        }
    }

    /// <summary>
    /// 递归收集命名空间下的类型（可按 kinds 过滤）。
    /// </summary>
    private static void CollectClasses(INamespaceSymbol ns, List<ClassInfo> results, bool includeSubNamespaces, IReadOnlyList<string>? kinds)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            if (!MatchesKindFilter(GetTypeKind(type), kinds)) continue;

            var declaration = GetSourceDeclaration(type);
            if (declaration == null) continue;

            results.Add(CreateClassInfo(type, declaration));
        }

        if (includeSubNamespaces)
        {
            foreach (var child in ns.GetNamespaceMembers())
            {
                CollectClasses(child, results, includeSubNamespaces: true, kinds);
            }
        }
    }

    /// <summary>
    /// 判断语法节点是否是类型声明（类、结构体、接口、record、枚举、委托）。
    /// </summary>
    private static bool IsTypeDeclaration(SyntaxNode node) =>
        node is TypeDeclarationSyntax or EnumDeclarationSyntax or DelegateDeclarationSyntax;

    /// <summary>
    /// 判断路径是否位于生成目录（obj / bin），用于排除源生成文件，避免类型重复出现。
    /// </summary>
    private static bool IsGeneratedPath(string filePath) =>
        filePath.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase) ||
        filePath.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
        filePath.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase) ||
        filePath.Contains("/bin/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 取类型在源码中的第一个声明节点；无源码声明或位于生成目录时返回 null。
    /// </summary>
    private static SyntaxNode? GetSourceDeclaration(INamedTypeSymbol type)
    {
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            var node = reference.GetSyntax();
            var filePath = node.SyntaxTree.FilePath;

            if (string.IsNullOrEmpty(filePath) || IsGeneratedPath(filePath)) continue;

            return node;
        }

        return null;
    }

    /// <summary>
    /// 判断类型种类：class / static class / record / struct / record struct / interface / enum / delegate。
    /// </summary>
    private static string GetTypeKind(INamedTypeSymbol type) => type.TypeKind switch
    {
        TypeKind.Class => type.IsRecord ? "record" : type.IsStatic ? "static class" : "class",
        TypeKind.Struct => type.IsRecord ? "record struct" : "struct",
        TypeKind.Interface => "interface",
        TypeKind.Enum => "enum",
        TypeKind.Delegate => "delegate",
        _ => type.TypeKind.ToString().ToLowerInvariant()
    };

    /// <summary>
    /// 按 kinds 过滤类型种类（大小写不敏感、可多值）。
    /// kinds 为空时等价于 ["class"]，即所有类（含 static class 与 record）。
    /// </summary>
    private static bool MatchesKindFilter(string kind, IReadOnlyList<string>? kinds)
    {
        if (kinds == null || kinds.Count == 0)
            return kind is "class" or "static class" or "record";

        foreach (var raw in kinds)
        {
            var value = raw.Trim().ToLowerInvariant();

            var matched = value switch
            {
                "" => false,
                "all" => true,
                "class" => kind is "class" or "static class" or "record",
                "static" or "static class" => kind == "static class",
                "record" => kind is "record" or "record struct",
                "struct" => kind is "struct" or "record struct",
                _ => string.Equals(value, kind, StringComparison.OrdinalIgnoreCase)
            };

            if (matched) return true;
        }

        return false;
    }

    /// <summary>
    /// 取语法节点在文件中的起止行（1 基）；使用 Span，不含前置 XML 文档注释。
    /// </summary>
    private static (int StartLine, int EndLine) GetLineRange(SyntaxNode node)
    {
        var lineSpan = node.GetLocation().GetLineSpan();
        return (lineSpan.StartLinePosition.Line + 1, lineSpan.EndLinePosition.Line + 1);
    }

    /// <summary>
    /// 由类型符号与声明节点构造 ClassInfo（摘要按列表上限截断）。
    /// </summary>
    private static ClassInfo CreateClassInfo(INamedTypeSymbol type, SyntaxNode declaration)
    {
        var (startLine, endLine) = GetLineRange(declaration);
        var declarationCount = type.DeclaringSyntaxReferences.Length;

        return new ClassInfo
        {
            ClassName = type.Name,
            Namespace = type.ContainingNamespace.ToDisplayString(),
            Kind = GetTypeKind(type),
            FilePath = declaration.SyntaxTree.FilePath,
            Line = startLine,
            EndLine = endLine,
            IsPartial = declarationCount > 1,
            DeclarationCount = declarationCount,
            DocumentationSummary = TextTruncator.Truncate(GetDocumentationSummary(type), TextTruncator.SummaryCharsInList)
        };
    }

    /// <summary>
    /// 从 XML 文档注释中提取 summary 文本。
    /// </summary>
    private static string? GetDocumentationSummary(ISymbol symbol)
    {
        var xml = symbol.GetDocumentationCommentXml();
        if (string.IsNullOrWhiteSpace(xml)) return null;

        try
        {
            var doc = System.Xml.Linq.XDocument.Parse(xml);
            return doc.Root?.Element("summary")?.Value?.Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 从 className 中拆出类型短名（去掉命名空间限定）。
    /// </summary>
    private static string GetShortTypeName(string className)
    {
        var trimmed = className.Trim();
        var lastDot = trimmed.LastIndexOf('.');
        return lastDot >= 0 ? trimmed[(lastDot + 1)..] : trimmed;
    }

    /// <summary>
    /// 确定命名空间限定条件：className 中的限定优先，其次 namespaceName（空字符串表示全局命名空间）；都未给则返回 null。
    /// </summary>
    private static string? ResolveQualifier(string className, string? namespaceName)
    {
        var trimmed = className.Trim();
        var lastDot = trimmed.LastIndexOf('.');
        if (lastDot >= 0) return trimmed[..lastDot];

        return namespaceName?.Trim();
    }

    /// <summary>
    /// 递归查找匹配短名与命名空间限定的所有类型。
    /// </summary>
    private static IEnumerable<INamedTypeSymbol> FindTypes(INamespaceSymbol ns, string shortName, string? qualifier)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            if (!string.Equals(type.Name, shortName, StringComparison.Ordinal)) continue;
            if (qualifier != null && !MatchesQualifier(type, qualifier)) continue;

            yield return type;
        }

        foreach (var child in ns.GetNamespaceMembers())
        {
            foreach (var found in FindTypes(child, shortName, qualifier))
            {
                yield return found;
            }
        }
    }

    /// <summary>
    /// 判断类型是否位于指定命名空间下（空字符串表示全局命名空间）。
    /// </summary>
    private static bool MatchesQualifier(INamedTypeSymbol type, string qualifier) =>
        qualifier.Length == 0
            ? type.ContainingNamespace.IsGlobalNamespace
            : string.Equals(type.ContainingNamespace.ToDisplayString(), qualifier, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 在类型中按方法名与参数类型查找方法（只查该类自身声明的方法，不含继承成员）。
    /// </summary>
    private static List<IMethodSymbol> FindMethodSymbols(INamedTypeSymbol type, string methodName, IReadOnlyList<string>? parameterTypes)
    {
        var candidates = type.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary && string.Equals(m.Name, methodName, StringComparison.Ordinal))
            .ToList();

        if (candidates.Count == 0 || parameterTypes == null || parameterTypes.Count == 0) return candidates;

        var expected = parameterTypes.Select(NormalizeTypeName).ToList();

        // 依次放宽：严格 → 忽略泛型实参 → 允许只给出前若干个参数（可选参数场景）
        var attempts = new[]
        {
            (IgnoreGenericArguments: false, AllowPrefix: false),
            (IgnoreGenericArguments: true, AllowPrefix: false),
            (IgnoreGenericArguments: false, AllowPrefix: true),
            (IgnoreGenericArguments: true, AllowPrefix: true)
        };

        foreach (var (ignoreGenericArguments, allowPrefix) in attempts)
        {
            var matched = candidates
                .Where(m => ParametersMatch(m, expected, ignoreGenericArguments, allowPrefix))
                .ToList();

            if (matched.Count > 0) return matched;
        }

        return [];
    }

    private static bool ParametersMatch(IMethodSymbol method, IReadOnlyList<string> expected, bool ignoreGenericArguments, bool allowPrefix)
    {
        var actual = method.Parameters;

        if (allowPrefix)
        {
            if (expected.Count > actual.Length) return false;
        }
        else if (expected.Count != actual.Length)
        {
            return false;
        }

        for (var i = 0; i < expected.Count; i++)
        {
            var actualName = ignoreGenericArguments ? actual[i].Type.Name : actual[i].Type.ToDisplayString();
            if (!string.Equals(NormalizeTypeName(actualName), expected[i], StringComparison.OrdinalIgnoreCase)) return false;
        }

        return true;
    }

    /// <summary>
    /// 归一化类型名：去空白、去尾部数组/可空标记、去命名空间前缀、C# 别名映射；泛型实参递归归一化。
    /// </summary>
    private static string NormalizeTypeName(string typeName)
    {
        var text = new string(typeName.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (text.Length == 0) return text;

        var suffix = string.Empty;
        while (text.Length > 0)
        {
            if (text.EndsWith("[]", StringComparison.Ordinal))
            {
                suffix = "[]" + suffix;
                text = text[..^2];
            }
            else if (text.EndsWith("?", StringComparison.Ordinal) || text.EndsWith("*", StringComparison.Ordinal))
            {
                suffix = text[^1] + suffix;
                text = text[..^1];
            }
            else
            {
                break;
            }
        }

        var genericStart = text.IndexOf('<');
        var genericEnd = text.EndsWith(">", StringComparison.Ordinal) ? text.Length - 1 : -1;
        var hasGenericArguments = genericStart >= 0 && genericEnd > genericStart;

        var head = hasGenericArguments ? text[..genericStart] : text;
        var genericArguments = hasGenericArguments ? text[(genericStart + 1)..genericEnd] : string.Empty;

        var lastDot = head.LastIndexOf('.');
        var shortName = lastDot >= 0 ? head[(lastDot + 1)..] : head;
        if (TypeAliases.TryGetValue(shortName, out var alias)) shortName = alias;

        if (genericArguments.Length == 0) return shortName + suffix;

        var arguments = SplitGenericArguments(genericArguments).Select(NormalizeTypeName);
        return shortName + "<" + string.Join(",", arguments) + ">" + suffix;
    }

    /// <summary>
    /// 按顶层逗号切分泛型实参（忽略嵌套泛型内部的逗号）。
    /// </summary>
    private static IEnumerable<string> SplitGenericArguments(string arguments)
    {
        var depth = 0;
        var start = 0;

        for (var i = 0; i < arguments.Length; i++)
        {
            switch (arguments[i])
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    depth--;
                    break;
                case ',' when depth == 0:
                    yield return arguments[start..i];
                    start = i + 1;
                    break;
            }
        }

        yield return arguments[start..];
    }

    /// <summary>
    /// 构造人类可读的方法签名（修饰符 + 返回类型 + 名称 + 参数列表）。
    /// </summary>
    private static string BuildMethodSignature(IMethodSymbol method, SyntaxNode? declaration)
    {
        var modifiers = declaration is MethodDeclarationSyntax syntax && syntax.Modifiers.Count > 0
            ? string.Join(' ', syntax.Modifiers) + " "
            : string.Empty;

        var parameters = string.Join(", ", method.Parameters.Select(p => $"{p.Type.ToDisplayString()} {p.Name}"));

        return $"{modifiers}{method.ReturnType.ToDisplayString()} {method.Name}({parameters})";
    }

    /// <summary>
    /// 由方法符号与声明节点构造 MethodInfo（方法体、XML 文档注释与摘要按阈值截断）。
    /// </summary>
    private static MethodInfo CreateMethodInfo(IMethodSymbol method, SyntaxNode? declaration, int maxBodyChars)
    {
        var syntax = declaration as MethodDeclarationSyntax;
        var rawBody = syntax?.Body?.ToFullString() ?? syntax?.ExpressionBody?.ToFullString();
        var (startLine, endLine) = declaration == null ? (0, 0) : GetLineRange(declaration);

        return new MethodInfo
        {
            ClassName = method.ContainingType.Name,
            Namespace = method.ContainingNamespace.ToDisplayString(),
            MethodName = method.Name,
            Signature = BuildMethodSignature(method, declaration),
            ReturnType = method.ReturnType.ToDisplayString(),
            ParameterTypes = method.Parameters.Select(p => p.Type.ToDisplayString()).ToList(),
            FilePath = declaration?.SyntaxTree.FilePath ?? string.Empty,
            StartLine = startLine,
            EndLine = endLine,
            HasBody = rawBody != null,
            IsAbstract = method.IsAbstract,
            IsOverride = method.IsOverride,
            IsExtension = method.IsExtensionMethod,
            DocumentationXml = TextTruncator.Truncate(method.GetDocumentationCommentXml(), TextTruncator.DocumentationXmlChars),
            Summary = TextTruncator.Truncate(GetDocumentationSummary(method), TextTruncator.SummaryChars),
            Body = TextTruncator.Truncate(rawBody, maxBodyChars)
        };
    }

    /// <summary>
    /// 收集候选签名以便调用方纠正查询条件：优先同类同名方法，其次同类全部方法。
    /// </summary>
    private static List<string> CollectCandidateSignatures(INamedTypeSymbol type, string methodName, int limit = 50)
    {
        var methods = type.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary)
            .ToList();

        var sameName = methods.Where(m => string.Equals(m.Name, methodName, StringComparison.Ordinal)).ToList();
        var candidates = sameName.Count > 0 ? sameName : methods;

        return candidates
            .Take(limit)
            .Select(m => BuildMethodSignature(m, m.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()))
            .ToList();
    }

    public void Dispose()
    {
        _workspace?.Dispose();
        _initLock.Dispose();
    }
}