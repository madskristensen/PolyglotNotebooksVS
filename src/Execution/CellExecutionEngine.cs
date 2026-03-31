using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using PolyglotNotebooks.Diagnostics;
using PolyglotNotebooks.Models;
using PolyglotNotebooks.Options;
using PolyglotNotebooks.Protocol;

namespace PolyglotNotebooks.Execution
{
    /// <summary>
    /// Bridges the notebook UI and the dotnet-interactive kernel.
    /// Handles the full lifecycle of a single cell execution: state management,
    /// command dispatch, output streaming, and terminal event handling.
    /// Thread-safe: only one cell executes at a time (serialized via SemaphoreSlim).
    /// </summary>
    internal sealed class CellExecutionEngine : IDisposable
    {
        private readonly IKernelClient _kernelClient;
        private readonly SemaphoreSlim _executionGate = new SemaphoreSlim(1, 1);
        private CancellationTokenSource? _currentCts;
        private bool _disposed;

        public CellExecutionEngine(KernelClient kernelClient)
            : this((IKernelClient)kernelClient)
        {
        }

        internal CellExecutionEngine(IKernelClient kernelClient)
        {
            _kernelClient = kernelClient ?? throw new ArgumentNullException(nameof(kernelClient));
        }

        /// <summary>
        /// Executes the given cell: clears outputs, sends SubmitCode to the kernel, streams
        /// events back into <see cref="NotebookCell.Outputs"/>, and updates execution state
        /// on completion. Only one cell executes at a time; concurrent calls queue behind the gate.
        /// </summary>
        public async Task ExecuteCellAsync(NotebookCell cell, CancellationToken ct = default)
        {
            if (cell == null) throw new ArgumentNullException(nameof(cell));

            // Link the caller's token so either side can cancel.
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _currentCts = linkedCts;

            var timeoutSeconds = PolyglotNotebooksOptions.Instance.CellExecutionTimeoutSeconds;
            if (timeoutSeconds > 0)
                linkedCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            // Acquire gate on background thread; throws OperationCanceledException if ct fires.
            await _executionGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var effectiveCt = linkedCts.Token;

                // ── 1. Set Running state on the UI thread ─────────────────────
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(effectiveCt);
                if (cell.ExecutionStatus != CellExecutionStatus.Running)
                    cell.ExecutionStatus = CellExecutionStatus.Running;
                cell.Outputs.Clear();
                cell.LastExecutionDuration = null;

                // ── 2. Build the SubmitCode command envelope ──────────────────
                var kernelName = MapKernelName(cell.KernelName);
                var code = cell.Contents?.TrimStart('\uFEFF');
                var envelope = KernelCommandEnvelope.Create(CommandTypes.SubmitCode, new SubmitCode
                {
                    Code = code ?? string.Empty,
                    TargetKernelName = kernelName
                });

                // Diagnostic logging: capture raw inputs so we can trace #r "nuget:" failures.
                var codeSnippet = code?.Length > 120 ? code.Substring(0, 120) + "…" : code;
                ExtensionLogger.LogInfo(nameof(CellExecutionEngine),
                    $"SubmitCode token={envelope.Token} kernel={kernelName} " +
                    $"cellKernel={cell.KernelName} codeLen={code?.Length} code=[{codeSnippet}]");

                var json = JsonSerializer.Serialize(envelope, ProtocolSerializerOptions.Default);
                ExtensionLogger.LogInfo(nameof(CellExecutionEngine),
                    $"SubmitCode JSON ({json.Length} chars): {(json.Length > 300 ? json.Substring(0, 300) + "…" : json)}");

                // ── 3. Subscribe to events before sending to avoid races ──────
                // EventObserver handles terminal event detection for this token.
                using var observer = new EventObserver(_kernelClient.Events, envelope.Token);

                // Intermediate output events are forwarded to cell.Outputs on the UI thread.
                using var intermediateSub = _kernelClient.Events.Subscribe(
                    new ActionObserver<KernelEventEnvelope>(e =>
                    {
                        // Match events from this command and its child sub-commands.
                        // The dotnet-interactive kernel uses hierarchical tokens: child
                        // commands get tokens prefixed with the parent token (e.g.
                        // "parentToken.childGuid"). Using StartsWith covers both the
                        // exact parent token and any child tokens spawned by magic
                        // commands like #!connect or kernel-switching directives.
                        var eventToken = e.Command?.Token;
                        if (eventToken == null || !eventToken.StartsWith(envelope.Token)) return;

                        if (IsTerminalEvent(e.EventType)) return;

#pragma warning disable VSTHRD110, VSSDK007 // intentional fire-and-forget; exceptions caught inside
                        _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                        {
                            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                            ApplyOutputEvent(cell, e);
                        });
#pragma warning restore VSTHRD110, VSSDK007
                    }));

