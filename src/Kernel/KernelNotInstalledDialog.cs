using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using PolyglotNotebooks.Diagnostics;

namespace PolyglotNotebooks.Kernel
{
    /// <summary>
    /// Displays a non-blocking VS InfoBar when dotnet-interactive is not installed.
    /// The InfoBar provides Install and Open Docs actions; it does not block the UI thread.
    /// </summary>
    public static class KernelNotInstalledDialog
    {
        private const string InstallDocsUrl =
            "https://github.com/dotnet/interactive#installation";

        private const string InstallCommand =
            "dotnet tool install -g Microsoft.dotnet-interactive";

        /// <summary>
        /// Shows a non-blocking VS InfoBar offering Install / Open Docs / Dismiss actions
        /// and returns <c>false</c> immediately — the caller should still throw so the cell
        /// displays an error, while the InfoBar lets the user act at their own pace.
        /// </summary>
        /// <param name="detector">
        /// The detector whose cache should be invalidated after a successful install.
        /// May be <c>null</c> if cache invalidation is not needed.
        /// </param>
        /// <returns>Always <c>false</c>; the InfoBar handles install asynchronously.</returns>
        public static async Task<bool> ShowAsync(KernelInstallationDetector detector = null)
        {
            ExtensionLogger.LogWarning(nameof(KernelNotInstalledDialog),
                "dotnet-interactive is not installed; showing InfoBar.");

            // Fire and forget — create the InfoBar on the UI thread without blocking the caller.
            ThreadHelper.JoinableTaskFactory.RunAsync(
                async () => await ShowInfoBarAsync(detector).ConfigureAwait(false));

            return false;
        }

        private static async Task ShowInfoBarAsync(KernelInstallationDetector detector)
        {
            try
            {
                var model = new InfoBarModel(
                    "dotnet-interactive is required but not installed.",
                    new IVsInfoBarActionItem[]
                    {
                        new InfoBarHyperlink("Install", "install"),
                        new InfoBarHyperlink("Open Docs", "docs"),
                    },
                    KnownMonikers.StatusWarning,
                    isCloseButtonVisible: true);

                var infoBar = await VS.InfoBar.CreateAsync(model).ConfigureAwait(false);
                if (infoBar == null)
                    return;

                infoBar.ActionItemClicked += (s, e) =>
                    OnInfoBarActionClicked(e.ActionItem, detector, infoBar);

                await infoBar.TryShowInfoBarUIAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ExtensionLogger.LogException(nameof(KernelNotInstalledDialog),
                    "Failed to show InfoBar for missing dotnet-interactive", ex);
            }
        }

        private static void OnInfoBarActionClicked(
            IVsInfoBarActionItem actionItem,
            KernelInstallationDetector detector,
            InfoBar infoBar)
        {
            infoBar.Close();

            var context = actionItem?.ActionContext as string;
            if (context == "install")
            {
                ThreadHelper.JoinableTaskFactory.RunAsync(
                    async () => await RunInstallAsync(detector).ConfigureAwait(false));
            }
            else if (context == "docs")
            {
                OpenDocs();
            }
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
