# Document Outline

The Document Outline provides a bird's-eye view of your notebook's structure. It shows all cells in a hierarchical tree where Markdown cells act as section headings and code cells nest beneath them. Click any item to jump instantly to that cell.

---

## Opening the Document Outline

Go to **View > Other Windows > Document Outline** in the Visual Studio menu bar. The outline appears as a dockable window alongside your other tool windows.

## Understanding the Hierarchy

The Document Outline organizes your notebook into a logical tree structure:

- **Markdown cells** appear as top-level nodes with a "Class" icon, representing sections or headings
- **Code cells** appear nested beneath their preceding Markdown cell with a "Field" icon
- If a Markdown cell is followed directly by another Markdown cell (no code in between), the code cells that follow will be grouped under whichever Markdown cell precedes them
- Code cells at the very top of a notebook (before any Markdown) appear as top-level nodes

This structure mirrors how you'd organize a real document — sections with subsections and content within them.

## Navigating Your Notebook

### Click to Jump

Click any cell name in the outline to scroll to that cell in the editor and set focus. This is the fastest way to navigate to a specific cell, especially in long notebooks.

### Current Cell Highlighting

As you edit or move through cells in the editor, the outline automatically highlights the cell you're currently working on. This visual indicator helps you stay oriented in large notebooks.

## How It Stays in Sync

The Document Outline updates automatically when you:

- **Add cells** — new Markdown and code cells appear in the tree
- **Delete cells** — removed cells disappear from the outline
- **Reorder cells** — moving cells up or down reorganizes the tree structure
- **Edit cell content** — changes to Markdown titles reflect in the outline (typically after a short delay)

No manual refresh is needed — the outline watches your notebook and updates in real time.

## Tips

- **Use descriptive Markdown headings.** They become the labels in your outline, so make them clear and concise.
- **Keep related code together.** Group code cells under relevant Markdown sections for a cleaner tree structure.
- **Use the outline for long notebooks.** In notebooks with 20+ cells, the outline becomes invaluable for quick navigation.
- **Theming support** — the outline respects your VS theme. Selection colors and icons adapt to light and dark modes.

## Keyboard Navigation

While the outline window is focused, you can use standard Windows tree view navigation:

- **Up/Down arrows** — move between items
- **Left arrow** — collapse a parent item
- **Right arrow** — expand a parent item
- **Enter** — select the highlighted item (jumps to that cell in the editor)

## Next Steps

- [Working with Cells](working-with-cells.md) — create and organize cells effectively
- [Keyboard Shortcuts](keyboard-shortcuts.md) — more navigation techniques
- [Getting Started](getting-started.md) — create your first notebook

← [Back to Documentation Index](index.md)
