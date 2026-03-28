using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PolyglotNotebooks.Kernel
{
    /// <summary>
    /// Detects whether dotnet-interactive is installed as a global .NET tool.
    /// Results are cached for the lifetime of this instance.
    /// </summary>
    public sealed class KernelInstallationDetector
    {
        private bool? _cachedIsInstalled;
        private string? _cachedVersion;
        private readonly SemaphoreSlim _detectLock = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Returns true if <c>microsoft.dotnet-interactive</c> appears in <c>dotnet tool list -g</c>.
        /// Returns false if dotnet itself is not on PATH.
        /// </summary>
        public async Task<bool> IsInstalledAsync(CancellationToken ct = default)
        {
            if (_cachedIsInstalled.HasValue)
                return _cachedIsInstalled.Value;

            await _detectLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_cachedIsInstalled.HasValue)
                    return _cachedIsInstalled.Value;

                var output = await RunDotnetToolListAsync(ct).ConfigureAwait(false);
                _cachedIsInstalled = output != null
                    && output.IndexOf("microsoft.dotnet-interactive", StringComparison.OrdinalIgnoreCase) >= 0;
                return _cachedIsInstalled.Value;
            }
            finally
            {
                _detectLock.Release();
            }
        }

        /// <summary>
        /// Parses and returns the installed version string, or null if not installed.
        /// </summary>
        public async Task<string?> GetInstalledVersionAsync(CancellationToken ct = default)
        {
            if (_cachedVersion != null)
                return _cachedVersion;

            var output = await RunDotnetToolListAsync(ct).ConfigureAwait(false);
            if (output == null)
                return null;

            foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.IndexOf("microsoft.dotnet-interactive", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Format: "microsoft.dotnet-interactive   1.0.0-beta.xxxx   dotnet-interactive"
                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        _cachedVersion = parts[1];
                        return _cachedVersion;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Clears the cached installation state so the next call to
        /// <see cref="IsInstalledAsync"/> re-runs detection.
        /// </summary>
        public void InvalidateCache()
        {
            _cachedIsInstalled = null;
            _cachedVersion = null;
        }

        /// <summary>Returns the command users should run to install dotnet-interactive.</summary>
        public static string GetInstallCommand()
            => "dotnet tool install -g Microsoft.dotnet-interactive";

        private static async Task<string?> RunDotnetToolListAsync(CancellationToken ct)
        {
            try
            {
                var psi = new ProcessStartInfo("dotnet", "tool list -g")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
                var output = new StringBuilder();
                var tcs = new TaskCompletionSource<int>();

                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                        output.AppendLine(e.Data);
                };

                process.Exited += (s, e) => tcs.TrySetResult(process.ExitCode);

                process.Start();
                process.BeginOutputReadLine();

                try
                {
                    using (ct.Register(() => tcs.TrySetCanceled()))
                    {
                        await tcs.Task.ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Kill the process if cancellation fires while it's still running
                    try { if (!process.HasExited) process.Kill(); } catch { }
                    return null;
                }

                return output.ToString();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // dotnet is not on PATH
                return null;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }
    }
}
