# 开发与发布（CSharpAnalyserMcp）

本文档面向仓库维护者：本地构建调试、打包发布、CI 发版流程，以及踩过的坑。
**使用说明请看 [README](../README.zh-CN.md)**。

## 1. 本地构建与调试

```powershell
# 构建
dotnet build CSharpAnalyserMcp\CSharpAnalyserMcp.csproj

# 直接从源码运行（"--" 之后的内容传给服务器）
dotnet run --project CSharpAnalyserMcp -- --workspace D:\MyProject\MySolution.sln
```

按 `F5` 可调试服务器本身，`.vscode/launch.json` 已带示例 `--workspace`。

## 2. 打包与发布（本地）

```powershell
# Native AOT 单文件可执行程序（需要 MSVC 链接器 -> 用 Developer PowerShell / 命令提示符）
dotnet publish CSharpAnalyserMcp\CSharpAnalyserMcp.csproj -c Release -r win-x64

# 没有 C++ 工具链时的替代方案：框架依赖发布
dotnet publish CSharpAnalyserMcp\CSharpAnalyserMcp.csproj -c Release -r win-x64 -p:PublishAot=false
```

产物路径：`CSharpAnalyserMcp\bin\Release\net8.0\win-x64\publish\CSharpAnalyserMcp.exe`。
本机的 AOT 可执行程序只供直接用 exe 的客户端使用；NuGet 上的工具体验是另一条路径（见 §4）。

```powershell
# 本地打工具包并验证包结构（-p:Version 显式传，别依赖仓库里的默认值）
dotnet pack CSharpAnalyserMcp\CSharpAnalyserMcp.csproj -c Release -p:Version=1.0.2 -o .\nupkg
```

打包验证要点（解包 `nupkg\CSharpAnalyserMcp.<版本>.nupkg`）：
- `<packageTypes>` 同时含 `DotnetTool` 与 `McpServer`；
- 有 `tools/net8.0/any/DotnetToolSettings.xml`，其中命令名为 `csharp-analyser-mcp`；
- 有 **`.mcp/server.json`**（NuGet 的“MCP Server”标签页靠它生成 VS Code 一键配置）。

## 3. 项目结构

| 路径 | 作用 |
| --- | --- |
| `CSharpAnalyserMcp/Program.cs` | 解析 `--workspace`，配置日志 / JSON / MCP，加载工作区并启动 stdio 服务器 |
| `CSharpAnalyserMcp/Tools/CSharpAnalyzerTools.cs` | 对外暴露的 6 个 MCP 工具 |
| `CSharpAnalyserMcp/Tools/ServerInstructions.cs` | `initialize` 阶段下发的服务级说明 |
| `CSharpAnalyserMcp/Services/RoslynWorkspaceService.cs` | `MSBuildWorkspace` 加载 / 刷新与全部 Roslyn 查询 |
| `CSharpAnalyserMcp/Services/TextTruncator.cs` | 统一的截断阈值与工具方法 |
| `CSharpAnalyserMcp/Models/` | 结果 DTO（`ClassInfo`、`MethodInfo` 等）与 `AppJsonContext`（AOT 用的源生成 JSON） |
| `CSharpAnalyserMcp/.mcp/server.json` | MCP Registry 清单；同时被 `<None Include=".mcp/server.json" Pack="true" .../>` 打进 nupkg |
| `.github/workflows/main.yml` | 打 tag 或手动触发时：发布到 NuGet + 发布到 MCP Registry |
| `.vscode/launch.json`、`.vscode/task.json` | 按 `F5` 调试服务器，附带示例 `--workspace` |

## 4. 发版流程（CI）

**触发方式**
1. 打 tag 推送：`git tag v1.0.2 && git push origin v1.0.2`（`v*.*.*` 会触发）；
2. 手动触发：Actions → *Publish to NuGet* → Run workflow，填 `version`。**只补 MCP Registry 条目（不新增 NuGet 版本）时，就填一个已存在于 NuGet 的版本号**（例如 `1.0.1`）：pack 后 `--skip-duplicate` 会跳过推送，上架检查通过后直接发布 registry。

**tag 必须是合法 NuGet 版本**

| tag | 结果 |
| --- | --- |
| `v1.0.2` | ✅ 正式版 |
| `v1.0.1-a` | ✅ 预发行版（`1.0.1-a`） |
| `v1.0.1a` | ❌ 工作流在“Resolve version”步骤直接 `::error::` 退出 |

**工作流步骤**
1. `Resolve version`：取 tag（或手动输入）→ 去掉前缀 `v` → 正则校验 → 写入环境变量 **`PKG_VERSION`**；
2. `Sync the version into server.json`：把版本写进 `.mcp/server.json`（**必须在 pack 之前**，因为这个文件既进包又发 registry）；
3. `Restore` → `Build` → `Pack`（都带 `-p:Version=$PKG_VERSION`）；
4. `Publish to NuGet`（`--skip-duplicate`）；
5. `Wait until the package is listed on NuGet.org`：轮询 gallery 页面 200（见 §5 为何不用 flat-container）；
6. `mcp-publisher login github-oidc` → `validate` → `publish`（位置参数传 server.json，失败重试 3 次）。

