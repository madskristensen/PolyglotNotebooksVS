using System;
using System.Threading;
using System.Threading.Tasks;

namespace PolyglotNotebooks.Protocol
{
    /// <summary>
    /// Abstraction over <see cref="KernelClient"/> to enable unit testing
    /// with mock implementations (e.g. Moq).
    /// </summary>
    public interface IKernelClient : IDisposable
    {
        /// <summary>Observable stream of all kernel events received from stdout.</summary>
        IObservable<KernelEventEnvelope> Events { get; }

        /// <summary>
        /// Timeout in milliseconds applied to all kernel commands.
        /// </summary>
        int CommandTimeoutMs { get; set; }

        /// <summary>Starts the background stdout reader.</summary>
        void Start(CancellationToken ct = default);

        /// <summary>Sends a pre-built command envelope to the kernel.</summary>
        Task SendCommandAsync(KernelCommandEnvelope envelope, CancellationToken ct = default);

        /// <summary>Creates an envelope for the given command, sends it, and returns the envelope.</summary>
        Task<KernelCommandEnvelope> SendCommandAsync<T>(string commandType, T command, CancellationToken ct = default);

        /// <summary>Waits until the kernel emits a KernelReady event.</summary>
        Task WaitForReadyAsync(CancellationToken ct = default);

        /// <summary>Submits code and waits for CommandSucceeded; throws on failure.</summary>
        Task SubmitCodeAsync(string code, string? targetKernelName = null, CancellationToken ct = default);
    }
}
