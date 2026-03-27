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
