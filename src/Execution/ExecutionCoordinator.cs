using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using PolyglotNotebooks.Diagnostics;
using PolyglotNotebooks.Editor;
using PolyglotNotebooks.IntelliSense;
using PolyglotNotebooks.Kernel;
using PolyglotNotebooks.Models;
using PolyglotNotebooks.Protocol;

namespace PolyglotNotebooks.Execution
{
    /// <summary>
    /// Coordinates between UI run events and the kernel execution engine.
    /// Manages kernel lifecycle (start-on-demand), per-execution cancellation,
    /// and provides entry points for all cell execution modes.
    /// </summary>
    internal sealed class ExecutionCoordinator : IDisposable
    {
        private readonly IKernelProcessManager _kernelProcessManager;
        private readonly KernelInstallationDetector _installationDetector;
        private readonly Func<System.Diagnostics.Process, IKernelClient>? _kernelClientFactory;
        private readonly SemaphoreSlim _startupLock = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _lifetimeCts = new CancellationTokenSource();
        private readonly NodeJsExecutor _nodeJsExecutor = new NodeJsExecutor();

        private IKernelClient? _kernelClient;
        private CellExecutionEngine? _executionEngine;
        private CancellationTokenSource? _currentCts;
        private bool _kernelStarted;
        private bool _disposed;

        public event Action<KernelClient>? KernelClientAvailable;

        /// <summary>Raised when any run operation (Run All, Run Cell, etc.) completes or is cancelled.</summary>
        public event EventHandler? ExecutionCompleted;

        public KernelClient? KernelClient => _kernelClient as KernelClient;

        public ExecutionCoordinator(KernelProcessManager kernelProcessManager)
        {
            _kernelProcessManager = kernelProcessManager
                ?? throw new ArgumentNullException(nameof(kernelProcessManager));
            _installationDetector = new KernelInstallationDetector();
        }

        /// <summary>
        /// Internal constructor for unit testing: accepts interfaces so dependencies can be mocked.
        /// </summary>
        internal ExecutionCoordinator(
            IKernelProcessManager kernelProcessManager,
            Func<System.Diagnostics.Process, IKernelClient>? kernelClientFactory = null,
            KernelInstallationDetector? installationDetector = null)
        {
            _kernelProcessManager = kernelProcessManager
                ?? throw new ArgumentNullException(nameof(kernelProcessManager));
            _kernelClientFactory = kernelClientFactory;
            _installationDetector = installationDetector ?? new KernelInstallationDetector();
        }

        // ── Fire-and-forget handlers (called from UI thread) ──────────────────

