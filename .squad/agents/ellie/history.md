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
