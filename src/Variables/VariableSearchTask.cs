using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace PolyglotNotebooks.Variables
{
    /// <summary>
    /// Search task that filters the Variable Explorer grid by matching
    /// the search string against variable name, type, value, or kernel.
    /// </summary>
    internal sealed class VariableSearchTask : VsSearchTask
    {
        private readonly VariableExplorerToolWindow.Pane _pane;

        public VariableSearchTask(
            uint dwCookie,
            IVsSearchQuery pSearchQuery,
            IVsSearchCallback pSearchCallback,
            VariableExplorerToolWindow.Pane pane)
            : base(dwCookie, pSearchQuery, pSearchCallback)
        {
            _pane = pane;
        }

        protected override void OnStartSearch()
        {
            ErrorCode = VSConstants.S_OK;

            string? searchText = SearchQuery?.SearchString?.Trim();

            if (string.IsNullOrEmpty(searchText))
            {
                SearchResults = 0;
            }
            else
            {
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    _pane.ApplyFilter(searchText!);
                });

                SearchResults = 1;
            }

            base.OnStartSearch();
        }

        protected override void OnStopSearch()
        {
            SearchResults = 0;
        }
    }
}
