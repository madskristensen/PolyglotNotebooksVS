# Running Code

This guide covers how to execute cells, understand execution order, manage the kernel process, and use NuGet packages in your notebooks.

---

## Executing a Single Cell

To run the currently focused cell:

- Press **Shift+Enter** — runs the cell and advances focus to the next cell
- Click the **▶ Run** button in the cell toolbar

While a cell is running, you'll see:

- A **live timer** counting elapsed time in the cell toolbar
- The **execution counter** updates to show the run order (e.g., `[1]`, `[2]`)
- The notebook toolbar shows **Running** status

When execution completes, the timer stops and shows the final duration (e.g., `1.2s`).

## Run Options

The **▶** button has a split dropdown with additional run modes:

| Option | What It Does |
|--------|--------------|
| **Run Cell** | Execute only this cell |
| **Run Cells Above** | Execute all cells above this one (not including this cell) |
| **Run Cell and Below** | Execute this cell and all cells below it |
| **Run Selection** | Execute only the selected/highlighted text within the cell |
| **Debug Cell** | Attach the Visual Studio debugger and step through this cell's code |

## Running All Cells

To run every cell in the notebook from top to bottom:

- Press **Ctrl+Shift+Enter**, or
- Click **Run All** (▶▶) in the notebook toolbar

Cells execute sequentially in document order. If any cell throws an unhandled exception, execution stops at that cell.

## Execution Order

Each time you run a cell, it receives an **execution counter** that increments globally across the notebook. The counter appears in the cell toolbar as `[1]`, `[2]`, `[3]`, etc.

This counter helps you understand:

