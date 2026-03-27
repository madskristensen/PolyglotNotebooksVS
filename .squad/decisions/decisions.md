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

---

*Decisions merged from inbox by Scribe, 2026-03-27T19:48:01Z*
