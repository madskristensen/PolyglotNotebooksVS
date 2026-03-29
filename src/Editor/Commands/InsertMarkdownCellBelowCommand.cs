namespace PolyglotNotebooks.Editor.Commands
{
    [Command(PackageIds.InsertMarkdownCellBelow)]
    internal sealed class InsertMarkdownCellBelowCommand : BaseCommand<InsertMarkdownCellBelowCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
        {
            Command.Enabled = NotebookControl.ActiveInstance?.HasFocusedCell == true;
        }

        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            NotebookControl.ActiveInstance?.InsertMarkdownCellBelow();
        }
    }
}
