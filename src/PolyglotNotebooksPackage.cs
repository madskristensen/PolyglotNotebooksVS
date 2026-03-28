global using System;
global using System.Threading.Tasks;
global using Community.VisualStudio.Toolkit;
global using Microsoft.VisualStudio.Shell;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio;

namespace PolyglotNotebooks
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration(Vsix.Name, Vsix.Description, Vsix.Version)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideEditorFactory(typeof(Editor.NotebookEditorFactory), 101)]
    [ProvideEditorExtension(typeof(Editor.NotebookEditorFactory), ".dib", 50)]
    [ProvideEditorExtension(typeof(Editor.NotebookEditorFactory), ".ipynb", 50)]
    [ProvideEditorLogicalView(typeof(Editor.NotebookEditorFactory), VSConstants.LOGVIEWID.Designer_string)]
    [ProvideToolWindow(typeof(Variables.VariableExplorerToolWindow.Pane))]
    [Guid(PackageGuids.PolyglotNotebooksString)]
    public sealed class PolyglotNotebooksPackage : ToolkitPackage
    {
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await this.RegisterCommandsAsync();

            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            RegisterEditorFactory(new Editor.NotebookEditorFactory(this));
        }
    }

    internal static class PackageGuids
    {
        public const string PolyglotNotebooksString = "7a8b905d-687d-4b8c-8842-85067272e3ab";
        public static readonly Guid PolyglotNotebooks = new(PolyglotNotebooksString);
    }
}
