using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using PolyglotNotebooks.Diagnostics;

namespace PolyglotNotebooks.Variables
{
    /// <summary>
    /// Handles the Refresh button on the Variable Explorer toolbar.
    /// </summary>
    [Command(PackageIds.RefreshVariables)]
    internal sealed class RefreshVariablesCommand : BaseCommand<RefreshVariablesCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            try
            {
                await VariableService.Current.RefreshVariablesAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ExtensionLogger.LogWarning(nameof(RefreshVariablesCommand),
                    $"Refresh failed: {ex.Message}");
            }
        }
    }
}
