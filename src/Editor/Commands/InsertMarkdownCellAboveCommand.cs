namespace PolyglotNotebooks.Editor.Commands
{
    [Command(PackageIds.InsertMarkdownCellAbove)]
    internal sealed class InsertMarkdownCellAboveCommand : BaseCommand<InsertMarkdownCellAboveCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
        {
            Command.Enabled = NotebookControl.ActiveInstance?.HasFocusedCell == true;
        }

        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            NotebookControl.ActiveInstance?.InsertMarkdownCellAbove();
        }
    }
}
