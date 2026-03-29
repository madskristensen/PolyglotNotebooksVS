# Ellie History

## Core Context

**Role**: Editor Specialist for VS Extension team. Specializes in editor UI, IntelliSense providers, WPF control design, and keyboard/mouse event handling.

**Authority Scope**:
- Token-based editor tagging and classification
- IntelliSense provider architecture (completion, hover, signature, diagnostics)
- Editor keyboard shortcuts and event routing
- WPF code-only control design (no XAML)
- Debounce patterns and UI marshaling

**Key Knowledge**:
- dotnet-interactive stdio JSON protocol (line-delimited, camelCase fields, token correlation)
- Execution engine architecture (CellExecutionEngine, ExecutionCoordinator lifecycle)
- Cell UI model (Contents, KernelName, ExecutionStatus, ExecutionOrder, Outputs)
- Kernel client lifecycle and event subscriptions
- IntelliSense session management (CompletionProvider, HoverProvider, SignatureHelpProvider, DiagnosticsProvider)

---

## Phase History Summary (P1–P4)

**P1–P2 Foundations**: Kernel protocol, execution engine wiring, cell UI (4 files: NotebookControl, CellControl, CellToolbar, OutputControl). Established protocol patterns (camelCase, token correlation, event streaming), execution threading (fire-and-forget with ThreadHelper), cell model binding (TwoWay, Contents ↔ TextBox).

**P3 Batch – IntelliSense & Kernel Selector**: IntelliSense (5 providers + manager), KernelInfoCache, execution modes (Run Above/Below/Selection), magic command parsing (`#!kernelname`). Added token reflection tests via Theo's test framework. Kernel selector ComboBox wired to CellToolbar with cache refresh on KernelReady event (subscribe-before-wait pattern to avoid race).

**P4 Batch – Execution Modes Final**: Execution modes finalized (RunCellsAboveAsync, RunCellsBelowAsync, RunSelectionAsync, RestartAndRunAllAsync). All handlers wired to NotebookControl event bubbling. IntelliSense fully active after first kernel run (KernelClientAvailable event). Keyboard shortcuts: Shift+Enter (run+advance), Ctrl+Shift+Backspace (clear outputs).

**Total**: Core UI complete with 309 tests passing. Keyboard-driven execution, multi-kernel support, rich diagnostics, hover tips all live.

---

**New files:**
- `src/Execution/CellExecutionEngine.cs` — Cell execution lifecycle manager
- `src/Execution/ExecutionCoordinator.cs` — UI event → kernel bridge

**Actual model shape (differs from task description):**
- `NotebookCell.Contents` (not `Source`), `NotebookCell.KernelName` (not `Language`)
- `NotebookCell.ExecutionOrder` is `int?` (not `ExecutionCount`); set from `CommandSucceeded.ExecutionOrder` or auto-incremented
- `CellOutput(CellOutputKind, List<FormattedOutput>, string? valueId)` — not a flat Text/MimeType model
- `FormattedOutput(string mimeType, string value, bool suppressDisplay)`
- `CellOutputKind`: ReturnValue, StandardOutput, StandardError, Display, Error
- `CellRunEventArgs` (not `CellRunRequestedEventArgs`)
- No `ErrorProduced` POCO class — extracted via `JsonElement.TryGetProperty("message")`

**Threading pattern for output streaming:**
- Subscribe to `KernelClient.Events` observable for intermediate events before sending command
- Filter by `envelope.Token` via `e.Command?.Token`
- Marshal UI updates via `ThreadHelper.JoinableTaskFactory.RunAsync` with `SwitchToMainThreadAsync`
- Use `#pragma warning disable VSTHRD110, VSSDK007` for intentional fire-and-forget output handlers
  (`.Forget()` extension not available in Community.VisualStudio.Toolkit.17 v17.0.549)

**Kernel client lifecycle:**
- `KernelClient` takes the raw `Process` object in its constructor
- Must be created AFTER `KernelProcessManager.StartAsync` completes
- `KernelClient.Start(lifetimeCts.Token)` starts the stdout reader; use a coordinator-lifetime token, NOT per-execution
- `ExecutionCoordinator` owns `KernelClient` and `KernelProcessManager`; both disposed on `Close()`

**Cancellation architecture:**
- Per-execution `CancellationTokenSource` in `ExecutionCoordinator`; replaced (old one cancelled) on each new Run click
- `CellExecutionEngine._executionGate` (SemaphoreSlim) serializes all executions
- On cancellation: send `CancelCommand` to kernel (type string `"Cancel"`) then set cell to Idle
- `_lifetimeCts` in coordinator drives the KernelClient reader loop; separate from execution CTSs

**Kernel name mapping:**
- Cell `KernelName` is stored as dotnet-interactive canonical name (e.g. "csharp", "fsharp")
- `MapKernelName` normalizes display variants ("C#", "c#") → canonical wire names
---

## 2026-03-27 — Phase 3 Batch: CellRunRequested Hook Available (p2-cell-ui)

**Status**: Phase 2.2 COMPLETE — Awaiting Phase 2.3 (Ellie) execution wiring

**What Changed**: Wendy completed NotebookControl UI with CellRunRequested event as the execution integration point.

**Why**: Phase 2.3 (Cell Execution) needs a clear entry point to wire kernel dispatch.

**Integration Point for Ellie**:
- **Hook**: `NotebookControl.CellRunRequested` — `EventHandler<CellRunEventArgs>` event
- **Access**: Via `NotebookEditorPane._control` (NotebookControl instance)
- **Payload**: `CellRunEventArgs.Cell` — the NotebookCell to execute
- **After Execution**: Update cell state:
  - `cell.ExecutionStatus = CellExecutionStatus.Running` (before starting)
  - `cell.ExecutionOrder = <N>` (sequence number after execution)
  - `cell.Outputs.Add(CellOutput item)` (append outputs as they arrive)
  - `cell.ExecutionStatus = CellExecutionStatus.Succeeded` or `Failed` (when complete)
  - UI auto-updates via PropertyChanged/CollectionChanged subscriptions

**Related Decisions**:
- Decision 8: Cell UI Code-Only WPF Pattern
- Decision 5: Custom Editor Architecture (NotebookEditorPane is view + data)

**Status**: READY — Integration point is stable; awaiting Ellie

---

## 2026 — Phase p3-intellisense: IntelliSense Integration COMPLETE

**Status**: COMPLETE — All 4 IntelliSense providers built and wired. Build: 0 errors.

### What was built

