# Settings & Configuration

Customize how Polyglot Notebooks behaves through Visual Studio's Tools → Options page. All settings take effect immediately — no restart required.

---

## Accessing Settings

1. Open **Tools → Options** in Visual Studio.
2. Navigate to **Polyglot Notebooks** in the left tree.
3. Adjust settings and click **OK**.

## General Settings

| Setting | Default | Description |
|---------|---------|-------------|
| **Default kernel** | C# | The language kernel used for new code cells. Options: C#, F#, JavaScript, SQL, PowerShell, HTML |
| **Default file format** | `.dib` | File format when creating new notebooks via Add New Item. Options: `.dib` (Polyglot Notebook), `.ipynb` (Jupyter Notebook) |
| **Clear outputs on kernel restart** | ✅ Enabled | Automatically remove all cell outputs when the kernel is restarted |
| **Auto-save before execution** | ❌ Disabled | Save the notebook to disk before running cells |

### Default Kernel

Controls which language is pre-selected when you insert a new code cell. If you primarily write C#, keep the default. If you mostly write PowerShell scripts in notebooks, change it to PowerShell.

### Default File Format

- **`.dib`** — the Polyglot Notebook format. Text-based, human-readable, excellent for version control with Git. Recommended for most users.
- **`.ipynb`** — the Jupyter Notebook format. Choose this if you share notebooks with colleagues who use VS Code, JupyterLab, or other Jupyter-compatible tools.

### Clear Outputs on Kernel Restart

When enabled (the default), clicking **Restart Kernel** or **Restart + Run All** clears all cell outputs before the kernel restarts. Disable this if you want to preserve outputs during a restart.

### Auto-save Before Execution

When enabled, the notebook file is saved to disk every time you run a cell. This prevents data loss if the kernel process crashes during a long-running computation. Disabled by default to avoid frequent disk writes.

## Editor Settings

| Setting | Default | Description |
|---------|---------|-------------|
| **Show execution timing** | ✅ Enabled | Display how long each cell took to execute |
| **Show cell status indicators** | ✅ Enabled | Show the kernel name and execution state badge on each cell |
| **Enable Mermaid diagrams** | ✅ Enabled | Render Mermaid diagram syntax in output cells |

### Show Execution Timing

When enabled, each cell shows its execution duration in the cell toolbar after running (e.g., `1.2s`). While a cell is running, a live timer counts up. Disable this to reduce visual clutter.

### Show Cell Status Indicators

Displays the kernel name and execution state (idle, running, succeeded, failed) on each cell. Useful for notebooks with many languages. Disable if you find it distracting.

### Enable Mermaid Diagrams

When enabled, output text that starts with Mermaid keywords (`graph`, `sequenceDiagram`, `flowchart`, etc.) is automatically rendered as a diagram. When disabled, Mermaid content appears as plain text.

> **Note:** Mermaid rendering requires WebView2 and loads the Mermaid.js library from a CDN. Disable this if you're working offline or have network restrictions.

## Execution Settings

| Setting | Default | Description |
|---------|---------|-------------|
| **Kernel startup timeout** | 30 seconds | Maximum time to wait for the kernel process to start |
| **Maximum output length** | 50,000 characters | Cell output is truncated after this many characters |
| **Maximum image width** | 800 pixels | Images wider than this are scaled down to fit |
| **Cell execution timeout** | 0 (disabled) | Automatically cancel a cell after this many seconds. Set to 0 to disable |

### Kernel Startup Timeout

How long to wait for `dotnet-interactive` to start before showing a timeout error. Increase this on slower machines or when the .NET SDK is installed on a network drive. If you consistently see timeout errors, try 60 or 90 seconds.

### Maximum Output Length

Prevents runaway output from consuming excessive memory. If a cell produces more than 50,000 characters of output, it's truncated. Increase this if you're working with large datasets, or decrease it to keep the notebook snappy.

### Maximum Image Width

Images wider than this value (in pixels) are automatically scaled down to fit within the cell output area. The default of 800px works well for most monitors. Increase it for high-DPI displays or when working with detailed diagrams.

### Cell Execution Timeout

Automatically cancels a cell if it runs longer than the specified number of seconds. When a cell times out, it shows an error message and its status changes to **Failed**.

Set to `0` (the default) to disable automatic timeout. This is recommended for workflows involving [KQL queries](kusto-kql.md) or other long-running operations that can legitimately take several minutes. If you do enable a timeout, choose a generous value (e.g., 300–600 seconds).

> **Note:** You can always cancel a running cell manually by clicking the **■ Stop** button in the cell toolbar, regardless of whether a timeout is configured.

## Tips

- **Start with the defaults.** The default settings work well for most workflows. Adjust only when you have a specific need.
- **Auto-save is great for long computations.** Enable it if you're running cells that take minutes to execute, so a kernel crash doesn't lose your notebook.
- **Lower the output limit for performance.** If your notebook feels sluggish, reducing the max output length can help — especially for cells that produce thousands of lines.

← [Back to Documentation Index](index.md)
