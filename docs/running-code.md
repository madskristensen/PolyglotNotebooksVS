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

If a cell is running too long (infinite loop, expensive computation):

- Press **Ctrl+.** (Ctrl+Period), or
- Click **Interrupt** (⏹) in the notebook toolbar

This sends a cancellation signal to the kernel. Some operations may not respond immediately.

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

## Multi-Language Execution

Each language kernel runs independently. You can mix languages freely:

```
[C# cell]       → runs in the C# kernel
[JavaScript cell] → runs in the JavaScript kernel
[SQL cell]      → runs in the SQL kernel
```

Each kernel maintains its own state. A variable defined in C# is not automatically available in JavaScript. To pass data between kernels, see [Variable Sharing](variable-sharing.md).

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

- [Rich Output](rich-output.md) — display HTML, images, charts, and diagrams
- [Variable Sharing](variable-sharing.md) — pass data between C#, JavaScript, and other kernels
- [Troubleshooting](troubleshooting.md) — fix common execution problems

← [Back to Documentation Index](index.md)