**New files** (`src\IntelliSense\`):
- `CompletionProvider.cs` — Debounced (150ms) auto-complete via WPF Popup/ListBox. Triggers on `.` and identifier characters. Keyboard nav: ↑↓/Enter/Tab/Escape. Inserts `InsertText` replacing the typed prefix (tracked via `FindWordStart`).
- `HoverProvider.cs` — Mouse hover tooltips with 500ms debounce. Uses `GetCharacterIndexFromPoint` to resolve hover position. Shows plain-text content from `HoverTextProduced.Content`.
- `SignatureHelpProvider.cs` — Triggered on `(` and `,`; dismissed on `)` or Escape. Shows active signature with active parameter in Bold using WPF `Run` elements.
- `DiagnosticsProvider.cs` — Debounced (500ms), draws zigzag squiggly underlines via `DiagnosticAdorner` (nested `Adorner` subclass). Red=error, Gold=warning. Hover ToolTip shows diagnostic message. Adorner added after `TextBox.Loaded`.
- `IntelliSenseManager.cs` — Central coordinator. Holds `Dictionary<CellControl, CellProviders>`. `AttachToCell`/`DetachFromCell` manage provider lifecycle. `SetKernelClient` propagates to all attached providers.

**Modified files**:
- `CellControl.cs` — Added `private TextBox _editor` field and `internal TextBox CodeEditor => _editor;` property.
- `KernelClient.cs` — Added `RequestSignatureHelpAsync` following the exact pattern of `RequestHoverTextAsync`.
- `ExecutionCoordinator.cs` — Added `public event Action<KernelClient>? KernelClientAvailable` and `public KernelClient? KernelClient => _kernelClient`. Event fires after `_kernelStarted = true`.
- `NotebookControl.cs` — Added `IntelliSenseManager?` property; setter triggers `RebuildCells()`. `RebuildCells` detaches old cells before clear and attaches new cells after creation.
- `NotebookEditorPane.cs` — Subscribes to `KernelClientAvailable`, marshals to UI thread, creates/sets `IntelliSenseManager`. Disposes on Close.

### Key technical decisions

**Kernel availability**: IntelliSense features are inactive until the kernel first starts (triggered by first cell run). `ExecutionCoordinator.KernelClientAvailable` fires once; `IntelliSenseManager.SetKernelClient` distributes the client to all providers.

**TextBox as editor surface**: The project uses a plain WPF `TextBox` (code-only WPF, net48). Adorner layer is accessed via `AdornerLayer.GetAdornerLayer(textBox)` — works because the TextBox is inside `ScrollViewer` which contains an `AdornerDecorator`. Adorner setup deferred to `TextBox.Loaded` event.

**Debounce pattern**: `DispatcherTimer` (UI thread) used for all debouncing. `Stop(); Start();` on each trigger. No `System.Reactive` dependency.

**Fire-and-forget**: Standard `#pragma warning disable VSTHRD110, VSSDK007` + `_ = ThreadHelper.JoinableTaskFactory.RunAsync(...)` pattern, consistent with rest of codebase.

**Char offset helpers**: `CaretToLinePosition(text, caretIndex)` scans the string to produce `LinePosition{Line, Character}` for protocol commands. `GetCharOffset(text, line, char)` is the inverse (used in DiagnosticsProvider).

### Build result
`dotnet build src\PolyglotNotebooks.csproj` → **0 errors, 0 warnings**

---

## 2026-03-27 — Phase 3 Batch: Execution Engine Wiring Complete (p2-basic-exec)

**Status**: Phase 2.3 COMPLETE ✅

**What Changed**: Wired Run button to kernel execution via `CellExecutionEngine` and `ExecutionCoordinator`. Created 2 new files in `src/Execution/`.

**Why**: Phase 2.3 required execution dispatch architecture and intermediate output streaming.

**What Was Built**:
- `CellExecutionEngine.cs` — Cell execution lifecycle manager
- `ExecutionCoordinator.cs` — UI event → kernel bridge

**Key Discoveries**:
- Cell model uses `Contents`/`KernelName` (not `Source`/`Language` from spec)
- Output structure: `CellOutput(CellOutputKind, List<FormattedOutput>, string? valueId)`
- Intermediate events streamed live via `KernelClient.Events` subscription, filtered by token

**Architecture Pattern** (Decision 11):
- `KernelClient` created lazily after `KernelProcessManager.StartAsync()` completes
- Coordinator lifetime token (`_lifetimeCts`) separate from per-execution token
- Output marshalling via `ThreadHelper.JoinableTaskFactory.RunAsync` with pragma suppression
- Fire-and-forget exception handling inside lambda

**Ownership**:
- `NotebookEditorPane` owns `KernelProcessManager` and `ExecutionCoordinator`
- Both disposed in `Close()` and `Dispose(bool)`

**Integration Complete**:
- Subscribe to `NotebookEditorPane._control.CellRunRequested` event
- Update `cell.ExecutionStatus`, `cell.ExecutionOrder`, `cell.Outputs`
- UI auto-renders via PropertyChanged/CollectionChanged

**Related Decisions**:
- Decision 11: Execution Engine Architecture (ACTIVE)
- Decision 2: Async-First, ThreadHelper-Based Threading Model

**Status**: ACTIVE — Ready for Phase 2.4 testing

---

## 2026 — Phases p3-kernel-selector + p4-exec-modes COMPLETE

**Status**: COMPLETE ✅ — Build: 0 errors, 2 pre-existing warnings.

### What was built

**New file:**
- `src/IntelliSense/KernelInfoCache.cs` — Static singleton (`KernelInfoCache.Default`). Pre-populated with 8 known default kernels. `Populate(KernelReady)` refreshes from real kernel info. `Reset()` reverts to defaults on restart. `KernelsChanged` event fires on background thread.

**Modified files:**
- `src/Protocol/Events.cs` — Added `KernelInfoProduced` POCO (has `KernelInfo?` field).
- `src/Execution/CellExecutionEngine.cs` — Added `ExecuteSelectionAsync(cell, selectedText, ct)` which submits only the selected code and **appends** (does not clear) outputs to the original cell.
- `src/Execution/ExecutionCoordinator.cs` — Major overhaul:
  - Extracted `FireAndForget(operation, name)` helper (DRY).
  - Added `RunCellsAboveAsync`, `RunCellsBelowAsync`, `RunSelectionAsync`, `RestartAndRunAllAsync` awaitables.
  - Added fire-and-forget handlers: `HandleRunCellsAboveRequested`, `HandleRunCellsBelowRequested`, `HandleRunSelectionRequested`, `HandleRestartAndRunAllRequested`.
  - `EnsureKernelStartedAsync` now subscribes to `KernelReady` event **before** `WaitForReadyAsync` (avoiding a race) and calls `KernelInfoCache.Default.Populate(kernelReadyInfo)`.
  - `RestartAndRunAllAsync` acquires startup lock, stops kernel, resets state, resets cache, then delegates to `RunAllCellsAsync` (which calls `EnsureKernelStartedAsync` for a fresh start).
- `src/Editor/CellToolbar.cs` — Replaced static language badge with a `ComboBox` (kernel selector). Added `▾` run-dropdown button beside `▶` with Run Above / Run Below / Run Selection items. Added `⊗` Clear Output button. `KernelInfoCache.KernelsChanged` refreshes dropdown via `ThreadHelper.JoinableTaskFactory.RunAsync`. Guard flag `_syncingKernelCombo` prevents cell-property ↔ combo feedback loops.
- `src/Editor/CellControl.cs` — Added `RunAboveRequested`, `RunBelowRequested`, `RunSelectionRequested` (with `RunSelectionEventArgs`) events, bubbled from toolbar. Added `ParseMagicCommand(editor, cell)` called from `TextChanged`: detects `#!<kernelname>` on the first line and updates `cell.KernelName` only when changed. `RunSelectionRequested` handler wired after `_editor` assigned to avoid null dereference.
- `src/Editor/NotebookToolbar.cs` — Added `RestartAndRunAllRequested` event and `↺▶▶` button.
- `src/Editor/NotebookControl.cs` — Added `RunCellAboveRequested`, `RunCellBelowRequested`, `RunSelectionRequested` (`CellRunSelectionEventArgs`), `RestartAndRunAllRequested` events. Added `_focusedCell` tracking via `GotFocus`/`LostFocus`. New keyboard shortcuts: `Shift+Enter` = run focused cell + advance focus; `Ctrl+Shift+Backspace` = clear focused cell outputs. Added `AdvanceFocusToNextCell(CellControl)` helper.
- `src/Editor/NotebookEditorPane.cs` — Subscribed to all new `NotebookControl` events; added `OnRunCellAboveRequested`, `OnRunCellBelowRequested`, `OnRunSelectionRequested`, `OnRestartAndRunAllRequested` handlers; full unsub in `Close()` and `Dispose(bool)`.

### Key technical decisions

**KernelInfoCache subscribe-before-wait pattern:** In `EnsureKernelStartedAsync`, subscribe to `client.Events` for `KernelReady` **before** calling `WaitForReadyAsync`. Since `Subject<T>` does not buffer, subscribing after would miss the event. Using `using` block ensures unsub after wait completes.

**RestartAndRunAll teardown:** Acquires `_startupLock`, disposes old KernelClient + engine, calls `KernelProcessManager.StopAsync()`, then releases lock. `RunAllCellsAsync` → `EnsureKernelStartedAsync` (with `_kernelStarted=false`) restarts fresh. Avoids calling `RestartAsync` (which itself calls stop+start) and then `StartAsync` (which would no-op if running).

**Dispatcher vs JoinableTaskFactory:** `KernelInfoCache.KernelsChanged` fires on background thread. Using `ThreadHelper.JoinableTaskFactory.RunAsync` + `SwitchToMainThreadAsync` (not `Dispatcher.BeginInvoke`) to satisfy VSTHRD001 analyzer.

**ComboBox guard:** `_syncingKernelCombo` bool prevents the `SelectionChanged` handler from re-writing `cell.KernelName` when the combo is updated programmatically (from `PropertyChanged` or cache refresh).

**Magic command parsing:** `ParseMagicCommand` reads `editor.Text` (not `cell.Contents`) on each `TextChanged` to avoid binding timing uncertainty. Only updates `KernelName` when the parsed value differs from current — prevents tight change loops.

### Build result
`dotnet build src\PolyglotNotebooks.csproj` → **0 errors, 2 pre-existing warnings** (WebView2OutputHost.cs VSSDK007, not from this change).


---

## 2026-03-27 — Phase 4 Batch Complete: IntelliSense Integration + Tests + Rich Output + Toolbar

**Status**: COMPLETE ✅ — IntelliSense fully wired; Theo's 69 new tests integrated; Wendy's rich output live; Vince's toolbar available.

**What Changed**: 
- Phase 3 IntelliSense (Decision 13) — 5 providers (Completion, Hover, SignatureHelp, Diagnostics) + IntelliSenseManager. All providers idle until first kernel run (KernelClientAvailable event).
- Phase 2 Tests (Theo) — 69 new unit tests (CellExecutionEngineTests, ExecutionCoordinatorTests, EditorFactoryTests, OutputRoutingTests). Total: 204, all passing.
- Phase 3 Rich Output (Wendy) — 8 MIME types via WebView2OutputHost + ImageOutputControl. DisplayedValueUpdated in-place updates (no flicker).
- Phase 4 Toolbar (Vince) — Run All, Interrupt, Restart, Clear Outputs commands + kernel status indicator + Ctrl+Shift+Enter / Ctrl+. shortcuts.

**Decisions Captured**: Decisions 13, 14, 15 merged into decisions.md.

**Build Status**: ✅ 0 errors  
**Test Status**: ✅ 204 tests, all passing

**Related Decisions**:
- Decision 11: Execution Engine Architecture (still active)
- Decision 2: Async-First, ThreadHelper-Based Threading Model

**Status**: ACTIVE — Phase 4 complete; production-ready for marketplace submission prep

---

## 2026-03-27T19:48:01Z — Final Batch Complete: p3-kernel-selector + p4-exec-modes (Phase 3+4 UI)

**Status**: COMPLETE ✅ — Kernel selector, execution modes, magic commands all finalized

**Cross-Agent Final Integration**:
- **Theo**: 105 new tests validate kernel selector, execution modes, UI shortcuts
- **Wendy**: Variable explorer uses kernel selector for multi-kernel context
- **Vince**: Toolbar status indicator mirrors kernel selector state
- **All**: Decisions 1–2 captured and finalized in decisions.md

**Build**: ✅ 0 errors, 0 warnings  
**Test Coverage**: ✅ 309 tests passing (all phases)  
**Project Milestone**: ✅ All 22 work items complete

**Decisions Captured**:
- Decision 1: KernelInfoCache Population Strategy (subscribe-before-wait, pre-populated defaults, reset on restart)

**Related Decisions**:
- Decision 11: Execution Engine Architecture (ACTIVE)
- Decision 13: IntelliSense Architecture (ACTIVE)

**Status**: COMPLETE — Ready for marketplace preparation

---

## Learnings

### IWpfTextViewHost Keyboard & Resilience Fixes (2026-03-28)
- **Explicit roles fix**: `ITextEditorFactoryService.DefaultRoles` may not include `Editable` and `Interactive`. Always use `CreateTextViewRoleSet(Editable, Interactive, Document, Zoomable)` explicitly when creating text views that must accept keyboard input.
- **WPF focus routing**: When hosting `IWpfTextViewHost.HostControl` inside a `WindowPane`, set `hostControl.Focusable = true` and add a `GotFocus` handler that calls `Keyboard.Focus(textView.VisualElement)`. Without this, the host control receives focus but the text view's editable surface doesn't.
- **Try/catch fallback pattern**: MEF service resolution (`IComponentModel.GetService<T>`) can fail at runtime if assemblies aren't loaded. Wrapping `BuildCodeCellContent` in try/catch with TextBox fallback ensures cells are always editable even if the VS editor platform isn't available.
- **Nullable `_editor` field**: For code cells with IWpfTextViewHost, `_editor` is null. All consumers (IntelliSenseManager, AdvanceFocusToNextCell) must null-check `CodeEditor`. IntelliSenseManager skips attachment (VS editor provides its own IntelliSense). AdvanceFocusToNextCell falls through: `TextView.VisualElement` → `CodeEditor` → `Focus()`.

### ITextDocument Lifecycle & Content Type Fix (2026-07-22)
- **ITextDocument duplicate key**: `GetDataBuffer()` can internally create an `ITextDocument` in `buffer.Properties`, so calling `CreateTextDocument()` afterwards throws a duplicate-key exception. Fix: use `TryGetTextDocument()` to detect the pre-existing document, remove it from properties (and dispose it), then create a fresh one with our fake file path. This ensures Roslyn sees the buffer with a `.cs`/`.fs`/etc. path via `MiscellaneousFilesWorkspace`.
- **HTML content type**: VS resolves lowercase `"html"` to `"WebForms"`, not the modern HTML editor. Changed the `_kernelContentTypeMap` entry to `"htmlx"`, which is VS's modern HTML content type. If `"htmlx"` isn't available, the existing fallback-to-`"text"` logic handles it gracefully.
- **Root cause of "Unexpected buffer without document"**: This VS-internal error was a downstream consequence of the `ITextDocument` duplicate-key failure. With the document properly created, VS's tagger and margin controllers can now find it through the factory's registry.

---

## 2026-03-28 — IWpfTextViewHost Runtime Fixes (Ellie-7)

**Date**: 2026-03-28T01:06:57Z  
**Status**: COMPLETED  
**Task**: Fixed three critical runtime issues with IWpfTextViewHost integration

### What Changed

1. **Resilience via Try/Catch + TextBox Fallback**
   - `BuildCodeCellContent` wrapped in try/catch block
   - On MEF initialization failure or content type resolution failure, falls back to `BuildCodeCellFallback()` (plain TextBox)
   - Logs to ExtensionLogger + VS ActivityLog for debugging
   - Ensures cells are always editable even if VS editor services unavailable

2. **Explicit Text View Roles for Keyboard Routing**
   - Changed from `textEditorFactory.DefaultRoles` to explicit role set: `Editable`, `Interactive`, `Document`, `Zoomable`
   - Uses `CreateTextViewRoleSet()` API for role creation
   - Guarantees keyboard input routing regardless of DefaultRoles configuration

3. **WPF Focus Routing Fix**
   - Added `hostControl.Focusable = true`
   - Implemented `GotFocus` event handler routing to `Keyboard.Focus(textView.VisualElement)`
   - Ensures WPF keyboard focus reaches the actual text view surface when host control receives focus
   - Primary validation point for keyboard input flow

4. **Content Type Diagnostic Logging**
   - Enhanced `ResolveContentType` method with:
     - Log of resolved content type
     - Fallback tracking (when unavailable, logs fallback to "text")
     - Lookup failure logging for debugging
   - Debuggable via VS Activity Log at runtime

5. **Dead Code Cleanup**
   - Removed `_syntaxAdorner` field from CellControl
   - Removed `SetupSyntaxAdorner()` method
   - Removed `OnCellPropertyChanged()` handler and subscription
   - Removed `using PolyglotNotebooks.Editor.SyntaxHighlighting;` from CellControl.cs
   - Kept SyntaxHighlighting folder files (no compilation impact; may be useful for markdown syntax highlighting in future)

6. **Null-Safety for `_editor` Field**
   - Changed `_editor` to nullable `TextBox?` type
   - For code cells with IWpfTextViewHost: `_editor` remains null, `CodeEditor` property returns null
   - IntelliSenseManager: Added null guard on `CodeEditor` property; skips attachment when null (VS editor provides native IntelliSense)
   - `AdvanceFocusToNextCell` in NotebookControl: Updated to handle null case using `textView.VisualElement` instead

### Files Changed

| File | Changes |
|------|---------|
| CellControl.cs | Major refactor of `BuildCodeCellContent`, new `BuildCodeCellFallback()` method, dead code removal, null-safety |
| NotebookControl.cs | Updated `AdvanceFocusToNextCell()` for IWpfTextView/TextBox/null cases |
| IntelliSenseManager.cs | Added null guard on `CodeEditor` property access |

### Build & Test Results

- **Build Status**: Clean (0 errors)
- **Test Results**: 309 tests passing
- **Ready for**: Live keyboard input testing, focus routing validation

### Next Steps

- **Validation**: Live keyboard input required to verify WPF focus fix works end-to-end
- **Fallback Plan**: If keyboard input still fails, next step is `IVsEditorAdaptersFactoryService.CreateVsTextViewAdapter()` + `IVsTextView.Initialize()` for full OLE command target routing (requires Microsoft.VisualStudio.Editor reference)

### Decision Recorded

**Decision 15: IWpfTextViewHost Keyboard & Resilience Fixes** — Added to decisions.md. Addresses keyboard input routing, content type resilience, diagnostic logging, and dead code cleanup.
- **Content type logging**: `ResolveContentType` now logs resolved and fallback content types. Common VS content type strings: "CSharp", "F#", "JavaScript", "TypeScript", "Python", "PowerShell", "SQL", "html", "markdown".
- **Dead adorner code**: `SyntaxHighlightAdorner` field/methods removed from CellControl. The SyntaxHighlighting folder files stay (no compilation impact, possible future use for markdown).
- **IOleCommandTarget fallback**: If WPF focus routing still doesn't work at runtime, the next step is `IVsEditorAdaptersFactoryService.CreateVsTextViewAdapter()` to get full OLE command target chain integration. This requires `Microsoft.VisualStudio.Editor` assembly reference.

### Auto-install dialog pattern (KernelNotInstalledDialog)
- `System.Windows.MessageBox` with `YesNoCancel` is the simplest way to offer 3 options in a VS extension dialog without pulling in TaskDialog or InfoBar infrastructure.
- The `IVsStatusbar` via `ServiceProvider.GlobalProvider.GetService(typeof(SVsStatusbar))` is the correct way to show progress text in the VS status bar from a static context. Requires `SwitchToMainThreadAsync` before access.
- Process execution pattern for `dotnet tool install`: use `ProcessStartInfo` with `RedirectStandardOutput/Error`, `CreateNoWindow = true`, `UseShellExecute = false`. Wire `EnableRaisingEvents + Exited` event to a `TaskCompletionSource<int>` for async awaiting.
- `KernelInstallationDetector.InvalidateCache()` clears both `_cachedIsInstalled` and `_cachedVersion` so the next `IsInstalledAsync()` re-runs detection after installation.
- The call site in `NotebookEditorPane.LoadDocData` already has the `detector` instance available — just pass it through to `ShowAsync(detector)`.
- Build environment note: the VS SDK assemblies (`Microsoft.VisualStudio.*`) aren't resolvable outside the VS dev hive, so `dotnet msbuild` produces ~257 CS0234 errors as baseline. These are all namespace resolution failures, not logic errors. The project builds correctly inside VS.

### View Code / View Designer toggle (F7 / Shift+F7)
- Added `ProvideEditorLogicalView` attribute for `Designer_string` on the package class — registers our editor as the Designer view handler.
- Updated `MapLogicalView` in `NotebookEditorFactory`: `LOGVIEWID_Primary` and `LOGVIEWID_Designer` return `S_OK` (our WPF UI); everything else (including `LOGVIEWID_TextView` and `LOGVIEWID_Code`) returns `E_NOTIMPL` so VS falls back to its built-in text editor.
- Key insight: Previously `LOGVIEWID_TextView` returned `S_OK`, which would prevent VS from opening the text editor for F7. Changing it to `E_NOTIMPL` is what enables the code/designer split.
- No `ProvideEditorLogicalView` needed for `Code_string` or `TextView_string` — by NOT claiming those views, VS naturally falls back to the default text editor for F7.
- No test changes needed: existing `EditorFactoryTests` only test `NotebookDocumentManager`, not `MapLogicalView` (VS SDK types unavailable in test runner).

### Auto-start kernel after install (seamless post-install UX)
- Changed `KernelNotInstalledDialog.ShowAsync` return type from `Task` to `Task<bool>` — returns `true` on successful install, `false` on cancel/failure/docs.
- Removed the "please re-open" success MessageBox; kept status bar message and error/failure MessageBoxes.
- `RunInstallAsync` also changed from `Task` to `Task<bool>` to propagate the install result.
- In `NotebookEditorPane.LoadDocData`, the caller re-checks `detector.IsInstalledAsync()` after a successful install (cache already invalidated by the dialog) and proceeds down the normal "installed" path.
- The kernel starts lazily on first cell run via `EnsureKernelStartedAsync`, so no special kernel-start call is needed in `LoadDocData` — simply confirming `isInstalled = true` is sufficient for the notebook to load in full-capability mode.
- `ExecutionCoordinator` has no install check; it only manages kernel lifecycle after the tool is confirmed present.
---

## 2026-03-27 — View Code / View Designer Logical View Support

**What Changed**: Added F7/Shift+F7 logical view switching capability to editor factory. Now claims Designer logical view via ProvideEditorLogicalView attribute; returns E_NOTIMPL for Code/TextView, allowing VS text editor fallback.

**Why**: Standard VS pattern for multi-view file types. Aligns with .vsixmanifest, .resx, and other designer files. Provides code viewing without custom text editor implementation.

**Affected Areas**: 
- NotebookEditorFactory (MapLogicalView method)
- PolyglotNotebooksPackage (attribute registration)
- Keyboard/UI: F7 now functional; hotkey routing tested

**Status**: ACTIVE — Core capability for notebook file handling

**Related Decision**: Decision 10 (decisions.md)

### Syntax Highlighting — Adorner Overlay Pattern
- **Approach chosen**: Adorner overlay (TextBox.Foreground=Transparent + SyntaxHighlightAdorner on adorner layer), NOT RichTextBox replacement.
- **Why**: All 4 IntelliSense providers (CompletionProvider, HoverProvider, SignatureHelpProvider, DiagnosticsProvider) take `TextBox` in constructors and use `TextBox.Text`, `.CaretIndex`, `.GetRectFromCharacterIndex()`, `.GetCharacterIndexFromPoint()`. Switching to RichTextBox would break them all. The adorner approach preserves 100% backward compatibility — zero provider changes needed.
- **TextBox.CaretBrush**: Must be set explicitly via `SetResourceReference(TextBox.CaretBrushProperty, VsBrushes.ToolWindowTextKey)` because `Foreground=Transparent` also hides the caret by default. `CaretBrush` overrides this.
- **FormattedText alignment**: Uses same `Typeface`/`FontSize` as the TextBox, positioned at `Padding + BorderThickness + 2px` (TextBox internal margin), adjusted for scroll offset from the internal `ScrollViewer`.
- **Scroll sync**: `FindChild<ScrollViewer>(_textBox)` walks visual tree to find the TextBox's internal ScrollViewer, subscribes to `ScrollChanged` for `InvalidateVisual()`.
- **Tokenizer architecture**: Abstract `SyntaxTokenizer` base with `Regex Pattern` + `ClassifyMatch` per language. Static registry maps kernel names (csharp, fsharp, javascript, python, html, sql, pwsh) to tokenizer instances. Named regex groups (`comment`, `string`, `keyword`, `type`, `number`, `preproc`) provide clean token classification.
- **Colors**: Frozen `SolidColorBrush` constants matching VS Dark theme palette (blue keywords, brown strings, green comments, teal types, light-green numbers, gray preprocessor). Default text color uses `VsBrushes.ToolWindowTextKey` for theme awareness.
- **Key files**: `src/Editor/SyntaxHighlighting/SyntaxTokenizer.cs`, `src/Editor/SyntaxHighlighting/SyntaxHighlightAdorner.cs`. Wired in `CellControl.BuildCodeCellContent()`.
- **Performance**: 50ms debounce on text change. `FormattedText.SetForegroundBrush()` applies colors in O(tokens). Clipping prevents off-screen rendering.
- **Language switching**: `CellControl` subscribes to `NotebookCell.PropertyChanged` for `KernelName` changes; calls `_syntaxAdorner.SetLanguage()` which swaps the tokenizer and re-renders.
- **VsBrushes limitation**: `VsBrushes` doesn't expose syntax-specific color keys (those live in the VS editor's classification format map, which requires ITextView). Used hardcoded frozen brushes matching VS Dark theme colors — acceptable for a custom WPF editor.

