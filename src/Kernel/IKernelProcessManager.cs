using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace PolyglotNotebooks.Kernel
{
    /// <summary>
    /// Abstraction over <see cref="KernelProcessManager"/> to enable unit testing
    /// with mock implementations (e.g. Moq).
    /// </summary>
    public interface IKernelProcessManager : IDisposable
    {
        /// <summary>True if the process is running and has not exited.</summary>
        bool IsRunning { get; }

        /// <summary>The underlying process. Available after StartAsync completes.</summary>
        Process? Process { get; }

        /// <summary>Current kernel status.</summary>
        KernelStatus Status { get; }

        /// <summary>Connection metadata, populated after StartAsync completes.</summary>
        KernelConnectionInfo? ConnectionInfo { get; }

        /// <summary>Whether the kernel supports re-running cells after auto-restart.</summary>
        bool CanReRunCellsAfterRestart { get; }

        /// <summary>Launches the kernel process.</summary>
        Task StartAsync(CancellationToken ct = default);

        /// <summary>Gracefully shuts down the process.</summary>
        Task StopAsync();

        /// <summary>Stops and restarts the process.</summary>
        Task RestartAsync(CancellationToken ct = default);

        /// <summary>Fired when the process exits unexpectedly.</summary>
        event EventHandler<ProcessExitedEventArgs>? ProcessExited;

        /// <summary>Fired whenever the kernel status changes.</summary>
        event EventHandler<KernelStatusChangedEventArgs>? StatusChanged;

        /// <summary>Fired when the kernel crashes unexpectedly.</summary>
        event EventHandler<KernelCrashedEventArgs>? KernelCrashed;
    }
}
