using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace PolyglotNotebooks.Kernel
{
    /// <summary>
    /// Windows workaround: sends a newline to dotnet-interactive's stdin every 500 ms
    /// while a SubmitCode command is in flight, preventing the pipe from stalling on
    /// long-running executions. Thread-safe: Start/Stop may be called concurrently.
    /// </summary>
    public sealed class StdinPingTimer : IDisposable
    {
        private const int PingIntervalMs = 500;

        private readonly Func<StreamWriter?> _stdinAccessor;
        private Timer? _timer;

        // 0 = stopped, 1 = active — manipulated with Interlocked
        private int _active;
        private bool _disposed;

        public StdinPingTimer(Func<StreamWriter?> stdinAccessor)
        {
            _stdinAccessor = stdinAccessor ?? throw new ArgumentNullException(nameof(stdinAccessor));
        }

        /// <summary>Begin pinging stdin. Call when SubmitCode is sent.</summary>
        public void Start()
        {
            if (_disposed || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;

            // Only one concurrent activation
            if (Interlocked.CompareExchange(ref _active, 1, 0) != 0)
                return;

            _timer = new Timer(OnTick, null, PingIntervalMs, PingIntervalMs);
        }

        /// <summary>Stop pinging stdin. Call when CommandSucceeded/Failed is received.</summary>
        public void Stop()
        {
            if (Interlocked.CompareExchange(ref _active, 0, 1) != 1)
                return; // was already stopped

            var t = _timer;
            _timer = null;
            t?.Change(Timeout.Infinite, Timeout.Infinite);
            t?.Dispose();
        }

        private void OnTick(object state)
        {
            if (Volatile.Read(ref _active) == 0)
                return;

            try
            {
                var stdin = _stdinAccessor();
                stdin?.Write('\n');
                stdin?.Flush();
            }
            catch (ObjectDisposedException) { }
            catch (IOException) { }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Stop();
        }
    }
}
