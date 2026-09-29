using CSharpAnalyzerMcp;
using CSharpAnalyzerMcp.Services;
using CSharpAnalyzerMcp.Tools;
using Microsoft.Build.Locator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.Text.Encodings.Web;
using System.Text.Json;

// 解析命令行参数：--workspace <path>
var workspacePath = args
    .SkipWhile(a => a != "--workspace")
    .Skip(1)
    .FirstOrDefault();

if (string.IsNullOrWhiteSpace(workspacePath))
{
    Console.Error.WriteLine("worred：请通过 --workspace 参数指定解决方案路径。");
    Console.Error.WriteLine("示例：CSharpAnalyzerMcp.exe --workspace D:\\MyProject\\MySolution.sln");
    return 1;
}

var builder = Host.CreateApplicationBuilder(args);

// 所有日志输出到 stderr，避免污染 MCP 的 stdio 通信
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

// 注册 Roslyn 工作区服务（单例，全局共享）
builder.Services.AddSingleton<RoslynWorkspaceService>();
// 配置 MCP 的 JSON 序列化选项：链式加入自定义 Context，
// 并改用宽松编码器——默认编码器会把中文、"、<、> 等转义成 \uXXXX，导致输出不可读且浪费 token
var serializerOptions = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions)
{
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    TypeInfoResolverChain =
    {
        AppJsonContext.Default,
        McpJsonUtilities.DefaultOptions.TypeInfoResolver!
    }
};
// 注册 MCP 服务器：下发服务级说明（kind 取值等），并显式注册工具类（泛型重载，兼容 Native AOT / 裁剪）
builder.Services
    .AddMcpServer(options => options.ServerInstructions = ServerInstructions.Text)
    .WithStdioServerTransport()
    .WithTools<CSharpAnalyzerTools>(serializerOptions: serializerOptions);

var app = builder.Build();

// 在启动 MCP 服务器之前，先初始化 Roslyn 工作区
var workspaceService = app.Services.GetRequiredService<RoslynWorkspaceService>();
await workspaceService.InitializeAsync(workspacePath);

// 启动 MCP 服务器
await app.RunAsync();
return 0;