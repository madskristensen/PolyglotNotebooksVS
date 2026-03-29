using System;
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
        private readonly KernelProcessManager _kernelProcessManager;
        private readonly KernelInstallationDetector _installationDetector = new KernelInstallationDetector();
        private readonly SemaphoreSlim _startupLock = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _lifetimeCts = new CancellationTokenSource();
        private readonly NodeJsExecutor _nodeJsExecutor = new NodeJsExecutor();

        private KernelClient? _kernelClient;
        private CellExecutionEngine? _executionEngine;
        private CancellationTokenSource? _currentCts;
        private bool _kernelStarted;
        private bool _disposed;

        public event Action<KernelClient>? KernelClientAvailable;

        /// <summary>Raised when any run operation (Run All, Run Cell, etc.) completes or is cancelled.</summary>
        public event EventHandler? ExecutionCompleted;

        public KernelClient? KernelClient => _kernelClient;

        public ExecutionCoordinator(KernelProcessManager kernelProcessManager)
        {
            _kernelProcessManager = kernelProcessManager
                ?? throw new ArgumentNullException(nameof(kernelProcessManager));
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
                    cell.ExecutionStatus = CellExecutionStatus.Running;
            FireAndForget(ct => RunAllCellsAsync(document, ct), "Run All");
        }

        /// <summary>Fire-and-forget entry point for "Run Cells Above" (excludes current cell).</summary>
        public void HandleRunCellsAboveRequested(NotebookDocument document, NotebookCell currentCell)
        {
            if (document == null || currentCell == null) return;
            foreach (var cell in document.Cells)
            {
                if (ReferenceEquals(cell, currentCell)) break;
                if (cell.Kind == CellKind.Code)
                    cell.ExecutionStatus = CellExecutionStatus.Running;
            }
            FireAndForget(ct => RunCellsAboveAsync(document, currentCell, ct), "Run Cells Above");
        }

        /// <summary>Fire-and-forget entry point for "Run Cell and Below" (includes current cell).</summary>
        public void HandleRunCellsBelowRequested(NotebookDocument document, NotebookCell currentCell)
        {
            if (document == null || currentCell == null) return;
            bool reached = false;
            foreach (var cell in document.Cells)
            {
                if (ReferenceEquals(cell, currentCell)) reached = true;
                if (reached && cell.Kind == CellKind.Code)
                    cell.ExecutionStatus = CellExecutionStatus.Running;
            }
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
                    cell.ExecutionStatus = CellExecutionStatus.Running;
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

            foreach (var cell in document.Cells)
            {
                if (ReferenceEquals(cell, currentCell)) break;
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

            bool reached = false;
            foreach (var cell in document.Cells)
            {
                if (ReferenceEquals(cell, currentCell)) reached = true;
                if (!reached) continue;
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
        /// Returns true if the cell's kernel name is JavaScript and should be
        /// executed via Node.js instead of the dotnet-interactive kernel.
        /// </summary>
        private static bool IsJavaScriptCell(NotebookCell cell)
        {
            var name = cell.KernelName?.ToLowerInvariant();
            return name == "javascript" || name == "js";
        }

        /// <summary>
        /// Executes a single cell, routing JavaScript cells to Node.js and
        /// everything else to the dotnet-interactive kernel engine.
        /// </summary>
        private async Task ExecuteCellRoutedAsync(NotebookCell cell, CancellationToken ct)
        {
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
                        "dotnet-interactive not installed; prompting user.");

                    bool installed = await KernelNotInstalledDialog.ShowAsync(_installationDetector).ConfigureAwait(false);
                    if (installed)
                    {
                        // Dialog reports success — re-verify (cache was invalidated by the dialog).
                        isInstalled = await _installationDetector.IsInstalledAsync(ct).ConfigureAwait(false);
                    }

                    if (!isInstalled)
                    {
                        throw new InvalidOperationException(
                            "dotnet-interactive is not installed. " +
                            $"Install it with: {KernelInstallationDetector.GetInstallCommand()}");
                    }
                }

                await _kernelProcessManager.StartAsync(ct).ConfigureAwait(false);

                var process = _kernelProcessManager.Process
                    ?? throw new InvalidOperationException(
                        "KernelProcessManager.Process is null after StartAsync completed.");

                _kernelClient = new KernelClient(process);
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
                        throw;
                    }
                }

                if (kernelReadyInfo != null)
                    KernelInfoCache.Default.Populate(kernelReadyInfo);

                _executionEngine = new CellExecutionEngine(_kernelClient);
                _kernelStarted = true;

                KernelClientAvailable?.Invoke(_kernelClient);

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
