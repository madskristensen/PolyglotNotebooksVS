using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using PolyglotNotebooks.Diagnostics;

namespace PolyglotNotebooks.Protocol
{
    /// <summary>
    /// Shared serialization options for the dotnet-interactive JSON wire protocol.
    /// camelCase naming matches the TypeScript contracts exactly.
    /// </summary>
    internal static class ProtocolSerializerOptions
    {
        public static readonly JsonSerializerOptions Default = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true
        };
    }

    /// <summary>
    /// Thrown when the kernel replies with a CommandFailed event.
    /// </summary>
    public sealed class KernelCommandException : Exception
    {
        public KernelEventEnvelope EventEnvelope { get; }

        public KernelCommandException(KernelEventEnvelope envelope)
            : base(ExtractMessage(envelope))
        {
            EventEnvelope = envelope;
        }

        private static string ExtractMessage(KernelEventEnvelope envelope)
        {
            try
            {
                var failure = envelope.Event.Deserialize<CommandFailed>(ProtocolSerializerOptions.Default);
                return failure?.Message ?? "Kernel command failed.";
            }
            catch
            {
                return "Kernel command failed.";
            }
        }
    }

    /// <summary>
    /// Protocol client for communicating with a dotnet-interactive kernel over stdin/stdout
    /// using line-delimited JSON.  All public methods are async and thread-safe.
    /// </summary>
    public sealed class KernelClient : IDisposable
    {
        private readonly Process _process;
        private readonly Subject<KernelEventEnvelope> _events = new Subject<KernelEventEnvelope>();
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource? _cts;
        private Task? _readerTask;

        /// <summary>
        /// Timeout in milliseconds applied to all kernel commands (SubmitCode, RequestCompletions, etc.).
        /// Default is 30 seconds. Set to <see cref="Timeout.Infinite"/> to disable.
        /// </summary>
        public int CommandTimeoutMs { get; set; } = 30_000;

        /// <summary>
        /// Observable stream of all kernel events received from stdout.
        /// </summary>
        public IObservable<KernelEventEnvelope> Events => _events;

        public KernelClient(Process process)
        {
            _process = process ?? throw new ArgumentNullException(nameof(process));
        }

        /// <summary>
        /// Starts the background stdout reader.  Call once after starting the process.
        /// </summary>
        public void Start(CancellationToken ct = default)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _readerTask = Task.Run(() => ReadLoopAsync(_cts.Token), _cts.Token);
            ExtensionLogger.LogInfo(nameof(KernelClient), "Stdout reader started.");
        }

        // ── Low-level send ────────────────────────────────────────────────────

        /// <summary>
        /// Serializes the envelope to a single JSON line and writes it to stdin.
        /// Thread-safe: a semaphore serializes concurrent writes.
        /// </summary>
        public async Task SendCommandAsync(KernelCommandEnvelope envelope, CancellationToken ct = default)
        {
            if (envelope is null) throw new ArgumentNullException(nameof(envelope));

            var json = JsonSerializer.Serialize(envelope, ProtocolSerializerOptions.Default);

            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await _process.StandardInput.WriteLineAsync(json).ConfigureAwait(false);
                await _process.StandardInput.FlushAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException ex)
            {
                ExtensionLogger.LogError(nameof(KernelClient),
                    $"Cannot send command '{envelope.CommandType}': process stdin is disposed.");
                throw new InvalidOperationException("The kernel process is no longer available.", ex);
            }
            catch (IOException ex)
            {
                ExtensionLogger.LogError(nameof(KernelClient),
                    $"IO error sending command '{envelope.CommandType}': {ex.Message}");
                throw new InvalidOperationException(
                    "Communication with the kernel process failed.", ex);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// Creates an envelope for the given command, then sends it.
        /// Returns the envelope so the caller can correlate events via its Token.
        /// </summary>
        public async Task<KernelCommandEnvelope> SendCommandAsync<T>(
            string commandType,
            T command,
            CancellationToken ct = default)
        {
            var envelope = KernelCommandEnvelope.Create(commandType, command);
            await SendCommandAsync(envelope, ct).ConfigureAwait(false);
            return envelope;
        }

        // ── Lifecycle helpers ─────────────────────────────────────────────────

        /// <summary>
        /// Waits until the kernel emits a KernelReady event.  Call immediately after Start().
        /// </summary>
        public async Task WaitForReadyAsync(CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<bool>();
            using var reg = ct.Register(() => tcs.TrySetCanceled());
            using var sub = Events.Subscribe(new ActionObserver<KernelEventEnvelope>(e =>
            {
                if (e.EventType == KernelEventTypes.KernelReady)
                    tcs.TrySetResult(true);
            }));
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(30), ct)).ConfigureAwait(false);
            if (completed != tcs.Task)
                throw new TimeoutException("Kernel did not send KernelReady within 30 seconds.");
            await tcs.Task.ConfigureAwait(false); // propagate cancellation
        }

        // ── Convenience methods ───────────────────────────────────────────────

        /// <summary>
        /// Submits code to the kernel and waits until CommandSucceeded or throws on CommandFailed.
        /// Applies <see cref="CommandTimeoutMs"/> as an outer deadline.
        /// </summary>
        public async Task SubmitCodeAsync(string code, string? targetKernelName = null, CancellationToken ct = default)
        {
            using var timeoutCts = NewTimeoutCts(ct);
            var effectiveCt = timeoutCts.Token;

            var envelope = KernelCommandEnvelope.Create(CommandTypes.SubmitCode, new SubmitCode
            {
                Code = code,
                TargetKernelName = targetKernelName
            });

            ExtensionLogger.LogInfo(nameof(KernelClient),
                $"SubmitCode token={envelope.Token} kernel={targetKernelName ?? "default"}.");

            using var observer = new EventObserver(Events, envelope.Token);
            var terminalTask = observer.WaitForTerminalEventAsync(effectiveCt);

            await SendCommandAsync(envelope, effectiveCt).ConfigureAwait(false);

            var terminal = await terminalTask.ConfigureAwait(false);
            if (terminal.EventType == KernelEventTypes.CommandFailed)
            {
                ExtensionLogger.LogWarning(nameof(KernelClient),
                    $"SubmitCode failed. Token={envelope.Token}.");
                throw new KernelCommandException(terminal);
            }

            ExtensionLogger.LogInfo(nameof(KernelClient),
                $"SubmitCode succeeded. Token={envelope.Token}.");
        }

        /// <summary>
        /// Requests completions for the given code and cursor position.
        /// </summary>
        public async Task<CompletionsProduced> RequestCompletionsAsync(
            string code,
            LinePosition position,
            CancellationToken ct = default)
        {
            using var timeoutCts = NewTimeoutCts(ct);
            var effectiveCt = timeoutCts.Token;

            var envelope = KernelCommandEnvelope.Create(CommandTypes.RequestCompletions, new RequestCompletions
            {
                Code = code,
                LinePosition = position
            });

            using var observer = new EventObserver(Events, envelope.Token);
            var completionsTask = observer.WaitForEventTypeAsync(KernelEventTypes.CompletionsProduced, effectiveCt);
            var terminalTask = observer.WaitForTerminalEventAsync(effectiveCt);

            await SendCommandAsync(envelope, effectiveCt).ConfigureAwait(false);

            var terminal = await terminalTask.ConfigureAwait(false);
            if (terminal.EventType == KernelEventTypes.CommandFailed)
            {
                ExtensionLogger.LogWarning(nameof(KernelClient),
                    $"RequestCompletions failed. Token={envelope.Token}.");
                throw new KernelCommandException(terminal);
            }

            var resultEnvelope = await completionsTask.ConfigureAwait(false);
            return resultEnvelope.Event.Deserialize<CompletionsProduced>(ProtocolSerializerOptions.Default)
                ?? new CompletionsProduced();
        }

        /// <summary>
        /// Requests hover text for the given code and cursor position.
        /// </summary>
        public async Task<HoverTextProduced> RequestHoverTextAsync(
            string code,
            LinePosition position,
            CancellationToken ct = default)
        {
            using var timeoutCts = NewTimeoutCts(ct);
            var effectiveCt = timeoutCts.Token;

            var envelope = KernelCommandEnvelope.Create(CommandTypes.RequestHoverText, new RequestHoverText
            {
                Code = code,
                LinePosition = position
            });

            using var observer = new EventObserver(Events, envelope.Token);
            var hoverTask = observer.WaitForEventTypeAsync(KernelEventTypes.HoverTextProduced, effectiveCt);
            var terminalTask = observer.WaitForTerminalEventAsync(effectiveCt);

            await SendCommandAsync(envelope, effectiveCt).ConfigureAwait(false);

            var terminal = await terminalTask.ConfigureAwait(false);
            if (terminal.EventType == KernelEventTypes.CommandFailed)
            {
                ExtensionLogger.LogWarning(nameof(KernelClient),
                    $"RequestHoverText failed. Token={envelope.Token}.");
                throw new KernelCommandException(terminal);
            }

            var resultEnvelope = await hoverTask.ConfigureAwait(false);
            return resultEnvelope.Event.Deserialize<HoverTextProduced>(ProtocolSerializerOptions.Default)
                ?? new HoverTextProduced();
        }

        /// <summary>
        /// Requests signature help for the given code and cursor position.
        /// </summary>
        public async Task<SignatureHelpProduced> RequestSignatureHelpAsync(
            string code,
            LinePosition position,
            CancellationToken ct = default)
        {
            using var timeoutCts = NewTimeoutCts(ct);
            var effectiveCt = timeoutCts.Token;

            var envelope = KernelCommandEnvelope.Create(CommandTypes.RequestSignatureHelp, new RequestSignatureHelp
            {
                Code = code,
                LinePosition = position
            });

            using var observer = new EventObserver(Events, envelope.Token);
            var sigHelpTask = observer.WaitForEventTypeAsync(KernelEventTypes.SignatureHelpProduced, effectiveCt);
            var terminalTask = observer.WaitForTerminalEventAsync(effectiveCt);

            await SendCommandAsync(envelope, effectiveCt).ConfigureAwait(false);

            var terminal = await terminalTask.ConfigureAwait(false);
            if (terminal.EventType == KernelEventTypes.CommandFailed)
            {
                ExtensionLogger.LogWarning(nameof(KernelClient),
                    $"RequestSignatureHelp failed. Token={envelope.Token}.");
                throw new KernelCommandException(terminal);
            }

            var resultEnvelope = await sigHelpTask.ConfigureAwait(false);
            return resultEnvelope.Event.Deserialize<SignatureHelpProduced>(ProtocolSerializerOptions.Default)
                ?? new SignatureHelpProduced();
        }

        /// <summary>
        /// Requests diagnostics for the given code.
        /// </summary>
        public async Task<DiagnosticsProduced> RequestDiagnosticsAsync(
            string code,
            string? targetKernelName = null,
            CancellationToken ct = default)
        {
            using var timeoutCts = NewTimeoutCts(ct);
            var effectiveCt = timeoutCts.Token;

            var envelope = KernelCommandEnvelope.Create(CommandTypes.RequestDiagnostics, new RequestDiagnostics
            {
                Code = code,
                TargetKernelName = targetKernelName
            });

            using var observer = new EventObserver(Events, envelope.Token);
            var diagTask = observer.WaitForEventTypeAsync(KernelEventTypes.DiagnosticsProduced, effectiveCt);
            var terminalTask = observer.WaitForTerminalEventAsync(effectiveCt);

            await SendCommandAsync(envelope, effectiveCt).ConfigureAwait(false);

            var terminal = await terminalTask.ConfigureAwait(false);
            if (terminal.EventType == KernelEventTypes.CommandFailed)
            {
                ExtensionLogger.LogWarning(nameof(KernelClient),
                    $"RequestDiagnostics failed. Token={envelope.Token}.");
                throw new KernelCommandException(terminal);
            }

            var resultEnvelope = await diagTask.ConfigureAwait(false);
            return resultEnvelope.Event.Deserialize<DiagnosticsProduced>(ProtocolSerializerOptions.Default)
                ?? new DiagnosticsProduced();
        }

        /// <summary>
        /// Requests the list of variable infos from the specified sub-kernel.
        /// </summary>
        public async Task<ValueInfosProduced> RequestValueInfosAsync(
            string? targetKernelName = null,
            string mimeType = "text/plain",
            CancellationToken ct = default)
        {
            using var timeoutCts = NewTimeoutCts(ct);
            var effectiveCt = timeoutCts.Token;

            var envelope = KernelCommandEnvelope.Create(CommandTypes.RequestValueInfos, new RequestValueInfos
            {
                MimeType = mimeType,
                TargetKernelName = targetKernelName
            });

            using var observer = new EventObserver(Events, envelope.Token);
            var resultTask = observer.WaitForEventTypeAsync(KernelEventTypes.ValueInfosProduced, effectiveCt);
            var terminalTask = observer.WaitForTerminalEventAsync(effectiveCt);

            await SendCommandAsync(envelope, effectiveCt).ConfigureAwait(false);

            var terminal = await terminalTask.ConfigureAwait(false);
            if (terminal.EventType == KernelEventTypes.CommandFailed)
            {
                ExtensionLogger.LogWarning(nameof(KernelClient),
                    $"RequestValueInfos failed. Token={envelope.Token}.");
                throw new KernelCommandException(terminal);
            }

            var resultEnvelope = await resultTask.ConfigureAwait(false);
            return resultEnvelope.Event.Deserialize<ValueInfosProduced>(ProtocolSerializerOptions.Default)
                ?? new ValueInfosProduced();
        }

        /// <summary>
        /// Requests the full formatted value for a specific variable from a sub-kernel.
        /// </summary>
        public async Task<ValueProduced> RequestValueAsync(
            string name,
            string? targetKernelName = null,
            string mimeType = "text/plain",
            CancellationToken ct = default)
        {
            using var timeoutCts = NewTimeoutCts(ct);
            var effectiveCt = timeoutCts.Token;

            var envelope = KernelCommandEnvelope.Create(CommandTypes.RequestValue, new RequestValue
            {
                Name = name,
                MimeType = mimeType,
                TargetKernelName = targetKernelName
            });

            using var observer = new EventObserver(Events, envelope.Token);
            var resultTask = observer.WaitForEventTypeAsync(KernelEventTypes.ValueProduced, effectiveCt);
            var terminalTask = observer.WaitForTerminalEventAsync(effectiveCt);

            await SendCommandAsync(envelope, effectiveCt).ConfigureAwait(false);

            var terminal = await terminalTask.ConfigureAwait(false);
            if (terminal.EventType == KernelEventTypes.CommandFailed)
            {
                ExtensionLogger.LogWarning(nameof(KernelClient),
                    $"RequestValue failed for '{name}'. Token={envelope.Token}.");
                throw new KernelCommandException(terminal);
            }

            var resultEnvelope = await resultTask.ConfigureAwait(false);
            return resultEnvelope.Event.Deserialize<ValueProduced>(ProtocolSerializerOptions.Default)
                ?? new ValueProduced();
        }

        /// <summary>
        /// Sends a value to the specified target kernel.
        /// </summary>
        public async Task SendValueAsync(
            string name,
            string formattedValue,
            string mimeType = "text/plain",
            string? targetKernelName = null,
            CancellationToken ct = default)
        {
            using var timeoutCts = NewTimeoutCts(ct);
            var effectiveCt = timeoutCts.Token;

            var envelope = KernelCommandEnvelope.Create(CommandTypes.SendValue, new SendValue
            {
                Name = name,
                FormattedValue = new FormattedValue { MimeType = mimeType, Value = formattedValue },
                TargetKernelName = targetKernelName
            });

            using var observer = new EventObserver(Events, envelope.Token);
            var terminalTask = observer.WaitForTerminalEventAsync(effectiveCt);

            await SendCommandAsync(envelope, effectiveCt).ConfigureAwait(false);

            var terminal = await terminalTask.ConfigureAwait(false);
            if (terminal.EventType == KernelEventTypes.CommandFailed)
            {
                ExtensionLogger.LogWarning(nameof(KernelClient),
                    $"SendValue failed for '{name}'. Token={envelope.Token}.");
                throw new KernelCommandException(terminal);
            }
        }

        // ── Background reader ─────────────────────────────────────────────────

        private async Task ReadLoopAsync(CancellationToken ct)
        {
            var reader = _process.StandardOutput;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (line is null)
                    {
                        ExtensionLogger.LogInfo(nameof(KernelClient),
                            "Kernel stdout closed (process ended).");
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    DispatchLine(line);
                }
            }
            catch (OperationCanceledException) { /* normal shutdown */ }
            catch (Exception ex)
            {
                ExtensionLogger.LogException(nameof(KernelClient),
                    "Unexpected error in kernel stdout reader", ex);
                _events.OnError(ex);
                return;
            }
            _events.OnCompleted();
        }

        private void DispatchLine(string line)
        {
            try
            {
                var envelope = JsonSerializer.Deserialize<KernelEventEnvelope>(line, ProtocolSerializerOptions.Default);
                if (envelope != null)
                    _events.OnNext(envelope);
            }
            catch (JsonException ex)
            {
                // Malformed line — log and skip; kernel may emit progress or diagnostic text.
                ExtensionLogger.LogWarning(nameof(KernelClient),
                    $"Malformed JSON from kernel stdout (skipped): {ex.Message} " +
                    $"| Line[0..200]: {line.Substring(0, Math.Min(line.Length, 200))}");
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a CancellationTokenSource linked to <paramref name="ct"/> and the command timeout.
        /// Caller must dispose the returned source.
        /// </summary>
        private CancellationTokenSource NewTimeoutCts(CancellationToken ct)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (CommandTimeoutMs != Timeout.Infinite && CommandTimeoutMs > 0)
                cts.CancelAfter(CommandTimeoutMs);
            return cts;
        }

        public void Dispose()
        {
            ExtensionLogger.LogInfo(nameof(KernelClient), "Disposing kernel client; cancelling pending requests.");
            _cts?.Cancel();
            _cts?.Dispose();
            _writeLock.Dispose();
            _events.Dispose(); // triggers OnCompleted → faults all pending EventObserver tasks
        }
    }
}