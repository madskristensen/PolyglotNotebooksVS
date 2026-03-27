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

---

## 2026 — Phase 5: VS Theming (p5-theming)

### What Was Done

Audited all four Editor controls for hardcoded colors. Only `CellToolbar.cs` and `OutputControl.cs` needed changes — 4 hardcoded `SetValue(brush)` calls replaced with `SetResourceReference`.

| File | Fixes |
|------|-------|
| `NotebookControl.cs` | Already fully themed — no changes |
| `CellControl.cs` | Already fully themed — no changes |
| `CellToolbar.cs` | `UpdateStatusIndicator()`: 3 hardcoded brushes replaced |
| `OutputControl.cs` | `RenderOutput()`: 1 hardcoded error foreground replaced |

### Key Theming Rule

**NEVER use `SetValue(ForegroundProperty, new SolidColorBrush(...))` for theme-sensitive colors.** This creates a static value that won't update when the VS theme changes. Always use `SetResourceReference(ForegroundProperty, VsBrushes.SomeKey)`.

### VizSurface Color Keys for Semantic Status

`VsBrushes` exposes a `VizSurface*` palette designed for data-visualization. These are fully theme-aware (Light/Dark/Blue/High Contrast). Mapping:
- **Running** (was `Colors.Orange`) → `VsBrushes.VizSurfaceGoldMediumKey`
- **Succeeded** (was hardcoded `#4EC94E`) → `VsBrushes.VizSurfaceGreenMediumKey`
- **Failed / Error** (was hardcoded `#F44444`) → `VsBrushes.VizSurfaceRedMediumKey`

Available VizSurface colors: Green, Red, Gold, Brown, Plum, SteelBlue, StrongBlue, SoftBlue, DarkGold — each with Light/Medium/Dark variants.

### High Contrast Notes

- Text labels ("⟳ Running", "✓", "✗ Error") provide semantic meaning independent of color — HC compliant.
- `Brushes.Transparent` on a Button background is acceptable; WPF HC templates overlay system button colors anyway.
- All `SetResourceReference` calls are automatically HC-aware because VS maps VsBrushes keys to system colors in HC mode.

---

## 2026-03-27 — Phase 5 Complete: VS Theming Fully Implemented (p5-theming)

**Status**: COMPLETE ✅

**What Changed**: Audited all four Editor controls. Replaced 4 hardcoded `SetValue(brush)` assignments with `SetResourceReference` + `VsBrushes.VizSurface*MediumKey` in `CellToolbar.cs` and `OutputControl.cs`.

**Why**: Phase 5 required full VS theme compliance (Light/Dark/Blue/High Contrast) for the notebook UI.

**Changes by File**:

| File | Changes |
|------|---------|
| `CellToolbar.cs` | 3 hardcoded brushes → VizSurface keys (Running=Gold, Succeeded=Green, Failed=Red) |
| `OutputControl.cs` | 1 error foreground → VizSurfaceRedMedium |
| `NotebookControl.cs` | Already compliant — no changes |
| `CellControl.cs` | Already compliant — no changes |

**Decision 12 Established**:
- Use `VsBrushes.VizSurface*MediumKey` for semantic status colors
- Rule: Never use static `SetValue(brush)` for theme-sensitive properties
- All `SetResourceReference` calls automatically map to system colors in HC mode

**High Contrast Compliance**:
- Semantic meaning conveyed by text labels independent of color (✓ WCAG)
- Color is supplementary polish
- HC mode tested via automatic system color mapping

**Related Decisions**:
- Decision 12: VizSurface Color Keys for Notebook Semantic Status Indicators (ACTIVE)
- Decision 8: Cell UI Code-Only WPF Pattern

**Status**: ACTIVE — Theming complete and verified
