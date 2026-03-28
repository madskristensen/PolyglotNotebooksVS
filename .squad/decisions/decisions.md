# Decisions

## Decision 1: KernelInfoCache Population Strategy

**Date**: 2026  
**Author**: Ellie (Editor Specialist)  
**Status**: ACTIVE  
**Type**: Architecture / Protocol Integration

### Context

Phase p3-kernel-selector required a `KernelInfoCache` that provides the list of available kernels to the per-cell ComboBox. The kernel emits a `KernelReady` event on startup that contains the full list of kernels (`KernelInfos`). A `RequestKernelInfo` command can also be sent to query kernels individually (returning `KernelInfoProduced` per kernel), but requires collecting multiple events.

### Decision

Populate `KernelInfoCache` from the `KernelReady` event payload rather than sending `RequestKernelInfo` + collecting `KernelInfoProduced` events.

**Subscribe-before-wait pattern:** In `ExecutionCoordinator.EnsureKernelStartedAsync`, subscribe to `client.Events` for `KernelReady` **before** calling `WaitForReadyAsync`. Since `Subject<T>` does not buffer past events, a post-wait subscription would miss it.

**Pre-populated defaults:** `KernelInfoCache.Default` ships with 8 well-known kernels (csharp, fsharp, javascript, typescript, sql, pwsh, html, markdown) so the UI is functional before the kernel starts.

**Reset on restart:** `RestartAndRunAllAsync` calls `KernelInfoCache.Default.Reset()` before tearing down the kernel, so the ComboBox reverts to defaults while the new process starts.

### Rationale

- `KernelReady` already carries the full list in a single event; no extra round-trip needed.
- `RequestKernelInfo` returns one `KernelInfoProduced` per kernel and requires collecting them until `CommandSucceeded` — adding protocol complexity not justified by the benefit over using `KernelReady`.
- Pre-populated defaults give instant UI feedback with no kernel latency.

### Implications

- `KernelInfoProduced` POCO was added to `Events.cs` for completeness, but is not currently used.
- If the kernel reports additional/custom kernels not in the default list, they will appear in the ComboBox after the first cell run triggers kernel startup.
- `KernelInfoCache.Default` is a process-wide singleton; tabs sharing a process share the same cache (acceptable since all tabs use the same dotnet-interactive installation).

### Related Work

- Kernel selector UI implementation (p3-kernel-selector)
- Execution modes with kernel switching (p4-exec-modes)

---

## Decision 2: Variable Explorer Architecture

**Date**: 2026  
**Author**: Wendy (UI Specialist)  
**Status**: ACTIVE  
**Type**: Architecture / Tool Window Design

### Context

Phase p3-variables required a Variable Explorer tool window that tracks and displays in-scope variables from the kernel. The service needed to be wired into the notebook runtime lifecycle, auto-update on code execution, and properly integrate with VS's tool window system.

### Decision

Variable Explorer is implemented as a **singleton `VariableService`** (no MEF component registration) initialized by the package, wired into each notebook via `NotebookEditorPane.OnKernelClientAvailable`.

**Auto-refresh trigger:** Refresh triggers on `CommandSucceeded` events where `envelope.Command?.CommandType == CommandTypes.SubmitCode` — this is the correct filter for "a cell was executed", avoiding refresh after completion/hover/diagnostics commands.

**SelectionChangedEventArgs qualification:** Any file with **both** `global using Community.VisualStudio.Toolkit` AND `using System.Windows.Controls` must qualify `SelectionChangedEventArgs` as `System.Windows.Controls.SelectionChangedEventArgs`. The toolkit's global using imports `Community.VisualStudio.Toolkit.SelectionChangedEventArgs` into scope, causing CS0104 ambiguity.

**Tool Window Pane GUID placement:** For `BaseToolWindow<T>`, the `[Guid]` attribute goes on the nested `Pane : ToolWindowPane` class (not on the `BaseToolWindow<T>` subclass). The package registers via `[ProvideToolWindow(typeof(MyToolWindow.Pane))]`.

