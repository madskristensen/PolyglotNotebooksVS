namespace PolyglotNotebooks.Editor.Commands
{
    [Command(PackageIds.InsertCodeCellAbove)]
    internal sealed class InsertCodeCellAboveCommand : BaseCommand<InsertCodeCellAboveCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
        {
            Command.Enabled = NotebookControl.ActiveInstance?.HasFocusedCell == true;
        }

        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            NotebookControl.ActiveInstance?.InsertCodeCellAbove();
        }
    }
}
