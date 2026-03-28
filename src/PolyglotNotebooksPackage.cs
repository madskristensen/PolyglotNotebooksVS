global using Community.VisualStudio.Toolkit;

global using Microsoft.VisualStudio.Shell;

global using System;
global using System.Threading.Tasks;

using Microsoft.VisualStudio;

using System.Runtime.InteropServices;
using System.Threading;

namespace PolyglotNotebooks
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration(Vsix.Name, Vsix.Description, Vsix.Version)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [Guid(PackageGuids.PolyglotNotebooksString)]

    [ProvideEditorFactory(typeof(Editor.NotebookEditorFactory), 101)]
    [ProvideEditorExtension(typeof(Editor.NotebookEditorFactory), ".dib", 50)]
    [ProvideEditorExtension(typeof(Editor.NotebookEditorFactory), ".ipynb", 50)]
    [ProvideEditorLogicalView(typeof(Editor.NotebookEditorFactory), VSConstants.LOGVIEWID.Designer_string)]
    [ProvideToolWindow(typeof(Variables.VariableExplorerToolWindow.Pane), DockedHeight = 500, DocumentLikeTool = false, Orientation = ToolWindowOrientation.Bottom, Style = VsDockStyle.Linked, Window = WindowGuids.SolutionExplorer)]
    [ProvideFileIcon(".dib", "KnownMonikers.ActionLog")]
    [ProvideFileIcon(".ipynb", "KnownMonikers.ActionLog")]
    public sealed class PolyglotNotebooksPackage : ToolkitPackage
    {
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await this.RegisterCommandsAsync();

            this.RegisterToolWindows();

            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            RegisterEditorFactory(new Editor.NotebookEditorFactory(this));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Kernel.KernelProcessManager.DisposeAll();
            }

            base.Dispose(disposing);
        }
    }
}
