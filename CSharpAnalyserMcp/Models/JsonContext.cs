using System.Text.Json.Serialization;
using CSharpAnalyzerMcp.Models;

namespace CSharpAnalyzerMcp;

[JsonSerializable(typeof(ClassInfo))]
[JsonSerializable(typeof(List<ClassInfo>))]
[JsonSerializable(typeof(ClassDocumentationInfo))]
[JsonSerializable(typeof(List<ClassDocumentationInfo>))]
[JsonSerializable(typeof(MethodInfo))]
[JsonSerializable(typeof(List<MethodInfo>))]
[JsonSerializable(typeof(MethodQueryResult))]
[JsonSerializable(typeof(WorkspaceInfo))]
internal partial class AppJsonContext : JsonSerializerContext
{
}