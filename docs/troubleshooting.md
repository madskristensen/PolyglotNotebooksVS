# Troubleshooting

This guide covers common problems you may encounter when using Polyglot Notebooks for Visual Studio and how to resolve them.

---

## Kernel and Execution Issues

### "dotnet-interactive is not installed"

**Symptoms:** A dialog appears saying `dotnet-interactive` is not installed when you try to run a cell.

**Solutions:**

1. **Let the extension install it for you.** Click **Yes** in the dialog. The extension runs `dotnet tool install -g Microsoft.dotnet-interactive` automatically.

2. **Install it manually.** Open a terminal (Command Prompt, PowerShell, or Windows Terminal) and run:

   ```bash
   dotnet tool install -g Microsoft.dotnet-interactive
   ```

3. **Verify the .NET SDK is installed.** Run `dotnet --version` in a terminal. If this fails, [download and install the .NET SDK](https://dotnet.microsoft.com/download).

4. **Check your PATH.** Ensure the `dotnet` command is on your system PATH. After installing the SDK, you may need to restart Visual Studio or your terminal.

### "Kernel startup timed out"

**Symptoms:** The kernel takes too long to start, and you see a timeout error.

**Solutions:**

1. **Increase the timeout.** Go to **Tools → Options → Polyglot Notebooks** and increase **Kernel startup timeout** to 60 or 90 seconds.

2. **Check system resources.** Close unnecessary applications. The kernel process needs CPU and memory to start.

3. **Verify `dotnet-interactive` works outside VS.** Open a terminal and run:

   ```bash
   dotnet interactive stdio
   ```

   You should see JSON output. Press Ctrl+C to exit. If this fails, reinstall the tool.

4. **Update `dotnet-interactive`.** An outdated version may have startup issues:

   ```bash
   dotnet tool update -g Microsoft.dotnet-interactive
   ```

### Cell execution hangs or takes too long

**Symptoms:** A cell is running with the timer counting up, but nothing happens.

**Solutions:**

1. **Interrupt the execution.** Press **Ctrl+.** or click **Interrupt** (⏹) in the notebook toolbar.

2. **Check for infinite loops.** Review your code for unintended infinite loops or blocking calls.

3. **Restart the kernel.** Click **Restart Kernel** (🔁) in the toolbar to reset all state and start fresh.

### Cells won't run after a kernel error

**Symptoms:** After a kernel error, subsequent cells fail or produce no output.

**Solutions:**

1. **Restart the kernel.** Click **Restart Kernel** in the toolbar. This creates a fresh kernel process.

2. **Use Restart + Run All** to re-execute everything from the beginning in a clean state.

## Output Issues

### "HTML output — WebView2 runtime not available"

**Symptoms:** Instead of rendered HTML, you see a plain text message about WebView2.

**Solutions:**

1. **Install the WebView2 runtime.** Download it from [Microsoft's WebView2 page](https://go.microsoft.com/fwlink/p/?LinkId=2124703). Most Windows 10/11 installations include it by default, but some enterprise configurations may not.

2. **Restart Visual Studio** after installing WebView2.

### Output is truncated

**Symptoms:** Cell output ends with `...` or appears incomplete.

**Solution:** Increase the **Maximum output length** in **Tools → Options → Polyglot Notebooks**. The default is 50,000 characters.

### Images appear too large or too small

**Solution:** Adjust the **Maximum image width** in **Tools → Options → Polyglot Notebooks**. The default is 800 pixels.

### Mermaid diagrams don't render

**Symptoms:** Mermaid code appears as plain text instead of a diagram.

**Solutions:**

1. **Check that Mermaid is enabled.** Go to **Tools → Options → Polyglot Notebooks** and verify **Enable Mermaid diagrams** is checked.

2. **Check your internet connection.** Mermaid diagrams require loading `mermaid.js` from a CDN. If you're offline or behind a restrictive firewall, diagrams won't render.

3. **Verify the WebView2 runtime is installed** (see above). Mermaid diagrams are rendered inside WebView2.

4. **Check for syntax errors.** If the diagram has a syntax error, the raw source is shown along with an error message. Review the Mermaid syntax at [mermaid.js.org](https://mermaid.js.org/syntax/flowchart.html).

## IntelliSense Issues

### No completions appear

**Symptoms:** Pressing Ctrl+Space shows nothing, or completions are missing.

**Solutions:**

1. **Wait for the kernel to start.** IntelliSense is powered by `dotnet-interactive`. It needs a running kernel to provide completions.

2. **Run at least one cell first.** The kernel starts when you first execute a cell. After that, IntelliSense becomes available.

3. **Check the cell language.** Make sure the cell's language kernel matches the code you're writing.

### Diagnostics (red squiggles) are wrong or stale

**Solution:** Run the cell or a nearby cell to refresh diagnostics. The diagnostics provider queries the kernel, which needs up-to-date context.

## File Format Issues

### "File format not recognized" or garbled content

**Symptoms:** Opening a notebook shows raw JSON or markup instead of the notebook editor.

**Solutions:**

1. **Check the file extension.** Polyglot Notebooks opens `.dib` and `.ipynb` files. Other extensions won't trigger the notebook editor.

2. **Verify the file is valid.** Open the file in a text editor. `.dib` files should start with `#!meta` or `#!csharp`. `.ipynb` files should be valid JSON.

3. **Re-associate the file type.** Right-click the file in Solution Explorer → **Open With** → select **Polyglot Notebook Editor** → click **Set as Default**.

### Losing data when switching formats

Switching between `.dib` and `.ipynb` preserves cell content and outputs. However, some metadata specific to one format may not transfer perfectly. If you need both formats, keep a primary format and export to the other as needed.

## Variable Sharing Issues

### "#!share" fails with "variable not found"

**Solutions:**

1. **Run the source cell first.** The variable must exist in the source kernel before you can share it. Execute the cell that defines the variable, then run the cell with `#!share`.

2. **Check the variable name.** Names are case-sensitive. `myVar` and `myvar` are different variables.

3. **Check the kernel name.** Use the correct kernel name in `--from`. Common names: `csharp`, `fsharp`, `javascript`, `pwsh`, `sql`.

4. **Use the Variable Explorer** to verify the variable exists. Open **View → Polyglot Variables** and look for the variable in the list.

### Shared variable has unexpected value

**Cause:** Variables are serialized as JSON during sharing. Complex types may lose information during serialization.

**Solution:** Prefer simple types (strings, numbers, arrays, plain objects) for sharing. If you need to share complex data, serialize it to JSON explicitly and deserialize on the other side.

## Debugging Issues

### Debug Cell does nothing or falls back to normal execution

**Symptoms:** You select **Debug Cell** but the cell runs without the debugger attaching.

**Solutions:**

1. **Check the cell language.** Debug Cell only works for **C#** and **F#** cells. Other languages (JavaScript, SQL, PowerShell, KQL, HTML, Mermaid) fall back to normal execution.

2. **Make sure the kernel is running.** The debugger attaches to the kernel process. If the kernel hasn't started yet, run any cell first to start it, then try Debug Cell again.

3. **Restart the kernel and retry.** Click **Restart Kernel** (🔁) and try Debug Cell again.

### The debugger attaches but doesn't break

**Symptoms:** The debugger attaches (you see a brief VS debugger UI flash) but no source file opens and you can't step through code.

**Solutions:**

1. **Check that Just My Code isn't blocking.** The extension temporarily disables Just My Code during Debug Cell sessions. If it can't toggle the setting (e.g., due to policy restrictions), the debugger may not break into dynamically compiled code.

2. **Look for the Submission source file.** The debugger opens a file named `Submission_1.cs` (or similar). It may open in a background tab — check your open document tabs.

3. **Restart Visual Studio** if the debugger is in an unusual state.

### Breakpoints don't persist between debug sessions

**Cause:** Each time you run Debug Cell, the kernel compiles a new assembly. Breakpoints set in the `Submission` source file are tied to the previous assembly and won't match.

**Solution:** This is expected behavior. Set breakpoints after the debugger breaks at the first line each time you use Debug Cell.

## Extension Issues

### The extension doesn't load

**Solutions:**

1. **Check Visual Studio version.** Polyglot Notebooks requires Visual Studio 2022 (17.0+).

2. **Verify the extension is enabled.** Go to **Extensions → Manage Extensions** and check that Polyglot Notebooks is installed and enabled.

3. **Check the Activity Log.** If the extension fails to load, Visual Studio logs the error. Go to **Help → Feedback → View Activity Log** (or open `%AppData%\Microsoft\VisualStudio\<version>\ActivityLog.xml`). Search for "PolyglotNotebooks" to find error messages.

4. **Reinstall the extension.** Uninstall via **Extensions → Manage Extensions**, restart VS, then install again from the Marketplace.

## Getting More Help

If none of the solutions above fix your problem:

1. **Check the [issue tracker](https://github.com/madskristensen/PolyglotNotebooksVS/issues)** for known issues.
2. **Open a new issue** with:
   - Your Visual Studio version (Help → About)
   - The `dotnet-interactive` version (`dotnet tool list -g`)
   - Steps to reproduce the problem
   - Any error messages from the Activity Log

← [Back to Documentation Index](index.md)
