# Wendy History

## 2024-01-XX — Xtenders Founded

**Context**: Founded as specialized team for Visual Studio extension developers.

**Wendy's Focus**:
- Tool window design and implementation (BaseToolWindow<T>)
- WPF UserControl development with VS theming
- Dialog and modal window design
- Status bar, info bar, and progress notifications
- Custom editor implementation (IVsEditorFactory)
- Icon management (KnownMonikers and custom icons)
- Fonts & Colors category registration
- Accessibility and theme compliance

**Authority Scope**:
- Tool window architecture and UX patterns
- WPF control theming ({DynamicResource} bindings, UseVsTheme)
- Dialog creation and styling
- Icon selection and custom icon registration
- Status bar messaging and notifications
- Accessibility validation

**Knowledge Base**:
- BaseToolWindow<T> lifecycle and patterns
- WPF XAML, bindings, data templates
- VS EnvironmentColors and theming
- KnownMonikers library
- Image manifest setup and registration
- Accessibility standards (WCAG, VS conventions)

**Key References**:
- VS Tool Windows API (Microsoft Docs)
- KnownImageIds Reference
- Environment Colors Reference
- Community.VisualStudio.Toolkit UI samples
- Image Service and Catalog documentation

**Active Integrations**:
- Vince: Tool window scaffolding and package registration
- Ellie: Editor UI components (completion, quick info popup styling)
- Sam: Error List UI, custom tree node styling

---

## 2026 — Phase 2.2: Cell-Based Notebook UI (p2-cell-ui)

### What Was Built

Replaced the Phase 2.1 placeholder `NotebookControl` with a full WPF cell-based notebook editor.
All UI is code-only (C#) — no XAML files. Four files in `src/Editor/`:

| File | Role |
|------|------|
| `NotebookControl.cs` | Main notebook host; scrollable StackPanel of CellControls + "Add Cell" buttons |
| `CellControl.cs` | Single cell card (toolbar + code TextBox + output area) |
| `CellToolbar.cs` | Language badge, ▶ Run button, [N] counter, status, ··· menu |
| `OutputControl.cs` | MIME-routed output rendering with expand/collapse |

### UI Patterns Used

- **All WPF UI in C#**: `new StackPanel()`, `new Grid()`, etc. No XAML files needed.
- **VS Theming via DynamicResource**: All controls use `SetResourceReference()` with `VsBrushes.*Key` constants — NOT hardcoded colors — so the UI reacts to VS theme changes automatically.
  - `VsBrushes.ToolWindowBackgroundKey` — panel/card background
  - `VsBrushes.ToolWindowTextKey` — primary text
  - `VsBrushes.GrayTextKey` — secondary text (execution counter, status labels)
  - `VsBrushes.ButtonFaceKey` — toolbar buttons and language badge background
  - `VsBrushes.ToolWindowBorderKey` — cell card borders
- **TwoWay WPF Binding**: `TextBox.TextProperty` bound to `NotebookCell.Contents` via `new Binding(...)` with `Source = cell, Mode = BindingMode.TwoWay, UpdateSourceTrigger = PropertyChanged`. Requires `System.Xaml` reference.
- **CollectionChanged rebuild**: `NotebookControl` subscribes to `Cells.CollectionChanged` and rebuilds the entire StackPanel on any change. Acceptable for v1 (cell counts are small).
- **Unloaded cleanup**: `CellToolbar` and `OutputControl` unsubscribe from model events via `this.Unloaded += ...` to prevent memory leaks when controls are removed from the visual tree.
- **Auto-grow TextBox**: Editor TextBox uses `Measure()` + `Height = desired` pattern to grow from MinHeight 60 to MaxHeight 400 as content grows.

### Integration Points for Phase 2.3 (Ellie)

- **`NotebookControl.CellRunRequested` event**: `EventHandler<CellRunEventArgs>` raised when the ▶ button is clicked on any cell. `CellRunEventArgs.Cell` is the `NotebookCell` to execute. Subscribe here from `NotebookEditorPane` or the execution layer.
- **`CellControl.RunRequested` event**: Same signal, cell-level (bubbled up to `NotebookControl.CellRunRequested`).
- **`NotebookCell.ExecutionOrder`**: Set to `[N]` after execution; CellToolbar displays it automatically.
- **`NotebookCell.ExecutionStatus`**: Set to `Running`/`Succeeded`/`Failed`; CellToolbar shows status indicator.
- **`NotebookCell.Outputs`**: Append `CellOutput` items; `OutputControl` auto-renders via `CollectionChanged`.

### Integration Points for Phase 2.3 (Ellie)

- **`NotebookControl.CellRunRequested` event**: `EventHandler<CellRunEventArgs>` raised when the ▶ button is clicked on any cell. `CellRunEventArgs.Cell` is the `NotebookCell` to execute. Subscribe here from `NotebookEditorPane` or the execution layer.
- **`CellControl.RunRequested` event**: Same signal, cell-level (bubbled up to `NotebookControl.CellRunRequested`).
- **`NotebookCell.ExecutionOrder`**: Set to `[N]` after execution; CellToolbar displays it automatically.
- **`NotebookCell.ExecutionStatus`**: Set to `Running`/`Succeeded`/`Failed`; CellToolbar shows status indicator.
- **`NotebookCell.Outputs`**: Append `CellOutput` items; `OutputControl` auto-renders via `CollectionChanged`.

### Project Fixes Made

Two pre-existing duplicate-code bugs were fixed (same pattern: namespace closing `}` appeared mid-file, leaving a second copy of class declarations outside the namespace):
- `src/Kernel/KernelProcessManager.cs` — trimmed to 446 lines
- `src/Protocol/KernelClient.cs` — trimmed to 380 lines

Also added `<Reference Include="System.Xaml" />` to `PolyglotNotebooks.csproj` (required for WPF `Binding` when targeting net48 without implicit XAML compilation).

---

## 2026-03-27 — Phase 3 Batch Complete (p2-cell-ui)

**Status**: COMPLETE ✅

**What Changed**: Delivered full cell-based notebook UI (4 new files). All code-only WPF (no XAML). System.Xaml reference added.

**Why**: User task p2-cell-ui — build cell-based UI with Run buttons, language badges, output rendering.

**Affected Areas**:
- Ellie (Phase 2.3): CellRunRequested event is the execution hook
- Theo: Fixed pre-existing bugs in KernelProcessManager.cs and KernelClient.cs
- Penny: Dependency on working build for VSIX packaging

**Status**: ACTIVE — Awaiting Phase 2.3 (Ellie) for execution wiring