**与 registry 相关的两个约定**
- 包内嵌 `.mcp/server.json`：NuGet 包页的 “MCP Server” 标签页靠它生成 VS Code 配置；缺了会显示 *The VS Code MCP server configuration entry cannot be generated because this package does not include a server.json file.*
- README 末尾的 `<!-- mcp-name: io.github.undy-mosq/CSharpAnalyserMcp -->`：MCP Registry 用它校验 NuGet 包归属，**必须与 registry 清单里的 `name` 完全一致**；这个 README 就是被打进 nupkg 的那份（`PackageReadmeFile`）。

**mcp-publisher 备忘**
- `publish [server.json]` 是**位置参数**：它没有 `--file`，写 `--file=...` 会被当成未知参数忽略，然后去找 `./server.json` 并报 `server.json not found`；
- `login github-oidc` 需要 workflow 的 `permissions: id-token: write`；
- `status` 子命令可把某个版本标记为 `deprecated` / `deleted`。

## 5. 已知坑与排查

**① 环境变量 `VERSION` 会污染 MSBuild 属性**
MSBuild 会把环境变量导入为属性，且属性名大小写不敏感，因此 `VERSION=...` 等价于全局设置 `$(Version)`。如果它不是合法版本串（例如 tag `v1.0.1a` → `1.0.1a`），`dotnet restore` 会失败而且报得很含糊：

```
error MSB4181: “RestoreTask”任务返回 false 但未记录错误。
```
所以工作流用 `PKG_VERSION`，版本只在 build/pack 用 `-p:Version=` 显式传入。

**② nuget.org 的索引/缓存会滞后（中国大陆尤其明显）**
`api.nuget.org` 从中国大陆访问会被 302 到 `nuget.azure.cn`，而且 **registration（元数据）与 flat-container 索引可能明显落后于 gallery 页面**，于是：

```
dotnet tool install -g CSharpAnalyserMcp --version 1.0.1
在 NuGet 源 https://api.nuget.org/v3/index.json, ... 中找不到 包 csharpanalysermcp 的版本 1.0.1。
```
（`dotnet tool install` 解析版本走 registration，所以清缓存也未必有用。）

排查用：
```powershell
curl.exe -s https://api.nuget.org/v3/registration5-semver1/csharpanalysermcp/index.json   # 元数据索引
curl.exe -s https://api.nuget.org/v3-flatcontainer/csharpanalysermcp/index.json            # 版本列表
curl.exe -s https://www.nuget.org/packages/CSharpAnalyserMcp/1.0.1                         # gallery 页面（最先更新）
```

绕过的办法（本地文件夹源由客户端自己解析 .nupkg，完全不走 nuget.org 索引）：
```powershell
curl.exe -L -o "$env:TEMP\CSharpAnalyserMcp.1.0.1.nupkg" https://www.nuget.org/api/v2/package/CSharpAnalyserMcp/1.0.1
dotnet tool install -g --add-source "$env:TEMP" CSharpAnalyserMcp --version 1.0.1
```
客户端本地 HTTP 缓存 30 分钟，可用 `dotnet nuget locals http-cache --clear`（或安装时加 `--no-http-cache`）。

**③ CI 的“等上架”为什么轮询 gallery 而不是 flat-container**
flat-container 的 blob 路径（`.../{id}/{version}/{id}.nuspec`）一旦在包还没摄取时被请求过，**404 会被边缘节点长时间缓存**（实测已发布数小时的版本仍返回 404），所以同一 URL 反复轮询永远不会恢复。gallery 页面是动态渲染的，包一上架就 200，因此改用它（并加 `?cb=` 击穿缓存）。

**④ 常见构建报错**
- 发布时报 `error : Platform linker not found`：缺 MSVC 链接器 / Windows SDK，改用 Visual Studio *Developer PowerShell*（含“使用 C++ 的桌面开发”）或加 `-p:PublishAot=false`；
- 重新构建时报 `error MSB3026 / MSB3027 ... apphost.exe ... being used by another process`：MCP 客户端拉起的服务器实例还在运行，锁住了 `bin\Debug\net8.0\CSharpAnalyserMcp.exe`，先停掉它（或改输出目录）。

## 6. 发布前检查清单

1. `dotnet pack -p:Version=<版本>` → 解包确认包类型（`DotnetTool` + `McpServer`）、工具命令名、以及 `.mcp/server.json` 都在；
2. 工具形态实测：`dotnet tool install -g --add-source .\nupkg CSharpAnalyserMcp --version <版本>`，再 `csharp-analyser-mcp --workspace <不存在的路径>` 应快速失败并给非零退出码；
3. MCP 握手实测：向进程 stdin 依次写 `initialize`、`notifications/initialized`、`tools/list`，确认 stdout 上是合法 JSON-RPC（stderr 只放日志）；
4. `mcp-publisher validate CSharpAnalyserMcp/.mcp/server.json` 通过；
5. 打 tag 前确认 `README.md` 末尾的 `mcp-name` 与 registry 清单 `name` 一致。