                // ── 4. Send command — ConfigureAwait(false) leaves the UI thread ──
                var sw = Stopwatch.StartNew();
                await _kernelClient.SendCommandAsync(envelope, effectiveCt).ConfigureAwait(false);

                // ── 5. Await the terminal event on a background thread ────────
                KernelEventEnvelope terminal;
                try
                {
                    terminal = await observer.WaitForTerminalEventAsync(effectiveCt).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    await TrySendCancelAsync().ConfigureAwait(false);
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    bool timedOut = timeoutSeconds > 0 && linkedCts.IsCancellationRequested && !ct.IsCancellationRequested;
                    if (timedOut)
                    {
                        cell.ExecutionStatus = CellExecutionStatus.Failed;
                        cell.LastExecutionDuration = sw.Elapsed;
                        cell.Outputs.Add(new CellOutput(
                            CellOutputKind.Error,
                            new List<FormattedOutput> { new FormattedOutput("text/plain",
                                $"Cell execution timed out after {timeoutSeconds} seconds.") }));
                    }
                    else
                    {
                        cell.ExecutionStatus = CellExecutionStatus.Idle;
                    }

                    return;
                }

                sw.Stop();

                // ── 6. Update final cell state on the UI thread
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                cell.LastExecutionDuration = sw.Elapsed;

