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
