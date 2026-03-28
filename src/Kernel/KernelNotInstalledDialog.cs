using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using PolyglotNotebooks.Diagnostics;

namespace PolyglotNotebooks.Kernel
{
    /// <summary>
    /// Displays an actionable message when dotnet-interactive is not installed.
    /// Must be called from the VS UI context; switches to main thread internally.
    /// </summary>
    public static class KernelNotInstalledDialog
    {
        private const string InstallDocsUrl =
            "https://github.com/dotnet/interactive#installation";

        private const string InstallCommand =
            "dotnet tool install -g Microsoft.dotnet-interactive";

        /// <summary>
        /// Shows a message box asking the user whether to install dotnet-interactive
        /// automatically, open docs, or cancel.
        /// Yes = Install, No = Open Docs, Cancel = dismiss.
        /// </summary>
        /// <param name="detector">
        /// The detector whose cache should be invalidated after a successful install.
        /// May be <c>null</c> if cache invalidation is not needed.
        /// </param>
        /// <returns>
        /// <c>true</c> if dotnet-interactive was installed successfully; <c>false</c> if the user
        /// cancelled, chose to open docs, or the installation failed.
        /// </returns>
        public static async Task<bool> ShowAsync(KernelInstallationDetector detector = null)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            ExtensionLogger.LogWarning(nameof(KernelNotInstalledDialog),
                "dotnet-interactive is not installed; prompting user.");

            var message =
                "The dotnet-interactive tool is required but not installed. " +
                "Would you like to install it now?\n\n" +
                "• Yes — Install automatically\n" +
                "• No — Open installation docs in browser\n" +
                "• Cancel — Dismiss";

            var result = System.Windows.MessageBox.Show(
                message,
                "dotnet-interactive Not Installed",
                System.Windows.MessageBoxButton.YesNoCancel,
                System.Windows.MessageBoxImage.Warning);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                return await RunInstallAsync(detector).ConfigureAwait(false);
            }
            else if (result == System.Windows.MessageBoxResult.No)
            {
                OpenDocs();
            }

            return false;
        }

        private static async Task<bool> RunInstallAsync(KernelInstallationDetector detector)
        {
            await SetStatusBarTextAsync("Installing dotnet-interactive…").ConfigureAwait(false);

            ExtensionLogger.LogInfo(nameof(KernelNotInstalledDialog),
                $"Running: {InstallCommand}");

            try
            {
                var (exitCode, stdout, stderr) = await RunProcessAsync(
                    "dotnet", "tool install -g Microsoft.dotnet-interactive").ConfigureAwait(false);

                if (exitCode == 0)
                {
                    detector?.InvalidateCache();

                    await SetStatusBarTextAsync("dotnet-interactive installed successfully.").ConfigureAwait(false);

                    ExtensionLogger.LogInfo(nameof(KernelNotInstalledDialog),
                        "dotnet-interactive installed successfully.");

                    return true;
                }
                else
                {
                    var errorOutput = !string.IsNullOrWhiteSpace(stderr) ? stderr : stdout;

                    await SetStatusBarTextAsync("dotnet-interactive installation failed.").ConfigureAwait(false);

                    ExtensionLogger.LogError(nameof(KernelNotInstalledDialog),
                        $"Install failed (exit {exitCode}): {errorOutput}");

                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    System.Windows.MessageBox.Show(
                        $"Installation failed (exit code {exitCode}).\n\n{errorOutput}\n\n" +
                        "You can try running the command manually:\n" +
                        $"    {InstallCommand}",
                        "Installation Failed",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                await SetStatusBarTextAsync("dotnet-interactive installation failed.").ConfigureAwait(false);

                ExtensionLogger.LogException(nameof(KernelNotInstalledDialog),
                    "Exception during dotnet-interactive install", ex);

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                System.Windows.MessageBox.Show(
                    $"An error occurred while installing dotnet-interactive:\n\n{ex.Message}\n\n" +
                    "Ensure the .NET SDK is installed and 'dotnet' is on your PATH.",
                    "Installation Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }

            return false;
        }

        private static void OpenDocs()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = InstallDocsUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ExtensionLogger.LogException(nameof(KernelNotInstalledDialog),
                    "Failed to open documentation URL", ex);
            }
        }

        private static async Task<(int exitCode, string stdout, string stderr)> RunProcessAsync(
            string fileName, string arguments)
        {
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var stdoutBuilder = new StringBuilder();
            var stderrBuilder = new StringBuilder();
            var tcs = new TaskCompletionSource<int>();

            process.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null) stdoutBuilder.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null) stderrBuilder.AppendLine(e.Data);
            };
            process.Exited += (s, e) => tcs.TrySetResult(process.ExitCode);

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var exitCode = await tcs.Task.ConfigureAwait(false);

            return (exitCode, stdoutBuilder.ToString(), stderrBuilder.ToString());
        }

        private static async Task SetStatusBarTextAsync(string text)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            try
            {
                var statusBar = (IVsStatusbar)ServiceProvider.GlobalProvider.GetService(typeof(SVsStatusbar));
                if (statusBar != null)
                {
                    statusBar.SetText(text);
                }
            }
            catch
            {
                // Status bar may be unavailable in some contexts; non-critical.
            }
        }
    }
}