        /// <summary>
        /// Called when <see cref="NotebookControl.CellRunRequested"/> fires.
        /// </summary>
        public void HandleCellRunRequested(object sender, CellRunEventArgs e)
        {
            if (e?.Cell == null) return;
            var cell = e.Cell;

            // Immediate UI feedback before kernel startup begins.
            cell.ExecutionStatus = CellExecutionStatus.Running;

            var previous = Interlocked.Exchange(ref _currentCts, null);
            previous?.Cancel();

            var cts = new CancellationTokenSource();
            _currentCts = cts;

#pragma warning disable VSTHRD110, VSSDK007
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await ExecuteCellRoutedAsync(cell, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    ExtensionLogger.LogException(nameof(ExecutionCoordinator),
                        "Unhandled error during cell execution.", ex);
                }
                finally
                {
                    Interlocked.CompareExchange(ref _currentCts, null, cts);
                    cts.Dispose();
                    ExecutionCompleted?.Invoke(this, EventArgs.Empty);
                }
            });
#pragma warning restore VSTHRD110, VSSDK007
        }

        /// <summary>Fire-and-forget entry point for the toolbar "Run All" button.</summary>
        public void HandleRunAllRequested(NotebookDocument document)
        {
            if (document == null) return;
            foreach (var cell in document.Cells)
                if (cell.Kind == CellKind.Code)
                    cell.ExecutionStatus = CellExecutionStatus.Queued;
            FireAndForget(ct => RunAllCellsAsync(document, ct), "Run All");
        }

        /// <summary>Fire-and-forget entry point for "Run Cells Above" (excludes current cell).</summary>
        public void HandleRunCellsAboveRequested(NotebookDocument document, NotebookCell currentCell)
        {
            if (document == null || currentCell == null) return;
            foreach (var cell in SelectCellsAbove(document.Cells, currentCell))
                if (cell.Kind == CellKind.Code)
                    cell.ExecutionStatus = CellExecutionStatus.Queued;
            FireAndForget(ct => RunCellsAboveAsync(document, currentCell, ct), "Run Cells Above");
        }

        /// <summary>Fire-and-forget entry point for "Run Cell and Below" (includes current cell).</summary>
        public void HandleRunCellsBelowRequested(NotebookDocument document, NotebookCell currentCell)
        {
            if (document == null || currentCell == null) return;
            foreach (var cell in SelectCellsBelow(document.Cells, currentCell))
                if (cell.Kind == CellKind.Code)
                    cell.ExecutionStatus = CellExecutionStatus.Queued;
            FireAndForget(ct => RunCellsBelowAsync(document, currentCell, ct), "Run Cells Below");
        }

        /// <summary>Fire-and-forget entry point for "Run Selection".</summary>
        public void HandleRunSelectionRequested(NotebookCell cell, string selectedText)
        {
            if (cell == null || string.IsNullOrEmpty(selectedText)) return;
            FireAndForget(ct => RunSelectionAsync(cell, selectedText, ct), "Run Selection");
        }

        /// <summary>Fire-and-forget entry point for "Restart Kernel and Run All".</summary>
        public void HandleRestartAndRunAllRequested(NotebookDocument document)
        {
            if (document == null) return;
            foreach (var cell in document.Cells)
                if (cell.Kind == CellKind.Code)
                    cell.ExecutionStatus = CellExecutionStatus.Queued;
            FireAndForget(ct => RestartAndRunAllAsync(document, ct), "Restart and Run All");
        }

        /// <summary>
        /// Fire-and-forget entry point for "Restart Kernel" (without running cells afterwards).
        /// Resets the coordinator's KernelClient/ExecutionEngine so the next execution
        /// creates a fresh connection to the new kernel process.
        /// </summary>
        public void HandleRestartKernelRequested()
        {
            FireAndForget(async ct =>
            {
                await _startupLock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    _executionEngine?.Dispose();
                    _kernelClient?.Dispose();
                    _executionEngine = null;
                    _kernelClient = null;
                    _kernelStarted = false;

                    if (_kernelProcessManager.IsRunning)
                        await _kernelProcessManager.StopAsync().ConfigureAwait(false);

                    KernelInfoCache.Default.Reset();
                }
                finally
                {
                    _startupLock.Release();
                }

                // Start a fresh kernel immediately so it's warm when the user runs a cell.
                await _kernelProcessManager.StartAsync(ct).ConfigureAwait(false);

                ExtensionLogger.LogInfo(nameof(ExecutionCoordinator),
                    "Kernel restarted. Coordinator state reset.");
            }, "Restart Kernel");
        }

        /// <summary>Cancels any currently running or pending cell execution.</summary>
        public void CancelCurrentExecution()
        {
            var cts = Interlocked.Exchange(ref _currentCts, null);
            cts?.Cancel();
            cts?.Dispose();
        }

        // ── Awaitable execution methods ───────────────────────────────────────

        /// <summary>Runs all code cells in the document sequentially.</summary>
        public async Task RunAllCellsAsync(NotebookDocument document, CancellationToken ct = default)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            foreach (var cell in document.Cells)
            {
                ct.ThrowIfCancellationRequested();
                if (cell.Kind == CellKind.Code)
                    await ExecuteCellRoutedAsync(cell, ct).ConfigureAwait(false);
            }
        }

        /// <summary>Runs all code cells above (not including) <paramref name="currentCell"/>.</summary>
        public async Task RunCellsAboveAsync(NotebookDocument document, NotebookCell currentCell, CancellationToken ct = default)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (currentCell == null) throw new ArgumentNullException(nameof(currentCell));

            foreach (var cell in SelectCellsAbove(document.Cells, currentCell))
            {
                ct.ThrowIfCancellationRequested();
                if (cell.Kind == CellKind.Code)
                    await ExecuteCellRoutedAsync(cell, ct).ConfigureAwait(false);
            }
        }

        /// <summary>Runs <paramref name="currentCell"/> and all code cells below it.</summary>
        public async Task RunCellsBelowAsync(NotebookDocument document, NotebookCell currentCell, CancellationToken ct = default)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (currentCell == null) throw new ArgumentNullException(nameof(currentCell));

            foreach (var cell in SelectCellsBelow(document.Cells, currentCell))
            {
                ct.ThrowIfCancellationRequested();
                if (cell.Kind == CellKind.Code)
                    await ExecuteCellRoutedAsync(cell, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Executes only <paramref name="selectedText"/> (not the full cell) against the kernel,
        /// appending output to <paramref name="cell"/> without clearing existing outputs.
        /// </summary>
        public async Task RunSelectionAsync(NotebookCell cell, string selectedText, CancellationToken ct = default)
        {
            if (cell == null) throw new ArgumentNullException(nameof(cell));
            if (string.IsNullOrEmpty(selectedText)) return;

            if (IsJavaScriptCell(cell))
            {
                await _nodeJsExecutor.ExecuteCodeAsync(cell, selectedText, clearOutputs: false, ct).ConfigureAwait(false);
            }
            else
            {
                await EnsureKernelStartedAsync(ct).ConfigureAwait(false);
                await _executionEngine!.ExecuteSelectionAsync(cell, selectedText, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Stops the kernel, then runs all cells in the document from scratch.
        /// Resets state so <see cref="EnsureKernelStartedAsync"/> starts a fresh process.
        /// </summary>
        public async Task RestartAndRunAllAsync(NotebookDocument document, CancellationToken ct = default)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            // Reset kernel state under the startup lock so no concurrent start races.
            await _startupLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                _executionEngine?.Dispose();
                _kernelClient?.Dispose();
                _executionEngine = null;
                _kernelClient = null;
                _kernelStarted = false;

                if (_kernelProcessManager.IsRunning)
                    await _kernelProcessManager.StopAsync().ConfigureAwait(false);

                KernelInfoCache.Default.Reset();
            }
            finally
            {
                _startupLock.Release();
            }

            // EnsureKernelStartedAsync (called by RunAllCellsAsync) will start a fresh process.
            await RunAllCellsAsync(document, ct).ConfigureAwait(false);
        }

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Returns true if <paramref name="kernelName"/> identifies a JavaScript
        /// kernel that should be executed via Node.js instead of dotnet-interactive.
        /// </summary>
        internal static bool IsJavaScriptCell(string? kernelName)
        {
            var name = kernelName?.ToLowerInvariant();
            return name == "javascript" || name == "js";
        }

        private static bool IsJavaScriptCell(NotebookCell cell)
            => IsJavaScriptCell(cell.KernelName);

        /// <summary>
        /// Returns all cells that appear before <paramref name="current"/> in the list
        /// (exclusive of <paramref name="current"/> itself).
        /// </summary>
        internal static IReadOnlyList<NotebookCell> SelectCellsAbove(IList<NotebookCell> cells, NotebookCell current)
        {
            var result = new List<NotebookCell>();
            foreach (var cell in cells)
            {
                if (ReferenceEquals(cell, current)) break;
                result.Add(cell);
            }
            return result;
        }

        /// <summary>
        /// Returns <paramref name="current"/> and all cells that follow it in the list.
        /// </summary>
        internal static IReadOnlyList<NotebookCell> SelectCellsBelow(IList<NotebookCell> cells, NotebookCell current)
        {
            var result = new List<NotebookCell>();
            bool reached = false;
            foreach (var cell in cells)
            {
                if (ReferenceEquals(cell, current)) reached = true;
                if (reached) result.Add(cell);
            }
            return result;
        }

        /// <summary>
        /// Executes a single cell, routing JavaScript cells to Node.js and
        /// everything else to the dotnet-interactive kernel engine.
        /// </summary>
        private async Task ExecuteCellRoutedAsync(NotebookCell cell, CancellationToken ct)
        {
            // Mark as Running on the UI thread — PropertyChanged triggers WPF bindings in CellToolbar.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);
            cell.ExecutionStatus = CellExecutionStatus.Running;

            if (IsJavaScriptCell(cell))
            {
                await _nodeJsExecutor.ExecuteAsync(cell, ct).ConfigureAwait(false);
            }
            else
            {
                await EnsureKernelStartedAsync(ct).ConfigureAwait(false);
                await _executionEngine!.ExecuteCellAsync(cell, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Common fire-and-forget wrapper: cancels any pending execution, creates a new CTS,
        /// then runs the given async operation.
        /// </summary>
        private void FireAndForget(Func<CancellationToken, Task> operation, string operationName)
        {
            var previous = Interlocked.Exchange(ref _currentCts, null);
            previous?.Cancel();
            previous?.Dispose();

            var cts = new CancellationTokenSource();
            _currentCts = cts;

#pragma warning disable VSTHRD110, VSSDK007
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await operation(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    ExtensionLogger.LogException(nameof(ExecutionCoordinator),
                        $"Unhandled error during {operationName}.", ex);
                }
                finally
                {
                    Interlocked.CompareExchange(ref _currentCts, null, cts);
                    cts.Dispose();
                    ExecutionCompleted?.Invoke(this, EventArgs.Empty);
                }
            });
#pragma warning restore VSTHRD110, VSSDK007
        }

        /// <summary>
        /// Lazily starts the dotnet-interactive process and wires up the KernelClient.
        /// Captures the KernelReady payload to populate <see cref="KernelInfoCache"/>.
        /// Idempotent: fast-path when already running.
        /// </summary>
        private async Task EnsureKernelStartedAsync(CancellationToken ct)
        {
            if (_kernelStarted && _kernelProcessManager.IsRunning)
                return;

            await _startupLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_kernelStarted && _kernelProcessManager.IsRunning)
                    return;

                _executionEngine?.Dispose();
                _kernelClient?.Dispose();
                _executionEngine = null;
                _kernelClient = null;
                _kernelStarted = false;

                ExtensionLogger.LogInfo(nameof(ExecutionCoordinator),
                    "Starting dotnet-interactive kernel.");

                // Check installation before launching the process.
                // This runs lazily on first execution, not during LoadDocData.
                bool isInstalled = await _installationDetector.IsInstalledAsync(ct).ConfigureAwait(false);
                if (!isInstalled)
                {
                    ExtensionLogger.LogWarning(nameof(ExecutionCoordinator),
                        "dotnet-interactive not installed; showing InfoBar notification.");

                    // ShowAsync is non-blocking: it shows a VS InfoBar with Install/Open Docs
                    // actions and returns immediately. The exception below causes the cell to
                    // display an error; the user can then act via the InfoBar and re-run the cell.
                    await KernelNotInstalledDialog.ShowAsync(_installationDetector).ConfigureAwait(false);

                    throw new InvalidOperationException(
                        "dotnet-interactive is not installed. " +
                        $"Install it with: {KernelInstallationDetector.GetInstallCommand()}");
                }

                await _kernelProcessManager.StartAsync(ct).ConfigureAwait(false);

                var process = _kernelProcessManager.Process
                    ?? throw new InvalidOperationException(
                        "KernelProcessManager.Process is null after StartAsync completed.");

                _kernelClient = _kernelClientFactory != null
                    ? _kernelClientFactory(process)
                    : new KernelClient(process);
                _kernelClient.Start(_lifetimeCts.Token);

                ExtensionLogger.LogInfo(nameof(ExecutionCoordinator),
                    "Waiting for KernelReady event.");

                // Subscribe BEFORE WaitForReadyAsync so we don't miss the KernelReady event.
                KernelReady? kernelReadyInfo = null;
                using (var infoSub = _kernelClient.Events.Subscribe(
                    new ActionObserver<KernelEventEnvelope>(e =>
                    {
                        if (e.EventType == KernelEventTypes.KernelReady)
                        {
                            try
                            {
                                kernelReadyInfo = JsonSerializer.Deserialize<KernelReady>(
                                    e.Event.GetRawText());
                            }
                            catch { /* ignore parse errors */ }
                        }
                    })))
                {
                    try
                    {
                        await _kernelClient.WaitForReadyAsync(ct).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Kernel failed to start — clean up so next attempt starts fresh.
                        _kernelClient?.Dispose();
                        _kernelClient = null;
                        _executionEngine = null;
                        _kernelStarted = false;

                        // Stop the orphaned kernel process to prevent resource leaks.
                        try { await _kernelProcessManager.StopAsync().ConfigureAwait(false); }
                        catch { /* best-effort cleanup */ }

                        throw;
                    }
                }

                if (kernelReadyInfo != null)
                    KernelInfoCache.Default.Populate(kernelReadyInfo);

                _executionEngine = new CellExecutionEngine(_kernelClient);
                _kernelStarted = true;

                if (_kernelClient is KernelClient concreteClient)
                    KernelClientAvailable?.Invoke(concreteClient);

                ExtensionLogger.LogInfo(nameof(ExecutionCoordinator),
                    "Kernel ready. Execution engine initialized.");
            }
            finally
            {
                _startupLock.Release();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _lifetimeCts.Cancel();
            _lifetimeCts.Dispose();

            var cts = Interlocked.Exchange(ref _currentCts, null);
            cts?.Cancel();
            cts?.Dispose();

            _executionEngine?.Dispose();
            _kernelClient?.Dispose();
            _startupLock.Dispose();
        }
    }
}
