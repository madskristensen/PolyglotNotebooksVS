namespace PolyglotNotebooks.Editor.Commands
{
    [Command(PackageIds.InsertCodeCellBelow)]
    internal sealed class InsertCodeCellBelowCommand : BaseCommand<InsertCodeCellBelowCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
        {
            Command.Enabled = NotebookControl.ActiveInstance?.HasFocusedCell == true;
        }

        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            NotebookControl.ActiveInstance?.InsertCodeCellBelow();
        }
    }
}
