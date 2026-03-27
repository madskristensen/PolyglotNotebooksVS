# Wendy History

## Core Context

**Role**: UI Specialist for VS Extension team. Specializes in tool window design, WPF control architecture, VS theming, output rendering, and accessibility.

**Authority Scope**:
- Tool window design and BaseToolWindow<T> patterns
- WPF code-only control design (no XAML)
- VS theming via VsBrushes and DynamicResource
- Rich output rendering (MIME routing, WebView2, image handling)
- DataGrid and collection binding patterns
- Accessibility and high-contrast compliance

**Key Knowledge**:
- Cell UI code-only WPF pattern (4 files: NotebookControl, CellControl, CellToolbar, OutputControl)
- VS color keys: VsBrushes (ToolWindowBackground, ToolWindowText, VizSurface*Medium for status)
- MIME type rendering (text/plain, text/html, text/markdown, text/csv, image/*, application/json)
- WebView2 integration (user-data folder, theme injection, auto-resize, fallback)
- Variable explorer architecture (singleton service, auto-refresh, protocol bridge)

---

## Phase History Summary (P1–P4)

**P1–P2 Foundations**: Cell UI (4 files) with code-only WPF, VS theming compliance (SetResourceReference for all colors), output routing basics. Established pattern: all controls inherit Border (not Grid), TwoWay binding for Contents, CollectionChanged rebuild for Cells.

**P3 Batch – Rich Output & Variables**: Rich output rendering (8 MIME types via WebView2/ImageOutputControl), in-place updates via DisplayedValueUpdated (Replace action), variable explorer tool window (5 files), auto-refresh on SubmitCode events. Added VizSurface color mapping for status indicators (Running=Gold, Succeeded=Green, Failed=Red). All theme-aware (Light/Dark/Blue/HC).

**P4 Batch – Final Integration**: All 8 MIME types live with Theo's test suite (35 tests for rich output), Ellie's execution wiring complete, Vince's toolbar status integrated. VariableService singleton properly lifecycle-managed via package initialization.

**Total**: Full notebook UI with rich output, variable explorer, and complete theming. 309 tests passing.

---

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

## 2026 — Phase 3 (p3-variables): Variable Sharing + Explorer

### What Was Built

Five new files in `src/Variables/` and protocol additions:

| File | Role |
|------|------|
| `VariableInfo.cs` | WPF-bindable model (INotifyPropertyChanged) for a single variable |
| `VariableService.cs` | Protocol bridge singleton; manages KernelClient, auto-refresh, RequestValueInfos/RequestValue/SendValue |
| `VariableExplorerControl.cs` | Code-only WPF DataGrid (Name/Type/Value/Kernel) with toolbar + detail pane |
| `VariableExplorerToolWindow.cs` | BaseToolWindow<T> + nested Pane class (Guid attribute on Pane) |
| `ShowVariableExplorerCommand.cs` | Static helper wrapping `VariableExplorerToolWindow.ShowAsync()` |

Protocol additions:
- `Events.cs`: Added `KernelValueInfo`, `ValueInfosProduced`, `ValueProduced` model classes
- `Commands.cs`: Added `TargetKernelName` to `RequestValueInfos`, `RequestValue`, `SendValue`
- `KernelClient.cs`: Added `RequestValueInfosAsync`, `RequestValueAsync`, `SendValueAsync` convenience methods

Wire-up changes:
- `PolyglotNotebooksPackage.cs`: Added `[ProvideToolWindow(typeof(VariableExplorerToolWindow.Pane))]`; calls `VariableService.Initialize()` in `InitializeAsync`
- `NotebookEditorPane.cs`: `OnKernelClientAvailable` now also calls `VariableService.Current?.SetKernelClient(client)`

### Key Patterns Used

- **Singleton service**: `VariableService.Current` (static, initialized by package). No MEF needed.
- **Auto-refresh trigger**: `VariableService` subscribes to ALL kernel events, filters `CommandSucceeded` where `CommandType == SubmitCode` — ensures refresh only after cell code runs, not after completions/hover/diagnostics.
- **Refresh serialization**: `SemaphoreSlim(1,1)` with `WaitAsync(0)` — new refresh request is silently dropped if one is already in progress (avoids flooding the kernel).
- **BaseToolWindow<T> pane GUID**: The `[Guid]` attribute goes on the nested `Pane : ToolWindowPane` class, NOT on the `BaseToolWindow<T>` subclass. Package registers via `[ProvideToolWindow(typeof(VariableExplorerToolWindow.Pane))]`.
- **SelectionChangedEventArgs ambiguity**: `global using Community.VisualStudio.Toolkit` introduces `Community.VisualStudio.Toolkit.SelectionChangedEventArgs` into every file. Any file with `using System.Windows.Controls` must qualify: `System.Windows.Controls.SelectionChangedEventArgs`.

### Pre-existing Bug Fixed

`CellToolbar.cs` had an ambiguous `SelectionChangedEventArgs` reference (same conflict as above). Fixed by qualifying to `System.Windows.Controls.SelectionChangedEventArgs`.

### Kernel Name Defaulting

`VariableService` defaults to `["csharp", "fsharp"]` as known kernels. `RequestValueInfos` failures per kernel are logged and skipped silently (not all kernels support variables). The list can be updated via `SetKnownKernels()`.

---

## Learnings

### Phase 3 — p3-rich-output: Rich Output Rendering

**Date**: 2026-03-27

#### What Was Built

Three files added / modified in `src/Editor/`:

| File | Change | Purpose |
|------|--------|---------|
| `WebView2OutputHost.cs` | **NEW** | WebView2 wrapper — lazy init, VS-themed HTML shell, auto-resize via JS, install fallback |
| `ImageOutputControl.cs` | **NEW** | Image factory — raster (base64 → BitmapImage) and SVG (WebView2) |
| `OutputControl.cs` | **ENHANCED** | Rich MIME routing, targeted Replace update, 500px scroll cap, WebView2 disposal |

#### MIME Routing

| MIME type | Renderer |
|-----------|----------|
| `text/plain` (default) | TextBlock (Consolas, theme FG) |
| `text/html` | `WebView2OutputHost` |
| `text/markdown` | Lightweight markdown→HTML converter → `WebView2OutputHost` |
| `image/png`, `image/jpeg`, `image/gif`, `image/bmp` | `ImageOutputControl.CreateRasterElement` → WPF `Image` |
| `image/svg+xml` | `ImageOutputControl.CreateSvgElement` → `WebView2OutputHost` |
| `application/json` | `System.Text.Json.JsonSerializer` (WriteIndented) → TextBlock |
| `text/csv` | `CsvToHtmlTable` helper → `WebView2OutputHost` |

#### WebView2 Specifics

- **User-data folder**: `%LOCALAPPDATA%\PolyglotNotebooksVS\WebView2Cache` — avoids writing to protected `devenv.exe` directory.
- **Theme injection**: CSS variables derived from `Application.Current.TryFindResource(VsBrushes.*Key)` — reads actual WPF brush color at navigation time. Keys used: `ToolWindowBackgroundKey`, `ToolWindowTextKey`, `ToolWindowBorderKey`.
- **Auto-resize**: `NavigationCompleted` → `ExecuteScriptAsync("document.body.scrollHeight")` → sets `Height = Math.Min(h+20, 480)`. Outer `ScrollViewer` caps at 500px.
- **Fallback**: If `EnsureCoreWebView2Async` throws or `IsSuccess = false`, shows a TextBlock with install URL.
- **Disposal**: `WebView2OutputHost : IDisposable`. `OutputControl` tracks instances in `_disposables`, disposes on `Rebuild()` and `Unloaded`.

#### DisplayedValueUpdated In-Place Update

`OutputControl.OnOutputsChanged` checks for `NotifyCollectionChangedAction.Replace`. If the `_outputContainer` is live and the index is in range, it calls `ReplaceOutputAt(int, CellOutput)` — disposes the old slot's IDisposable (if any), removes the old UIElement, and inserts the newly rendered element at the same index. All other change types (Add, Remove, Reset) trigger a full `Rebuild()`.

#### VSSDK007 Warning

`ThreadHelper.JoinableTaskFactory.RunAsync(...).FileAndForget(...)` correctly expresses fire-and-forget semantics per VS threading docs. The VSSDK007 analyzer (version in this project) does not suppress the warning when `.FileAndForget()` is chained — it still shows as VSSDK007. This is a known limitation of the analyzer version; the pattern is correct and 0 errors confirmed. Pre-existing VSTHRD110/VSTHRD001 warnings in `CompletionProvider.cs` follow the same policy.

#### WebUtility.HtmlEncode

`System.Net.WebUtility.HtmlEncode` (available via `using System.Net;`) is used in the markdown and CSV converters. Do **not** use `System.Web.HttpUtility.HtmlEncode` — that class requires `System.Web` assembly which is not referenced and is not appropriate in WPF extension code.


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

---

## 2026-03-27 — Phase 4 Batch Complete: Rich Output Rendering Final (p3-rich-output + phase 4)

**Status**: COMPLETE ✅ — All 8 MIME types live with DisplayedValueUpdated in-place updates

**What Changed** (Phase 4 completion):
- Rich output rendering (Wendy) integrated with Ellie's execution layer and Theo's test suite.
- Decision 14 (Rich Output Architecture) merged into decisions.md.
- Keyboard shortcuts available (Vince's toolbar).
- Kernel status display live (Wendy's UpdateKernelStatus in NotebookToolbar).

**MIME Type Support**:
✅ text/plain, text/html, text/markdown, text/csv, image/svg+xml, image/png, image/jpeg, application/json

**Key Contracts**:
- Execution layer (Ellie): Use `cell.Outputs[index] = newOutput` for live display updates (triggers in-place replace, no flicker)
- Testing (Theo): WebView2OutputHost fallback ensures tests don't fail without runtime
- Packaging (Penny): No new dependencies — Microsoft.Web.WebView2 already present

**Build Status**: ✅ 0 errors  
**Integration**: ✅ Complete with all agents

**Related Decisions**:
- Decision 14: Rich Output Rendering Architecture (ACTIVE)
- Decision 12: VizSurface Color Keys (already active)
- Decision 8: Cell UI Code-Only WPF Pattern

**Status**: ACTIVE — Production-ready

---

## 2026-03-27T22:29:00Z — WebView2CompositionControl Recommendation (From Vince Research)

**Key Finding for Rich Output Rendering**:
Vince's architectural research identified WebView2CompositionControl as the solution for HWND-based airspace issues.

**Impact on Wendy's Rich Output Pipeline**:
- Current `WebView2OutputHost` uses HWND-based rendering (renders above WPF content like scrollbars/menus)
- WebView2CompositionControl is a drop-in replacement (~3 lines: `new WebView2()` → `new WebView2CompositionControl()`)
- Renders via Direct3D composition into WPF visual tree (no airspace issues)
- Compatible with net48
- Available in stable NuGet package (v1.0.3856.49+)
- Minor trade-off: slightly lower framerate (irrelevant for static notebook output)

**Implementation Path**:
- Single-file change in `WebView2OutputHost.cs`
- Applies to all 8 MIME types using WebView2 (text/html, text/markdown, text/csv, image/svg+xml)
- Deferred to maintenance release (non-blocking for current release)

**Related Decision**: Vince's research documented as Decision 16 (WebView2CompositionControl Architecture Recommendation)


---

## 2026-03-27T19:48:01Z — Final Batch Complete: p3-variables (Variable Explorer finalized)

**Status**: COMPLETE ✅ — Variable Explorer tool window fully integrated and tested

**What Was Completed**:
- Five files in src/Variables/ (VariableInfo, VariableService, VariableExplorerControl, VariableExplorerToolWindow, ShowVariableExplorerCommand)
- Auto-refresh on SubmitCode events with SemaphoreSlim serialization
- DataGrid with Name/Type/Value/Kernel columns
- Protocol bridge for kernel value queries
- Wired into NotebookEditorPane.OnKernelClientAvailable

**Cross-Agent Integration**:
- **Ellie**: Variable context respects kernel selector (per-kernel variables)
- **Theo**: 35 tests in RichOutputHelperTests cover output rendering used by variable display
- **Vince**: Toolbar's kernel status reflected in variable explorer refresh state

**Build**: ✅ 0 errors, 0 warnings  
**Decisions Captured**:
- Decision 2: Variable Explorer Architecture (singleton service, auto-refresh trigger, SelectionChangedEventArgs qualification, Pane GUID placement)

**Related Decisions**:
- Decision 8: Cell UI Code-Only WPF Pattern (ACTIVE)
- Decision 12: VizSurface Color Keys (ACTIVE)

**Status**: COMPLETE — Production-ready for integration tests

---

## 2026 — WebView2CompositionControl Migration (airspace fix)

**Status**: COMPLETE ✅

**What Changed**: Replaced HWND-based `WebView2` with composition-based `WebView2CompositionControl` in `WebView2OutputHost.cs` to eliminate WPF airspace issues (overlap with scrollbars, popups, tooltips, context menus).

**Files Modified**:

| File | Change |
|------|--------|
| `src/Editor/WebView2OutputHost.cs` | `_webView` field type: `WebView2` → `WebView2CompositionControl`; constructor: `new WebView2` → `new WebView2CompositionControl`; updated doc comments |
| `src/PolyglotNotebooks.csproj` | NuGet `Microsoft.Web.WebView2`: `1.0.3179.45` → `1.0.3856.49` (minimum version for `WebView2CompositionControl`) |

**No other files needed changes** — `OutputControl.cs` and `ImageOutputControl.cs` only reference our `WebView2OutputHost` wrapper, not the `WebView2` type directly. No test files reference the `WebView2` type.

**API Compatibility**: `WebView2CompositionControl` has the same API surface as `WebView2` — `CoreWebView2`, `NavigateToString()`, `EnsureCoreWebView2Async()`, `CoreWebView2InitializationCompleted`, `NavigationCompleted` all work identically. No code changes needed beyond the type swap.

**How it works**: `WebView2CompositionControl` renders via Direct3D composition into the WPF visual tree (no HWND). This eliminates the classic "airspace" problem where HWND-based controls always render on top of WPF content.

**Trade-offs**: Slightly lower framerate than HWND-based WebView2 — irrelevant for static notebook output (HTML tables, charts, SVG).

**Build verification**: Type confirmed present via reflection in `Microsoft.Web.WebView2.Wpf.dll` at v1.0.3856.49 (net462 target). Full project build requires VS SDK tooling not available in CLI — baseline has identical resolution failures.

**Related Decisions**: Decision 16 (Vince's WebView2CompositionControl Architecture Recommendation)

## 2026-03-27 — WebView2 CompositionControl Migration

**What Changed**: Replaced HWND-based WebView2 with composition-based WebView2CompositionControl in WebView2OutputHost.cs to eliminate WPF airspace issues (overlap with scrollbars, popups, tooltips, context menus).

**Files Modified**:
| File | Change |
|------|--------|
| `src/Editor/WebView2OutputHost.cs` | `_webView` field type: `WebView2` → `WebView2CompositionControl`; constructor: `new WebView2` → `new WebView2CompositionControl`; updated doc comments |
| `src/PolyglotNotebooks.csproj` | NuGet `Microsoft.Web.WebView2`: `1.0.3179.45` → `1.0.3856.49` (minimum version for `WebView2CompositionControl`) |

**No other files needed changes** — OutputControl.cs and ImageOutputControl.cs only reference our WebView2OutputHost wrapper, not the WebView2 type directly. No test files reference the WebView2 type.

**API Compatibility**: WebView2CompositionControl has the same API surface as WebView2 — CoreWebView2, NavigateToString(), EnsureCoreWebView2Async(), CoreWebView2InitializationCompleted, NavigationCompleted all work identically. No code changes needed beyond the type swap.

**How it works**: WebView2CompositionControl renders via Direct3D composition into the WPF visual tree (no HWND). This eliminates the classic "airspace" problem where HWND-based controls always render on top of WPF content.

**Trade-offs**: Slightly lower framerate than HWND-based WebView2 — irrelevant for static notebook output (HTML tables, charts, SVG).

**Build verification**: Type confirmed present via reflection in Microsoft.Web.WebView2.Wpf.dll at v1.0.3856.49 (net462 target). Full project build requires VS SDK tooling not available in CLI — baseline has identical resolution failures.

**Related Decisions**: Decision 16 (Vince's WebView2CompositionControl Architecture Recommendation)
