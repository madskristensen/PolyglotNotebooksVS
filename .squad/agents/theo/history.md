# Theo History

## 2024-01-XX — Xtenders Founded

**Context**: Founded as specialized team for Visual Studio extension developers.

**Theo's Focus**:
- JoinableTaskFactory patterns and best practices
- VS threading rules and constraints (single-threaded UI)
- Microsoft.VisualStudio.SDK.Analyzers enforcement
- Async/await patterns and CancellationToken usage
- Extension error handling and debugging
- ActivityLog.xml analysis and debugging
- Performance profiling and optimization
- Threading audit before code review

**Authority Scope**:
- All async/await pattern validation
- JoinableTaskFactory usage audit
- SDK Analyzer violation blocking
- CancellationToken contract enforcement
- Main thread vs background thread classification
- Error handling comprehensiveness
- Performance regression detection

**Knowledge Base**:
- VS threading model and constraints
- JoinableTaskFactory API and patterns
- Microsoft.VisualStudio.Threading NuGet package
- SDK Analyzers rules and implementation
- CancellationToken lifecycle and best practices
- ActivityLog.xml structure and debugging
- Deadlock diagnosis and prevention
- Extension profiling tools

**Key References**:
- JoinableTaskFactory Documentation
- VS Threading Rules (microsoft.github.io/vs-threading/)
- SDK Analyzers Reference
- Async/Await Best Practices (MSDN Magazine archive)
- ActivityLog Debugging Guide
- CancellationToken Pattern Reference

