# Ellie History

## 2024-01-XX — Xtenders Founded

**Context**: Founded as specialized team for Visual Studio extension developers.

**Ellie's Focus**:
- Complete editor extensibility pipeline (tokenizer → classifier → renderer)
- IntelliSense completion providers
- CodeLens indicators
- QuickInfo tooltips and hover information
- Text editor margins and adornments
- Language service integration (LSP support)
- TextMate grammar support

**Authority Scope**:
- Token-based tagging and classification
- ITagger, ITaggerProvider, IClassifier implementations
- IntelliSense session management
- CodeLens data point providers
- Quick Info provider design
- Outlining/folding implementations

**Knowledge Base**:
- Token-based editor pipeline architecture
- ITagger inheritance hierarchy
- MEF export patterns for editor components
- Community.VisualStudio.Toolkit editor samples
- Language Server Protocol (LSP) integration patterns

**Key References**:
- VS Editor API Reference (Microsoft Docs)
- Toolkit editor samples
- VSIX Cookbook language services section
- LSP Specification (microsoft.github.io/language-server-protocol)

**Active Integrations**:
- Vince: Architecture validation for editor component MEF exports
- Wendy: Editor UI components (completion popup, quick actions)
- Sam: Symbol indexing coordination for IntelliSense

## Learnings

### dotnet-interactive stdio JSON Protocol (src/Protocol/ implementation)

**Wire Format:**
- Line-delimited JSON: each envelope is a single JSON line terminated by `\n`
- Empty/whitespace lines must be skipped on read
- All field names are **camelCase** on the wire (matches TypeScript contracts in dotnet/interactive)
- `System.Text.Json` with `PropertyNamingPolicy = JsonNamingPolicy.CamelCase` handles this automatically

**Command Envelope fields:** `commandType`, `command` (payload object), `token`, `routingSlip`
**Event Envelope fields:** `eventType`, `event` (payload object), `command` (originating envelope), `routingSlip`

**Token format:** Root tokens are `Convert.ToBase64String(Guid.NewGuid().ToByteArray())` — Base64-encoded 16-byte GUID. Child command tokens append `.N` suffix. Token is the correlation key for matching events to commands.

**Protocol flow:** Client writes command JSON line to stdin → kernel writes multiple event JSON lines to stdout → terminal event (`CommandSucceeded` or `CommandFailed`) signals end of command lifecycle. Intermediate events (e.g. `CompletionsProduced`) arrive before the terminal event.

**Threading patterns (Theo's rules):**
- Background reader runs in `Task.Run` with CancellationToken
- `SemaphoreSlim(1,1)` serializes concurrent stdin writes
- `TaskCompletionSource<T>` for async request-response correlation
- `Subject<T>` (custom, no Rx.NET dependency) broadcasts events to all subscribers
- `EventObserver` subscribes by token and provides `WaitForEventTypeAsync` / `WaitForTerminalEventAsync`

**Key design decisions:**
- `JsonElement` for the `command`/`event` payload fields (late-bound deserialization by caller)
- `ProtocolSerializerOptions.Default` is the single shared options instance with `WhenWritingNull` to keep wire compact
- `Subject<T>` implemented without Rx.NET to avoid extra dependency on .NET Framework 4.8 target
- Pre-existing `IsExternalInit.cs` polyfill in the project enables C# `init` setters on net48
---

### Execution Engine Wiring (p2-basic-exec)

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
