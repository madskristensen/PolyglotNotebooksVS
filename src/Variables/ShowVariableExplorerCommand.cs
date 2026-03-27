namespace PolyglotNotebooks.Variables
{
    /// <summary>
    /// Helper that shows the Variable Explorer tool window.
    /// Can be called from the notebook toolbar button or any other entry point.
    /// </summary>
    internal static class ShowVariableExplorerCommand
    {
        /// <summary>Shows (or focuses) the Polyglot Variables tool window.</summary>
        public static async Task ExecuteAsync()
        {
            await VariableExplorerToolWindow.ShowAsync();
        }
    }
}
