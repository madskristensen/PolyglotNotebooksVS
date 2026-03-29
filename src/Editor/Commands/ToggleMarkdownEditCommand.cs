using PolyglotNotebooks.Models;

namespace PolyglotNotebooks.Editor.Commands
{
    [Command(PackageIds.ToggleMarkdownEdit)]
    internal sealed class ToggleMarkdownEditCommand : BaseCommand<ToggleMarkdownEditCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
        {
            var notebook = NotebookControl.ActiveInstance;
            Command.Enabled = notebook != null && notebook.FocusedCellKind == CellKind.Markdown;
        }

        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            NotebookControl.ActiveInstance?.ToggleFocusedMarkdownEdit();
        }
    }
}