                if (terminal.EventType == KernelEventTypes.CommandSucceeded)
                {
                    var succeeded = terminal.Event.Deserialize<CommandSucceeded>(
                        ProtocolSerializerOptions.Default);
                    cell.ExecutionStatus = CellExecutionStatus.Succeeded;
                    cell.ExecutionOrder = succeeded?.ExecutionOrder ?? (cell.ExecutionOrder ?? 0) + 1;
                    ExtensionLogger.LogInfo(nameof(CellExecutionEngine),
                        $"Cell execution succeeded. Token={envelope.Token}.");
                }
                else
                {
                    var failed = terminal.Event.Deserialize<CommandFailed>(
                        ProtocolSerializerOptions.Default);
                    cell.ExecutionStatus = CellExecutionStatus.Failed;
                    if (!string.IsNullOrEmpty(failed?.Message))
                    {
                        cell.Outputs.Add(new CellOutput(
                            CellOutputKind.Error,
                            new List<FormattedOutput> { new FormattedOutput("text/plain", failed!.Message) }));
                    }

                    // Full diagnostic dump for failed commands
                    var rawEvent = terminal.Event.GetRawText();
                    var rawCmd = terminal.Command != null
                        ? JsonSerializer.Serialize(terminal.Command, ProtocolSerializerOptions.Default)
                        : "null";
                    ExtensionLogger.LogWarning(nameof(CellExecutionEngine),
                        $"Cell execution failed. Token={envelope.Token}.\n" +
                        $"  Error={failed?.Message}\n" +
                        $"  RawEvent({rawEvent.Length} chars)={rawEvent.Substring(0, Math.Min(rawEvent.Length, 500))}\n" +
                        $"  RawCommand({rawCmd.Length} chars)={rawCmd.Substring(0, Math.Min(rawCmd.Length, 500))}");
                }
            }
            catch (OperationCanceledException)
            {
                try
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    cell.ExecutionStatus = CellExecutionStatus.Idle;
                }
                catch { /* best-effort UI update */ }
            }
            catch (Exception ex)
            {
                ExtensionLogger.LogException(nameof(CellExecutionEngine),
                    "Unexpected error during cell execution.", ex);
                try
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    cell.ExecutionStatus = CellExecutionStatus.Failed;
                    cell.Outputs.Add(new CellOutput(
                        CellOutputKind.Error,
                        new List<FormattedOutput> { new FormattedOutput("text/plain", ex.Message) }));
                }
                catch { /* best-effort UI update */ }
            }
            finally
            {
                if (ReferenceEquals(_currentCts, linkedCts))
                    _currentCts = null;
                _executionGate.Release();

                // Safety net: if the cell is still Running when the method exits (due to any
                // unexpected code path), force it back to Idle so the UI never gets stuck
                // with a spinning timer and a visible Stop button.
                try
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    if (cell.ExecutionStatus == CellExecutionStatus.Running)
                        cell.ExecutionStatus = CellExecutionStatus.Idle;
                }
                catch { /* best-effort UI cleanup */ }
            }
        }

        /// <summary>
        /// Executes the given cell in debug mode. The caller provides a
        /// <paramref name="onCodeSubmitted"/> callback that is invoked right after the code
        /// is sent to the kernel — this is used to trigger a VS-side Break All so the
        /// debugger pauses while the kernel is executing the cell code.
        /// The caller must ensure "Just My Code" is disabled before calling this method.
        /// </summary>
        public async Task ExecuteDebugCellAsync(
            NotebookCell cell,
            Func<CancellationToken, Task> onCodeSubmitted = null,
            CancellationToken ct = default)
        {
            if (cell == null) throw new ArgumentNullException(nameof(cell));

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _currentCts = linkedCts;

            var timeoutSeconds = PolyglotNotebooksOptions.Instance.CellExecutionTimeoutSeconds;
            if (timeoutSeconds > 0)
                linkedCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            await _executionGate.WaitAsync(ct).ConfigureAwait(false);
            Task onSubmittedTask = null;
            try
            {
                var effectiveCt = linkedCts.Token;

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(effectiveCt);
                if (cell.ExecutionStatus != CellExecutionStatus.Running)
                    cell.ExecutionStatus = CellExecutionStatus.Running;
                cell.Outputs.Clear();
                cell.LastExecutionDuration = null;

                var kernelName = MapKernelName(cell.KernelName);
                var code = cell.Contents?.TrimStart('\uFEFF') ?? string.Empty;

                // Prepend a Debugger.Break() call so the kernel breaks deterministically
                // inside the Submission# frame. The guard ensures we only break when a
                // debugger is attached (which it always is in this path). After the user
                // presses F5/Continue or Step Over, execution flows into their actual code.
                const string debugPreamble =
                    "if (System.Diagnostics.Debugger.IsAttached) { System.Diagnostics.Debugger.Break(); }\n";
                code = debugPreamble + code;

                var envelope = KernelCommandEnvelope.Create(CommandTypes.SubmitCode, new SubmitCode
                {
                    Code = code,
                    TargetKernelName = kernelName
                });

                ExtensionLogger.LogInfo(nameof(CellExecutionEngine),
                    $"SubmitCode [DEBUG] token={envelope.Token} kernel={kernelName}");

                using var observer = new EventObserver(_kernelClient.Events, envelope.Token);
                using var intermediateSub = _kernelClient.Events.Subscribe(
                    new ActionObserver<KernelEventEnvelope>(e =>
                    {
                        var eventToken = e.Command?.Token;
                        if (eventToken == null || !eventToken.StartsWith(envelope.Token)) return;
                        if (IsTerminalEvent(e.EventType)) return;

#pragma warning disable VSTHRD110, VSSDK007
                        _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                        {
                            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                            ApplyOutputEvent(cell, e);
                        });
#pragma warning restore VSTHRD110, VSSDK007
                    }));

                var sw = Stopwatch.StartNew();
                await _kernelClient.SendCommandAsync(envelope, effectiveCt).ConfigureAwait(false);

                // Fire the post-submission callback as a background task so it runs
                // concurrently with WaitForTerminalEventAsync. For debug cells this
                // callback waits for the user to finish stepping, then detaches the
                // debugger. It must NOT block the terminal-event wait — the kernel
                // sends the completion event through stdio after user code finishes,
                // and we need to be listening for it.
                if (onCodeSubmitted != null)
                {
                    onSubmittedTask = Task.Run(async () =>
                    {
                        try
                        {
                            await onCodeSubmitted(effectiveCt).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) { /* ok */ }
                        catch (Exception ex)
                        {
                            ExtensionLogger.LogException(nameof(CellExecutionEngine),
                                "onCodeSubmitted callback failed.", ex);
                        }
                    });
                }

                KernelEventEnvelope terminal;
                try
                {
                    terminal = await observer.WaitForTerminalEventAsync(effectiveCt).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    await TrySendCancelAsync().ConfigureAwait(false);
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    bool timedOut = timeoutSeconds > 0 && linkedCts.IsCancellationRequested && !ct.IsCancellationRequested;
                    if (timedOut)
                    {
                        cell.ExecutionStatus = CellExecutionStatus.Failed;
                        cell.LastExecutionDuration = sw.Elapsed;
                        cell.Outputs.Add(new CellOutput(
                            CellOutputKind.Error,
                            new List<FormattedOutput> { new FormattedOutput("text/plain",
                                $"Cell execution timed out after {timeoutSeconds} seconds.") }));
                    }
                    else if (cell.ExecutionStatus == CellExecutionStatus.Running)
                    {
                        // Only reset to Idle if the cell is still Running.
                        // Debug cells may have already been set to Succeeded by the
                        // debugger-detach callback before cancelling the terminal wait.
                        cell.ExecutionStatus = CellExecutionStatus.Idle;
                    }
                    return;
                }

                sw.Stop();

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                cell.LastExecutionDuration = sw.Elapsed;

                if (terminal.EventType == KernelEventTypes.CommandSucceeded)
                {
                    var succeeded = terminal.Event.Deserialize<CommandSucceeded>(ProtocolSerializerOptions.Default);
                    cell.ExecutionStatus = CellExecutionStatus.Succeeded;
                    cell.ExecutionOrder = succeeded?.ExecutionOrder ?? (cell.ExecutionOrder ?? 0) + 1;
                }
                else
                {
                    var failed = terminal.Event.Deserialize<CommandFailed>(ProtocolSerializerOptions.Default);
                    cell.ExecutionStatus = CellExecutionStatus.Failed;
                    if (!string.IsNullOrEmpty(failed?.Message))
                    {
                        cell.Outputs.Add(new CellOutput(
                            CellOutputKind.Error,
                            new List<FormattedOutput> { new FormattedOutput("text/plain", failed!.Message) }));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                try
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    cell.ExecutionStatus = CellExecutionStatus.Idle;
                }
                catch { }
            }
            catch (Exception ex)
            {
                ExtensionLogger.LogException(nameof(CellExecutionEngine), "Error during debug cell execution.", ex);
                try
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    cell.ExecutionStatus = CellExecutionStatus.Failed;
                    cell.Outputs.Add(new CellOutput(
                        CellOutputKind.Error,
                        new List<FormattedOutput> { new FormattedOutput("text/plain", ex.Message) }));
                }
                catch { }
            }
            finally
            {
                // Wait for the background detach task to complete before releasing the gate.
                if (onSubmittedTask != null)
                {
                    try { await onSubmittedTask.ConfigureAwait(false); }
                    catch { /* errors already logged inside the task */ }
                }

                if (ReferenceEquals(_currentCts, linkedCts))
                    _currentCts = null;
                _executionGate.Release();

                // Safety net: if the cell is still Running when the method exits (due to any
                // unexpected code path), force it back to Idle so the UI never gets stuck
                // with a spinning timer and a visible Stop button.
                try
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    if (cell.ExecutionStatus == CellExecutionStatus.Running)
                        cell.ExecutionStatus = CellExecutionStatus.Idle;
                }
                catch { }
            }
        }

        private static bool IsDebuggableKernel(string? kernelName)
        {
            var name = kernelName?.ToLowerInvariant();
            return name == "csharp" || name == "fsharp" || name == "pwsh" || name == "powershell";
        }

        /// <summary>
        /// Executes only a snippet of selected text against the kernel, appending output to
        /// <paramref name="cell"/> without clearing its existing outputs. Useful for "Run Selection".
        /// </summary>
        public async Task ExecuteSelectionAsync(NotebookCell cell, string selectedText, CancellationToken ct = default)
        {
            if (cell == null) throw new ArgumentNullException(nameof(cell));
            if (string.IsNullOrEmpty(selectedText)) return;

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _currentCts = linkedCts;

            var timeoutSeconds = PolyglotNotebooksOptions.Instance.CellExecutionTimeoutSeconds;
            if (timeoutSeconds > 0)
                linkedCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            await _executionGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var effectiveCt = linkedCts.Token;

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(effectiveCt);
                cell.ExecutionStatus = CellExecutionStatus.Running;
                // NOTE: outputs are NOT cleared

                var kernelName = MapKernelName(cell.KernelName);
                var envelope = KernelCommandEnvelope.Create(CommandTypes.SubmitCode, new SubmitCode
                {
                    Code = selectedText,
                    TargetKernelName = kernelName
                });

                using var observer = new EventObserver(_kernelClient.Events, envelope.Token);

#pragma warning disable VSTHRD110, VSSDK007
                using var intermediateSub = _kernelClient.Events.Subscribe(
                    new ActionObserver<KernelEventEnvelope>(e =>
                    {
                        var eventToken = e.Command?.Token;
                        if (eventToken == null || !eventToken.StartsWith(envelope.Token)) return;
                        if (IsTerminalEvent(e.EventType)) return;

                        _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                        {
                            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                            ApplyOutputEvent(cell, e);
                        });
                    }));
