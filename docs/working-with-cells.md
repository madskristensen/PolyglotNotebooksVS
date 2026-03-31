# Working with Cells

Cells are the building blocks of a notebook. Each cell is an independent unit that holds either executable code or Markdown text. This guide covers everything you can do with cells.

---

## Cell Types

### Code Cells

Code cells contain executable code. Each code cell is associated with a **kernel** (language runtime). When you run a code cell, its code is sent to the kernel and the output is displayed below.

### Markdown Cells

Markdown cells contain formatted text, headings, lists, links, and other documentation written in [Markdown syntax](https://docs.github.com/en/get-started/writing-on-github/getting-started-with-writing-and-formatting-on-github/basic-writing-and-formatting-syntax). Markdown cells render as formatted HTML when you leave edit mode.

To toggle between editing and preview mode, press **F2** or double-click the rendered Markdown.

## Creating Cells

### Using Keyboard Shortcuts

| Action | Shortcut |
|--------|----------|
| Insert code cell above | Ctrl+Shift+A |
| Insert code cell below | Ctrl+Shift+B |
| Insert Markdown cell above | Ctrl+Shift+Alt+A |
| Insert Markdown cell below | Ctrl+Shift+Alt+B |

### Using the Cell Menu

Click the **⋯** button on any cell's toolbar to open the cell options menu. From there, select:

- **Insert Code Cell Above**
- **Insert Code Cell Below**
- **Insert Markdown Cell Above**
- **Insert Markdown Cell Below**

New code cells use the [default kernel](settings.md) unless you change it.

## Switching Cell Languages

Each code cell runs in one language kernel at a time. You can change it two ways:

### Using the Language Dropdown

Click the language dropdown in the cell toolbar (it shows the current kernel name, e.g., "C#"). Select a different language from the list.

**Keyboard shortcut:** Press **Ctrl+Shift+L** to open the language picker.

### Using Magic Commands

Type a magic command on the first line of a cell to set its language:

```
#!csharp
Console.WriteLine("This is C#");
```

Available magic commands:

| Magic Command | Language |
|---------------|----------|
| `#!csharp` or `#!cs` or `#!c#` | C# |
| `#!fsharp` or `#!fs` or `#!f#` | F# |
| `#!javascript` or `#!js` | JavaScript |
| `#!pwsh` or `#!powershell` | PowerShell |
| `#!sql` | SQL |
| `#!kql` or `#!kusto` | KQL (Kusto Query Language) |
| `#!html` | HTML |
| `#!mermaid` | Mermaid diagrams |
| `#!http` | HTTP requests |
| `#!markdown` | Markdown |

## Moving Cells

Reorder cells to organize your notebook:

| Action | Shortcut |
|--------|----------|
| Move cell up | Ctrl+Alt+↑ |
| Move cell down | Ctrl+Alt+↓ |

You can also use **Move Up** and **Move Down** from the **⋯** cell menu.

## Deleting Cells

To delete a cell:

- Press **Ctrl+Shift+Delete**, or
- Click **⋯ → Delete Cell**

> **Note:** Deleted cells cannot be undone from the notebook. Use **Edit → Undo** (Ctrl+Z) in the file to revert.

## Clearing Cell Output

To remove the output from a single cell:

- Press **Ctrl+Shift+Backspace**, or
- The output is cleared automatically when you re-run the cell.

To clear all outputs at once, click **Clear All Outputs** in the notebook toolbar.

## Editing Markdown Cells

Markdown cells have two modes:

1. **Preview mode** — the Markdown is rendered as formatted text. This is the default after the cell is created or the notebook is opened.
2. **Edit mode** — you see and edit the raw Markdown source.

Toggle between modes with:

- **F2** key
- Double-clicking the rendered preview

### Markdown Features

You can use standard Markdown syntax in Markdown cells:

```markdown
# Heading 1
## Heading 2

**Bold text** and *italic text*

- Bullet list
- Another item

1. Numbered list
2. Second item

[Link text](https://example.com)

`inline code`

​```csharp
// Fenced code block
Console.WriteLine("hello");
​```

> Blockquote
```

## IntelliSense in Code Cells

Code cells provide full IntelliSense powered by `dotnet-interactive`:

- **Completions** — type and see suggestions appear automatically (or press Ctrl+Space)
- **Signature Help** — see parameter info when typing function arguments
- **Hover Info** — hover over a symbol to see its type and documentation
- **Diagnostics** — syntax errors and warnings appear with red/yellow squiggles

IntelliSense works across all supported languages — C#, F#, JavaScript, PowerShell, and more.

## Tips

- **Cell order matters for execution.** Variables defined in one cell are available in later cells of the same kernel, but only after the defining cell has been executed.
- **Each kernel maintains its own state.** A C# cell and a JavaScript cell have separate scopes. Use [variable sharing](variable-sharing.md) to pass data between them.
- **Use Markdown cells liberally.** They make your notebook easier to read and understand. Document what each code section does and why.
- **Debug C# and F# cells** by choosing **Debug Cell** from the ▶ run dropdown. The full Visual Studio debugger attaches and lets you step through your cell code. See [Debugging Cells](running-code.md#debugging-cells) for details.

## Next Steps

- [Running Code](running-code.md) — learn about execution flow and the kernel lifecycle
- [Rich Output](rich-output.md) — display HTML, images, tables, and diagrams
- [Keyboard Shortcuts](keyboard-shortcuts.md) — complete shortcut reference

← [Back to Documentation Index](index.md)
