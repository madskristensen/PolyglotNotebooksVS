using System;

namespace PolyglotNotebooks.Kernel
{
    public enum KernelStatus
    {
        NotStarted,
        Starting,
        Ready,
        Busy,
        Restarting,
        Stopped,
        Error
    }

    public sealed class KernelStatusChangedEventArgs : EventArgs
    {
        public KernelStatus OldStatus { get; }
        public KernelStatus NewStatus { get; }

        public KernelStatusChangedEventArgs(KernelStatus oldStatus, KernelStatus newStatus)
        {
            OldStatus = oldStatus;
            NewStatus = newStatus;
        }
    }

    /// <summary>
    /// Event arguments raised when the kernel crashes unexpectedly.
    /// Indicates whether auto-restart will be attempted.
    /// </summary>
    public sealed class KernelCrashedEventArgs : EventArgs
    {
        /// <summary>Process exit code at the time of crash.</summary>
        public int ExitCode { get; }

        /// <summary>Captured stderr output for diagnostics.</summary>
        public string StderrOutput { get; }

        /// <summary>
        /// The restart attempt that triggered this crash event (0 = first crash).
        /// </summary>
        public int AttemptNumber { get; }

        /// <summary>True if another auto-restart will be attempted.</summary>
        public bool WillRetry { get; }

        public KernelCrashedEventArgs(int exitCode, string stderrOutput, int attemptNumber, bool willRetry)
        {
            ExitCode = exitCode;
            StderrOutput = stderrOutput;
            AttemptNumber = attemptNumber;
            WillRetry = willRetry;
        }
    }
}
