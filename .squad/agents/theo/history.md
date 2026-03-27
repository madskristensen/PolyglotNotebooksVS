# Theo History

## Core Context

**Role**: Threading & Reliability Engineer for VS Extension team. Specializes in JoinableTaskFactory patterns, async/await validation, threading audits, and reliable error handling.

**Authority**: All async/await validation, SDK Analyzer compliance, CancellationToken enforcement, threading audit before code review.

**Key Principles**:
- Async-first threading model (Decision #2)
- JoinableTaskFactory for main thread safety
- No `.Result` or `.Wait()` — violates analyzer rules
- CancellationToken mandatory in all async APIs
- All VS-only facade code needs JIT-safety pattern (split public wrapper + [NoInlining] private helper)

**Knowledge**: VS threading rules, KernelProcessManager lifecycle (Process.Start off UI thread, WaitForExitAsync via TaskCompletionSource on net48, Exited handler on ThreadPool thread), StdinPingTimer lock-free design (Interlocked for thread-safe int state), SemaphoreSlim for serialization, SDKAnalyzers, ActivityLog debugging.

---

## Phase 1 Learning: Kernel Process Manager Threading Model

**Process.Start off UI thread**: `StartAsync` uses `Task.Run(() => LaunchProcess(), ct)` to ensure `ProcessStartInfo` construction and `Process.Start()` never block the VS UI thread. Process object is assigned inside background work, then consumed via public `Process` property.

**Exited handler design**: `EnableRaisingEvents = true` causes `Exited` to fire on ThreadPool thread. Handler does lightweight work: read `ExitCode` (try/catch for races), snapshot stderr under lock, set status, raise event. No awaits, no blocking calls.

**Restart serialization**: `SemaphoreSlim(1,1)` gates `RestartAsync` entry, queuing callers safely without deadlock on UI thread; honors `CancellationToken` throughout.

**WaitForExitAsync on net48**: No native API. Implemented via `TaskCompletionSource<bool>` + `process.Exited` event subscription + `Task.Delay` timeout fallback. Guarded subscribe/HasExited check for race where process exits before subscription.

**StdinPingTimer lock-free**: Uses `Interlocked.CompareExchange` on `int` field (not bool) for lock-free Start/Stop; timer callback reads field with `Volatile.Read`. Disposing done only by CAS winner.

**Stderr ring-buffer**: 100-line cap under `_stderrLock` (dedicated object, not `this`). Diagnostics without unbounded memory growth.

**Dispose pattern**: Sets `_intentionalStop = true` before killing (Exited handler is no-op during cleanup); unsubscribes event handlers before `Process.Dispose()` to prevent post-dispose callbacks.

---

## Phase 1 Testing: Unit Test Patterns (135 tests)

**MSTest.Sdk 4.0.1 limitation**: `Assert.ThrowsException<T>()` unavailable on net48. Replaced with try/catch + `Assert.IsTrue(threw, ...)` pattern. Now team standard (Decision 4).

**ProtocolSerializerOptions is internal**: Tests define `TestSerializerOptions.Default` locally with identical settings (CamelCase + WhenWritingNull + CaseInsensitive).

**JSON error handling**: `JsonSerializer.Deserialize<T>` throws `JsonException` on malformed input (never returns null). `KernelClient.DispatchLine` correctly catches and logs.

**Build order**: Main project (`src/`) must build first since tests project-reference it. Both projects build clean: 0 errors, 0 warnings (after fixes).

---

## 2026-03-27 — Phase 5 Reliability Complete (p5-reliability)

**Status**: COMPLETE ✅ — All 135 tests passing

**What Changed**: Implemented 5 reliability pillars:
1. Crash recovery with exponential backoff (1s, 2s, 4s)
2. Kernel-not-installed dialog with browser launch
3. ExtensionLogger JIT-safety pattern (enables testable logging of VS facades)
4. Protocol error handling (malformed JSON, 30s command timeouts)
5. Proper disposal cascade (event unsubscribe, no post-dispose callbacks)

**Why**: User task p5-reliability — make the extension resilient to kernel crashes, network issues, and missing dependencies.

**Affected Areas**:
- Ellie (Phase 2.3): Command execution must handle timeouts (30s default)
- All agents: ExtensionLogger JIT-safety pattern must be applied to any new VS-specific facades
- Penny: Build still passes clean (no breaking changes)

**Critical Pattern — ExtensionLogger JIT-Safety**: When testable code calls VS-only types (ActivityLog, InfoBar, etc.), the JIT fails before try-catch executes. Solution: split into public wrapper (try-catch) + private [NoInlining] helper. The wrapper's catch block catches the JIT compilation failure at runtime.

**Status**: ACTIVE — Ready for integration into Phase 2.3

---

## 2026-03-27 — Phase 5 Reliability Complete (p5-reliability)

**Status**: COMPLETE ✅ — All 135 tests passing

**What Changed**: Implemented 5 reliability pillars:
1. Crash recovery with exponential backoff (1s, 2s, 4s)
2. Kernel-not-installed dialog with browser launch
3. ExtensionLogger JIT-safety pattern (enables testable logging of VS facades)
4. Protocol error handling (malformed JSON, 30s command timeouts)
5. Proper disposal cascade (event unsubscribe, no post-dispose callbacks)

**Why**: User task p5-reliability — make the extension resilient to kernel crashes, network issues, and missing dependencies.

**Affected Areas**:
- Ellie (Phase 2.3): Command execution must handle timeouts (30s default)
- All agents: ExtensionLogger JIT-safety pattern must be applied to any new VS-specific facades
- Penny: Build still passes clean (no breaking changes)

**Critical Pattern — ExtensionLogger JIT-Safety**: When testable code calls VS-only types (ActivityLog, InfoBar, etc.), the JIT fails before try-catch executes. Solution: split into public wrapper (try-catch) + private [NoInlining] helper. The wrapper's catch block catches the JIT compilation failure at runtime.

**Status**: ACTIVE — Ready for integration into Phase 2.3

---

## Phase 2 Testing: Phase 2 Unit Tests (p2-tests)

**Status**: COMPLETE ✅ — 204 tests passing (69 new tests added)

**What was added:**
- `CellExecutionEngineTests.cs` (31 tests): Reflection-based tests for private static `MapKernelName` (21 mapping cases covering all kernel aliases) and `IsTerminalEvent` (8 event-type cases), plus constructor null guard and double-dispose safety. Uses `Process.GetCurrentProcess()` as a stub process to construct a valid `CellExecutionEngine` without a live kernel.
- `ExecutionCoordinatorTests.cs` (4 tests): Constructor null guard, double-dispose safety, and `RunAllCellsAsync(null)` null-document guard (exercisable because the `throw` fires synchronously before the first `await`).
- `EditorFactoryTests.cs` (13 tests): `NotebookDocumentManager` lifecycle — `RegisterDocument`, `IsOpen`/`GetDocument`, `CloseAsync`, event firing (DocumentOpened, DocumentClosed, DocumentDirtyChanged), case-insensitive path lookup, and the rename scenario. `NotebookEditorFactory` itself is untestable (VS `IVsEditorFactory` interface makes class-loading uncertain in test runner).
- `OutputRoutingTests.cs` (21 tests): `FormattedOutput` and `CellOutput` model coverage (MIME types, output kinds, SuppressDisplay, ValueId, null-FormattedValues, multiple values), output accumulation in `NotebookCell.Outputs`, and a single `OutputControl` WPF construction test on STA thread.

**VS SDK JIT-blocking boundaries discovered:**
- `OutputControl.Rebuild()` directly references `VsBrushes` (VS SDK). Any test that sets `OutputControl.Cell` triggers JIT of `Rebuild()` → `FileNotFoundException` for `Microsoft.VisualStudio.Shell.15.0`. Only the no-Cell construction path is testable.
- `ExecutionCoordinator.HandleCellRunRequested()` directly references `ThreadHelper` in its method body. Even null-args tests that would hit the early return fail at JIT time. Not testable in unit-test runner.
- `NotebookEditorFactory.MapLogicalView` references `VSConstants.LOGVIEWID_Primary` (a `static readonly Guid`, not a const), so JIT of `MapLogicalView` requires VS Shell assembly. Not testable.
- Methods that call ONLY VS types via the JIT-safety pattern (ExtensionLogger, Decision 9) ARE safe to call from tests — the safety pattern prevents JIT failure propagation.

**InternalsVisibleTo**: Added via `<AssemblyAttribute Include="...InternalsVisibleToAttribute">` in the main project's `.csproj`. This allows tests to directly reference `internal` classes like `CellExecutionEngine` and `ExecutionCoordinator`.

**DirtyState gotcha**: `NotebookDocument.AddCell()` calls `SetDirty()` internally. Tests that check `DocumentDirtyChanged` must call `doc.MarkClean()` after setup to reset to a clean baseline, otherwise the subsequent `IsDirty = true` assignment is a no-op (same value → no PropertyChanged → no event).

---

## 2026-03-27 — Phase 4 Batch Complete: Tests, IntelliSense, Rich Output, Toolbar

**Status**: COMPLETE ✅ — All four workstreams delivered and integrated

**What Changed**: Four-agent parallel batch completed:
1. **Theo (Tests)**: 69 new tests (CellExecutionEngineTests, ExecutionCoordinatorTests, EditorFactoryTests, OutputRoutingTests) — 204 total, all passing.
2. **Ellie (IntelliSense)**: 5 new providers (CompletionProvider, HoverProvider, SignatureHelpProvider, DiagnosticsProvider, IntelliSenseManager) integrated into NotebookControl.
3. **Wendy (Rich Output)**: 8 MIME types supported via WebView2OutputHost and ImageOutputControl — in-place DisplayedValueUpdated rendering.
4. **Vince (Toolbar)**: NotebookToolbar with Run All, Interrupt, Restart, Clear Outputs buttons + kernel status indicator + keyboard shortcuts.

**Build Status**: ✅ 0 errors, 0 warnings  
**Test Count**: 204 total (all passing)

**Why**: Phase 4 required comprehensive testing, advanced IntelliSense, production-grade output rendering, and user-accessible toolbar commands.

**Affected Areas**:
- All agents: Decision 13 (IntelliSense), 14 (Rich Output), 15 (Toolbar) captured in decisions.md
- Wendy: DisplayedValueUpdated contract for live display updates (use `cell.Outputs[index] = newOutput`)
- Theo: WebView2OutputHost fallback ensures test stability
- Penny: Build verified clean; ready for marketplace submission prep

**Status**: ACTIVE — All Phase 4 tasks complete and production-ready

**What was added:**
- `CellExecutionEngineTests.cs` (31 tests): Reflection-based tests for private static `MapKernelName` (21 mapping cases covering all kernel aliases) and `IsTerminalEvent` (8 event-type cases), plus constructor null guard and double-dispose safety. Uses `Process.GetCurrentProcess()` as a stub process to construct a valid `CellExecutionEngine` without a live kernel.
- `ExecutionCoordinatorTests.cs` (4 tests): Constructor null guard, double-dispose safety, and `RunAllCellsAsync(null)` null-document guard (exercisable because the `throw` fires synchronously before the first `await`).
- `EditorFactoryTests.cs` (13 tests): `NotebookDocumentManager` lifecycle — `RegisterDocument`, `IsOpen`/`GetDocument`, `CloseAsync`, event firing (DocumentOpened, DocumentClosed, DocumentDirtyChanged), case-insensitive path lookup, and the rename scenario. `NotebookEditorFactory` itself is untestable (VS `IVsEditorFactory` interface makes class-loading uncertain in test runner).
- `OutputRoutingTests.cs` (21 tests): `FormattedOutput` and `CellOutput` model coverage (MIME types, output kinds, SuppressDisplay, ValueId, null-FormattedValues, multiple values), output accumulation in `NotebookCell.Outputs`, and a single `OutputControl` WPF construction test on STA thread.

**VS SDK JIT-blocking boundaries discovered:**
- `OutputControl.Rebuild()` directly references `VsBrushes` (VS SDK). Any test that sets `OutputControl.Cell` triggers JIT of `Rebuild()` → `FileNotFoundException` for `Microsoft.VisualStudio.Shell.15.0`. Only the no-Cell construction path is testable.
- `ExecutionCoordinator.HandleCellRunRequested()` directly references `ThreadHelper` in its method body. Even null-args tests that would hit the early return fail at JIT time. Not testable in unit-test runner.
- `NotebookEditorFactory.MapLogicalView` references `VSConstants.LOGVIEWID_Primary` (a `static readonly Guid`, not a const), so JIT of `MapLogicalView` requires VS Shell assembly. Not testable.
- Methods that call ONLY VS types via the JIT-safety pattern (ExtensionLogger, Decision 9) ARE safe to call from tests — the safety pattern prevents JIT failure propagation.

**InternalsVisibleTo**: Added via `<AssemblyAttribute Include="...InternalsVisibleToAttribute">` in the main project's `.csproj`. This allows tests to directly reference `internal` classes like `CellExecutionEngine` and `ExecutionCoordinator`.

**DirtyState gotcha**: `NotebookDocument.AddCell()` calls `SetDirty()` internally. Tests that check `DocumentDirtyChanged` must call `doc.MarkClean()` after setup to reset to a clean baseline, otherwise the subsequent `IsDirty = true` assignment is a no-op (same value → no PropertyChanged → no event).
