**English** | [简体中文](README.zh-CN.md)

# CSharpAnalyserMcp

A read-only [Model Context Protocol](https://modelcontextprotocol.io) (MCP) server that lets an AI assistant browse any C# solution through Roslyn.

The server loads a solution with `MSBuildWorkspace` into a cached Roslyn snapshot and then answers structured queries about it: where each type is declared, its XML documentation, and a method's signature, documentation, source location and body. An agent can therefore pull in the exact slice of source it needs instead of reading whole files into its context window.

- Transport: stdio (`StdioServerTransport`), protocol traffic on stdout only; all logs go to stderr.
- Solution path is passed on the command line (`--workspace <path to .sln>`); the solution is loaded **before** the MCP handshake, so a bad path fails fast with a message on stderr and a non-zero exit code.
- Tools declare structured output (`UseStructuredContent`), so results arrive as MCP `structuredContent` with camelCase JSON fields.
- Distribution: published to NuGet as the .NET tool **CSharpAnalyserMcp** (command `csharp-analyser-mcp`) and listed in the official [MCP Registry](https://registry.modelcontextprotocol.io) as `io.github.undy-mosq/CSharpAnalyserMcp`.

## Installation

### NuGet .NET tool (recommended)

```powershell
dotnet tool install --global CSharpAnalyserMcp
csharp-analyser-mcp --workspace D:\MyProject\MySolution.sln

# upgrade / remove
dotnet tool update --global CSharpAnalyserMcp
dotnet tool uninstall --global CSharpAnalyserMcp
```

Install it **globally**: a `--local` install only writes a manifest entry, so `csharp-analyser-mcp` never lands on `PATH` and MCP clients cannot start it.

### On demand with `dnx` (nothing to install, .NET SDK 10+)

```powershell
# "--" separates dnx options from the arguments forwarded to the server
dnx CSharpAnalyserMcp --yes -- --workspace D:\MyProject\MySolution.sln
```

### From source

```powershell
git clone https://github.com/undy-mosq/CSharpAnalyserMcp
dotnet build CSharpAnalyserMcp\CSharpAnalyserMcp.csproj
dotnet run --project CSharpAnalyserMcp -- --workspace D:\MyProject\MySolution.sln
```

## Requirements

| Purpose | Requirement |
| --- | --- |
| Run the published tool (`dotnet tool install --global`) | .NET SDK 8.0 or newer on `PATH` (the package targets `net8.0`) |
| Run it on demand with `dnx` | .NET SDK 10.0 or newer — `dnx` ships with the SDK |
| Build / run from source | .NET SDK 8.0 or newer (the project targets `net8.0`) |
| Load the analyzed solution | An MSBuild toolchain that can open it — Visual Studio 2022 or VS Build Tools (`Microsoft.Build.Locator` registers the newest installed instance), or the .NET SDK itself for SDK-style projects |

## Client configuration

The server is started with a single argument, `--workspace`, pointing at the solution file.

### Installed .NET tool

The global tool shim lives in `%USERPROFILE%\.dotnet\tools`. MCP clients spawn the process themselves and only see the `PATH` they inherited, so point the configuration at the shim **by absolute path** (expand `%USERPROFILE%`, e.g. `C:\Users\you\.dotnet\tools\csharp-analyser-mcp.exe`):

Claude Desktop (`claude_desktop_config.json`):

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

VS Code (`.vscode/mcp.json`):

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

The bare command name (`"command": "csharp-analyser-mcp"`) works only when that directory is already on the client's `PATH`; a client started before the tool was installed keeps the old environment, so restart it after changing `PATH`.

### On demand with `dnx` (nothing installed)

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

Append `@<version>` to the package id to pin a version. The **MCP Server** tab of the [NuGet package page](https://www.nuget.org/packages/CSharpAnalyserMcp) offers the same JSON for copying. `dnx` is a `.cmd` shim; if the client cannot spawn it, use the equivalent `dotnet` form instead: `"command": "dotnet"` with `"args": ["tool", "exec", "CSharpAnalyserMcp", "--yes", "--", "--workspace", "D:\\MyProject\\MySolution.sln"]`.

### Published executable

VS Code (`.vscode/mcp.json`):

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

### Running the project from source

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

Press `F5` in VS Code to debug the server itself; `.vscode/launch.json` already passes a `--workspace` argument — point it at your own solution.

## Tools

| Tool | MCP annotations | Purpose |
| --- | --- | --- |
| `list_classes_in_folder` | read-only, structured output | List type declarations in a folder and its subfolders |
| `list_classes_in_namespace` | read-only, structured output | List type declarations in a namespace (sub-namespaces optional) |
| `get_class_documentation` | read-only, structured output | Return the XML documentation of every type with a given name |
| `get_method` | read-only, structured output | Return a method's signature, XML docs, location and body |
| `reload_solution` | idempotent, structured output | Rebuild the Roslyn snapshot after source files changed |
| `set_resolve_base_path` | idempotent, structured output | Change the directory used to resolve relative paths |

### `list_classes_in_folder`

```json
{ "folderPath": "src/Services", "kinds": ["class", "interface"] }
```

| Parameter | Type | Default | Description |
| --- | --- | --- | --- |
| `folderPath` | string, required | — | Absolute path, or a path relative to the resolve base directory (default: the solution's directory). |
| `kinds` | string[] | `["class"]` | Type kinds to include (see [Type kinds and `kinds`](#type-kinds-and-kinds)). |

Files under `bin`/`obj` are skipped. For a `partial` type, the location of the first declaration is reported together with `isPartial` and `declarationCount`.

### `list_classes_in_namespace`

```json
{ "namespaceName": "MyApp.Services", "includeSubNamespaces": true, "kinds": ["all"] }
```

| Parameter | Type | Default | Description |
| --- | --- | --- | --- |
| `namespaceName` | string | global namespace | Namespace name, e.g. `MyApp.Services`. Empty, `global`, `global namespace` or `<global namespace>` all mean the global namespace. |
| `includeSubNamespaces` | bool | `true` | Also include types declared in child namespaces. |
| `kinds` | string[] | `["class"]` | Type kinds to include. |

### `get_class_documentation`

```json
{ "className": "UserService", "maxXmlChars": 2000 }
```

| Parameter | Type | Default | Description |
| --- | --- | --- | --- |
| `className` | string, required | — | Type name, e.g. `UserService`. Matching is exact (case-sensitive) and applies to every project/namespace, so all same-named types are returned. |
| `maxXmlChars` | int | `2000` | Maximum characters of the raw XML documentation per type; `0` = no limit. |

### `get_method`

```json
{ "className": "DuelModel.RoleAttributes", "methodName": "SetEffect", "parameterTypes": ["RoleAttributes", "int"] }
```

| Parameter | Type | Default | Description |
| --- | --- | --- | --- |
| `className` | string, required | — | Type name, optionally namespace-qualified (`DuelModel.RoleAttributes`). |
| `methodName` | string, required | — | Method name, e.g. `SetEffect`. |
| `parameterTypes` | string[] | all overloads | Parameter type names in declaration order, e.g. `["RoleAttributes", "int"]`. Namespace prefixes and C# aliases are ignored. |
| `namespaceName` | string | inferred from `className` | Namespace of the type, used when the name is ambiguous. Empty string means the global namespace. |
| `maxBodyChars` | int | `8000` | Maximum characters of the method body; `0` = no limit. |

Matching rules:

- Only members declared by the type itself are searched (ordinary methods; no inherited members, constructors or properties).
- Parameter types are compared case-insensitively and whitespace-insensitively, without namespace prefixes; C# aliases are mapped to their metadata names (`int` → `Int32`, `string` → `String`, …).
- The comparison is relaxed step by step: exact → ignoring generic arguments → allowing a prefix of the parameter list (optional parameters) → both.
- When nothing matches, `matches` is empty and `candidates` holds corrective hints: the same-named overloads of that type first, otherwise every method of that type.

### `reload_solution`

No parameters. Discards the cached snapshot and re-opens the solution, so later queries see the current files on disk. Returns a `WorkspaceInfo` with `reloaded: true` and `elapsedMilliseconds`.

### `set_resolve_base_path`

```json
{ "basePath": "src" }
```

| Parameter | Type | Default | Description |
| --- | --- | --- | --- |
| `basePath` | string | solution directory | Absolute path, or a path relative to the current base directory. Empty/omitted restores the default (the directory of the solution file). |

## Type kinds and `kinds`

`kind` is lowercase and mutually exclusive: `class`, `static class`, `record`, `struct`, `record struct`, `interface`, `enum`, `delegate`. Only classes can be `static`, so `class` and `static class` are distinct; structs, enums, interfaces and delegates have no static form.

The `kinds` filter is case-insensitive and accepts multiple values (an omitted `kinds` behaves like `["class"]`):

| `kinds` value | Matches |
| --- | --- |
| *(omitted)* / `class` | every class, including static classes and records |
| `static` / `static class` | static classes only |
| `record` | records and record structs |
| `struct` | structs and record structs |
| `record struct` | record structs only |
| `enum` / `interface` / `delegate` | that kind |
| `all` | every type |

The same table is sent to the client as server-level instructions during the `initialize` handshake, so the model does not have to guess the accepted values.

## Result shapes

All fields are serialized in camelCase.

`ClassInfo` (returned by `list_classes_in_folder` / `list_classes_in_namespace`):

| Field | Description |
| --- | --- |
| `className` | Simple type name |
| `namespace` | Declaring namespace |
| `kind` | One of the kind values above |
| `filePath` | Absolute path of the declaring source file |
| `line`, `endLine` | 1-based declaration range; for `partial` types only the first declaration |
| `isPartial`, `declarationCount` | Whether the type is declared in several places, and in how many |
| `documentationSummary` | `<summary>` text of the type, truncated to the list limit |

`ClassDocumentationInfo` (`get_class_documentation`) carries `className`, `namespace`, `kind`, `filePath`, `line`, `endLine`, `documentationXml` and `summary` (both `null` when the type has no XML documentation).

`MethodQueryResult` (`get_method`) is `{ "matches": MethodInfo[], "candidates": string[] }`, where every `MethodInfo` contains `className`, `namespace`, `methodName`, `signature`, `returnType`, `parameterTypes`, `filePath`, `startLine`, `endLine`, `hasBody`, `isAbstract`, `isOverride`, `isExtension`, `documentationXml`, `summary`, `body`.

`WorkspaceInfo` (`reload_solution` / `set_resolve_base_path`) is `{ "solutionPath", "resolveBaseDirectory", "projectCount", "reloaded", "elapsedMilliseconds" }`.

Example — `list_classes_in_folder` with `{ "folderPath": "src/Services" }`:

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
    "documentationSummary": "Provides user lookup."
  }
]
```

## Output limits

Long payloads are truncated to keep tool results small; the appended marker reads `... [truncated: showing {n} of {m} chars]`. All thresholds live in `Services/TextTruncator.cs`.

| Payload | Default limit | How to lift it |
| --- | --- | --- |
| Type summary inside list results | 300 chars | call `get_class_documentation` instead |
| `documentationXml` | 2000 chars | `maxXmlChars` on `get_class_documentation` |
| `summary` | 500 chars | — |
| Method `body` | 8000 chars | `maxBodyChars` on `get_method` |

Passing `0` to `maxXmlChars` / `maxBodyChars` disables truncation for that call.

## Typical workflow

1. Start the server once with `--workspace` pointing at your solution (an MCP client does this for you).
2. Discover types: `list_classes_in_namespace { "namespaceName": "MyApp.Services" }`, or `list_classes_in_folder { "folderPath": "src/Services", "kinds": ["all"] }`.
3. Read a type's contract: `get_class_documentation { "className": "UserService" }`.
4. Read the exact implementation: `get_method { "className": "UserService", "methodName": "GetById", "parameterTypes": ["int"] }`.
5. After the source files change, call `reload_solution` before the next query — otherwise queries keep answering from the stale snapshot.

## Troubleshooting

- The server exits immediately with `Solution file not found` (or another MSBuild error) on stderr → the `--workspace` path is wrong; it must point at the solution file, not at a folder.
- `The command "dnx" was not found` in the client's MCP log → `dnx` ships with the .NET SDK 10 and newer. Install .NET SDK 10, or use an installed tool or the published executable instead.
- `dotnet tool install` reports that the package is not a .NET tool → the version you asked for was packed before `PackAsTool` was enabled; install the current version instead (`dotnet tool list --global` shows what is installed).
- `spawn csharp-analyser-mcp ENOENT` or `MCP error -32000: Connection closed` in the client log → the client could not resolve the command it was told to run. Use the absolute path to `%USERPROFILE%\.dotnet\tools\csharp-analyser-mcp.exe` (a `--local` install has no shim there at all) and restart the client.
- `Cannot find package CSharpAnalyserMcp with version x.y.z` right after a release → NuGet's index can lag behind the release. Retry later, or install from the downloaded `.nupkg`: `dotnet tool install -g --add-source <folder> CSharpAnalyserMcp --version x.y.z`.

## Behavior notes and limitations

- **Snapshot semantics.** The solution is read once from disk; file edits become visible only after `reload_solution`. Unsaved editor buffers are never visible.
- **Source-only view.** Syntax trees under `bin`/`obj` are skipped, so types without source declarations are not reported.
- **Declared members only.** `get_method` does not walk base types, interfaces or the other parts of a `partial` type.
- **Exact names.** Type and method names must match exactly; namespaces are compared as full display strings. Use `namespaceName` (or a qualified `className`) to disambiguate.
- **Cost.** Every query asks each project for its compilation, so response time grows with solution size.
- **Diagnostics.** Workspace load problems are written to stderr as `[WorkspaceFailed] {Kind}: {Message}`; nothing but JSON-RPC ever reaches stdout.
- **One solution per process.** The workspace path is fixed at startup; start another instance to analyse another solution.

## License

MIT — see [LICENSE](LICENSE).

---

Building, publishing and the release process: [docs/DEVELOPMENT.zh-CN.md](docs/DEVELOPMENT.zh-CN.md) (中文).


<!-- mcp-name: io.github.undy-mosq/CSharpAnalyserMcp -->