### IWpfTextViewHost Integration (Code Cells)
- **Date**: 2026-03-27
- **Task**: Replaced WPF TextBox in code cells with hosted VS editor instance (IWpfTextViewHost)
- **Key decisions**:
  - Used MEF services (ITextEditorFactoryService, ITextBufferFactoryService, IContentTypeRegistryService) obtained via IComponentModel/SComponentModel
  - Content type mapping: kernel names → VS content types (CSharp, F#, JavaScript, etc.) with "text" fallback
  - Two-way sync pattern with _suppressBufferSync flag to prevent feedback loops between ITextBuffer.Changed and NotebookCell.PropertyChanged
  - Kept _editor field nullable for code cells (markdown cells still use TextBox); added TextView property for IWpfTextView access
  - SyntaxHighlightAdorner no longer attached for code cells — VS editor provides native syntax highlighting
  - IWpfTextViewHost.Close() called on Unloaded event for proper cleanup
  - Content type updated dynamically when KernelName changes via buffer.ChangeContentType()
  - ParseMagicCommand refactored to accept string (for buffer) with TextBox overload kept for markdown cells
  - Microsoft.VisualStudio.Text.Span fully qualified to avoid ambiguity with System.Windows.Documents.Span
- **Key file**: src/Editor/CellControl.cs (BuildCodeCellContent method)
- **Assembly references**: All needed assemblies (Text.Data, Text.UI.Wpf, Editor, ComponentModelHost) already available transitively via Community.VisualStudio.Toolkit.17


## 2026-03-28 — Markdown & ITextViewHost Implementation Complete

### Cross-Agent Coordination

**Wendy-4** (Markdown Cells): Implemented complete markdown cell rendering + UI integration. Pure WPF StackPanel/TextBlock rendering with double-click edit toggle. Dual ＋Code / ＋Markdown buttons in toolbar. CellToolbar simplified for markdown (no kernel, no run controls). 309 tests passing.

**Ellie-5** (Adorner Syntax Highlighting - SUPERSEDED): Regex-based tokenizer + adorner approach for syntax highlighting. 7 languages supported. Build clean. Later superseded by ITextViewHost approach.

**Ellie-6** (ITextViewHost Integration - ACTIVE): Replaced TextBox in code cells with hosted VS editor. MEF-based content type resolution. Two-way buffer sync with suppression. Dynamic content type updates on kernel change. Build clean, 0 errors.

### Decision Impact

Three new decisions recorded in decisions.md:
- **Decision 5**: Markdown Cell UI Architecture (Wendy-4 work)
- **Decision 6**: Syntax Highlighting via Adorner Overlay (Ellie-5 work, SUPERSEDED)
- **Decision 7**: ITextViewHost for Code Cells (Ellie-6 work, ACTIVE)

### Architecture Pattern Established

CellControl now branches on CellKind in constructor:
- **Code cells**: IWpfTextViewHost (hosted VS editor)
- **Markdown cells**: TextBox with rendered markdown display
- Future cell types will follow same branching pattern

### Outstanding Work

- IntelliSense providers still reference TextBox; migration to IWpfTextView APIs needed (future work)
- Syntax tokenizer/adorner files retained; may be removed if markdown cells don't need them

### Session Documentation

- Orchestration logs: wendy-4, ellie-5, ellie-6 (2026-03-28T00:43:01Z)
- Session log: itextviewhost-markdown (2026-03-28T00:43:01Z)
- Decisions merged into decisions.md; inbox cleared

## Learnings — Keyboard Input & Syntax Highlighting Fix (2025-07-25)

### Architecture Decisions
- **PreProcessMessage override** is the correct VS extensibility pattern for WindowPane subclasses that host WPF text views. Without it, VS's accelerator pre-translation swallows keyboard messages before they reach WPF controls.
- **ITextDocument association** is required for Roslyn's syntactic classifiers to engage on standalone buffers. Creating a document with a fake file name (e.g. `cell.cs`) triggers language service activation by file extension.
- **HTML QuickInfo crash** (`HtmlQuickInfoSource.GetDescription`) is a VS language service bug — non-fatal, caught by AsyncQuickInfoSession. Left alone per Option 2.

### Patterns
- `WindowPane.PreProcessMessage` checks msg range 0x100–0x109 (WM_KEYDOWN through WM_SYSKEYUP) and defers to WPF when any hosted `IWpfTextView.HasAggregateFocus` is true.
- `ITextDocumentFactoryService.CreateTextDocument(buffer, fakeFileName)` is wrapped in try/catch so failure doesn't break cell rendering.
- `GetFakeFileName()` maps kernel names to file extensions; mirrors `_kernelContentTypeMap` but for file association.

### Key File Paths
- `src/Editor/NotebookEditorPane.cs` — PreProcessMessage override for keyboard routing
- `src/Editor/CellControl.cs` — HasFocusedTextView(), ITextDocument creation, GetFakeFileName()
- `src/Editor/NotebookControl.cs` — HasFocusedTextView() iterates child CellControls

### User Preferences
- Mads prefers `base(null)` in NotebookEditorPane constructor (no automation object)
- Build via MSBuild.exe directly, not `dotnet build`
- Old-style csproj; no new files — modify existing only

## Learnings — Win32 Focus Fix for Keyboard Input (2025-07-25)

### Root Cause
- `PreProcessMessage` alone does NOT fix keyboard input in hosted `IWpfTextView` cells. The real issue is that **Win32 focus stays on the parent frame HWND**, not the `ElementHost` HWND that contains WPF content. Even if `PreProcessMessage` returns false, keyboard messages dispatch to the frame's `WndProc`, never reaching the `ElementHost` for WPF translation.

### Fix — Three Complementary Approaches
1. **`GotAggregateFocus` → `SetFocus(HwndSource.Handle)`**: When the text view gets WPF focus, explicitly move Win32 keyboard focus to the hosting HWND via P/Invoke `user32.dll SetFocus`. This ensures `WM_KEYDOWN` etc. go to the right HWND.
2. **`PreviewMouseDown` on hostControl**: Grab both WPF (`Keyboard.Focus`) and Win32 (`SetFocus`) focus on any mouse click, in case `GotAggregateFocus` doesn't fire.
3. **Broadened `HasFocusedTextView()`**: Check `VisualElement.IsKeyboardFocusWithin` in addition to `HasAggregateFocus`, making the `PreProcessMessage` guard more robust.

### Patterns
- `HwndSource.FromVisual(textView.VisualElement)` returns the Win32 HWND hosting the WPF visual tree. `SetFocus(source.Handle)` moves Win32 keyboard focus there.
- `[DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr hWnd);` is placed on the `CellControl` class (already `internal sealed`).
- The `hostControl.GotFocus` handler was enhanced to also call `SetFocus` after `Keyboard.Focus`, ensuring both WPF and Win32 focus are in sync.

### Key Insight
- In VS extensions hosting WPF inside Win32 frames, WPF mouse handling works independently of Win32 focus, but keyboard messages always follow Win32 focus. You must explicitly bridge the two focus systems.

---

## 2026-03-27 --- IVsCodeWindow Adapter Pattern (keyboard-fix)

**Status**: COMPLETE

### What Changed

Refactored CellControl.BuildCodeCellContent() from ITextEditorFactoryService.CreateTextView() (no HWND, broken keyboard input) to the IVsEditorAdaptersFactoryService + IVsCodeWindow adapter pattern. This fixes keyboard input in all hosted code cells.

**Files modified**:
- src/Editor/CellControl.cs -- Core refactor; new _vsTextView/_codeWindow fields; VsTextView property; GetOleServiceProvider() helper; removed SetFocus P/Invoke and GotFocus/PreviewMouseDown hacks
- src/Editor/NotebookEditorPane.cs -- Added IOleCommandTarget; new IVsFilterKeys2-based PreProcessMessage; removed GetFocus/SetFocus P/Invoke
- src/Editor/NotebookControl.cs -- Added GetFocusedCommandTarget()
- src/PolyglotNotebooks.csproj -- Added Microsoft.VisualStudio.TextManager.Interop.8.0 v17.14.40260 PackageReference

### Key Technical Decisions

**Root cause of keyboard failure**: ITextEditorFactoryService.CreateTextView() creates a pure WPF view with no HWND. VS Win32 message loop never delivers keystrokes to it. Fix: IVsEditorAdaptersFactoryService.CreateVsCodeWindowAdapter() which creates a proper HWND-backed editor with its own IOleCommandTarget chain.

**Pattern reference**: Ryan Molden ToolWindowHostedEditorExample on GitHub.

**Namespace gotcha -- critical**: VSUSERCONTEXTATTRIBUTEUSAGE is in Microsoft.VisualStudio.Shell.Interop, NOT in Microsoft.VisualStudio.TextManager.Interop. Requires using Microsoft.VisualStudio.Shell.Interop even though IVsCodeWindowEx is in TextManager.Interop.

**NuGet type-forwarder stubs**: The TextManager.Interop NuGet packages have 0 types -- they are empty stubs that forward to VS runtime assemblies. Had to add explicit Microsoft.VisualStudio.TextManager.Interop.8.0 v17.14.40260 PackageReference so MSBuild picks up the right resolution chain.

**Buffer access in adapter pattern**: Get ITextBuffer from adapter via editorAdapterFactory.GetDataBuffer((IVsTextBuffer)bufferAdapter), NOT from ITextBufferFactoryService. The MEF ITextBuffer is needed for event subscriptions and ChangeContentType.

**IOleCommandTarget on WindowPane**: WindowPane base already implements IOleCommandTarget. Adding explicit implementation intercepts and forwards to focused IVsTextView. OLECMDERR_E_NOTSUPPORTED causes VS to fall back to next target (OleMenuCommandService), so toolbar menu commands still work.

**IVsFilterKeys2 keyboard routing**: PreProcessMessage uses SVsFilterKeys service with TranslateAcceleratorEx + VSTAEXF_UseTextEditorKBScope. Two-pass: first pass fires command; if multi-key chord, second pass with VSTAEXF_NoFireCommand checks if key is handled. Returns true = handled by VS.

**Fallback preserved**: try/catch around CreateVsCodeWindowAdapter falls back to TextBox if creation fails.

**Build result**: MSBuild 0 errors. PolyglotNotebooks.dll, .vsix, and Test.dll all produced.
## Learnings

### MEF Classifier for Notebook Syntax Highlighting (2025)

#### What Was Built
Replaced the dead WPF adorner-based syntax highlighting (SyntaxHighlightAdorner.cs + SyntaxTokenizer.cs) with a proper MEF ITagger<ClassificationTag> pipeline that integrates with VS's native editor classifier infrastructure.

#### Key Files
- src/Editor/SyntaxHighlighting/NotebookClassifierProvider.cs -- MEF [Export(typeof(ITaggerProvider))], [ContentType("text")] provider; guards on "PolyglotNotebook.KernelName" buffer property; caches tagger per buffer via GetOrCreateSingletonProperty.
- src/Editor/SyntaxHighlighting/NotebookClassifier.cs -- ITagger<ClassificationTag> implementation; per-language LanguagePattern (C#, F#, JS, TS, Python, PowerShell, SQL, HTML); fires TagsChanged on ITextBuffer.Changed.
- src/Editor/CellControl.cs -- Sets uffer.Properties.AddProperty("PolyglotNotebook.KernelName", ...) after GetDataBuffer() to mark notebook cell buffers.
- src/PolyglotNotebooks.csproj -- Updated <Compile Include> entries to new filenames.

#### Patterns Used
- **Buffer property sentinel**: Use uffer.Properties.AddProperty("PolyglotNotebook.KernelName", kernelName) in CellControl after getting the data buffer. The classifier provider checks for this property to skip non-notebook buffers.
- **ContentType "text"**: Register classifier on the base "text" content type so it fires regardless of what VS-assigned content type the hosted buffer gets.
- **Classification type names as strings**: PredefinedClassificationTypeNames is not available without adding another assembly reference. Use string literals "keyword", "string", "comment", "number", "class name" directly with IClassificationTypeRegistryService.GetClassificationType().
- **Line-span extension in GetTags**: Extend requested spans to full line boundaries before running regex, then filter intersection back, to handle multi-line comment/string patterns correctly.
- **TagsChanged on buffer.Changed**: Raise TagsChanged for each changed span's new span so the editor re-classifies as the user types.

#### Build Note
Warnings about nullable reference types (CS8603, CS8618) are pre-existing in the project; the new files produce the same pattern of nullable warnings, which is acceptable.

## 2026-03-28T15:29:45Z — Overflow-Aware Scroll + Deferred Height Render

**Status**: COMPLETE ✅

**What Changed**: Implemented scroll event isolation (inner vs. outer) and deferred height rendering to eliminate viewport jitter on Enter key and output growth.

**Why**: Cell output overflow handling leaked scroll events to notebook level, causing unintended document scrolling. Direct height changes during output streaming caused viewport reflow churn.

**Implementation**:
- **CellControl.cs**: Added `OnPreviewMouseWheel()` interception; routes scroll to OutputControl if `HasVerticalOverflow`, else bubbles to notebook.
- **OutputControl.cs**: New `HasVerticalOverflow` property (`ScrollViewer.ExtentHeight > ScrollViewer.ViewportHeight`); added `RenderAsync()` method using `Dispatcher.BeginInvoke()` for deferred layout.
- **WebView2OutputHost.cs**: Updated height binding to use deferred render pattern for smooth resizing.

**Affected Areas**:
- Cell scroll behavior (isolated to output viewer when overflow present)
- Enter key / output streaming (smooth, no jitter)
- Variable Explorer scroll isolation (Wendy)
- UI trace cleanliness (Theo)
- Toolbar command responsiveness (Vince)

**Status**: ACTIVE — Scroll pattern now standard for all OutputControl instances; deferred render extends to any height-changing operation.

**Related Decisions**:
- Decision 2: Async-First Threading (Dispatcher aligns with ThreadHelper)
- Decision 8: Cell UI Code-Only WPF

## Session: Ellie — Classifier Fix + Run Button UX (2026-03-27)

### Task
Fix MEF classifier engagement for syntax highlighting, and disable run buttons during execution.

### Learnings
- **SetLanguageServiceID interference**: Calling SetLanguageServiceID on the VS text buffer adapter installs a COM-level colorizer that blocks MEF ITaggerProvider classifiers entirely. Removing this call is the prerequisite for any MEF-based syntax highlighting to work.
- **Content type forcing and temp file/ITextDocument**: Forcing a content type change after buffer creation and creating an ITextDocument via ITextDocumentFactoryService were both unnecessary and potentially harmful to MEF classifier engagement.
- **View buffer vs data buffer**: The VS editor adapter may expose different ITextBuffer instances for the data buffer and the view's TextBuffer. Setting the PolyglotNotebook.KernelName property on both ensures the MEF tagger sees it regardless of which buffer it receives.
- **Diagnostic logging in MEF providers**: Adding ExtensionLogger.LogInfo at the top of CreateTagger (before any early returns) lets you confirm via ActivityLog whether the provider is being invoked at all during debugging.
- **Run button field promotion**: Promoting unBtn/unDropdownBtn/unAllBtn/estartRunAllBtn from constructor locals to class fields is the pattern for controlling button state from event handlers.
- **SetExecuting pattern**: A SetExecuting(bool) method threading through NotebookControl → NotebookToolbar is the right extensibility hook for callers (e.g. NotebookEditorPane) to toggle toolbar button state.

---

## 2025 — Execution Freeze, Run Button Feedback & Classifier Fix

See commit 000082f. Changes:
- WaitForReadyAsync: 30s timeout (TimeoutException)
- WaitForTerminalEventAsync: 60s timeout (TimeoutException)
- EnsureKernelStartedAsync: cleanup on failure (dispose client/engine)
- HandleCellRunRequested: sets Running immediately before kernel startup
- HandleRunAll/Above/Below/Restart: sets relevant cells Running before fire-and-forget
- ExecutionCompleted event: fired from HandleCellRunRequested finally and FireAndForget finally
- NotebookEditorPane: SetExecuting(true) on RunAll/RestartRunAll; SetExecuting(false) on ExecutionCompleted
- NotebookClassifierProvider: switched ITaggerProvider -> IClassifierProvider
- NotebookClassifier: switched ITagger<ClassificationTag> -> IClassifier
- Regex timeouts: 250ms on all 8 language patterns
- CellControl: IClassifierAggregatorService.GetClassifier(buffer) forces MEF discovery
- Button.BackgroundProperty removed from all toolbar buttons (lets VS theme handle hover)

### Learnings
- ITaggerProvider and IClassifierProvider are distinct MEF contracts; IClassifierProvider more reliable for embedded IVsCodeWindow hosts
- IClassifierAggregatorService.GetClassifier(buffer) forces MEF lazy-load for that buffer
- Setting Button.BackgroundProperty bypasses WPF ControlTemplate hover states - don't set it
- ExecutionCompleted must fire from both single-cell and multi-cell paths

### Learnings — Checkmark Moniker & Classifier Diagnostics (2025-07-25)
- `CellToolbar` uses `CrispImage` + `KnownMonikers` for icon-based indicators; 20x20 for status, 16x16 for buttons
- `_statusIcon` (CrispImage) and `_statusIndicator` (TextBlock) are toggled via Visibility in `UpdateStatusIndicator()`
- `registry.GetClassificationType("class name")` can return null — fall back to `"type"`
- Force initial classification via delayed `ClassificationChanged` event (100ms `Task.Delay`) to handle late classifier creation
- Diagnostic logging in `GetClassificationSpans` is essential for debugging "no color" issues: log kernel name, text length, span count
- Build with: `& "C:\Program Files\Microsoft Visual Studio\2022\Preview\MSBuild\Current\Bin\MSBuild.exe" PolyglotNotebooks.slnx /v:m -restore`
- VSSDK1026 VSIX lock error is benign when VS is running the extension — DLL compiles fine

### Learnings — Deferred Code Window Loading (2026-03-28)
- Two-phase loading pattern: show lightweight TextBox placeholder first, upgrade to IVsCodeWindow via `Dispatcher.BeginInvoke(DispatcherPriority.Background)` after Loaded event
- Key files modified: `CellControl.cs` (placeholder + upgrade logic), `NotebookEditorPane.cs` (async kernel check)
- `BuildCodeCellPlaceholder()` creates a read-only Consolas TextBox at row 1 with VS theme colors — no COM calls, renders in <10ms
- `UpgradeToCodeWindow()` removes placeholder + OutputControl, then calls existing `BuildCodeCellContent()` to create the real IVsCodeWindow
- Guard with `_codeWindowCreated` bool to prevent double-upgrade; check `IsLoaded` to avoid upgrade on disposed controls
- `IntelliSenseManager.AttachToCell()` already short-circuits when `CodeEditor` is null (IVsCodeWindow cells), so no special handling needed
- Kernel installation check (`KernelInstallationDetector`) converted from blocking `JoinableTaskFactory.Run` to fire-and-forget `RunAsync`
- Suppress `VSTHRD110`, `VSTHRD001`, `VSSDK007` pragmas for intentional fire-and-forget and Dispatcher usage

## 2026-03-28 — Performance Optimization: Regex Caching in CellControl

**Event**: Optimized text formatting by caching compiled Regex patterns.

**What Changed**: Converted inline Regex creation in AddInlineFormatting and related methods to static eadonly cached patterns with RegexOptions.Compiled flag.

**Why**: 
- Regex compilation is expensive (~5-50ms per pattern) and was happening on every call
- Particularly noticeable on large notebooks with many cells and many formatting operations
- Identified in prior architecture audit (Vince's Finding 17)

**Files Modified**: src/Editor/CellControl.cs

**Patterns Cached**:
- Bold/italic formatting detection
- Code block delimiters
- Special character escaping

**Impact**: 
- UI thread responds faster during cell rendering and editing
- Startup/initial load time slightly improved
- Zero behavioral change — formatting output identical

**Status**: IMPLEMENTED

**Coordination**: Part of three-agent quick-wins session (Vince + Theo + Ellie, parallel) — orchestration log at .squad/orchestration-log/2026-03-28T1435-ellie.md

### Incremental Cell Collection Updates (2026)

- **Problem**: `OnCellsChanged` called `RebuildCells()` on every `NotifyCollectionChangedEventArgs`, destroying and recreating ALL `CellControl` instances (and their IVsCodeWindow HWNDs) even for a single add/remove.
- **Fix**: Replaced with a `switch` on `e.Action` dispatching to `HandleCellsAdded`, `HandleCellsRemoved`, `HandleCellsMoved`, `HandleCellsReplaced`. Only `Reset` still calls `RebuildCells()`.
- **Add-cell button index fix**: Buttons previously captured a fixed `insertIndex` at creation time, which went stale after incremental changes. Now compute index dynamically from `_cellStack.Children.IndexOf(parentPanel) / 2` at click time.
- **Layout invariant**: `_cellStack` children follow `[AddBtn, Cell, AddBtn, Cell, …, AddBtn]` — AddButtons at even positions (0, 2, 4, …), CellControls at odd positions (1, 3, 5, …). Cell at document index N is at stack position 2*N+1.
- **Move limitation**: WPF `Unloaded` fires on visual tree removal, which triggers CellControl's `_codeWindow?.Close()`. Can't reparent without destroying the HWND. Move handler creates one new CellControl (destroys 1 HWND) instead of rebuilding all.
- **Extracted helper**: `CreateWiredCellControl(NotebookCell)` encapsulates CellControl creation, event wiring (Run/RunAbove/RunBelow/RunSelection/Focus), and IntelliSense attachment — used by both `RebuildCells` and incremental handlers.

---

## 2026-03-28 — Decision 18: Incremental Cell Collection Change Handling

**Session**: 2026-03-28T1439 (Medium-effort perf fixes)
**Lead**: Ellie (Editor Extension Specialist)
**Status**: ACTIVE

### Context
The incremental cell changes work from earlier phases (2026-03-27) laid the groundwork but the handlers were stubs. Full implementation completed: OnCellsChanged no longer calls RebuildCells() for every collection change. Prior implementation was O(N) HWNDs destroyed/created even for single add/remove (5-10s lag on 50-cell notebooks).

### Decision
Replace full-rebuild handler with incremental dispatches by NotifyCollectionChangedAction:
- Add: HandleCellsAdded() — insert new CellControl + AddButtons at position
- Remove: HandleCellsRemoved() — detach IntelliSense, remove affected control
- Move: HandleCellsMoved() — create 1 new CellControl (1 HWND recycled vs all)
- Replace: HandleCellsReplaced() — swap control at position
- Reset: RebuildCells() — only for clear-all/document swap

### Add-Cell Button Index Strategy
Buttons now compute insertion index **at click time** from _cellStack.Children position rather than capturing fixed index at creation. Eliminates stale-index bugs after incremental changes.

### Layout Invariant
_cellStack.Children pattern: [AddBtn, Cell, AddBtn, Cell, ..., AddBtn]
- Even indices: AddButton panels (0, 2, 4, ...)
- Odd indices: CellControl instances (1, 3, 5, ...)
- Cell at document index N → stack position 2*N+1

### Files Modified
- src/Editor/NotebookControl.cs — All changes (new handlers, CreateWiredCellControl extraction)

### Implications
1. Add/remove/move cells now O(1) HWND ops instead of O(N)
2. CreateWiredCellControl(NotebookCell) is single source of truth for cell creation + wiring + IntelliSense attachment
3. Any new cell lifecycle must go through CreateWiredCellControl (not just RebuildCells)
4. Move limitation persists: WPF Unloaded fires on visual tree removal, destroying HWND. True reparenting blocked until CellControl uses explicit Dispose pattern.

### Coordination
Parallel session with Vince (defer install check). Both address startup/edit latency independently. No blocking dependencies.

### Learnings — Compact Code Cell Chrome (2026-03-28)
- To hide the bottom status bar (Ln/Ch/encoding/zoom) on embedded IVsCodeWindow cells, collapse the `bottom` margin container via `textViewHost.GetTextViewMargin("bottom") as IWpfTextViewMargin` and set `VisualElement.Visibility = Collapsed`
- Additional `DefaultTextViewHostOptions` to disable for compact cells: `HorizontalScrollBarId`, `VerticalScrollBarId`, `ZoomControlId`, `SelectionMarginId`, `ChangeTrackingId`
- `_codewindowbehaviorflags.CWB_DISABLEDROPDOWNBAR` (OR'd with `CWB_DISABLESPLITTER`) hides the navigation dropdown bar at the top of the code window
- Key file: `src/Editor/CellControl.cs` lines 227-265 — code window initialization and option configuration

---

## 2026-03-28 — UI Optimization: Bottom Margin Collapse

**Status**: COMPLETE ✅ — Build passes

**What Changed**: Collapsed the bottom status bar on cell text views in CellControl.cs to improve compact notebook interface. Disabled scrollbars, zoom, selection, and change tracking via editor options. Added CWB_DISABLEDROPDOWNBAR flag.

**Files Modified**:
- src/Editor/CellControl.cs

**Team Coordination**: Parallel with Theo (reliability fixes). Both tasks in perf-reliability-round3.

### Learnings — Dynamic Cell Sizing & Scroll Forwarding (2026-03-28)

- **IWpfTextView.LayoutChanged** is the right hook for auto-sizing code cells. It fires after any text change, fold, or layout update. `TextSnapshot.LineCount` gives actual line count for height calculation.
- **PreviewMouseWheel** (tunneling event) fires on WPF wrappers *before* the message reaches native HWNDs like IVsCodeWindow. This is the key to intercepting scroll events that would otherwise be consumed by embedded Win32 controls.
- **VisualTreeHelper.GetParent** walk is the reliable way to find the notebook's outer ScrollViewer from inside a cell. The pattern `FindParentScrollViewer(DependencyObject)` is reusable.
- **WebView2 wheel forwarding** requires a two-part approach: inject JS `wheel` event listener that calls `postMessage`, then handle `CoreWebView2.WebMessageReceived` on the C# side. Must use `{ passive: false }` on the JS listener so `preventDefault()` works.
- **OutputControl's inner ScrollViewer** (MaxHeight=500) also steals wheel events. Forwarding via PreviewMouseWheel on that ScrollViewer ensures the notebook scrolls as a unit.
- Guard JS injection with `window.__polyglotWheelHooked` flag to avoid re-registering on repeated navigations.

---

## 2026-03-28 — Dynamic Code Cell Height & Scroll-Wheel Forwarding

**Status**: COMPLETE ✅ — Build passes (0 errors)

**What Changed**:
1. **Dynamic cell height**: Min changed from 2→1 lines, max from 20→25 lines. Added `LayoutChanged` subscription to auto-size `hostControl.Height` based on actual `TextSnapshot.LineCount`. Initial height also set on creation.
2. **Scroll-wheel forwarding (3 layers)**:
   - CellControl: `PreviewMouseWheel` on `textViewHost.HostControl` forwards wheel to parent ScrollViewer
   - WebView2OutputHost: JS `wheel` listener + `postMessage` → `WebMessageReceived` handler forwards to parent ScrollViewer
   - OutputControl: `PreviewMouseWheel` on inner ScrollViewer forwards to parent ScrollViewer

**Files Modified**:
- src/Editor/CellControl.cs — dynamic sizing, PreviewMouseWheel, FindParentScrollViewer
- src/Editor/WebView2OutputHost.cs — JS wheel injection, WebMessageReceived, FindParentScrollViewer, updated Dispose
- src/Editor/OutputControl.cs — PreviewMouseWheel on inner ScrollViewer, FindParentScrollViewer

## Learnings — Scroll/Resize Fix (2025-07-25)

**Problem 1: Mouse wheel always forwarded to notebook (too aggressive)**
The original PreviewMouseWheel handlers in CellControl, OutputControl, and WebView2OutputHost unconditionally intercepted wheel events and forwarded them to the notebook ScrollViewer. This prevented scrolling inside code cells or outputs with overflow content.

**Fix**: Overflow-aware wheel forwarding pattern:
- **CellControl**: Check `hostControl.Height >= maxH`. If overflow → don't handle (let native editor scroll). If no overflow → forward to notebook. Also toggle `VerticalScrollBarId` option so the editor scrollbar appears when content overflows.
- **OutputControl**: Check `scroll.ScrollableHeight > 0`. If overflow → `return` (let inner ScrollViewer handle). If no overflow → forward to notebook.
- **WebView2OutputHost**: JS-side check: compare `scrollTop + clientHeight < scrollHeight` and `scrollTop > 0`. Only `postMessage` + `preventDefault` when content can't scroll further in the wheel direction.

**Problem 2: Enter key pushes content above out of view**
Setting `hostControl.Height` inside the `LayoutChanged` handler caused layout reentrancy — WPF re-laid out during the text view's own layout pass, causing visual glitches.

**Fix**: Deferred height update via `Dispatcher.BeginInvoke` at `DispatcherPriority.Render`. Added a 0.5px threshold to avoid unnecessary dispatches. Suppressed VSTHRD110/VSTHRD001 with pragma (intentional fire-and-forget Dispatcher usage, same pattern used elsewhere in the codebase).

**Key Pattern**: For WPF controls hosted inside a ScrollViewer, the standard notebook scroll pattern is:
1. Inner content has overflow → let it scroll internally (don't mark `Handled`)
2. Inner content fits → forward wheel to parent ScrollViewer (`e.Handled = true`)
3. Never set `hostControl.Height` inside `LayoutChanged` synchronously — always defer via Dispatcher to avoid layout reentrancy

**Files Modified**:
- src/Editor/CellControl.cs — overflow-aware PreviewMouseWheel, deferred LayoutChanged resize, dynamic VerticalScrollBarId toggle
- src/Editor/OutputControl.cs — overflow-aware PreviewMouseWheel
- src/Editor/WebView2OutputHost.cs — overflow-aware JS wheel injection

### Left-Side Margin Removal (2026-03-28)
**Task**: Collapse all left-side editor margins (indicator/breakpoint gutter, selection margin, line numbers) from code cell IVsCodeWindow text views.
**Approach**: The options were already set to false (GlyphMarginId, LineNumberMarginId, SelectionMarginId, ChangeTrackingId), but the left margin container can still occupy space. Added 	extViewHost.GetTextViewMargin("left") collapse, mirroring the existing bottom margin pattern.
**Files Modified**: src/Editor/CellControl.cs — added left margin container collapse at line 267-270.

### Bug Fix: Margins Reappear on Language Change (2026-07-25)
**Problem**: When the user changes the kernel/language from the dropdown, `buffer.ChangeContentType()` triggers VS to rebuild margin containers, re-showing previously collapsed bottom and left margins.
**Fix**: Added deferred margin re-collapse in the `KernelName` PropertyChanged handler. After calling `ChangeContentType`, we dispatch at `Render` priority to re-hide the "bottom" and "left" margins using the same `GetTextViewMargin` + `Visibility.Collapsed` pattern used during initial creation.
**Key Pattern**: Any operation that changes content type on an IVsCodeWindow buffer can cause margin reconstruction. Always re-apply margin hiding after content type changes.
**Files Modified**: src/Editor/CellControl.cs — KernelName handler in PropertyChanged lambda (~line 362-381).

### Bug Fix: Code Cell Minimum Height (2026-07-25)
**Problem**: When all text is deleted from a code cell, the auto-sizing logic could shrink the editor below one visible line because `TextSnapshot.LineCount` wasn't defensively clamped.
**Fix**: Added `Math.Max(1, ...)` around `textView.TextSnapshot.LineCount` in both the `LayoutChanged` handler and the initial height calculation. This ensures the height never drops below `lineHeight + padding` even in edge cases.
**Files Modified**: src/Editor/CellControl.cs — lines 286 and 306.

### Learnings — Cell Auto-Sizing Clipping Fix (2026-07-25)

**Problem**: Code cell text was clipped at the bottom — last line partially visible on both initial load and after buffer changes. Root cause: the height calculation used `FontFamily.LineSpacing * fontSize` (a WPF font metric), but the VS text editor renders lines at a different height available via `IWpfTextView.LineHeight`.

**Fix** (4 changes in `CellControl.cs`):
1. **LayoutChanged handler**: Replaced `lineHeight` (WPF estimate) with `textView.LineHeight` (actual VS editor metric). Also recomputes min/max dynamically and pushes them via Dispatcher along with the height to keep everything consistent.
2. **Initial height after text view creation**: Uses `textView.LineHeight` (with fallback to WPF estimate) to recompute `minH`/`maxH` and initial height before first layout.
3. **Padding buffer**: Increased `editorVerticalPadding` from `4+4` (8px) to `6+6` (12px) to better account for VS editor internal chrome.
4. **Mouse wheel handler**: Changed `maxH` (stale captured variable) to `hostControl.MaxHeight` (dynamically updated by LayoutChanged).

**Key Insight**: `IWpfTextView.LineHeight` is the authoritative line height for VS editor auto-sizing. The WPF `FontFamily.LineSpacing * fontSize` is only a rough pre-layout estimate — always prefer the editor's own metric once the text view exists.

### ITextDocument Lifecycle Fix: Rename Instead of Remove/Recreate (2026-07-25)

**Problem**: ITextDocument creation crashed with `"Key already added: WasAssociatedWithTextDocument"` for ALL languages. Disposing/removing the existing ITextDocument left a stale string key `"WasAssociatedWithTextDocument"` in buffer.Properties. `CreateTextDocument` then tried to add it again → crash. This blocked Roslyn C# QuickInfo/IntelliSense downstream.

**Fix (Option A — Rename)**: Instead of removing + recreating the ITextDocument, call `existingDoc.Rename(fakePath)` on the pre-existing document. This changes the FilePath without touching any buffer properties, completely avoiding the property collision. Fallback path (no existing doc) cleans up both `typeof(ITextDocument)` and `"WasAssociatedWithTextDocument"` string keys before `CreateTextDocument`.

**HTML content type fix**: Changed `"htmlx"` → `"HTML"` in `_kernelContentTypeMap`. The `"htmlx"` content type doesn't exist in VS 2022; `"HTML"` is the registered name.

**Key Insight**: `ITextDocument.Rename(string)` is the safe way to change a document's file path on an existing buffer. Never dispose + recreate — the VS text infrastructure scatters additional property keys (string-keyed, not just typed) that are impossible to fully clean up.

**Files Modified**: `src/Editor/CellControl.cs` — lines ~53 (HTML content type), ~219-246 (ITextDocument lifecycle).

### KernelLanguageMap extraction for testability
**Date**: 2026-03-28
**Task**: Add unit tests for content type mapping and fake file name generation
**Problem**: The kernel→content-type dictionary and GetFakeFileName were both private static inside CellControl.cs, making them untestable without reflection. Past regressions included `sql` mapping to `SQL` instead of `SQL Server Tools`, and `html` mapping to `htmlx` instead of `HTML`.
**Solution**: Extracted both the content type dictionary and the file extension mapping into a new `internal static class KernelLanguageMap` in `src/Editor/KernelLanguageMap.cs`. CellControl now delegates to it. Created 30+ regression tests in `test/PolyglotNotebooks.Test/KernelLanguageMapTests.cs` covering every kernel mapping, case insensitivity, unknown/null fallbacks, and fake file path structure. All 346 tests pass (0 failures).
**Key Insight**: The main project already had `[assembly: InternalsVisibleTo("PolyglotNotebooks.Test")]` in Properties/AssemblyInfo.cs, so `internal` visibility was sufficient. The old-style .csproj requires explicit `<Compile Include>` entries for new files.
**Files Created**: `src/Editor/KernelLanguageMap.cs`, `test/PolyglotNotebooks.Test/KernelLanguageMapTests.cs`
**Files Modified**: `src/Editor/CellControl.cs`, `src/PolyglotNotebooks.csproj`

### RDT_VirtualDocument Flag Removal for Roslyn DataTip Fix (2026-07-25)

**Problem**: After fixing the ITextDocument lifecycle (Rename fix), hover/DataTip still failed for C# cells with "Document is null when it was required for textdocument/_vs_dataTipRange". Root cause: RDT_VirtualDocument flag in RegisterAndLockDocument told VS the buffer wasn't a real document. Roslyn's MiscellaneousFilesWorkspace filters out virtual documents, so it never received 	extDocument/didOpen for the buffer. HTML/JS worked because their LSP servers activate on content type alone, but Roslyn requires workspace-level document registration.

**Fix**: Removed _VSRDTFLAGS.RDT_VirtualDocument from the RDT registration flags, changing from RDT_ReadLock | RDT_VirtualDocument | RDT_DontAddToMRU to RDT_ReadLock | RDT_DontAddToMRU. The file already exists on disk (written via File.WriteAllText), so marking it as virtual was incorrect and actively harmful.

**Key Insight**: RDT_VirtualDocument prevents Roslyn's MiscellaneousFilesWorkspace from discovering the document. For notebook cell buffers backed by real files on disk, omit this flag so language services that rely on workspace document tracking (like Roslyn C#) can provide hover, completion, and diagnostics.

**Files Modified**: src/Editor/CellControl.cs — line 247 (RDT flags).

## Learnings — DataTip Diagnostic Logging (2026-07-25)

**Task**: Add comprehensive `[DIAG]`-prefixed diagnostic logging to `BuildCodeCellContent` in CellControl.cs to investigate C# DataTip error ("Document is null when it was required for textdocument/_vs_dataTipRange").

**What was added** (4 diagnostic points, all wrapped in try/catch to never crash main flow):

1. **After ITextDocument Rename** (line ~219): Logs FilePath, IsDirty, FileExists after rename — verifies the fake path is properly set.
2. **After RDT registration** (line ~280): Logs cookie and moniker, then calls `FindAndLockDocument` with `RDT_NoLock` to verify the RDT can find the document by moniker. Logs HRESULT and returned cookie.
3. **Buffer properties** (line ~372): Logs `PolyglotNotebook.KernelName`, `WasAssociatedWithTextDocument`, and `ITextDocument` property presence/values on the buffer.
4. **Roslyn workspace check** (line ~398): Uses reflection to call `Microsoft.CodeAnalysis.Text.Extensions.AsTextContainer(buffer)` → `Workspace.TryGetWorkspace()` → `Solution.GetDocumentIdsWithFilePath(fakePath)`. Reports workspace type and document count. Gracefully degrades if Roslyn assemblies aren't loaded.

**Key decisions**:
- `IVsRunningDocumentTable4.IsMonikerRegistered` is not available in our SDK version — used `FindAndLockDocument` with `RDT_NoLock` flag instead (read-only probe).
- `Microsoft.CodeAnalysis.Workspaces` is not directly referenced — used reflection via `AppDomain.CurrentDomain.GetAssemblies()` to find Roslyn assemblies at runtime.
- All logging uses `ExtensionLogger.LogInfo` (not Warning/Error) with `[DIAG]` prefix for easy ActivityLog filtering.

**Files Modified**: src/Editor/CellControl.cs — 4 diagnostic blocks added to BuildCodeCellContent.

## Learnings — IVsInvisibleEditorManager for Roslyn Integration (2026-07-25)

**Problem**: C# cells had syntax highlighting but NOT Roslyn semantic colorization, IntelliSense, or QuickInfo. Despite setting content type to "CSharp", setting language service GUID, writing fake .cs files, renaming ITextDocument, and registering in RDT — Roslyn never engaged. HTML/JS worked because their language services activate by content type alone.

**Root cause**: Roslyn's MiscellaneousFilesWorkspace subscribes to RDT *events* (specifically `OnAfterFirstDocumentLock`). Our direct `RegisterAndLockDocument` call may not fire the same events that Roslyn is listening for. Roslyn needs documents to be opened through VS's standard document pipeline.

**Solution**: Implemented `IVsInvisibleEditorManager.RegisterInvisibleEditor()` approach for C#/F# kernels:
1. Write fake `.cs` file to disk (already done)
2. Call `RegisterInvisibleEditor()` with the fake path — this opens the file through VS's full document pipeline, triggering all RDT events Roslyn subscribes to
3. Extract the `IVsTextLines` buffer from the invisible editor via `GetDocData()`
4. Use THAT buffer as our code window's buffer instead of creating a new one via `CreateVsTextBufferAdapter`
5. This means one buffer, one document — Roslyn sees it natively

**Key architecture decisions**:
- `NeedsRoslynWorkspace()` helper gates the invisible editor path — only C# and F# use it; HTML/JS/etc. keep the existing `CreateVsTextBufferAdapter` path
- Invisible editor's buffer already has correct content type and language service — no need to call `SetLanguageServiceID` or `ChangeContentType` for these kernels
- Buffer content is synced: if cell content differs from disk content, we replace the invisible editor's buffer text
- `_invisibleEditor` COM reference stored for cleanup; released via `Marshal.ReleaseComObject` in Unloaded handler
- For non-Roslyn kernels, all existing behavior (CreateVsTextBufferAdapter, ITextDocument, RDT registration) is preserved unchanged

**Files Modified**: src/Editor/CellControl.cs — BuildCodeCellContent refactored with dual-path buffer creation, NeedsRoslynWorkspace helper added, cleanup handler updated.

## Learnings

### Invisible Editor Approach Reverted (Roslyn Integration)
- IVsInvisibleEditorManager caused DataTipServiceImpl errors when hovering over C# code cells
- All languages now use standard CreateVsTextBufferAdapter + SetLanguageServiceID path
- GetLanguageServiceGuid() provides colorization GUIDs for C#, F#, JS, HTML, SQL
- RDT registration and ITextDocument creation run uniformly for all languages
- Removed: NeedsRoslynWorkspace(), _invisibleEditor field, invisible editor path, all [DIAG] logging

### Markdown Double-Click Fix
- WPF StackPanel without Background is transparent to hit-testing
- Fix: Background = Brushes.Transparent on RenderMarkdownToPanel StackPanel
- Background = Transparent alone was insufficient — WPF's MouseLeftButtonDown is a Direct routed event and doesn't reliably bubble from child TextBlocks/Borders to the parent StackPanel
- Fix: Switched to PreviewMouseLeftButtonDown (tunneling) which fires on the StackPanel before children, guaranteeing double-click detection
- Also set e.Handled = true on double-click to prevent further tunneling after entering edit mode
- Added PreviewKeyDown handler on the editor TextBox for Escape key to exit edit mode
- Deferred _editor.Focus() via Dispatcher.BeginInvoke(DispatcherPriority.Input) to ensure WPF layout pass completes before focusing the newly-visible TextBox

### Key File Paths
- src/Editor/CellControl.cs: buffer creation, language service wiring, markdown edit toggle
- Activity log: %APPDATA%\Microsoft\VisualStudio\18.0_*Exp\ActivityLog.xml

### Learnings — Kernel Fallback List Fix (2026-03-28)

- **Problem**: `KernelInfoCache._fallbackKernels` included `javascript`, `typescript`, `sql`, and `markdown` which are NOT built-in dotnet-interactive kernels. Users selecting these before kernel startup got `NoSuitableKernelException`.
- **Fix**: Trimmed fallback list to only guaranteed built-in kernels: `csharp`, `fsharp`, `pwsh`, `html`. Removed `javascript`, `typescript`, `sql`, `markdown`.
- **Why markdown is safe to remove from fallback**: Markdown cells use `CellKind.Markdown` and are handled by the editor directly, not dispatched to the kernel. They don't need to appear in the kernel dropdown.
- **Dynamic kernels still work**: When the kernel starts and sends `KernelReady`, `Populate()` replaces the fallback with the real kernel list. Extension-installed kernels (JS, SQL, etc.) appear automatically.
- **Existing notebooks safe**: `SyncKernelComboSelection()` in `CellToolbar.cs` adds ad-hoc dropdown entries for any kernel name found in a notebook file but not in the current list.
- **Key file**: `src/IntelliSense/KernelInfoCache.cs`
## 2026-03-28T21:20Z — Kernel Fallback List Fix

**Status**: COMPLETE ✅ — Trimmed _fallbackKernels to only built-in kernels (csharp, fsharp, pwsh, html)

**What Changed**:
- **KernelInfoCache.cs**: Removed javascript, 	ypescript, sql, markdown from fallback list. These are not guaranteed built-in kernels and caused NoSuitableKernelException when selected before kernel startup.
- **Dynamic Population**: Extension-installed kernels now populate correctly after KernelReady event triggers Populate().
- **Markdown Handling**: Markdown cells use CellKind.Markdown and bypass kernel selection entirely — not affected.
- **Backward Compatibility**: SyncKernelComboSelection() in CellToolbar.cs adds ad-hoc entries for any kernel name in existing notebooks but missing from dropdown.

**Rationale**: Fallback list is displayed before dotnet-interactive starts and reports actual available kernels. Including non-built-in kernels in the fallback was misleading and caused errors. Dynamic population via KernelReady ensures accuracy after startup.

**Build Status**: ✅ Clean (0 errors, 309 tests passing)

**Cross-Agent**: Works seamlessly with Wendy's toolbar color fix (status text now clearly visible for any kernel in fallback).

**Related Decision**: Decision 11 (Kernel Fallback List — Only Built-in Kernels)

**Future Notes**: If dotnet-interactive adds new built-in default kernels, update _fallbackKernels accordingly.

---



## 2025-03-28 — Cross-Agent Update: BaseToolWindow<T> Pattern from Wendy

**From**: Wendy (UI & Tool Window Specialist)  
**Topic**: Tool Window Initialization Pattern

PolyglotNotebooksPackage.InitializeAsync() now calls VariableExplorerToolWindow.Initialize(this) to satisfy a Community.VisualStudio.Toolkit requirement. All future tool windows must follow this pattern, or ShowAsync() fails silently.

**Your Action**: When adding new BaseToolWindow<T> subclasses, ensure they're initialized during InitializeAsync. This is NOT optional—the toolkit has no auto-discovery mechanism.

---

## Learnings — Mermaid Diagram Rendering Support

### Output Routing Pattern for New MIME Types
- To add a new output type, modify both OutputControl.cs (detection/routing) and WebView2OutputHost.cs (rendering)
- Use ResolveEffectiveMimeType() to upgrade 	ext/plain to a richer MIME type when context (kernel name, content keywords) allows
- New MIME cases go in the switch block in CreateElementForMimeType()
- WebView2OutputHost supports multiple public methods: SetHtmlContent() for raw HTML, SetMermaidContent() for diagrams

### Mermaid Detection Logic (3-tier)
1. **MIME type**: 	ext/vnd.mermaid — direct match from dotnet-interactive's #!mermaid kernel
2. **Kernel name**: Cell KernelName == ""mermaid"" with 	ext/plain output → treat as mermaid
3. **Content keywords**: 	ext/plain starting with Mermaid keywords (graph, sequenceDiagram, classDiagram, etc.) → auto-detect

### VS Theme Integration for Mermaid
- IsDarkBackground() checks luminance of VsBrushes.ToolWindowBackgroundKey (threshold 0.5)
- Maps to mermaid themes: dark → ""dark"", light → ""default""
- Error color adapts: dark → #f48771, light → #d32f2f

### Mermaid Rendering Architecture
- Uses CDN: https://cdn.jsdelivr.net/npm/mermaid/dist/mermaid.min.js
- securityLevel: 'strict' — prevents script execution in diagrams
- Mermaid content stored in hidden <pre> element, read by JS to avoid string escaping issues
- Uses mermaid.render() async API (mermaid v10+) with try/catch for error display
- Invalid diagrams show error message + raw source text (not blank output)

### Key File Paths
- src/Editor/OutputControl.cs — ResolveEffectiveMimeType(), StartsWithMermaidKeyword(), MermaidKeywords[]
- src/Editor/WebView2OutputHost.cs — SetMermaidContent(), BuildMermaidHtml(), IsDarkBackground()

### Build Commands
- MSBuild (VS 2022): `& ""C:\Program Files\Microsoft Visual Studio\2022\Preview\MSBuild\Current\Bin\MSBuild.exe"" src\PolyglotNotebooks.csproj /t:Build`
- Tests: `dotnet test test\PolyglotNotebooks.Test --no-build`
- Note: `dotnet build` fails with pre-existing NuGet/SDK resolution issues; use MSBuild directly