- Which cells have been run (cells without a counter haven't been executed yet)
- The order in which cells were executed (useful when running cells out of order)

> **Important:** Cells run in the order you execute them, not necessarily top-to-bottom. If you run cell 3 before cell 2, the variable definitions in cell 2 won't be available in cell 3. Use **Run All** to ensure correct top-to-bottom ordering.

## The Kernel Process

### What is the kernel?

The kernel is a `dotnet-interactive` process running in the background. It receives your code, executes it, and returns the results. Each notebook gets its own kernel process.

### Kernel Lifecycle

1. **Not Started** — the kernel hasn't been launched yet (grey indicator)
2. **Starting** — the kernel process is launching (yellow indicator)
3. **Ready** — the kernel is idle and waiting for code (green indicator)
4. **Running/Busy** — the kernel is executing a cell (green indicator)
5. **Error** — something went wrong with the kernel (red indicator)
6. **Stopped** — the kernel process has exited (grey indicator)

The kernel starts automatically when you run your first cell. You don't need to start it manually.

### Restarting the Kernel

Restart the kernel to reset all state (clear all variables, loaded assemblies, etc.):

- Click **Restart Kernel** (🔁) in the notebook toolbar — resets state without running cells
- Click **Restart + Run All** (🔄) — resets state and re-runs every cell top to bottom

By default, outputs are cleared on restart. You can disable this in [Settings](settings.md).

### Interrupting Execution

If a cell is running too long (infinite loop, expensive computation, or a long-running KQL query):

- Click the **■ Stop** button that appears in the cell toolbar while the cell is running
- Press **Ctrl+.** (Ctrl+Period), or
- Click **Interrupt** (⏹) in the notebook toolbar

The cell immediately returns to idle — the timer stops and the Stop button disappears. A cancellation signal is sent to the kernel to abort the running operation.

> **Tip:** The per-cell Stop button is the fastest way to cancel a single cell. The toolbar Interrupt button cancels whatever is currently running across the entire notebook.

### Execution Timeout

You can set an automatic timeout so cells are cancelled after a fixed duration:

1. Go to **Tools → Options → Polyglot Notebooks**
2. Set **Cell execution timeout (seconds)** to your preferred limit
3. Set to `0` (the default) to disable automatic timeout

When a cell times out, it shows an error message and its status changes to **Failed**. This is useful as a safety net against runaway computations, but note that some workloads (like [KQL queries against large datasets](kusto-kql.md#handling-long-running-queries)) can legitimately take several minutes. Leave the timeout at `0` or set a generous value for those scenarios.

## Debugging Cells

You can debug C# and F# cells with the full Visual Studio debugger. This lets you step through your cell code line by line, inspect variables, and examine the call stack — the same debugging experience you use for regular projects.

### How to Debug a Cell

1. Click the **▶** split dropdown on the cell toolbar
2. Select **Debug Cell**
3. The extension attaches the VS debugger to the kernel process
4. The cell code begins executing and immediately breaks at the first line
5. Use **F10** (Step Over), **F11** (Step Into), or **F5** (Continue) to step through your code
6. When execution finishes, the debugger detaches automatically and the cell shows its result

### What Happens Under the Hood

When you choose **Debug Cell**, the extension:

1. Attaches the Visual Studio managed debugger to the `dotnet-interactive` kernel process
2. Temporarily disables **Just My Code** (so the debugger can see dynamic Roslyn-compiled code)
3. Prepends a `Debugger.Break()` call to your cell code so the debugger stops at a known point
4. After you finish stepping, the debugger detaches and restores your Just My Code setting

The kernel process stays alive — you can continue running or debugging other cells without restarting.

### Supported Languages

| Kernel | Debug Support |
|--------|:-------------:|
| C# (`csharp`) | ✅ |
| F# (`fsharp`) | ✅ |
| PowerShell (`pwsh`) | ❌ |
| JavaScript (`javascript`) | ❌ |
| SQL, KQL, HTML, etc. | ❌ |

Debug Cell falls back to normal execution for unsupported languages.

### Tips

- **The debugger opens a temporary source file** called `Submission_1.cs` (or similar). This contains your cell code — it's the same code, just shown in the debugger's source view.
- **Variables from previous cells are available.** The kernel retains state across cells, so variables defined in earlier cells are visible in the debugger's Locals and Watch windows.
- **You can set breakpoints** in the `Submission` source file once it opens. They work for the current debug session.
- **Press F5 to skip ahead.** If you don't need to step through every line, press F5 (Continue) to let the cell finish executing.

### Limitations

- Debugging is not available for JavaScript, SQL, KQL, PowerShell, HTML, or Mermaid cells.
- Breakpoints set in the `Submission` file do not persist across debug sessions because the kernel compiles a new assembly each time.
- The debugger cannot break inside code from NuGet packages or workspace references unless those assemblies have PDBs available.

## Using NuGet Packages

You can reference NuGet packages directly in C# and F# cells using the `#r "nuget:..."` directive:

```csharp
#r "nuget: Newtonsoft.Json, 13.0.3"
using Newtonsoft.Json;

var obj = new { Name = "Test", Value = 42 };
Console.WriteLine(JsonConvert.SerializeObject(obj));
```

The package is downloaded and loaded at runtime. Subsequent cells in the same kernel can use the package's types without the `#r` directive again.

### Tips for NuGet

- **Include a version** — `#r "nuget: PackageName, 1.2.3"` for reproducible notebooks.
- **First load is slower** — the package must be downloaded. Subsequent runs use the cached version.
- **Works in C# and F# cells** — other kernels have their own package mechanisms.

## Workspace References

When a notebook file is part of a project in the loaded solution, the extension automatically injects `#r` references for the solution's project assemblies and NuGet packages into the kernel when it starts. This means you can use your own types and packages in notebook cells without any `#r` ceremony.

### How It Works

1. When the kernel starts for a notebook, the extension checks whether the notebook file belongs to a project in Solution Explorer.
2. If it does, the extension enumerates all projects in the solution and collects:
   - **Project output assemblies** (e.g., `bin/Debug/net8.0/MyProject.dll`)
   - **Resolved NuGet package assemblies** that the projects reference
3. These are submitted as `#r` directives to the C# kernel before your first cell runs.
4. IntelliSense reflects the imported types automatically.

### When It Activates

Workspace references are injected **only** when the notebook file is included in a project in Solution Explorer. This happens automatically — there's no setting to enable or disable.

| Scenario | References Injected? |
|----------|:-------------------:|
| Notebook added to a project in Solution Explorer | ✅ Yes |
| Notebook opened via **File > Open > File** (not in a project) | ❌ No |
| Notebook in a Solution Folder (not a project) | ❌ No |

### Example

Given a solution with a `MyLib` project containing:

```csharp
// MyLib/Calculator.cs
namespace MyLib;

public class Calculator
{
    public static int Add(int a, int b) => a + b;
}
```

A notebook file added to the same solution can use `Calculator` directly:

```csharp
using MyLib;

var result = Calculator.Add(17, 25);
Console.WriteLine($"17 + 25 = {result}");
```

NuGet packages referenced by solution projects are also available. If `MyLib` references `Newtonsoft.Json`, your notebook can use it without a `#r "nuget:..."` directive:

```csharp
using Newtonsoft.Json;

var json = JsonConvert.SerializeObject(new { Name = "Test", Value = 42 });
Console.WriteLine(json);
```

### Rebuilding Your Project

The kernel loads assemblies into memory when it starts. If you rebuild a project, the kernel still has the **old** version loaded. To pick up changes:

- Click **Restart Kernel** (🔁) in the notebook toolbar, then re-run your cells
- Or click **Restart + Run All** (🔄) to restart and re-execute everything

> **Tip:** Workspace references are resolved from disk when the kernel starts. Make sure your projects are **built** before running notebook cells — the extension can only inject assemblies that exist on disk.

### Supported Target Frameworks

The `dotnet-interactive` kernel runs on modern .NET. Not all project target frameworks are compatible:

| Project TFM | Works? | Notes |
|-------------|:------:|-------|
| `net8.0`, `net9.0`, `net10.0`+ | ✅ | Fully supported |
| `net6.0`, `net7.0` | ✅ | Fully supported |
| `netstandard2.0`, `netstandard2.1` | ✅ | Fully supported — designed for cross-runtime compatibility |
| `net48`, `net472`, `net462` (.NET Framework) | ⚠️ | **Not reliably supported.** Assemblies may load, but types that depend on Framework-only APIs (`System.Web`, WCF, `System.Drawing` without the compat pack, etc.) will fail at runtime. Simple data model classes may work; anything else is unpredictable. |

For multi-TFM projects (e.g., `<TargetFrameworks>net8.0;netstandard2.0</TargetFrameworks>`), the extension uses the output assembly that Visual Studio has currently selected as the active build configuration.

> **Recommendation:** If your solution contains .NET Framework projects and you want to use their types in notebooks, consider adding a `netstandard2.0` target to the projects you need. .NET Standard assemblies load cleanly in both .NET Framework and the `dotnet-interactive` kernel.

### Limitations

- Only **C# kernel** cells benefit from the injected `#r` directives. Other kernels (JavaScript, SQL, PowerShell) are not affected.
- Projects that haven't been built yet (no output DLL on disk) are skipped silently.
- .NET runtime and framework assemblies are excluded — the kernel already has these.

## Multi-Language Execution

Each language kernel runs independently. You can mix languages freely:

```
[C# cell]       → runs in the C# kernel
[JavaScript cell] → runs in the JavaScript kernel
[SQL cell]      → runs in the SQL kernel
[KQL cell]      → runs in a connected Kusto kernel
```

Each kernel maintains its own state. A variable defined in C# is not automatically available in JavaScript. To pass data between kernels, see [Variable Sharing](variable-sharing.md).

> **Working with Kusto?** See [Querying Kusto (KQL)](kusto-kql.md) for a complete guide on connecting to Azure Data Explorer clusters, running KQL queries, and sharing results into other kernels.

## HTTP Requests

Use the `#!http` kernel to send HTTP requests directly from a cell:

```http
GET https://jsonplaceholder.typicode.com/todos/1
```

The response is displayed as formatted output below the cell.

## Tips

- **Run cells top-to-bottom** for reproducible results. Out-of-order execution can lead to confusing state.
- **Use Restart + Run All** to verify your notebook works from a clean state before sharing it.
- **Execution timing** is shown per cell, helping you identify slow cells.
- **Auto-save before execution** can be enabled in [Settings](settings.md) so your work is always preserved.

## Next Steps

- [Querying Kusto (KQL)](kusto-kql.md) — connect to Azure Data Explorer and run KQL queries
- [Rich Output](rich-output.md) — display HTML, images, charts, and diagrams
- [Variable Sharing](variable-sharing.md) — pass data between C#, JavaScript, and other kernels
- [Troubleshooting](troubleshooting.md) — fix common execution problems

← [Back to Documentation Index](index.md)
