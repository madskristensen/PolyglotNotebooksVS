using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using PolyglotNotebooks.Diagnostics;
using PolyglotNotebooks.Protocol;

namespace PolyglotNotebooks.Variables
{
    /// <summary>
    /// Bridges the Variable Explorer UI with the kernel protocol.
    /// Sends RequestValueInfos/RequestValue/SendValue commands, caches results
    /// in <see cref="Variables"/>, and auto-refreshes after SubmitCode completions.
    /// </summary>
    public sealed class VariableService : IDisposable
    {
        private static VariableService? _current;

        /// <summary>Global singleton, created lazily on first access.</summary>
        public static VariableService Current
        {
            get
            {
                if (_current == null)
                    Initialize();
                return _current!;
            }
        }

        private KernelClient? _kernelClient;
        private IDisposable? _eventSubscription;
        private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);
        private readonly List<string> _knownKernels = new List<string> { "csharp", "fsharp" };
        private bool _disposed;

        /// <summary>Observable list bound to the Variable Explorer DataGrid.</summary>
        public ObservableCollection<VariableInfo> Variables { get; } = new ObservableCollection<VariableInfo>();

        /// <summary>Creates and stores the global singleton.</summary>
        public static void Initialize()
        {
            _current?.Dispose();
            _current = new VariableService();
        }

        // ── Kernel wiring ──────────────────────────────────────────────────────

        /// <summary>
        /// Called from NotebookEditorPane when a new kernel client becomes available.
        /// Replaces any previous client and subscribes to events for auto-refresh.
        /// </summary>
        public void SetKernelClient(KernelClient client)
        {
            _eventSubscription?.Dispose();
            _kernelClient = client;

            _eventSubscription = client.Events.Subscribe(
                new ActionObserver<KernelEventEnvelope>(OnKernelEvent));

            ExtensionLogger.LogInfo(nameof(VariableService), "Kernel client attached.");
        }

        /// <summary>Replaces the kernel name list used for refresh sweeps.</summary>
        public void SetKnownKernels(IEnumerable<string> kernelNames)
        {
            _knownKernels.Clear();
            _knownKernels.AddRange(kernelNames);
        }

        // ── Protocol helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Queries each known kernel for variable infos and refreshes the
        /// <see cref="Variables"/> collection on the UI thread.
        /// </summary>
        public async Task RefreshVariablesAsync(CancellationToken ct = default)
        {
            if (_kernelClient == null) return;

            // Serialize refreshes; skip if one is already running.
            if (!await _refreshLock.WaitAsync(0, ct).ConfigureAwait(false))
                return;

            try
            {
                var fresh = new List<VariableInfo>();
                foreach (var kernelName in _knownKernels.ToArray())
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var result = await _kernelClient
                            .RequestValueInfosAsync(kernelName, ct: ct)
                            .ConfigureAwait(false);

                        foreach (var info in result.ValueInfos)
                        {
                            fresh.Add(new VariableInfo
                            {
                                Name = info.Name,
                                TypeName = info.TypeName ?? string.Empty,
                                Value = Truncate(info.FormattedValue?.Value ?? string.Empty),
                                KernelName = kernelName
                            });
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        ExtensionLogger.LogWarning(nameof(VariableService),
                            $"RequestValueInfos failed for kernel '{kernelName}': {ex.Message}");
                    }
                }

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);
                Variables.Clear();
                foreach (var v in fresh)
                    Variables.Add(v);
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        /// <summary>
        /// Returns the full (untruncated) formatted value for a specific variable.
        /// </summary>
        public async Task<string> GetFullValueAsync(string name, string kernelName, CancellationToken ct = default)
        {
            if (_kernelClient == null) return string.Empty;
            try
            {
                var result = await _kernelClient
                    .RequestValueAsync(name, kernelName, ct: ct)
                    .ConfigureAwait(false);
                return result.FormattedValue?.Value ?? string.Empty;
            }
            catch (Exception ex)
            {
                ExtensionLogger.LogWarning(nameof(VariableService),
                    $"RequestValue failed for '{name}' in '{kernelName}': {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Sends a value to a target kernel using SendValue.
        /// </summary>
        public async Task SendVariableAsync(string name, string formattedValue, string mimeType,
            string targetKernel, CancellationToken ct = default)
        {
            if (_kernelClient == null) return;
            await _kernelClient
                .SendValueAsync(name, formattedValue, mimeType, targetKernel, ct)
                .ConfigureAwait(false);
        }

        // ── Event handling ────────────────────────────────────────────────────

        private void OnKernelEvent(KernelEventEnvelope envelope)
        {
            // Auto-refresh after a SubmitCode command completes successfully.
            if (envelope.EventType == KernelEventTypes.CommandSucceeded &&
                envelope.Command?.CommandType == CommandTypes.SubmitCode)
            {
#pragma warning disable VSTHRD110, VSSDK007
                _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    try
                    {
                        await RefreshVariablesAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        ExtensionLogger.LogWarning(nameof(VariableService),
                            $"Auto-refresh after execution failed: {ex.Message}");
                    }
                });
#pragma warning restore VSTHRD110, VSSDK007
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string Truncate(string value, int max = 100) =>
            value.Length > max ? value.Substring(0, max) + "…" : value;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _eventSubscription?.Dispose();
            _refreshLock.Dispose();
            if (ReferenceEquals(_current, this))
                _current = null;
        }
    }
}