#pragma warning restore VSTHRD110, VSSDK007

                var sw = Stopwatch.StartNew();
                await _kernelClient.SendCommandAsync(envelope, effectiveCt).ConfigureAwait(false);

                KernelEventEnvelope terminal;
                try
                {
                    terminal = await observer.WaitForTerminalEventAsync(effectiveCt).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    await TrySendCancelAsync().ConfigureAwait(false);
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    bool timedOut = timeoutSeconds > 0 && linkedCts.IsCancellationRequested && !ct.IsCancellationRequested;
                    if (timedOut)
                    {
                        cell.ExecutionStatus = CellExecutionStatus.Failed;
                        cell.LastExecutionDuration = sw.Elapsed;
                        cell.Outputs.Add(new CellOutput(
                            CellOutputKind.Error,
                            new List<FormattedOutput> { new FormattedOutput("text/plain",
                                $"Cell execution timed out after {timeoutSeconds} seconds.") }));
                    }
                    else
                    {
                        cell.ExecutionStatus = CellExecutionStatus.Idle;
                    }

                    return;
                }

                sw.Stop();

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                cell.LastExecutionDuration = sw.Elapsed;

                if (terminal.EventType == KernelEventTypes.CommandSucceeded)
                {
                    cell.ExecutionStatus = CellExecutionStatus.Succeeded;
                }
                else
                {
                    var failed = terminal.Event.Deserialize<CommandFailed>(ProtocolSerializerOptions.Default);
                    cell.ExecutionStatus = CellExecutionStatus.Failed;
                    if (!string.IsNullOrEmpty(failed?.Message))
                    {
                        cell.Outputs.Add(new CellOutput(
                            CellOutputKind.Error,
                            new List<FormattedOutput> { new FormattedOutput("text/plain", failed!.Message) }));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                try
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    cell.ExecutionStatus = CellExecutionStatus.Idle;
                }
                catch { /* best-effort */ }
            }
            catch (Exception ex)
            {
                ExtensionLogger.LogException(nameof(CellExecutionEngine),
                    "Unexpected error during selection execution.", ex);
                try
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    cell.ExecutionStatus = CellExecutionStatus.Failed;
                    cell.Outputs.Add(new CellOutput(
                        CellOutputKind.Error,
                        new List<FormattedOutput> { new FormattedOutput("text/plain", ex.Message) }));
                }
                catch { /* best-effort */ }
            }
            finally
            {
                if (ReferenceEquals(_currentCts, linkedCts))
                    _currentCts = null;
                _executionGate.Release();

                try
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    if (cell.ExecutionStatus == CellExecutionStatus.Running)
                        cell.ExecutionStatus = CellExecutionStatus.Idle;
                }
                catch { /* best-effort UI cleanup */ }
            }
        }

        /// <summary>
        /// Cancels any in-progress cell execution and forwards a Cancel command to the kernel.
        /// </summary>
        public async Task CancelExecutionAsync()
        {
            _currentCts?.Cancel();
            await TrySendCancelAsync().ConfigureAwait(false);
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private async Task TrySendCancelAsync()
        {
            try
            {
                await _kernelClient.SendCommandAsync(
                    CommandTypes.CancelCommand, new CancelCommand(), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ExtensionLogger.LogWarning(nameof(CellExecutionEngine),
                    $"Failed to forward CancelCommand to kernel: {ex.Message}");
            }
        }

        /// <summary>
        /// Applies a single intermediate kernel event to the cell's output collection.
        /// Must be called on the UI thread.
        /// </summary>
        private static void ApplyOutputEvent(NotebookCell cell, KernelEventEnvelope envelope)
        {
            switch (envelope.EventType)
            {
                case KernelEventTypes.ReturnValueProduced:
                {
                    var e = envelope.Event.Deserialize<ReturnValueProduced>(
                        ProtocolSerializerOptions.Default);
                    if (e != null)
                        cell.Outputs.Add(BuildOutput(CellOutputKind.ReturnValue, e.FormattedValues, e.ValueId));
                    break;
                }
                case KernelEventTypes.DisplayedValueProduced:
                {
                    var e = envelope.Event.Deserialize<DisplayedValueProduced>(
                        ProtocolSerializerOptions.Default);
                    if (e != null)
                        cell.Outputs.Add(BuildOutput(CellOutputKind.Display, e.FormattedValues, e.ValueId));
                    break;
                }
                case KernelEventTypes.DisplayedValueUpdated:
                {
                    var e = envelope.Event.Deserialize<DisplayedValueUpdated>(
                        ProtocolSerializerOptions.Default);
                    if (e != null)
                    {
                        // Find and replace the existing output with the matching ValueId.
                        // This enables live-updating NuGet install progress.
                        int existingIndex = -1;
                        if (!string.IsNullOrEmpty(e.ValueId))
                        {
                            for (int i = 0; i < cell.Outputs.Count; i++)
                            {
                                if (cell.Outputs[i].ValueId == e.ValueId)
                                {
                                    existingIndex = i;
                                    break;
                                }
                            }
                        }

                        var updated = BuildOutput(CellOutputKind.Display, e.FormattedValues, e.ValueId);
                        if (existingIndex >= 0)
                            cell.Outputs[existingIndex] = updated;
                        else
                            cell.Outputs.Add(updated);
                    }
                    break;
                }
                case KernelEventTypes.StandardOutputValueProduced:
                {
                    var e = envelope.Event.Deserialize<StandardOutputValueProduced>(
                        ProtocolSerializerOptions.Default);
                    if (e != null)
                        cell.Outputs.Add(BuildOutput(CellOutputKind.StandardOutput, e.FormattedValues));
                    break;
                }
                case KernelEventTypes.StandardErrorValueProduced:
                {
                    var e = envelope.Event.Deserialize<StandardErrorValueProduced>(
                        ProtocolSerializerOptions.Default);
                    if (e != null)
                        cell.Outputs.Add(BuildOutput(CellOutputKind.StandardError, e.FormattedValues));
                    break;
                }
                case KernelEventTypes.ErrorProduced:
                {
                    // ErrorProduced carries a "message" field; extract via JsonElement since
                    // there is no dedicated strongly-typed class for this event.
                    if (envelope.Event.TryGetProperty("message", out var msgEl))
                    {
                        var message = msgEl.GetString() ?? string.Empty;
                        ExtensionLogger.LogWarning(nameof(CellExecutionEngine),
                            $"ErrorProduced: {message}\n  RawEvent: {envelope.Event.GetRawText()}");
                        if (!string.IsNullOrEmpty(message))
                        {
                            cell.Outputs.Add(new CellOutput(
                                CellOutputKind.Error,
                                new List<FormattedOutput> { new FormattedOutput("text/plain", message) }));
                        }
                    }
                    break;
                }
                case KernelEventTypes.InputRequested:
                {
                    var e = envelope.Event.Deserialize<InputRequested>(
                        ProtocolSerializerOptions.Default);
                    if (e != null && !string.IsNullOrEmpty(e.Prompt))
                    {
                        // Display the prompt text as cell output so the user can see the device code.
                        cell.Outputs.Add(new CellOutput(
                            CellOutputKind.StandardOutput,
                            new List<FormattedOutput> { new FormattedOutput("text/plain", e.Prompt) }));

                        // Auto-open the browser if the prompt contains a URL (e.g. device code auth).
                        var urlMatch = Regex.Match(e.Prompt, @"https?://\S+");
                        if (urlMatch.Success)
                        {
                            try
                            {
                                Process.Start(new ProcessStartInfo
                                {
                                    FileName = urlMatch.Value.TrimEnd('.', ',', ';'),
                                    UseShellExecute = true
                                });
                            }
                            catch (Exception ex)
                            {
                                ExtensionLogger.LogWarning(nameof(CellExecutionEngine),
                                    $"Failed to open browser for device code auth: {ex.Message}");
                            }
                        }
                    }
                    break;
                }
            }
        }

        private static CellOutput BuildOutput(
            CellOutputKind kind,
            List<FormattedValue> formattedValues,
            string? valueId = null)
        {
            var outputs = new List<FormattedOutput>(formattedValues?.Count ?? 0);
            if (formattedValues != null)
            {
                foreach (var fv in formattedValues)
                    outputs.Add(new FormattedOutput(fv.MimeType, fv.Value, fv.SuppressDisplay));
            }
            return new CellOutput(kind, outputs, valueId);
        }

        /// <summary>
        /// Maps a cell's kernel name (as stored in the notebook model) to the canonical
        /// dotnet-interactive target kernel name used on the wire.
        /// </summary>
        internal static string MapKernelName(string kernelName)
        {
            if (string.IsNullOrEmpty(kernelName))
                return "csharp";

            switch (kernelName.ToLowerInvariant())
            {
                case "c#":
                case "csharp":
                    return "csharp";
                case "f#":
                case "fsharp":
                    return "fsharp";
                case "javascript":
                case "js":
                    return "javascript";
                case "sql":
                    return "sql";
                case "kql":
                case "kusto":
                    return "kql";
                case "pwsh":
                case "powershell":
                    return "pwsh";
                case "html":
                    return "html";
                case "markdown":
                case "md":
                    return "markdown";
                default:
                    return kernelName;
            }
        }

        internal static bool IsTerminalEvent(string eventType) =>
            eventType == KernelEventTypes.CommandSucceeded ||
            eventType == KernelEventTypes.CommandFailed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _currentCts?.Cancel();
            _currentCts?.Dispose();
            _executionGate.Dispose();
        }
    }
}