### Rationale

- Singleton pattern avoids MEF overhead and simplifies lifecycle management.
- Filtering on SubmitCode events ensures accurate "code executed" semantics without false positives.
- Explicit type qualification prevents ambiguity and compilation errors.
- Pane-level GUID placement follows VS SDK conventions for nested tool window classes.

### Implications

- All notebook tabs in the same process share the same `VariableService` instance.
- Variable scope is determined by kernel state, not editor state; variables persist until kernel restart or reassignment.
- Tool window auto-initializes when package loads; no manual registration needed per tab.

### Related Work

- Kernel selector for multi-kernel variable context (p3-kernel-selector)
- Toolbar integration for Restart+Run All (p4-toolbar)

## Decision 3: WebView2CompositionControl for Airspace Fix

**Date**: 2026-03-27  
**Lead**: Vince (Architect)  
**Status**: ACTIVE  
**Type**: Architecture / Output Rendering

### Context

WebView2 is HWND-based and renders on top of WPF content (scrollbars, popups, tooltips, menus) due to a known WPF "airspace" limitation. This affects rich output rendering (text/html, text/markdown, text/csv, image/svg+xml) where WebView2 may obscure UI elements.

### Decision

Use **WebView2CompositionControl** (Microsoft's official solution in the same NuGet package) as a drop-in replacement for `WebView2`. 

**Implementation**: Single-file change in `WebView2OutputHost.cs`:
```csharp
// Before
var webView = new WebView2();
// After
var webView = new WebView2CompositionControl();
```

**Trade-offs**:
- Renders via Direct3D composition into WPF visual tree (no airspace issues)
- Slightly lower framerate than HWND-based (irrelevant for static notebook output)
- Confirmed compatible with .NET Framework 4.8 (net48)
- Available in stable NuGet package (v1.0.3856.49+)

### Rationale

- Microsoft's first-party solution; battle-tested in production extensions
- API surface identical to `WebView2` (truly drop-in replacement)
- Solves airspace issue with minimal code churn
- No performance degradation for typical notebook output (static HTML, charts)

### Implications

- Rich output rendering (Wendy) affected: all 8 MIME types using WebView2 benefit from airspace fix
- Non-breaking change: can ship in maintenance release post-GA
- Variable explorer tool window (also uses WebView2 for formatted output) also benefits

### Alternatives Considered

- **CefSharp OffScreen**: Solves airspace but adds ~100MB Chromium binaries, in-process crash risk, single-init-per-process. Disqualified.
- **HtmlRenderer.WPF**: HTML 4.01/CSS2 only, no JavaScript, cannot render Plotly or modern kernel output. Insufficient.
- **Hybrid WPF**: Reduces WebView2 usage but doesn't solve airspace for text/html (most impactful case). High code churn.
- **IVsWebBrowser**: Legacy IE-based, same airspace issue, worse rendering. Rejected.

### Related Decisions

- Decision 14: Rich Output Rendering Architecture (uses WebView2)
- Decision 2: Variable Explorer Architecture (displays formatted output via WebView2)

## Decision 4: Font-Metric-Based Cell Sizing

**Date**: 2026-03-27  
**Author**: Wendy (UI Specialist)  
**Status**: ACTIVE  
**Type**: Architecture / UI/UX

### Context

Code cells had hardcoded `MinHeight=60` / `MaxHeight=400` with `VerticalScrollBarVisibility=Hidden`. This caused:
- Short cells wasting vertical space
- Long cells clipped at 400px with no scroll mechanism

### Decision

Use `FontFamily.LineSpacing × FontSize` to derive line height, then calculate:
- **MinHeight**: ~2 lines (adaptive to font)
- **MaxHeight**: ~20 lines (adaptive to font)
- Enable `VerticalScrollBarVisibility=Auto` so scrollbar appears only when content exceeds 20-line cap

Implementation in `CellControl.cs` leverages existing `AdjustEditorHeight()` method which already respects Min/MaxHeight bounds.

### Rationale

- Font-metric calculation ensures MinHeight/MaxHeight scale with actual rendered line dimensions
- Auto scrollbar provides progressive disclosure: short cells remain compact, long cells become scrollable
- 20-line cap balances visibility and usability (typical large notebook cell is 15-18 lines)
- No breaking changes; existing cell styling and layout unaffected

### Implications

- If `FontSize` becomes user-configurable in future, the line-height calculation should move to a shared helper
- All `CellControl` instances use the same formula (consistent across notebook)
- `OutputControl`, `CellToolbar`, `NotebookControl` are unchanged
- Build verified clean, 0 errors

### Related Decisions

- None currently; standalone UI/UX improvement

---

---

## Decision 5: Keyboard Input & Syntax Highlighting Fix

**Date**: 2026-03-28  
**Lead**: Ellie (Editor Extension Specialist)  
**Status**: ACTIVE  
**Type**: Bug Fix / Runtime Behavior

### Context

Three IWpfTextViewHost runtime issues were blocking editor usability:
1. Keystrokes not reaching hosted `IWpfTextView` instances in cells — VS accelerator pre-translation consumed all input
2. C# syntax highlighting failing to activate — Roslyn classifiers require `ITextDocument` association, not just buffer
3. HTML QuickInfo crashing on hover — exception raised when processing formatted output

### Decision

**Keyboard Input**: Override `PreProcessMessage` in `NotebookEditorPane` to check aggregate focus via `HasFocusedTextView()` up the control tree. When a text view has focus, bypass VS accelerator table and let WPF routing handle the keystroke.

**C# Highlighting**: Create `ITextDocument` via `ITextDocumentFactoryService.CreateTextDocument(buffer, fakeFileName)` in `BuildCodeCellContent` immediately after buffer initialization. Store reference in `_textDocument` field.

**HTML Crash**: Leave non-fatal exception alone — it is caught by VS infrastructure and does not impede execution.

### Rationale

- `PreProcessMessage` fires before accelerator processing; checking text view focus allows selective bypass
- `ITextDocument` is a required companion to `ITextBuffer` for Roslyn classification; creating it post-buffer fixes the dependency ordering
- HTML QuickInfo crash is handled gracefully by the runtime; fixing it requires HTML parser changes outside Ellie's scope

### Implications

- Keyboard focus propagation now depends on `HasFocusedTextView()` chain: CellControl → NotebookControl → NotebookEditorPane
- All cell text views must use the same `ITextDocumentFactoryService` instance (injected singleton)
- No breaking changes; existing cell lifecycle and rendering unaffected
- Build verified clean (0 errors)

### Related Decisions

- Decision 2: Variable Explorer Architecture (also uses ITextDocument)
- P3 IntelliSense wiring (relies on ITextDocument for classification)

*Decisions merged from inbox by Scribe, 2026-03-28T01:56:49Z*

---

## Decision 6: Scroll-Wheel Forwarding Strategy for Notebook Cells

**Author**: Ellie (Editor Specialist)  
**Date**: 2026-03-28  
**Status**: IMPLEMENTED  
**Type**: Architecture / UI Event Routing

### Context

The notebook uses a WPF `ScrollViewer` wrapping a `StackPanel` of cells. Three types of controls inside cells capture `WM_MOUSEWHEEL` before WPF can route it to the outer ScrollViewer:
1. **IVsCodeWindow** — native Win32 HWND for code editing
2. **WebView2CompositionControl** — Chromium-based renderer for HTML outputs
3. **OutputControl's inner ScrollViewer** — WPF scroll container capping output height at 500px

### Decision

Use **three complementary interception strategies**, one per control type:

| Control | Strategy | Why it works |
|---------|----------|-------------|
| IVsCodeWindow | `PreviewMouseWheel` on `IWpfTextViewHost.HostControl` | Tunneling event fires on WPF wrapper *before* reaching the native HWND |
| WebView2 | JS `wheel` → `postMessage` → C# `WebMessageReceived` | No WPF tunneling for composition controls; must intercept in Chromium |
| Inner ScrollViewer | `PreviewMouseWheel` on the `ScrollViewer` itself | Standard WPF tunneling, forwarded to parent |

All three use `FindParentScrollViewer(DependencyObject)` to walk the visual tree and find the notebook's outer `ScrollViewer`.

### Trade-off

This approach **always forwards** wheel events, meaning individual outputs taller than 500px can no longer be scrolled internally. This is acceptable because:
- WebView2 outputs auto-size up to 480px (rarely need internal scroll)
- The notebook-as-a-whole scrolling experience is the primary UX concern
- Internal output scrolling can be re-added later with boundary detection if needed

### Also: Dynamic Cell Height

Changed code cell sizing from fixed min/max to **auto-sizing based on content**:
- Min: 2 lines → 1 line, Max: 20 lines → 25 lines
- `IWpfTextView.LayoutChanged` drives real-time height updates
- Initial height set from `TextSnapshot.LineCount` on creation

### Team Impact

- **Wendy**: The notebook ScrollViewer in `NotebookControl.cs` is now the sole scroll authority. No changes needed there.
- **Theo**: No test impact — these are UI-only changes in WPF event handlers.
- **Vince**: Part of overall UI/UX refinement initiative

### Implementation Files

- CellControl.cs: PreviewMouseWheel on text view host, dynamic height on LayoutChanged
- OutputControl.cs: PreviewMouseWheel forwarding on inner ScrollViewer
- WebView2OutputHost.cs: JS wheel interception + WebMessageReceived forwarding

---

## Decision 7: Performance & Reliability Audit Findings (Theo — Threading Specialist)

**Date**: 2026-07-21  
**Lead**: Theo (Threading & Reliability Engineer)  
**Status**: AUDIT COMPLETE — Findings documented, no code changes made  
**Type**: Quality Assurance / Audit Report

### Context

Full audit of the PolyglotNotebooksVS codebase covering async/threading patterns, timeout handling, error recovery, startup performance, runtime execution, and resource management. 48 hand-authored source files reviewed across 8 modules.

### Summary

Overall assessment: **The codebase is well-structured with strong threading discipline**. The `JoinableTaskFactory` patterns are correctly applied throughout. There are several issues of varying severity documented below.

### Findings by Severity

**🔴 CRITICAL (1)**
- **C1**: `.Result` blocking call in `KernelNotInstalledDialog.cs:178` — violates Decision #2 (no `.Result`)

**🟠 HIGH (5)**
- **H1**: `JoinableTaskFactory.Run()` blocks UI thread during `LoadDocData` (installation check + process spawn)
- **H2**: `CancellationTokenRegistration` leaks in `EventObserver.cs:143, 154` (not disposed)
- **H3**: Missing `CancellationToken` parameter in `StopAsync` (`KernelProcessManager.cs`)
- **H4**: Fire-and-forget `AutoRestartAsync` without `JoinableTaskFactory` (`KernelProcessManager.cs:296`)
- **H5**: `Process` leak in `KernelInstallationDetector.cs:100-128` (not disposed)

**🟡 MEDIUM (6)**
- **M1**: Dual timeout inconsistency (30s KernelClient vs 60s EventObserver)
- **M2**: Uncancelled `Task.Delay` timer on timeout path in `EventObserver.cs:157`
- **M3**: `NotebookDocumentManager` not thread-safe (`_openDocuments` plain Dictionary)
- **M4**: `VariableService` singleton initialization not synchronized
- **M5**: `Subject<T>.OnNext` can throw, skipping remaining observers
- **M6**: Full cell rebuild on every collection change (O(N) destruction)

**🟢 LOW (4)**
- **L1**: Sequential `JoinableTaskFactory.Run` calls in `LoadDocData` (lines 79, 104)
- **L2**: `DispatcherTimer` not stopped during rapid document switches
- **L3**: Hardcoded exponential backoff values (1s, 2s, 4s)
- **L4**: `KernelInfoCache.KernelsChanged` uses `Action?` instead of `EventHandler`

### Positive Patterns

1. **JoinableTaskFactory used correctly** throughout; fire-and-forget via `JoinableTaskFactory.RunAsync()`
2. **ConfigureAwait(false) consistently applied** after leaving UI thread
3. **SemaphoreSlim(1,1) serialization** correct in KernelClient, ExecutionCoordinator, CellExecutionEngine
4. **Proper disposal cascade** — event handlers unsubscribed in Close/Dispose
5. **ExtensionLogger JIT-safety pattern** prevents JIT failures in test runner
6. **Crash recovery with exponential backoff** well-implemented (1s → 2s → 4s)
7. **Lock-free StdinPingTimer** uses Interlocked-based state correctly
8. **Timeout protection** in both KernelClient (30s) and EventObserver (60s)

### Recommendations (Priority Order)

1. Fix **C1** — trivial `.Result` → already-awaited value (5-min fix)
2. Fix **H1** — cache installation detection at package init to unblock LoadDocData
3. Fix **H2** — dispose CancellationTokenRegistrations in EventObserver
4. Fix **H5** — dispose Process in KernelInstallationDetector
5. Address **M1** — unify timeout strategy between KernelClient and EventObserver
6. Address **M5** — wrap observer.OnNext in try/catch in Subject<T>

### Related Work

- Decision 7: Loading & Execution Pipeline Architecture Audit (Vince)
- Decision 2: Variable Explorer Architecture (threading implications)
- All threading patterns follow VS SDK guidelines and Decision #2

---

## Decision 9: Variable Explorer Menu Command Registration

**Date**: 2026-07-18  
**Author**: Wendy (UI & Tool Window Specialist)  
**Status**: ACTIVE  
**Type**: UI / Command Registration

### Context

The Variable Explorer tool window existed but had no VS menu command registration — no `.vsct` file, no proper `BaseCommand<T>` class. The package already had `[ProvideMenuResource]`, `[ProvideToolWindow]`, and `RegisterCommandsAsync()` in place.

### Decision

- Created `VSCommandTable.vsct` declaring a "Polyglot Variables" button under **View > Other Windows** using `IDG_VS_WNDO_OTRWNDWS1`.
- Used a new command set GUID (`{b527f541-fc5c-46c9-bc61-e063648877f0}`) separate from the package GUID.
- Used `ImageCatalogGuid`/`VariableProperty` with `IconIsMoniker` for the icon (no custom image needed).
- Rewrote `ShowVariableExplorerCommand` from a static helper to a proper `BaseCommand<ShowVariableExplorerCommand>` referencing auto-generated `PackageIds.ShowVariableExplorer`.
- Removed the hand-written `PackageGuids` class; the auto-generated `VSCommandTable.cs` now provides it.

### Impact

- The "Polyglot Variables" command now appears in the View > Other Windows menu.
- Future commands can be added to the same .vsct file and command set.
- The package class uses a string literal GUID instead of the removed `PackageGuids` constant.

---

## Decision 8: Loading & Execution Pipeline Architecture Audit (Vince — Architect)

**Date**: 2026-07-15  
**Lead**: Vince (Extension Architect)  
**Requested by**: Brady Gaster  
**Status**: AUDIT COMPLETE — Findings documented  
**Type**: Architecture / Performance Analysis

### Context

Thorough source-level audit of the full loading → editing → execution pipeline. Reviewed all initialization paths, cell rendering, kernel lifecycle, and MEF composition. Identified **6 High-impact**, **4 Medium-impact**, and **4 Low-impact** findings.

### Executive Summary

**Biggest performance risks**:
1. Aggressive package auto-load (loads on every VS startup)
2. UI-thread-blocking installation detection during document load
3. Non-virtualized cell rendering creating N HWNDs for N cells
4. Full rebuild of all cells on any single cell collection change

### Findings by Impact

**HIGH-IMPACT BOTTLENECKS (6)**

- **Finding 1**: Triple `[ProvideAutoLoad]` — package loads on NoSolution, SolutionExists, FolderOpened (should be removed; on-demand via ProvideEditorExtension)
- **Finding 4**: Synchronous `dotnet tool list -g` blocks UI thread in `LoadDocData`
- **Finding 7**: Per-cell `IVsCodeWindow` creation — N cells = N HWNDs, N MEF service lookups
- **Finding 8**: Full cell rebuild on any collection change — adding one cell destroys/recreates all
- **Finding 9**: `WebView2` instance per HTML output (expensive initialization per render)
- **Finding 15**: No cell virtualization — `StackPanel` materializes all cells simultaneously

**MEDIUM-IMPACT ISSUES (4)**

- **Finding 2**: `VariableService` singleton created on every VS startup
- **Finding 5**: File I/O on UI thread during document load (large .ipynb files hang)
- **Finding 13**: Classifier provider fires for all text buffers in entire IDE
- **Finding 16**: Temp file accumulation without cleanup

**LOW-IMPACT (4)**

- **Finding 6**: Eager infrastructure construction (acceptable pattern)
- **Finding 12**: Lazy kernel start is POSITIVE — no process if user only views notebook
- **Finding 17**: Regex recompilation in markdown formatting
- **Finding 18**: Reflection usage for ReturnValueElement (fragile but one-time cost)

### Positive Architecture Notes

- **Kernel lifecycle**: Correctly lazy — process only starts on first execution
- **Crash recovery**: Exponential backoff well-implemented (1s → 2s → 4s, 3 max attempts)
- **Threading model**: Consistent `JoinableTaskFactory` throughout
- **Protocol layer**: Clean stdin/stdout JSON-lines with proper event correlation via tokens
- **MEF footprint**: Minimal — one export, no circular dependencies, no eager composition
- **Model/view separation**: `NotebookDocument`/`NotebookCell` clean and independent from UI

### Quick Wins (Easy, High/Medium Impact)

1. Remove `[ProvideAutoLoad]` attributes (~5 min, HIGH impact)
2. Defer `VariableService.Initialize()` to first notebook open (~5 min)
3. Cache MEF services as statics in CellControl (~15 min)
4. Cache Regex in `AddInlineFormatting` (~2 min)

### Medium-Effort, High-Impact

5. Defer installation detection to background task (~30 min)
6. Incremental cell collection updates instead of full rebuild (~45 min)

### Hard, Transformative

7. Cell virtualization (2-3 days) — requires replacing `StackPanel` with virtualization-capable control
8. WebView2 environment sharing (1 day) — pool `CoreWebView2Environment` across outputs

### Related Work

- Decision 6: Performance & Reliability Audit (Theo) — overlaps on H1/Finding 4 (LoadDocData blocking)
- Decision 3: WebView2CompositionControl (airspace fix, separate initiative)

*Audit findings merged from inbox by Scribe, 2026-03-28T14:26:00Z*

---

# Decision: Reliability Fix Patterns for Resource Leaks and JTF Compliance

**Author**: Theo (Threading & Reliability Engineer)  
**Date**: 2026-07-21  
**Status**: IMPLEMENTED

## Context

Audit findings #7, #8, #9 identified three classes of resource leak / threading violation:
1. Process handles not disposed on cancellation paths
2. CancellationTokenRegistration delegates leaking when ct.Register() return value is discarded
3. Fire-and-forget async work bypassing JoinableTaskFactory shutdown tracking

## Decisions

### D1: Always dispose CancellationTokenRegistration
When calling `ct.Register(...)`, capture the return value and dispose it when the awaited task completes:
```csharp
var registration = ct.Register(() => tcs.TrySetCanceled());
try { return await tcs.Task; }
finally { registration.Dispose(); }
```
This prevents the registration delegate from leaking if the CancellationTokenSource outlives the caller.

### D2: Process objects require using + kill-on-cancel
Process objects hold OS handles. Use `using var process = ...` and kill the process on cancellation:
```csharp
using var process = new Process { ... };
try { await tcs.Task; }
catch (OperationCanceledException) {
    try { if (!process.HasExited) process.Kill(); } catch { }
    return null;
}
```

### D3: All fire-and-forget must use JoinableTaskFactory.RunAsync
Never use `Task.Run()` for fire-and-forget in VS extension code. Always use:
```csharp
_ = ThreadHelper.JoinableTaskFactory.RunAsync(() => SomeAsync(...));
```
Suppress `VSTHRD110` and `VSSDK007` with `#pragma warning disable` and a comment explaining the intent.

## Impact
- All agents: Follow these patterns when creating new async code that uses CancellationToken, Process, or fire-and-forget
- Applies to any future kernel management, protocol, or lifecycle code

---

## Decision 10: Use ToolWindowTextKey for Status Text Colors

**Date**: 2025-07-18  
**Author**: Wendy (UI & Tool Window Specialist)  
**Status**: IMPLEMENTED  

### Context

The cell toolbar status text ("⟳ Running", "✗ Error") used `VsBrushes.VizSurfaceGoldMediumKey` and `VsBrushes.VizSurfaceRedMediumKey`. These are data visualization colors — not designed for text — and are invisible on light VS themes.

### Decision

Use `VsBrushes.ToolWindowTextKey` for all status text. The Unicode symbols (⟳, ✗) and the green checkmark icon already convey state visually. Colored text that might be invisible is worse than readable neutral text.

### Rule

**Never use `VsBrushes.VizSurface*` keys for text.** These are chart/graph fill colors with no contrast guarantees against tool window backgrounds. For text, use `ToolWindowTextKey`, `GrayTextKey`, or `InfoTextKey`.

### Implications

- Status indicator repositioned left of execution counter for improved information grouping
- All status states (Running, Error, Completed) now use consistent, readable color
- Applies to all future status/state indicators in toolbar

---

## Decision 11: Kernel Fallback List — Only Built-in Kernels

**Date**: 2025-07-26  
**Author**: Ellie (Editor Extension Specialist)  
**Status**: IMPLEMENTED  

### Context

`KernelInfoCache._fallbackKernels` is the list of kernels shown in the kernel dropdown **before** dotnet-interactive starts and reports its actual kernels via `KernelReady`. The previous list included `javascript`, `typescript`, `sql`, and `markdown` — none of which are built-in dotnet-interactive kernels. Users who selected these got `NoSuitableKernelException`.

### Decision

The fallback list now contains only the four guaranteed built-in kernels: `csharp`, `fsharp`, `pwsh`, `html`.

Extension-installed kernels (JavaScript, SQL, etc.) will appear dynamically once the kernel starts and sends `KernelReady`, which triggers `Populate()` and replaces the fallback.

### Impact

- **Kernel selector**: Fewer entries before kernel startup; accurate entries after startup.
- **Existing notebooks**: No breakage. `SyncKernelComboSelection()` in `CellToolbar.cs` adds ad-hoc entries for any kernel name found in a file but missing from the dropdown.
- **Markdown**: Not affected. Markdown cells use `CellKind.Markdown` and bypass the kernel entirely.

### Team Note

If a new kernel is added to dotnet-interactive as a built-in default in the future, it should be added to `_fallbackKernels` in `KernelInfoCache.cs`.
