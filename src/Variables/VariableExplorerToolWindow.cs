using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using Microsoft.VisualStudio.Shell;

namespace PolyglotNotebooks.Variables
{
    /// <summary>
    /// Toolkit-managed tool window that hosts the <see cref="VariableExplorerControl"/>.
    /// Shown via <c>VariableExplorerToolWindow.ShowAsync()</c> or the show command.
    /// </summary>
    public class VariableExplorerToolWindow : BaseToolWindow<VariableExplorerToolWindow>
    {
        public override string GetTitle(int toolWindowId) => "Polyglot Variables";

        public override Type PaneType => typeof(Pane);

        public override Task<FrameworkElement> CreateAsync(int toolWindowId, CancellationToken ct)
        {
            return Task.FromResult<FrameworkElement>(
                new VariableExplorerControl(VariableService.Current));
        }

        /// <summary>The VS tool window pane; identified by its GUID in the registry.</summary>
        [Guid("a5b8e219-c7f4-4d8e-b3c1-9e0f8a7d6b5e")]
        internal class Pane : ToolWindowPane
        {
            public Pane() : base(null)
            {
                Caption = "Polyglot Variables";
            }
        }
    }
}
