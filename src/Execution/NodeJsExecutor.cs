using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using PolyglotNotebooks.Diagnostics;
using PolyglotNotebooks.Models;

namespace PolyglotNotebooks.Execution
{
    /// <summary>
    /// Executes JavaScript code cells via a Node.js subprocess.
    /// Each execution spawns a short-lived node process, captures stdout/stderr,
    /// and maps the results into <see cref="NotebookCell.Outputs"/>.
    /// </summary>
    internal sealed class NodeJsExecutor
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

        private static int s_executionCounter;

        /// <summary>
        /// Returns true if Node.js is available on the system PATH.
        /// </summary>
        public static bool IsAvailable()
        {
            try
            {
                var psi = new ProcessStartInfo("node", "--version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using (var proc = Process.Start(psi))
                {
                    proc.WaitForExit(5000);
                    return proc.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Executes the given JavaScript cell via Node.js, streaming outputs
        /// into <see cref="NotebookCell.Outputs"/>.
        /// </summary>
        public Task ExecuteAsync(NotebookCell cell, CancellationToken ct = default)
        {
            if (cell == null) throw new ArgumentNullException(nameof(cell));
            return ExecuteCodeAsync(cell, cell.Contents, clearOutputs: true, ct);
        }

        /// <summary>
        /// Executes the specified JavaScript code via Node.js against the given cell.
        /// When <paramref name="clearOutputs"/> is false, results append to existing outputs
        /// (used for Run Selection).
        /// </summary>
        public async Task ExecuteCodeAsync(NotebookCell cell, string code, bool clearOutputs, CancellationToken ct = default)
        {
            if (cell == null) throw new ArgumentNullException(nameof(cell));

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);
            cell.ExecutionStatus = CellExecutionStatus.Running;
            if (clearOutputs)
                cell.Outputs.Clear();

            try
            {
                if (string.IsNullOrWhiteSpace(code))
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);
                    cell.ExecutionStatus = CellExecutionStatus.Succeeded;
                    cell.ExecutionOrder = Interlocked.Increment(ref s_executionCounter);
                    return;
                }

                // Write code to a temp file so we don't have to worry about
                // escaping quotes in command-line arguments.
                var tempFile = Path.Combine(Path.GetTempPath(), $"polyglot_nb_{Guid.NewGuid():N}.js");
                try
                {
                    File.WriteAllText(tempFile, code, Encoding.UTF8);

                    var psi = new ProcessStartInfo("node", $"\"{tempFile}\"")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8,
                    };

                    ExtensionLogger.LogInfo(nameof(NodeJsExecutor),
                        $"Launching node process for JS cell.");

                    using (var process = Process.Start(psi))
                    {
                        if (process == null)
                            throw new InvalidOperationException("Failed to start Node.js process.");

                        // Read stdout and stderr concurrently.
                        var stdoutTask = process.StandardOutput.ReadToEndAsync();
                        var stderrTask = process.StandardError.ReadToEndAsync();

                        // Wait for exit with timeout + cancellation.
                        using (ct.Register(() => { try { process.Kill(); } catch { } }))
                        {
                            var completed = await WaitForExitAsync(process, DefaultTimeout).ConfigureAwait(false);
                            if (!completed)
                            {
                                try { process.Kill(); } catch { }
                                throw new TimeoutException(
                                    $"Node.js execution timed out after {DefaultTimeout.TotalSeconds}s.");
                            }
                        }

                        ct.ThrowIfCancellationRequested();

                        var stdout = await stdoutTask.ConfigureAwait(false);
                        var stderr = await stderrTask.ConfigureAwait(false);

                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

                        if (!string.IsNullOrEmpty(stdout))
                        {
                            cell.Outputs.Add(new CellOutput(
                                CellOutputKind.StandardOutput,
                                new List<FormattedOutput> { new FormattedOutput("text/plain", stdout) }));
                        }

                        if (!string.IsNullOrEmpty(stderr))
                        {
                            cell.Outputs.Add(new CellOutput(
                                CellOutputKind.StandardError,
                                new List<FormattedOutput> { new FormattedOutput("text/plain", stderr) }));
                        }

                        if (process.ExitCode == 0)
                        {
                            cell.ExecutionStatus = CellExecutionStatus.Succeeded;
                            cell.ExecutionOrder = Interlocked.Increment(ref s_executionCounter);
                        }
                        else
                        {
                            cell.ExecutionStatus = CellExecutionStatus.Failed;
                            if (string.IsNullOrEmpty(stderr))
                            {
                                cell.Outputs.Add(new CellOutput(
                                    CellOutputKind.Error,
                                    new List<FormattedOutput>
                                    {
                                        new FormattedOutput("text/plain",
                                            $"Node.js exited with code {process.ExitCode}.")
                                    }));
                            }
                        }

                        ExtensionLogger.LogInfo(nameof(NodeJsExecutor),
                            $"Node process exited with code {process.ExitCode}.");
                    }
                }
                finally
                {
                    try { File.Delete(tempFile); } catch { }
                }
            }
            catch (OperationCanceledException)
            {
                try
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    cell.ExecutionStatus = CellExecutionStatus.Idle;
                }
                catch { }
            }
            catch (Exception ex)
            {
                ExtensionLogger.LogException(nameof(NodeJsExecutor),
                    "Error executing JavaScript cell via Node.js.", ex);
                try
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    cell.ExecutionStatus = CellExecutionStatus.Failed;
                    cell.Outputs.Add(new CellOutput(
                        CellOutputKind.Error,
                        new List<FormattedOutput> { new FormattedOutput("text/plain", ex.Message) }));
                }
                catch { }
            }
        }

        private static Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
        {
            var tcs = new TaskCompletionSource<bool>();
            process.EnableRaisingEvents = true;
            process.Exited += (s, e) => tcs.TrySetResult(true);

            // If already exited before we subscribed.
            if (process.HasExited)
            {
                tcs.TrySetResult(true);
                return tcs.Task;
            }

            // Timeout fallback.
            var timer = new System.Threading.Timer(_ => tcs.TrySetResult(false),
                null, timeout, Timeout.InfiniteTimeSpan);

            return tcs.Task.ContinueWith(t => { timer.Dispose(); return t.Result; },
                TaskScheduler.Default);
        }
    }
}
