# Getting Started

Install, create a notebook, and see what makes this extension special — all in about 5 minutes.

---

## Prerequisites

- **Visual Studio 2026** (version 18.0 or later)
- **.NET SDK** installed and on your `PATH` — [download here](https://dotnet.microsoft.com/download)

That's it. The extension handles the rest (including installing the kernel — see [below](#automatic-kernel-installation)).

## Install the Extension

1. Open Visual Studio.
2. Go to **Extensions → Manage Extensions**.
3. Search for **Polyglot Notebooks**.
4. Click **Download**, then restart Visual Studio when prompted.

Or install directly from the [Visual Studio Marketplace](https://marketplace.visualstudio.com/items?itemName=MadsKristensen.PolyglotNotebooks).

## Create Your First Notebook

1. Right-click a project or folder in **Solution Explorer**.
2. Select **Add → New Item**.
3. Search for **Polyglot Notebook**.
4. Name the file (e.g., `scratch.dib`) and click **Add**.

> Already have a `.dib` or `.ipynb` file? Just double-click it in Solution Explorer.

## Your First Cell: Hello World

Click inside the empty code cell, type this, and press **Shift+Enter**:

```csharp
Console.WriteLine("Hello from Polyglot Notebooks!");
```

Output appears directly below the cell. You've just run code in a notebook. ✅

But `Console.WriteLine` is the least interesting thing you can do here. Let's try the features that make notebooks special.

## See Something Cool: Tables from Data

Add a new cell below (**Ctrl+Shift+B**) and run this:

```csharp
record Product(string Name, string Category, decimal Price);

var products = new[]
{
    new Product("Widget", "Tools", 9.99m),
    new Product("Gadget", "Electronics", 49.95m),
    new Product("Gizmo", "Electronics", 129.00m),
};

display(products);
```

You'll see a **formatted HTML table** rendered directly in the notebook — not text, not a console dump. That's `display()` — it picks the best visual representation for your data.

## See Something Cooler: Mermaid Diagrams

Add another cell, but this time **change the language**. Click the language dropdown in the cell toolbar and select **Mermaid**. Then type:

```markdown
graph TD
    A[Start] --> B{Is it working?}
    B -->|Yes| C[Great!]
    B -->|No| D[Debug]
    D --> B
    C --> E[Ship it 🚀]
```

Press **Shift+Enter**. A live-rendered flowchart appears below the cell. You can embed architecture diagrams, state machines, ER diagrams — all as code.

## The "Polyglot" Part: Multiple Languages in One File

This is the headline feature. Each cell can use a different language. Add a JavaScript cell (**Ctrl+Shift+B**, then switch the language dropdown to **JavaScript**):

```javascript
const languages = ["C#", "JavaScript", "SQL", "Mermaid"];
console.log(`This notebook speaks ${languages.length} languages: ${languages.join(", ")}`);
```

You can even **share data between languages**. Define something in C#:

```csharp
var message = "Hello from C#";
```

Then pull it into JavaScript using `#!share`:

```javascript
#!share --from csharp message
console.log(`JavaScript received: ${message}`);
```

That's Polyglot Notebooks in a nutshell: mix languages, see rich output, all in one document.

---

## Automatic Kernel Installation

The extension requires `dotnet-interactive` to execute notebook cells. The first time you run a cell, the extension checks whether the tool is installed. If it isn't:

1. A dialog appears offering to install it automatically.
2. Click **Yes**. The extension runs `dotnet tool install -g Microsoft.dotnet-interactive` for you.
3. Alternatively, click **No** to open the [installation docs](https://github.com/dotnet/interactive#installation) in your browser.

To install manually:

```bash
dotnet tool install -g Microsoft.dotnet-interactive
```

> **Tip:** If installation succeeds but execution still fails, make sure the .NET SDK is on your system `PATH` and restart Visual Studio.

## Understanding the Editor Layout

Now that you've seen what notebooks can do, here's a map of the interface:

### Notebook Toolbar (top of editor)

Global actions for the entire notebook:

| Button | Action |
|--------|--------|
| ▶▶ Run All | Execute every cell top to bottom |
| 🔄 Restart + Run All | Fresh kernel, then re-run everything |
| ⏹ Interrupt | Stop a long-running cell |
| 🔁 Restart Kernel | Reset all state without running cells |
| 🧹 Clear All Outputs | Remove all cell outputs |

The **kernel status indicator** (right side) shows the current state:

| Indicator | Meaning |
|-----------|---------|
| 🟢 Green | Ready or running |
| 🟡 Yellow | Starting / restarting |
| 🔴 Red | Error |
| ⚫ Grey | Not started or stopped |

### Cell Toolbar (per cell)

Each cell has its own toolbar with:

- **Language dropdown** — pick the cell's kernel (C#, JavaScript, SQL, etc.)
- **▶ Run button** with a split dropdown: Run Cell, Run Cells Above, Run Cell and Below, Run Selection
- **Execution counter** — `[1]`, `[2]`, etc., showing run order
- **Timer** — how long execution took
- **⋯ Menu** — insert, move, or delete cells

## Next Steps

You've seen the highlights. Now go deeper:

- [Working with Cells](working-with-cells.md) — create, move, delete, and switch cell languages
- [Running Code](running-code.md) — execution order, NuGet packages, and the kernel lifecycle
- [Rich Output](rich-output.md) — HTML, images, JSON, CSV, SVG, and more
- [Variable Sharing](variable-sharing.md) — the full guide to `#!share` and the Variable Explorer
- [Keyboard Shortcuts](keyboard-shortcuts.md) — speed up your workflow

Or open one of the [example notebooks](../examples/) to see complete, runnable scenarios.

← [Back to Documentation Index](index.md)
