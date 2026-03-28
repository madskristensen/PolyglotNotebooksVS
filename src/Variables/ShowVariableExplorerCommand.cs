using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using PolyglotNotebooks.Diagnostics;

namespace PolyglotNotebooks.Variables
{
    [Command(PackageIds.ShowVariableExplorer)]
    internal sealed class ShowVariableExplorerCommand : BaseCommand<ShowVariableExplorerCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            try
            {
                ExtensionLogger.LogInfo(nameof(ShowVariableExplorerCommand), "Showing Variable Explorer tool window");
                await VariableExplorerToolWindow.ShowAsync();
            }
            catch (Exception ex)
            {
                ExtensionLogger.LogException(nameof(ShowVariableExplorerCommand),
                    "Failed to show Variable Explorer", ex);
                System.Diagnostics.Debug.WriteLine(
                    $"[PolyglotNotebooks] ShowVariableExplorer failed: {ex}");
            }
        }
    }
}
