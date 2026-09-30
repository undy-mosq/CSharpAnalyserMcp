[English](README.md) | **简体中文**

# CSharpAnalyserMcp

一个只读的 [Model Context Protocol](https://modelcontextprotocol.io)（MCP）服务器，让 AI 助手通过 Roslyn 浏览任意 C# 解决方案。

服务器启动时用 `MSBuildWorkspace` 把解决方案加载成一份缓存的 Roslyn 快照，随后回答结构化查询：每个类型声明在哪个文件哪几行、类型的 XML 文档、方法的签名 / 文档 / 位置 / 方法体。这样助手可以只取出真正需要的那一小段源码，而不必把整份文件塞进上下文。

- 传输方式：stdio（`StdioServerTransport`），stdout 只走协议报文，所有日志写 stderr。
- 解决方案路径由命令行参数 `--workspace <解决方案文件路径>` 指定；解决方案在 MCP 握手**之前**加载，因此路径错误会立即失败：stderr 输出错误信息并返回非零退出码。
- 所有工具都声明结构化输出（`UseStructuredContent`），结果以 MCP `structuredContent` 返回，JSON 字段为 camelCase。
- 分发方式：以 .NET 工具包 **CSharpAnalyserMcp** 发布到 NuGet（命令为 `csharp-analyser-mcp`），并收录于官方 [MCP Registry](https://registry.modelcontextprotocol.io)，名称为 `io.github.undy-mosq/CSharpAnalyserMcp`。

## 安装

### NuGet .NET 工具（推荐）

```powershell
dotnet tool install --global CSharpAnalyserMcp
csharp-analyser-mcp --workspace D:\MyProject\MySolution.sln

# 升级 / 卸载
dotnet tool update --global CSharpAnalyserMcp
dotnet tool uninstall --global CSharpAnalyserMcp
```

请**全局安装**：`--local` 安装只在清单里加一条记录，`csharp-analyser-mcp` 不会出现在 `PATH` 上，MCP 客户端无法启动它。

### 用 `dnx` 免安装运行（.NET SDK 10 及以上）

```powershell
# "--" 用于分隔 dnx 自身的选项与转发给服务器的参数
dnx CSharpAnalyserMcp --yes -- --workspace D:\MyProject\MySolution.sln
```

### 从源码构建

```powershell
git clone https://github.com/undy-mosq/CSharpAnalyserMcp
dotnet build CSharpAnalyserMcp\CSharpAnalyserMcp.csproj
dotnet run --project CSharpAnalyserMcp -- --workspace D:\MyProject\MySolution.sln
```

## 环境要求

| 用途 | 要求 |
| --- | --- |
| 运行已发布的工具（`dotnet tool install --global`） | `PATH` 中存在 .NET SDK 8.0 及以上（工具包目标框架为 `net8.0`） |
| 用 `dnx` 免安装运行 | .NET SDK 10.0 及以上——`dnx` 随 SDK 一起提供 |
| 从源码构建 / 运行 | .NET SDK 8.0 及以上（项目目标框架为 `net8.0`） |
| 加载被分析的解决方案 | 能打开该解决方案的 MSBuild 工具链：Visual Studio 2022 或 VS Build Tools（`Microsoft.Build.Locator` 会注册已安装的最新实例）；SDK 风格项目用 .NET SDK 本身即可 |

## 客户端配置

服务器只需要一个参数 `--workspace`，指向解决方案文件。

### 已安装的 .NET 工具

全局工具的 shim 位于 `%USERPROFILE%\.dotnet\tools`。MCP 客户端是自己直接拉起进程的，只能看到它继承到的 `PATH`，因此配置里请**用绝对路径**指向该 shim（把 `%USERPROFILE%` 展开，例如 `C:\Users\you\.dotnet\tools\csharp-analyser-mcp.exe`）：

Claude Desktop（`claude_desktop_config.json`）：

```json
{
  "mcpServers": {
    "csharp-analyser": {
      "command": "C:\\Users\\you\\.dotnet\\tools\\csharp-analyser-mcp.exe",
      "args": ["--workspace", "D:\\MyProject\\MySolution.sln"]
    }
  }
}
```

VS Code（`.vscode/mcp.json`）：

```json
{
  "servers": {
    "csharp-analyser": {
      "type": "stdio",
      "command": "C:\\Users\\you\\.dotnet\\tools\\csharp-analyser-mcp.exe",
      "args": ["--workspace", "D:\\MyProject\\MySolution.sln"]
    }
  }
}
```

裸命令名（`"command": "csharp-analyser-mcp"`）只在客户端继承到的 `PATH` 已包含该目录时可用；如果客户端是在安装工具之前启动的，它仍是旧环境，改完 `PATH` 后请重启客户端。

### 用 `dnx` 免安装运行

```json
{
  "servers": {
    "csharp-analyser": {
      "type": "stdio",
      "command": "dnx",
      "args": ["CSharpAnalyserMcp", "--yes", "--", "--workspace", "D:\\MyProject\\MySolution.sln"]
    }
  }
}
```

在包标识符后追加 `@<版本>` 即可固定版本。[NuGet 包页](https://www.nuget.org/packages/CSharpAnalyserMcp)的 **MCP Server** 标签页提供了相同的 JSON 便于复制。`dnx` 是 `.cmd` 批处理 shim，若客户端无法 spawn 它，可改用等价的 `dotnet` 写法：`"command": "dotnet"` + `"args": ["tool", "exec", "CSharpAnalyserMcp", "--yes", "--", "--workspace", "D:\\MyProject\\MySolution.sln"]`。

### 已发布的独立可执行程序

VS Code（`.vscode/mcp.json`）：

```json
{
  "servers": {
    "csharp-analyser": {
      "type": "stdio",
      "command": "C:\\Tools\\csharp-analyser\\CSharpAnalyserMcp.exe",
      "args": ["--workspace", "D:\\MyProject\\MySolution.sln"]
    }
  }
}
```

### 直接从源码运行

```json
{
  "servers": {
    "csharp-analyser": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "D:\\MyProject\\CSharpAnalyserMcp\\CSharpAnalyserMcp\\CSharpAnalyserMcp.csproj",
        "--",
        "--workspace",
        "D:\\MyProject\\MySolution.sln"
      ]
    }
  }
}
```

在 VS Code 中按 `F5` 可调试服务器本身；`.vscode/launch.json` 已经带了 `--workspace` 参数，改成你自己的解决方案即可。

## 工具列表

| 工具 | MCP 标注 | 作用 |
| --- | --- | --- |
| `list_classes_in_folder` | 只读、结构化输出 | 列出某个文件夹（含子文件夹）内的类型声明 |
| `list_classes_in_namespace` | 只读、结构化输出 | 列出某个命名空间内的类型声明（可选含子命名空间） |
| `get_class_documentation` | 只读、结构化输出 | 返回所有同名的类型的 XML 文档注释 |
| `get_method` | 只读、结构化输出 | 返回方法的签名、XML 文档、位置与方法体 |
| `reload_solution` | 幂等、结构化输出 | 源码变更后重建 Roslyn 快照 |
| `set_resolve_base_path` | 幂等、结构化输出 | 修改相对路径的解析基准目录 |

### `list_classes_in_folder`

```json
{ "folderPath": "src/Services", "kinds": ["class", "interface"] }
```

| 参数 | 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `folderPath` | string，必填 | — | 绝对路径，或相对于解析基准目录（默认为解决方案所在目录）的路径。 |
| `kinds` | string[] | `["class"]` | 需要包含的类型种类，见[类型种类与 `kinds`](#类型种类与-kinds)。 |

`bin`/`obj` 下的文件会被跳过。`partial` 类型只报告第一个声明片段的位置，同时给出 `isPartial` 与 `declarationCount`。

### `list_classes_in_namespace`

```json
{ "namespaceName": "MyApp.Services", "includeSubNamespaces": true, "kinds": ["all"] }
```

| 参数 | 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `namespaceName` | string | 全局命名空间 | 命名空间名，例如 `MyApp.Services`。空字符串、`global`、`global namespace`、`<global namespace>` 均表示全局命名空间。 |
| `includeSubNamespaces` | bool | `true` | 是否同时包含子命名空间中的类型。 |
| `kinds` | string[] | `["class"]` | 需要包含的类型种类。 |

### `get_class_documentation`

```json
{ "className": "UserService", "maxXmlChars": 2000 }
```

| 参数 | 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `className` | string，必填 | — | 类型名，例如 `UserService`。精确匹配（区分大小写），且在所有项目 / 命名空间中查找，因此同名类型会全部返回。 |
| `maxXmlChars` | int | `2000` | 每个类型的原始 XML 文档注释上限；`0` 表示不限制。 |

### `get_method`

```json
{ "className": "DuelModel.RoleAttributes", "methodName": "SetEffect", "parameterTypes": ["RoleAttributes", "int"] }
```

| 参数 | 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `className` | string，必填 | — | 类型名，可带命名空间限定（`DuelModel.RoleAttributes`）。 |
| `methodName` | string，必填 | — | 方法名，例如 `SetEffect`。 |
| `parameterTypes` | string[] | 全部重载 | 按声明顺序排列的参数类型名，例如 `["RoleAttributes", "int"]`。命名空间前缀与 C# 别名会被忽略。 |
| `namespaceName` | string | 从 `className` 推断 | 类型所在命名空间，用于消歧。空字符串表示全局命名空间。 |
| `maxBodyChars` | int | `8000` | 方法体字符上限；`0` 表示不限制。 |

匹配规则：

- 只在该类型自身声明的成员中查找（普通方法；不含继承成员、构造函数与属性）。
- 参数类型比较忽略大小写与空白、忽略命名空间前缀；C# 别名会映射为元数据类型名（`int` → `Int32`，`string` → `String` 等）。
- 匹配逐步放宽：严格匹配 → 忽略泛型实参 → 允许只给出前若干个参数（可选参数场景）→ 两者同时放宽。
- 未命中时 `matches` 为空，`candidates` 给出纠正线索：优先同类同名重载，其次该类型的全部方法。

### `reload_solution`

无参数。丢弃当前快照并重新打开解决方案，使后续查询看到磁盘上的最新文件。返回 `WorkspaceInfo`，其中 `reloaded: true`，并带 `elapsedMilliseconds`。

### `set_resolve_base_path`

```json
{ "basePath": "src" }
```

| 参数 | 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `basePath` | string | 解决方案所在目录 | 绝对路径，或相对于当前基准目录的路径。留空则恢复默认（解决方案文件所在目录）。 |

## 类型种类与 `kinds`

`kind` 为小写且互斥，取值：`class`、`static class`、`record`、`struct`、`record struct`、`interface`、`enum`、`delegate`。只有类可以是 `static`，所以 `class` 与 `static class` 是两种不同取值；结构体、枚举、接口、委托没有静态形式。

`kinds` 筛选不区分大小写、支持多值（不传 `kinds` 等价于 `["class"]`）：

| `kinds` 取值 | 匹配范围 |
| --- | --- |
| 不传 / `class` | 所有类，含静态类与 record |
| `static` / `static class` | 仅静态类 |
| `record` | record 与 record struct |
| `struct` | struct 与 record struct |
| `record struct` | 仅 record struct |
| `enum` / `interface` / `delegate` | 对应种类 |
| `all` | 所有类型 |

该表也会在 `initialize` 握手时作为服务级说明下发给客户端，模型无需猜测可用取值。

## 返回结构

所有字段均以 camelCase 序列化。

`ClassInfo`（`list_classes_in_folder` / `list_classes_in_namespace` 返回）：

| 字段 | 说明 |
| --- | --- |
| `className` | 类型简单名 |
| `namespace` | 所在命名空间 |
| `kind` | 上表中的某个种类值 |
| `filePath` | 声明所在源文件的绝对路径 |
| `line`、`endLine` | 1 基的声明范围；`partial` 类型只表示第一个声明片段 |
| `isPartial`、`declarationCount` | 是否有多个声明片段及片段数量 |
| `documentationSummary` | 类型的 `<summary>` 文本，按列表上限截断 |

`ClassDocumentationInfo`（`get_class_documentation` 返回）包含 `className`、`namespace`、`kind`、`filePath`、`line`、`endLine`、`documentationXml`、`summary`（类型上没有 XML 文档注释时后两者为 `null`）。

`MethodQueryResult`（`get_method` 返回）为 `{ "matches": MethodInfo[], "candidates": string[] }`，每个 `MethodInfo` 含 `className`、`namespace`、`methodName`、`signature`、`returnType`、`parameterTypes`、`filePath`、`startLine`、`endLine`、`hasBody`、`isAbstract`、`isOverride`、`isExtension`、`documentationXml`、`summary`、`body`。

`WorkspaceInfo`（`reload_solution` / `set_resolve_base_path` 返回）为 `{ "solutionPath", "resolveBaseDirectory", "projectCount", "reloaded", "elapsedMilliseconds" }`。

示例——`list_classes_in_folder` 传入 `{ "folderPath": "src/Services" }`：

```json
[
  {
    "className": "UserService",
    "namespace": "MyApp.Services",
    "kind": "class",
    "filePath": "D:\\MyProject\\src\\Services\\UserService.cs",
    "line": 12,
    "endLine": 88,
    "isPartial": false,
    "declarationCount": 1,
    "documentationSummary": "提供用户查询能力。"
  }
]
```

## 输出上限

为避免单次返回过大，长文本会被截断，末尾附加标记 `... [truncated: showing {n} of {m} chars]`。所有阈值集中在 `Services/TextTruncator.cs`。

| 内容 | 默认上限 | 放开方式 |
| --- | --- | --- |
| 列表结果中的类型摘要 | 300 字符 | 改用 `get_class_documentation` |
| `documentationXml` | 2000 字符 | `get_class_documentation` 的 `maxXmlChars` |
| `summary` | 500 字符 | — |
| 方法 `body` | 8000 字符 | `get_method` 的 `maxBodyChars` |

将 `maxXmlChars` / `maxBodyChars` 传 `0` 即该次调用不截断。

## 典型工作流

1. 带 `--workspace` 指向目标解决方案启动服务器一次（MCP 客户端会自动完成）。
2. 发现类型：`list_classes_in_namespace { "namespaceName": "MyApp.Services" }`，或 `list_classes_in_folder { "folderPath": "src/Services", "kinds": ["all"] }`。
3. 读取类型约定：`get_class_documentation { "className": "UserService" }`。
4. 精确读取实现：`get_method { "className": "UserService", "methodName": "GetById", "parameterTypes": ["int"] }`。
5. 源码变更后，在下一次查询前调用 `reload_solution`，否则查询仍使用旧快照。

## 常见问题

- 服务器启动即退出，stderr 显示 `Solution file not found`（或其他 MSBuild 错误）：`--workspace` 路径不正确，必须指向解决方案文件而不是文件夹。
- 客户端的 MCP 日志出现 `The command "dnx" was not found`：`dnx` 自 .NET SDK 10 起才随 SDK 提供。请安装 .NET SDK 10，或改用已安装的工具 / 已发布的可执行程序。
- `dotnet tool install` 报该包不是 .NET 工具：你指定的版本是在启用 `PackAsTool` 之前打包的，请改为安装当前版本（`dotnet tool list --global` 可查看本机已安装的工具）。
- 客户端日志出现 `spawn csharp-analyser-mcp ENOENT` 或 `MCP error -32000: Connection closed`：客户端解析不到要执行的命令。请改用绝对路径 `%USERPROFILE%\.dotnet\tools\csharp-analyser-mcp.exe`（`--local` 安装根本不会生成该 shim），并重启客户端。
- 刚发版后出现 `Cannot find package CSharpAnalyserMcp with version x.y.z`（找不到某版本）：NuGet 的索引可能落后于发布。稍后重试，或用下载好的 .nupkg 安装：`dotnet tool install -g --add-source <目录> CSharpAnalyserMcp --version x.y.z`。

## 行为说明与限制

- **快照语义。** 解决方案只在启动（或刷新）时从磁盘读取一次；文件改动必须调用 `reload_solution` 才可见，编辑器里未保存的内容始终不可见。
- **仅源码视图。** `bin`/`obj` 下的语法树会被跳过，因此没有源码声明的类型不会出现在结果中。
- **仅声明成员。** `get_method` 不会遍历基类、接口，也不会合并 `partial` 类型的其他片段。
- **精确名称。** 类型名与方法名必须精确匹配；命名空间按完整显示字符串比较。可用 `namespaceName`（或带命名空间限定的 `className`）消歧。
- **开销。** 每次查询都会向每个项目索取编译结果，解决方案越大响应越慢。
- **诊断信息。** 工作区加载问题以 `[WorkspaceFailed] {Kind}: {Message}` 形式输出到 stderr；除 JSON-RPC 外不会向 stdout 写任何内容。
- **一个进程一个解决方案。** 工作区路径在启动时固定，分析另一个解决方案需另起一个实例。

---

构建、打包与发版流程（开发者）：[docs/DEVELOPMENT.zh-CN.md](docs/DEVELOPMENT.zh-CN.md)。

