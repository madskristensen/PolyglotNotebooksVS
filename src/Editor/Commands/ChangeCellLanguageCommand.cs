namespace PolyglotNotebooks.Editor.Commands
{
    [Command(PackageIds.ChangeCellLanguage)]
    internal sealed class ChangeCellLanguageCommand : BaseCommand<ChangeCellLanguageCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
        {
            Command.Enabled = NotebookControl.ActiveInstance?.HasFocusedCell == true;
        }

        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            NotebookControl.ActiveInstance?.FocusCellLanguagePicker();
        }
    }
}
