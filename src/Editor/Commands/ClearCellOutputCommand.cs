namespace PolyglotNotebooks.Editor.Commands
{
    [Command(PackageIds.ClearCellOutput)]
    internal sealed class ClearCellOutputCommand : BaseCommand<ClearCellOutputCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
        {
            Command.Enabled = NotebookControl.ActiveInstance?.HasFocusedCell == true;
        }

        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            NotebookControl.ActiveInstance?.ClearFocusedCellOutput();
        }
    }
}
