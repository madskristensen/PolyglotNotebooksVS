using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using PolyglotNotebooks.Diagnostics;

namespace PolyglotNotebooks.Kernel
{
    public sealed class ProcessExitedEventArgs : EventArgs
    {
        public int ExitCode { get; }
        public string StderrOutput { get; }

        /// <summary>True when the process exited without an explicit StopAsync call.</summary>
        public bool WasUnexpected { get; }

        public ProcessExitedEventArgs(int exitCode, string stderrOutput, bool wasUnexpected)
        {
            ExitCode = exitCode;
            StderrOutput = stderrOutput;
            WasUnexpected = wasUnexpected;
        }
    }

    /// <summary>
    /// Manages the lifecycle of the dotnet-interactive child process.
    /// All public async methods are safe to call from any thread.
    /// Supports automatic crash recovery with exponential backoff.
    /// </summary>
    public sealed class KernelProcessManager : IDisposable
    {
        private const int StopTimeoutMs = 5000;
        private const int MaxStderrLines = 100;

        /// <summary>Maximum number of automatic restart attempts after a crash.</summary>
        public const int MaxRestartAttempts = 3;

        private readonly string _workingDirectory;
        private readonly SemaphoreSlim _restartLock = new SemaphoreSlim(1, 1);
        private readonly Queue<string> _stderrLines = new Queue<string>();
        private readonly object _stderrLock = new object();

        private Process? _process;
        private KernelStatus _status = KernelStatus.NotStarted;
        private bool _intentionalStop;
        private bool _disposed;

        // Tracks cumulative crash/retry count; Interlocked for thread safety.
        private int _restartAttempts;

        /// <summary>Fired when the process exits unexpectedly (not via StopAsync).</summary>
        public event EventHandler<ProcessExitedEventArgs>? ProcessExited;

        /// <summary>Fired whenever the kernel status changes.</summary>
        public event EventHandler<KernelStatusChangedEventArgs>? StatusChanged;

        /// <summary>
        /// Fired when the kernel crashes unexpectedly, regardless of whether a retry will happen.
        /// UI can use this to notify the user.
        /// </summary>
        public event EventHandler<KernelCrashedEventArgs>? KernelCrashed;

        /// <summary>The underlying process. Available after StartAsync completes successfully.</summary>
        public Process? Process => _process;

        /// <summary>True if the process is running and has not exited.</summary>
        public bool IsRunning
        {
            get
            {
                try { return _process != null && !_process.HasExited; }
                catch { return false; }
            }
        }

        public KernelStatus Status => _status;

        /// <summary>Connection metadata, populated after StartAsync completes.</summary>
        public KernelConnectionInfo? ConnectionInfo { get; private set; }

        /// <summary>
        /// True when the kernel supports re-running cells after an auto-restart.
        /// UI can use this flag to offer a "Re-run cells" action after crash recovery.
        /// </summary>
        public bool CanReRunCellsAfterRestart => true;

        /// <param name="workingDirectory">
        /// Working directory for the child process. Defaults to the user's home folder.
        /// Pass the document's folder when a notebook file is open.
        /// </param>
        public KernelProcessManager(string? workingDirectory = null)
        {
            _workingDirectory = string.IsNullOrEmpty(workingDirectory)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : workingDirectory!;
        }

        /// <summary>
        /// Launches <c>dotnet interactive stdio</c> on a background thread.
        /// Throws <see cref="InvalidOperationException"/> if dotnet-interactive is not installed.
        /// </summary>
        public async Task StartAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            if (IsRunning)
                return;

            SetStatus(KernelStatus.Starting);

            ExtensionLogger.LogInfo(nameof(KernelProcessManager),
                $"Starting kernel in '{_workingDirectory}'.");

            // Process.Start must NOT block the VS UI thread
            await Task.Run(() => LaunchProcess(ct), ct).ConfigureAwait(false);

            ExtensionLogger.LogInfo(nameof(KernelProcessManager),
                $"Kernel started. PID={ConnectionInfo?.ProcessId}.");
        }

