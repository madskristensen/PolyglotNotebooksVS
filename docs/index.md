# Polyglot Notebooks for Visual Studio

Mix C#, JavaScript, SQL, PowerShell, and more in a single document. Run each piece of code interactively and see rich output — tables, diagrams, HTML — right below your code. All inside Visual Studio.

## What does that look like?

Imagine a single file where you:

1. **Query data in C#** and see it rendered as an interactive table
2. **Share that data into JavaScript** with one line (`#!share --from csharp myData`)
3. **Draw an architecture diagram** in Mermaid — rendered live, right in the editor
4. **Document everything** in Markdown cells between the code

That's a Polyglot Notebook. Each cell can be a different language. Run cells individually or all at once. Output appears inline. No console windows, no separate tools.

> **New to notebooks?** If you've only worked with `.cs` or `.js` files, think of a notebook as a living document — part code, part documentation, part REPL. You write code in small, runnable chunks called *cells*, execute them one at a time, and see results immediately. It's ideal for prototyping, data exploration, learning, and creating runnable documentation.

---

## Get Started in 2 Minutes

1. [Install the extension](getting-started.md#install-the-extension) from the Visual Studio Marketplace
2. **Add → New Item → Polyglot Notebook** to create a `.dib` file
3. Type `Console.WriteLine("Hello!");` and press **Shift+Enter**

That's your first notebook. Now try something more interesting — [follow the Getting Started guide](getting-started.md) to render tables, diagrams, and share data across languages in under 5 minutes.

---

## Guides

| Guide | What You'll Learn |
|-------|-------------------|
| [**Getting Started**](getting-started.md) | Install, create a notebook, and experience the "wow" moments in 5 minutes |
| [**Working with Cells**](working-with-cells.md) | Create, move, delete cells and switch languages with magic commands |
| [**Running Code**](running-code.md) | Execution order, kernel lifecycle, debugging cells, NuGet packages, and HTTP requests |
| [**Querying Kusto (KQL)**](kusto-kql.md) | Connect to Azure Data Explorer, run KQL queries, and share results across kernels |
| [**Rich Output**](rich-output.md) | HTML, tables, images, JSON, SVG, CSV, and Mermaid diagrams |
| [**Variable Sharing**](variable-sharing.md) | Pass data between C#, JavaScript, SQL, and other kernels |
| [**Document Outline**](document-outline.md) | Navigate your notebook structure with the Document Outline tree view |
| [**Keyboard Shortcuts**](keyboard-shortcuts.md) | Complete shortcut reference for power users |
| [**Settings & Configuration**](settings.md) | Default kernel, file format, timeouts, and editor behavior |
| [**Troubleshooting**](troubleshooting.md) | Common problems and step-by-step solutions |

## Quick Links

- [Example Notebooks](../examples/) — Ready-to-run `.dib` and `.ipynb` files covering common scenarios
- [Contributing](../CONTRIBUTING.md) — Build instructions and PR guidelines
- [License](../LICENSE.txt) — Apache 2.0

---

## Supported Languages

| Language | Kernel Name | Magic Command |
|----------|-------------|---------------|
| C# | `csharp` | `#!csharp` |
| F# | `fsharp` | `#!fsharp` |
| JavaScript | `javascript` | `#!javascript` |
| PowerShell | `pwsh` | `#!pwsh` |
| SQL | `sql` | `#!sql` |
| KQL (Kusto) | `kql` | `#!kql` |
| HTML | `html` | `#!html` |
| Mermaid | `mermaid` | `#!mermaid` |
| HTTP | `http` | `#!http` |
| Markdown | `markdown` | `#!markdown` |

## File Formats

- **`.dib`** — The Polyglot Notebook format. Text-based, diff-friendly, designed for multi-language notebooks. Best for version control.
- **`.ipynb`** — The Jupyter Notebook format. Compatible with VS Code, JupyterLab, and other notebook tools. Best for sharing outside Visual Studio.

Both formats support all features. You can open either format and save as the other.
