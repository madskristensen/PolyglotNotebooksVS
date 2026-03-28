# Decision: Replace TextBox with IWpfTextViewHost in Code Cells

**Author:** Ellie (Editor Extension Specialist)
**Date:** 2026-03-27
**Status:** Implemented

## Context
Code cells used a WPF TextBox with a SyntaxHighlightAdorner overlay for syntax coloring. This was limited: no real language services, no VS theming integration, and the adorner had to duplicate much of what the VS editor already provides.

## Decision
Replace the TextBox in `BuildCodeCellContent` with a hosted VS editor (`IWpfTextViewHost`) using MEF services. This gives us native syntax highlighting, proper VS theming, and all built-in editor features (undo/redo, find/replace, etc.) for free.

## Key Details
- MEF services obtained via `IComponentModel` / `SComponentModel`
- Content type resolved from kernel name → VS content type (e.g., "csharp" → "CSharp")
- Two-way sync between `ITextBuffer` and `NotebookCell.Contents` with suppression flag
- `_editor` (TextBox) kept as nullable field for backward compat with IntelliSense providers
- New `TextView` property exposed for future IWpfTextView consumers
- SyntaxHighlightAdorner no longer used for code cells (files retained in project)
- Markdown cells unchanged (still use TextBox)

## Impact
- **IntelliSense providers** (CompletionProvider, HoverProvider, etc.) still reference `CodeEditor` (TextBox). They'll need updating separately to use `IWpfTextView` APIs instead.
- **SyntaxHighlightAdorner/SyntaxTokenizer** files can be removed once markdown cells don't need them (currently only code cells used them).
