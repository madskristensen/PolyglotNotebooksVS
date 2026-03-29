using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using PolyglotNotebooks.Diagnostics;

namespace PolyglotNotebooks.Variables
{
    /// <summary>
    /// Toolkit-managed tool window that hosts the <see cref="VariableExplorerControl"/>.
    /// The pane exposes a VSCT-defined toolbar (with Refresh) and built-in search
    /// that filters the variable grid as the user types.
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
                ToolBar = new CommandID(PackageGuids.PolyglotNotebooks, PackageIds.VariableExplorerToolbar);
                ToolBarLocation = (int)VSTWT_LOCATION.VSTWT_TOP;
            }

            /// <summary>Enables the built-in search box in the tool window toolbar.</summary>
            public override bool SearchEnabled => true;

            /// <summary>Creates a search task that filters the variable grid.</summary>
            public override IVsSearchTask CreateSearch(uint dwCookie, IVsSearchQuery pSearchQuery, IVsSearchCallback pSearchCallback)
            {
                if (pSearchQuery == null || pSearchCallback == null)
                    return null!;

                return new VariableSearchTask(dwCookie, pSearchQuery, pSearchCallback, this);
            }

            /// <summary>Clears the search filter and restores the full variable list.</summary>
            public override void ClearSearch()
            {
                if (Content is VariableExplorerControl control)
                {
                    control.ClearFilter();
                }
            }

            /// <summary>Configures search for instant (as-you-type) filtering.</summary>
            public override void ProvideSearchSettings(IVsUIDataSource pSearchSettings)
            {
                Utilities.SetValue(pSearchSettings,
                    SearchSettingsDataSource.SearchStartTypeProperty.Name,
                    (uint)VSSEARCHSTARTTYPE.SST_INSTANT);

                Utilities.SetValue(pSearchSettings,
                    SearchSettingsDataSource.SearchStartMinCharsProperty.Name,
                    (uint)1);

                Utilities.SetValue(pSearchSettings,
                    SearchSettingsDataSource.SearchWatermarkProperty.Name,
                    "Filter variables...");

                Utilities.SetValue(pSearchSettings,
                    SearchSettingsDataSource.SearchStartDelayProperty.Name,
                    (uint)100);
            }

            /// <summary>Applies a search filter to the hosted control.</summary>
            internal void ApplyFilter(string searchText)
            {
                if (Content is VariableExplorerControl control)
                {
                    control.ApplyFilter(searchText);
                }
            }
        }
    }
}