**Authority Decisions**:
- Async-first threading model (Decision #2)
- SDK Analyzers required; violations block merge
- CancellationToken mandatory in all async APIs
- No `.Result` or `.Wait()` allowed
- Async void reserved for event handlers only

**Active Integrations**:
- All agents: Threading audit before code review (bottleneck: Theo sees all PRs)
- Penny: CI/CD async tasks, GitHub Actions automation

## Learnings

### Kernel Process Manager — Threading & Lifecycle Decisions

**Process.Start off UI thread**: `StartAsync` uses `Task.Run(() => LaunchProcess(), ct)` to ensure
`ProcessStartInfo` construction and `Process.Start()` never block the VS UI thread. The process object
is assigned inside the background work, then consumed from any thread via the public `Process` property.

**Process.Exited handler non-blocking**: `EnableRaisingEvents = true` causes `Exited` to fire on a
ThreadPool thread. The handler does only lightweight work: read `ExitCode` (with try/catch for races),
snapshot the stderr queue under a lock, set status, and raise an event. No awaits, no blocking calls.

**SemaphoreSlim for restart serialization**: `RestartAsync` gates entry with `SemaphoreSlim(1,1)` and
uses `WaitAsync(ct)` — never `.Wait()`. This queues callers safely without deadlock risk on the VS
main thread and honours CancellationToken throughout.

**WaitForExitAsync pattern (net48)**: No `process.WaitForExitAsync()` on net48. Implemented via
`TaskCompletionSource<bool>` + `process.Exited` event subscription + `Task.Delay` timeout fallback.
Guarded the subscribe/HasExited check for the inherent race where the process exits before we subscribe.

**StdinPingTimer thread-safety**: Uses `Interlocked.CompareExchange` on an `int` field (not a bool)
for lock-free Start/Stop. The timer callback reads the same field with `Volatile.Read` before writing.
Disposing the `Timer` is done only from the thread that won the CAS, avoiding double-dispose.

**Dispose pattern**: `KernelProcessManager.Dispose()` sets `_intentionalStop = true` before killing,
so the `Exited` event handler is a no-op during cleanup. Unsubscribes event handlers before calling
`Process.Dispose()` to prevent post-dispose callbacks.

**Stderr ring-buffer**: Capped at 100 lines under a dedicated `_stderrLock` object (not `this`).
Provides diagnostics for crash analysis without unbounded memory growth on long-lived processes.

**IAsyncDisposable not used on net48**: The interface requires `Microsoft.Bcl.AsyncInterfaces` on
net48. Went with `IDisposable` + a `Task StopAsync()` method instead — callers get full async
shutdown without pulling in an extra package.

### Phase 1 Unit Tests — What Was Tested and Patterns Found

**What was tested (135 tests across 3 files)**:
- `ProtocolClientTests.cs` (52 tests): `KernelCommandEnvelope.Create` token uniqueness and Base64-GUID
  format, JSON round-trip with camelCase policy, `WhenWritingNull` omission of optional fields,
  `KernelEventEnvelope` deserialization from realistic JSON payloads (KernelReady, CommandSucceeded,
  CompletionsProduced, DiagnosticsProduced), all `CommandTypes` and `KernelEventTypes` string constants,
  graceful handling of malformed JSON (expects `JsonException`, which `KernelClient.DispatchLine` catches).
- `DocumentModelTests.cs` (51 tests): `NotebookDocument` create/add/remove/move cells, dirty-tracking
  cascade from cell to document, `INotifyPropertyChanged` event coverage, `NotebookParser.ParseDib` /
  `SerializeDib` round-trip with real `Microsoft.DotNet.Interactive.Documents` parsing,
  `NotebookDocumentManager` initial state and event wiring, `CellOutput` and `FormattedOutput` construction.
- `KernelProcessManagerTests.cs` (32 tests): `KernelConnectionInfo` default values, `KernelStatus`
  enum completeness, `KernelProcessManager` initial state, dispose idempotency, `ObjectDisposedException`
  on post-dispose async calls, `KernelInstallationDetector` cache coherence (calls dotnet if on PATH),
  `StdinPingTimer` Start/Stop idempotency, double-dispose safety, exception swallowing in `OnTick`.

**Key patterns found in testing**:
- `ProtocolSerializerOptions` is `internal` — tests define `TestSerializerOptions.Default` locally with
  identical settings (CamelCase + WhenWritingNull + CaseInsensitive).
- `Assert.ThrowsException<T>` is NOT available in MSTest.Sdk 4.0.1 despite being present in legacy MSTest.
  Replaced all usages with explicit try/catch + `Assert.IsTrue(threw, ...)`.
- `ImplicitUsings=enable` in MSTest.Sdk does NOT make `Assert.ThrowsException<T>` available — explicit
  `using Microsoft.VisualStudio.TestTools.UnitTesting;` required, but `ThrowsException<T>` still missing.
  This may be a breaking change in MSTest v4; recommend team-wide adoption of try/catch pattern.
- `System.Text.Json.JsonSerializer.Deserialize<T>` throws `JsonException` on malformed input, it never
  returns null — important for callers to handle (as `KernelClient.DispatchLine` correctly does).
- Building `PolyglotNotebooks.slnx` with both projects: main project (`src/`) must succeed first since
  tests project-reference it. Both projects build clean with 0 errors, 0 warnings (after fixes).

### Phase 5 Reliability — Error Handling & Crash Recovery

**Crash recovery with exponential backoff**: `KernelProcessManager` now auto-restarts the kernel after
unexpected exit (up to `MaxRestartAttempts = 3` times) using `Task.Run` from `OnProcessExited`. Delays
are exponential: 1s, 2s, 4s (`Math.Pow(2, attempt) * 1000`). `_restartAttempts` is an `int` field
managed with `Interlocked.Increment` / `Interlocked.Exchange` for thread safety — the Exited handler
fires on a ThreadPool thread, not the UI thread.

**Auto-restart gates through `_restartLock`**: `AutoRestartAsync` acquires the same `SemaphoreSlim`
used by `RestartAsync`, preventing interleave between user-triggered and crash-triggered restarts. On
success, `_restartAttempts` resets to 0; on failure (StartAsync throws), status stays `Error`.

**`KernelCrashedEventArgs` design**: Carries `ExitCode`, `StderrOutput`, `AttemptNumber`, and a
`WillRetry` flag. UI can subscribe to `KernelCrashed` to notify users on each crash and know if
recovery is in progress or exhausted. `CanReRunCellsAfterRestart` property on `KernelProcessManager`
exposes the re-run capability flag for future UI wiring.

**EventObserver `OnCompleted`/`OnError` fix**: Previously, if the kernel process died mid-command,
`EventObserver` tasks would hang forever (TCS never set). Fixed by passing `onCompleted` and `onError`
callbacks to `ActionObserver` that call `FaultAllPending(exception)`. This faults both the
`_terminalTcs` and all per-event-type TCS objects with `InvalidOperationException`. Callers get
a clean exception instead of a deadlock.

**`KernelClient` command timeout**: New `CommandTimeoutMs` property (default 30 000 ms) applied to all
command methods via `NewTimeoutCts(ct)`. Creates a `CancellationTokenSource.CreateLinkedTokenSource`
linked to both the caller's token and a 30-second deadline. CTS is disposed via `using` to prevent
timer leak.

**`ExtensionLogger` JIT-safety pattern**: `ActivityLog` lives in `Microsoft.VisualStudio.Shell.Framework`
which is not present in the test runner process. If `ActivityLog` methods are called directly in a
try-catch, the try-catch is useless because the JIT fails to compile the METHOD BODY before executing.
Fix: each VS-specific call is moved to a private `[MethodImpl(NoInlining)]` helper method. The public
wrapper's try-catch catches the `FileNotFoundException` thrown at runtime when the helper's JIT
compilation fails. This is a critical pattern for any VS-specific code called from testable classes.

**`KernelNotInstalledDialog` thread-switching**: Uses `SwitchToMainThreadAsync()` + `System.Windows.MessageBox`
(WPF). The dialog fires `Process.Start` with `UseShellExecute = true` to open the browser URL,
with its own try-catch for robustness.

**`NotebookEditorPane` installation check**: `LoadDocData` fires a `JoinableTaskFactory.Run` background
check before opening the document. Detection is fire-and-continue (not blocking) — the document still
opens in degraded mode if dotnet-interactive is missing, so VS doesn't block.


**Process.Start off UI thread**: `StartAsync` uses `Task.Run(() => LaunchProcess(), ct)` to ensure
`ProcessStartInfo` construction and `Process.Start()` never block the VS UI thread. The process object
is assigned inside the background work, then consumed from any thread via the public `Process` property.

**Process.Exited handler non-blocking**: `EnableRaisingEvents = true` causes `Exited` to fire on a
ThreadPool thread. The handler does only lightweight work: read `ExitCode` (with try/catch for races),
snapshot the stderr queue under a lock, set status, and raise an event. No awaits, no blocking calls.

**SemaphoreSlim for restart serialization**: `RestartAsync` gates entry with `SemaphoreSlim(1,1)` and
uses `WaitAsync(ct)` — never `.Wait()`. This queues callers safely without deadlock risk on the VS
main thread and honours CancellationToken throughout.

**WaitForExitAsync pattern (net48)**: No `process.WaitForExitAsync()` on net48. Implemented via
`TaskCompletionSource<bool>` + `process.Exited` event subscription + `Task.Delay` timeout fallback.
Guarded the subscribe/HasExited check for the inherent race where the process exits before we subscribe.

**StdinPingTimer thread-safety**: Uses `Interlocked.CompareExchange` on an `int` field (not a bool)
for lock-free Start/Stop. The timer callback reads the same field with `Volatile.Read` before writing.
Disposing the `Timer` is done only from the thread that won the CAS, avoiding double-dispose.

**Dispose pattern**: `KernelProcessManager.Dispose()` sets `_intentionalStop = true` before killing,
so the `Exited` event handler is a no-op during cleanup. Unsubscribes event handlers before calling
`Process.Dispose()` to prevent post-dispose callbacks.

**Stderr ring-buffer**: Capped at 100 lines under a dedicated `_stderrLock` object (not `this`).
Provides diagnostics for crash analysis without unbounded memory growth on long-lived processes.

**IAsyncDisposable not used on net48**: The interface requires `Microsoft.Bcl.AsyncInterfaces` on
net48. Went with `IDisposable` + a `Task StopAsync()` method instead — callers get full async
shutdown without pulling in an extra package.

### Phase 1 Unit Tests — What Was Tested and Patterns Found

**What was tested (135 tests across 3 files)**:
- `ProtocolClientTests.cs` (52 tests): `KernelCommandEnvelope.Create` token uniqueness and Base64-GUID
  format, JSON round-trip with camelCase policy, `WhenWritingNull` omission of optional fields,
  `KernelEventEnvelope` deserialization from realistic JSON payloads (KernelReady, CommandSucceeded,
  CompletionsProduced, DiagnosticsProduced), all `CommandTypes` and `KernelEventTypes` string constants,
  graceful handling of malformed JSON (expects `JsonException`, which `KernelClient.DispatchLine` catches).
- `DocumentModelTests.cs` (51 tests): `NotebookDocument` create/add/remove/move cells, dirty-tracking
  cascade from cell to document, `INotifyPropertyChanged` event coverage, `NotebookParser.ParseDib` /
  `SerializeDib` round-trip with real `Microsoft.DotNet.Interactive.Documents` parsing,
  `NotebookDocumentManager` initial state and event wiring, `CellOutput` and `FormattedOutput` construction.
- `KernelProcessManagerTests.cs` (32 tests): `KernelConnectionInfo` default values, `KernelStatus`
  enum completeness, `KernelProcessManager` initial state, dispose idempotency, `ObjectDisposedException`
  on post-dispose async calls, `KernelInstallationDetector` cache coherence (calls dotnet if on PATH),
  `StdinPingTimer` Start/Stop idempotency, double-dispose safety, exception swallowing in `OnTick`.

**Key patterns found in testing**:
- `ProtocolSerializerOptions` is `internal` — tests define `TestSerializerOptions.Default` locally with
  identical settings (CamelCase + WhenWritingNull + CaseInsensitive).
- `Assert.ThrowsException<T>` is NOT available in MSTest.Sdk 4.0.1 despite being present in legacy MSTest.
  Replaced all usages with explicit try/catch + `Assert.IsTrue(threw, ...)`.
- `ImplicitUsings=enable` in MSTest.Sdk does NOT make `Assert.ThrowsException<T>` available — explicit
  `using Microsoft.VisualStudio.TestTools.UnitTesting;` required, but `ThrowsException<T>` still missing.
  This may be a breaking change in MSTest v4; recommend team-wide adoption of try/catch pattern.
- `System.Text.Json.JsonSerializer.Deserialize<T>` throws `JsonException` on malformed input, it never
  returns null — important for callers to handle (as `KernelClient.DispatchLine` correctly does).
- Building `PolyglotNotebooks.slnx` with both projects: main project (`src/`) must succeed first since
  tests project-reference it. Both projects build clean with 0 errors, 0 warnings (after fixes).

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
