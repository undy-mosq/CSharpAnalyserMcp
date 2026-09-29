**English** | [简体中文](README.zh-CN.md)

# CSharpAnalyserMcp

A read-only [Model Context Protocol](https://modelcontextprotocol.io) (MCP) server that lets an AI assistant browse any C# solution through Roslyn.

The server loads a solution with `MSBuildWorkspace` into a cached Roslyn snapshot and then answers structured queries about it: where each type is declared, its XML documentation, and a method's signature, documentation, source location and body. An agent can therefore pull in the exact slice of source it needs instead of reading whole files into its context window.

- Transport: stdio (`StdioServerTransport`), protocol traffic on stdout only; all logs go to stderr.
- Solution path is passed on the command line (`--workspace <path to .sln>`); the solution is loaded **before** the MCP handshake, so a bad path fails fast with a message on stderr and a non-zero exit code.
- Tools declare structured output (`UseStructuredContent`), so results arrive as MCP `structuredContent` with camelCase JSON fields.

## Requirements

| Purpose | Requirement |
| --- | --- |
| Build / run from source | .NET SDK 8.0 or newer (the project targets `net8.0`) |
| Load the analyzed solution | An MSBuild toolchain that can open it — Visual Studio 2022 or VS Build Tools (`Microsoft.Build.Locator` registers the newest installed instance), or the .NET SDK itself for SDK-style projects |
| Native AOT publish (default `PublishAot=true`) | The MSVC linker, i.e. the *Desktop development with C++* workload including the Windows SDK; publish from a Visual Studio **Developer PowerShell / Command Prompt** |

## Build and run

```powershell
# build
dotnet build CSharpAnalyserMcp\CSharpAnalyserMcp.csproj

# run straight from source (note the "--" separator: everything after it goes to the server)
dotnet run --project CSharpAnalyserMcp -- --workspace D:\MyProject\MySolution.sln
```

The very same binary can also run headless as a plain process and talk JSON-RPC on stdin/stdout, which is exactly what an MCP client does.

### Publishing

```powershell
# Native AOT single-file executable (needs the MSVC linker -> use a Developer PowerShell)
dotnet publish CSharpAnalyserMcp\CSharpAnalyserMcp.csproj -c Release -r win-x64

# Framework-dependent alternative when no C++ toolchain is available
dotnet publish CSharpAnalyserMcp\CSharpAnalyserMcp.csproj -c Release -r win-x64 -p:PublishAot=false
```

The second command writes `CSharpAnalyserMcp\bin\Release\net8.0\win-x64\publish\CSharpAnalyserMcp.exe`.

## Client configuration

The server is started with a single argument, `--workspace`, pointing at the solution file.

Claude Desktop (`claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "csharp-analyser": {
      "command": "C:\\Tools\\csharp-analyser\\CSharpAnalyserMcp.exe",
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
      "command": "C:\\Tools\\csharp-analyser\\CSharpAnalyserMcp.exe",
      "args": ["--workspace", "D:\\MyProject\\MySolution.sln"]
    }
  }
}
```

Running the project directly instead of a published executable:

```json
{
  "servers": {
    "csharp-analyser": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "D:\\13056\\classDatabase\\CSharpAnalyserMcp\\CSharpAnalyserMcp\\CSharpAnalyserMcp.csproj",
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

## Project layout

| Path | Role |
| --- | --- |
| `CSharpAnalyserMcp/Program.cs` | Parses `--workspace`, configures logging/JSON/MCP, loads the workspace, starts the stdio server |
| `CSharpAnalyserMcp/Tools/CSharpAnalyzerTools.cs` | The six MCP tools exposed to the client |
| `CSharpAnalyserMcp/Tools/ServerInstructions.cs` | Server-level instructions sent during `initialize` |
| `CSharpAnalyserMcp/Services/RoslynWorkspaceService.cs` | `MSBuildWorkspace` load / reload and all Roslyn queries |
| `CSharpAnalyserMcp/Services/TextTruncator.cs` | Central truncation limits and helper |
| `CSharpAnalyserMcp/Models/` | Result DTOs (`ClassInfo`, `MethodInfo`, …) plus `AppJsonContext` (source-generated JSON for AOT) |
| `.vscode/launch.json`, `.vscode/task.json` | `F5` debugging of the server with a sample `--workspace` |

## Troubleshooting

- `error : Platform linker not found` while publishing → the MSVC linker / Windows SDK is missing. Run the publish from a Visual Studio *Developer PowerShell* (Desktop development with C++), or fall back to `-p:PublishAot=false`.
- `error MSB3026 / MSB3027 ... apphost.exe ... being used by another process` while rebuilding → a server instance started by an MCP client is still running and locks `bin\Debug\net8.0\CSharpAnalyserMcp.exe`. Stop that client/instance (or build to another output path) and rebuild.
- The server exits immediately with `Solution file not found` (or another MSBuild error) on stderr → the `--workspace` path is wrong; it must point at the solution file, not at a folder.

## Behavior notes and limitations

- **Snapshot semantics.** The solution is read once from disk; file edits become visible only after `reload_solution`. Unsaved editor buffers are never visible.
- **Source-only view.** Syntax trees under `bin`/`obj` are skipped, so types without source declarations are not reported.
- **Declared members only.** `get_method` does not walk base types, interfaces or the other parts of a `partial` type.
- **Exact names.** Type and method names must match exactly; namespaces are compared as full display strings. Use `namespaceName` (or a qualified `className`) to disambiguate.
- **Cost.** Every query asks each project for its compilation, so response time grows with solution size.
- **Diagnostics.** Workspace load problems are written to stderr as `[WorkspaceFailed] {Kind}: {Message}`; nothing but JSON-RPC ever reaches stdout.
- **One solution per process.** The workspace path is fixed at startup; start another instance to analyse another solution.

<!-- mcp-name: io.github.undy-mosq.CSharpAnalyserMcp-->