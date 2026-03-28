using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.Shell;
using PolyglotNotebooks.Diagnostics;

namespace PolyglotNotebooks.Variables
{
    /// <summary>
    /// Toolkit-managed tool window that hosts the <see cref="VariableExplorerControl"/>.
    /// Shown via <c>VariableExplorerToolWindow.ShowAsync()</c> or the show command.
    /// Must be initialized in the package via <c>VariableExplorerToolWindow.Initialize(this)</c>.
    /// </summary>
    public class VariableExplorerToolWindow : BaseToolWindow<VariableExplorerToolWindow>
    {
        public override string GetTitle(int toolWindowId) => "Polyglot Variables";

        public override Type PaneType => typeof(Pane);

        public override Task<FrameworkElement> CreateAsync(int toolWindowId, CancellationToken ct)
        {
            try
            {
                ExtensionLogger.LogInfo(nameof(VariableExplorerToolWindow), "Creating Variable Explorer control");
                var control = new VariableExplorerControl(VariableService.Current);
                return Task.FromResult<FrameworkElement>(control);
            }
            catch (Exception ex)
            {
                ExtensionLogger.LogException(nameof(VariableExplorerToolWindow),
                    "Failed to create Variable Explorer control", ex);
                return Task.FromResult<FrameworkElement>(
                    new TextBlock { Text = $"Variable Explorer failed to load: {ex.Message}" });
            }
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