        /// <summary>
        /// Gracefully shuts down the process: closes stdin, waits up to 5 s, then force-kills.
        /// </summary>
        public async Task StopAsync()
        {
            ThrowIfDisposed();

            var process = _process;
            if (process == null || process.HasExited)
            {
                SetStatus(KernelStatus.Stopped);
                return;
            }

            ExtensionLogger.LogInfo(nameof(KernelProcessManager),
                $"Stopping kernel. PID={process.Id}.");

            _intentionalStop = true;
            SetStatus(KernelStatus.Stopped);

            try { process.StandardInput.Close(); }
            catch { /* stdin may already be closed */ }

            try
            {
                var exited = await WaitForExitAsync(process, StopTimeoutMs).ConfigureAwait(false);
                if (!exited)
                    process.Kill();
            }
            catch { /* process may have already exited */ }
            finally
            {
                DisposeProcess();
            }

            ExtensionLogger.LogInfo(nameof(KernelProcessManager), "Kernel stopped.");
        }

        /// <summary>
        /// Stops and restarts the process. Serialized: concurrent calls wait in line.
        /// Resets the crash counter so the new instance gets a fresh retry budget.
        /// </summary>
        public async Task RestartAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            await _restartLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ExtensionLogger.LogInfo(nameof(KernelProcessManager), "Manual restart requested.");
                SetStatus(KernelStatus.Restarting);
                await StopAsync().ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                _intentionalStop = false;
                Interlocked.Exchange(ref _restartAttempts, 0); // fresh restart budget
                await StartAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                _restartLock.Release();
            }
        }

        // ── private helpers ──────────────────────────────────────────────────

        private void LaunchProcess(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var psi = new ProcessStartInfo("dotnet", "interactive stdio --default-kernel csharp")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = _workingDirectory
            };

            var process = new Process
            {
                StartInfo = psi,
                EnableRaisingEvents = true
            };

            process.ErrorDataReceived += OnStderrDataReceived;
            process.Exited += OnProcessExited;

            bool started;
            try
            {
                started = process.Start();
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                process.Dispose();
                ExtensionLogger.LogException(nameof(KernelProcessManager),
                    "Failed to launch dotnet", ex);
                throw new InvalidOperationException(
                    "Failed to launch dotnet. Ensure the .NET SDK is installed and 'dotnet' is on PATH. " +
                    $"Install dotnet-interactive with: {KernelInstallationDetector.GetInstallCommand()}",
                    ex);
            }

            if (!started)
            {
                process.Dispose();
                ExtensionLogger.LogError(nameof(KernelProcessManager),
                    "Process.Start returned false; kernel did not start.");
                throw new InvalidOperationException(
                    "dotnet process could not be started. " +
                    $"Install dotnet-interactive with: {KernelInstallationDetector.GetInstallCommand()}");
            }

            process.BeginErrorReadLine();

            _process = process;

            DateTime startTime;
            try { startTime = process.StartTime; }
            catch { startTime = DateTime.UtcNow; }

            ConnectionInfo = new KernelConnectionInfo
            {
                ProcessId = process.Id,
                StartTime = startTime,
                WorkingDirectory = _workingDirectory
            };

            SetStatus(KernelStatus.Ready);
        }

        private void OnStderrDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null)
                return;

            lock (_stderrLock)
            {
                _stderrLines.Enqueue(e.Data);
                while (_stderrLines.Count > MaxStderrLines)
                    _stderrLines.Dequeue();
            }
        }

        private void OnProcessExited(object sender, EventArgs e)
        {
            if (_intentionalStop || _disposed)
                return;

            int exitCode;
            try { exitCode = _process?.ExitCode ?? -1; }
            catch { exitCode = -1; }

            string stderr;
            lock (_stderrLock)
                stderr = string.Join(Environment.NewLine, _stderrLines);

            ExtensionLogger.LogWarning(nameof(KernelProcessManager),
                $"Kernel process exited unexpectedly. ExitCode={exitCode}. " +
                $"RestartAttempts={_restartAttempts}/{MaxRestartAttempts}. " +
                (string.IsNullOrEmpty(stderr) ? "" : $"Stderr snippet: {stderr.Substring(0, Math.Min(stderr.Length, 200))}"));

            SetStatus(KernelStatus.Error);
            ProcessExited?.Invoke(this, new ProcessExitedEventArgs(exitCode, stderr, wasUnexpected: true));

            int currentAttempt = _restartAttempts; // snapshot before increment
            bool willRetry = currentAttempt < MaxRestartAttempts;

            KernelCrashed?.Invoke(this, new KernelCrashedEventArgs(
                exitCode, stderr, currentAttempt, willRetry));

            if (willRetry)
            {
#pragma warning disable VSTHRD110, VSSDK007 // Intentional fire-and-forget for auto-restart
                _ = ThreadHelper.JoinableTaskFactory.RunAsync(() => AutoRestartAsync(currentAttempt, exitCode));
#pragma warning restore VSTHRD110, VSSDK007
            }
            else
            {
                ExtensionLogger.LogError(nameof(KernelProcessManager),
                    $"Kernel has crashed {MaxRestartAttempts} time(s); giving up on auto-restart.");
            }
        }

        /// <summary>
        /// Performs a single auto-restart attempt with exponential backoff.
        /// Delays: attempt 0 → 1 s, attempt 1 → 2 s, attempt 2 → 4 s.
        /// </summary>
        private async Task AutoRestartAsync(int attemptBefore, int crashExitCode)
        {
            int newAttemptNumber = Interlocked.Increment(ref _restartAttempts);

            // Exponential backoff: 1s, 2s, 4s
            int delayMs = (int)Math.Pow(2, attemptBefore) * 1000;

            ExtensionLogger.LogInfo(nameof(KernelProcessManager),
                $"Auto-restart #{newAttemptNumber} scheduled in {delayMs / 1000}s " +
                $"(previous exit code: {crashExitCode}).");

            await Task.Delay(delayMs).ConfigureAwait(false);

            if (_disposed || _intentionalStop)
            {
                ExtensionLogger.LogInfo(nameof(KernelProcessManager),
                    "Auto-restart aborted: manager disposed or intentional stop.");
                return;
            }

            await _restartLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed || _intentionalStop)
                    return;

                SetStatus(KernelStatus.Restarting);
                DisposeProcess(); // clean up the crashed process reference

                ExtensionLogger.LogInfo(nameof(KernelProcessManager),
                    $"Auto-restart #{newAttemptNumber} of {MaxRestartAttempts} starting...");

                try
                {
                    _intentionalStop = false;
                    await StartAsync().ConfigureAwait(false);

                    // Success — reset the crash counter
                    Interlocked.Exchange(ref _restartAttempts, 0);

                    ExtensionLogger.LogInfo(nameof(KernelProcessManager),
                        $"Auto-restart #{newAttemptNumber} succeeded.");
                }
                catch (Exception ex)
                {
                    ExtensionLogger.LogException(nameof(KernelProcessManager),
                        $"Auto-restart #{newAttemptNumber} failed", ex);
                    SetStatus(KernelStatus.Error);
                }
            }
            finally
            {
                _restartLock.Release();
            }
        }

        private void SetStatus(KernelStatus newStatus)
        {
            var old = _status;
            _status = newStatus;
            if (old != newStatus)
                StatusChanged?.Invoke(this, new KernelStatusChangedEventArgs(old, newStatus));
        }

        private static Task<bool> WaitForExitAsync(Process process, int timeoutMs)
        {
            var tcs = new TaskCompletionSource<bool>();

            EventHandler onExited = null!;
            onExited = (s, e) =>
            {
                process.Exited -= onExited;
                tcs.TrySetResult(true);
            };

            process.Exited += onExited;

            // Guard against the process having already exited before we subscribed
            if (process.HasExited)
            {
                process.Exited -= onExited;
                tcs.TrySetResult(true);
                return tcs.Task;
            }

            // Timeout fallback
            _ = Task.Delay(timeoutMs).ContinueWith(
                _ => tcs.TrySetResult(false),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return tcs.Task;
        }

        private void DisposeProcess()
        {
            var process = _process;
            _process = null;
            ConnectionInfo = null;

            if (process == null)
                return;

            process.ErrorDataReceived -= OnStderrDataReceived;
            process.Exited -= OnProcessExited;
            try { process.Dispose(); }
            catch { }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(KernelProcessManager));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _intentionalStop = true;

            ExtensionLogger.LogInfo(nameof(KernelProcessManager), "Disposing kernel process manager.");

            var process = _process;
            if (process != null && !process.HasExited)
            {
                try { process.Kill(); }
                catch { }
            }

            DisposeProcess();
            _restartLock.Dispose();
        }
    }
}