using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;

using PolyglotNotebooks.Kernel;
using PolyglotNotebooks.Models;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace PolyglotNotebooks.Editor
{
    /// <summary>
    /// Notebook-level toolbar shown at the top of the editor.
    /// Provides Run All, Interrupt, Restart Kernel, and Clear All Outputs actions,
    /// plus a kernel status indicator showing the current dotnet-interactive state.
    /// </summary>
    internal sealed class NotebookToolbar : Border
    {
        private readonly TextBlock _statusDot;
        private readonly TextBlock _kernelLabel;
        private Button _runAllBtn;
        private Button _restartRunAllBtn;

        /// <summary>Raised when the user clicks Run All or presses Ctrl+Shift+Enter.</summary>
        public event EventHandler? RunAllRequested;

        /// <summary>Raised when the user clicks Restart + Run All.</summary>
        public event EventHandler? RestartAndRunAllRequested;

        /// <summary>Raised when the user clicks Interrupt or presses Ctrl+.</summary>
        public event EventHandler? InterruptRequested;

        /// <summary>Raised when the user clicks Restart Kernel.</summary>
        public event EventHandler? RestartKernelRequested;

        /// <summary>Raised when the user clicks Clear All Outputs.</summary>
        public event EventHandler? ClearAllOutputsRequested;

        /// <summary>Raised when the user selects an export format from the Export dropdown.</summary>
        public event EventHandler<ExportFormat>? ExportRequested;

        public NotebookToolbar()
        {
            BorderThickness = new Thickness(0, 0, 0, 1);
            Padding = new Thickness(8, 4, 8, 4);
            SetResourceReference(Border.BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
            SetResourceReference(Border.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);

            var layout = new DockPanel
            {
                LastChildFill = false,
                VerticalAlignment = VerticalAlignment.Center
            };

            // ── Status section (right) ─────────────────────────────────────────
            _statusDot = new TextBlock
            {
                Text = "●",
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0),
                ToolTip = "Kernel status"
            };
            _statusDot.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.GrayTextKey);

            _kernelLabel = new TextBlock
            {
                Text = "dotnet-interactive",
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            _kernelLabel.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.GrayTextKey);

            var statusPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 0)
            };
            statusPanel.Children.Add(_kernelLabel);
            statusPanel.Children.Add(_statusDot);
            DockPanel.SetDock(statusPanel, Dock.Right);
            layout.Children.Add(statusPanel);

            // Thin vertical separator between action buttons and status area
            var sep = new System.Windows.Shapes.Rectangle
            {
                Width = 1,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(8, 2, 8, 2),
                Opacity = 0.4
            };
            sep.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,
                VsBrushes.ToolWindowBorderKey);
            DockPanel.SetDock(sep, Dock.Right);
            layout.Children.Add(sep);

            // ── Action buttons (left) ──────────────────────────────────────────
            _runAllBtn = MakeButton(KnownMonikers.RunAll, "Run All (Ctrl+Shift+Enter)");
            _runAllBtn.Click += (s, e) => RunAllRequested?.Invoke(this, EventArgs.Empty);
            DockPanel.SetDock(_runAllBtn, Dock.Left);
            layout.Children.Add(_runAllBtn);

            _restartRunAllBtn = MakeButton(KnownMonikers.Rerun, "Restart Kernel and Run All");
            _restartRunAllBtn.Click += (s, e) => RestartAndRunAllRequested?.Invoke(this, EventArgs.Empty);
            DockPanel.SetDock(_restartRunAllBtn, Dock.Left);
            layout.Children.Add(_restartRunAllBtn);

            var interruptBtn = MakeButton(KnownMonikers.Stop, "Interrupt (Ctrl+.)");
            interruptBtn.Click += (s, e) => InterruptRequested?.Invoke(this, EventArgs.Empty);
            DockPanel.SetDock(interruptBtn, Dock.Left);
            layout.Children.Add(interruptBtn);

            var restartBtn = MakeButton(KnownMonikers.Restart, "Restart Kernel");
            restartBtn.Click += (s, e) => RestartKernelRequested?.Invoke(this, EventArgs.Empty);
            DockPanel.SetDock(restartBtn, Dock.Left);
            layout.Children.Add(restartBtn);

            var clearBtn = MakeButton(KnownMonikers.ClearWindowContent, "Clear All Outputs");
            clearBtn.Click += (s, e) => ClearAllOutputsRequested?.Invoke(this, EventArgs.Empty);
            DockPanel.SetDock(clearBtn, Dock.Left);
            layout.Children.Add(clearBtn);

            // ── Export dropdown ──────────────────────────────────────────
            var exportBtn = MakeButton(KnownMonikers.Export, "Export Notebook");
            exportBtn.Click += OnExportButtonClick;
            DockPanel.SetDock(exportBtn, Dock.Left);
            layout.Children.Add(exportBtn);

            Child = layout;

            // Show initial grey / not-started state
            UpdateKernelStatus(KernelStatus.NotStarted);
        }

        /// <summary>
        /// Disables or re-enables the Run All and Restart+Run All buttons during execution.
        /// </summary>
        public void SetExecuting(bool executing)
        {
            _runAllBtn.IsEnabled = !executing;
            _restartRunAllBtn.IsEnabled = !executing;
        }

        /// <summary>
        /// Updates the kernel status indicator. Must be called on the UI thread.
        /// </summary>
        public void UpdateKernelStatus(KernelStatus status)
        {
            string labelText;
            object dotBrushKey;

            switch (status)
            {
                case KernelStatus.Starting:
                    labelText = "Starting…";
                    dotBrushKey = VsBrushes.VizSurfaceGoldMediumKey;
                    break;
                case KernelStatus.Restarting:
                    labelText = "Restarting…";
                    dotBrushKey = VsBrushes.VizSurfaceGoldMediumKey;
                    break;
                case KernelStatus.Ready:
                    labelText = "dotnet-interactive";
                    dotBrushKey = VsBrushes.VizSurfaceGreenMediumKey;
                    break;
                case KernelStatus.Busy:
                    labelText = "Running";
                    dotBrushKey = VsBrushes.VizSurfaceGreenMediumKey;
                    break;
                case KernelStatus.Error:
                    labelText = "Error";
                    dotBrushKey = VsBrushes.VizSurfaceRedMediumKey;
                    break;
                case KernelStatus.Stopped:
                    labelText = "Stopped";
                    dotBrushKey = VsBrushes.GrayTextKey;
                    break;
                default: // NotStarted
                    labelText = "dotnet-interactive";
                    dotBrushKey = VsBrushes.GrayTextKey;
                    break;
            }

            _kernelLabel.Text = labelText;
            _statusDot.SetResourceReference(TextBlock.ForegroundProperty, dotBrushKey);
        }

        private void OnExportButtonClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn)
                return;

            var menu = new ContextMenu();
            menu.Items.Add(MakeExportMenuItem("HTML (.html)", ExportFormat.Html));
            menu.Items.Add(MakeExportMenuItem("PDF (.pdf)", ExportFormat.Pdf));
            menu.Items.Add(MakeExportMenuItem("Markdown (.md)", ExportFormat.Markdown));
            menu.Items.Add(MakeExportMenuItem("C# Script (.csx)", ExportFormat.CSharpScript));
            menu.Items.Add(MakeExportMenuItem("F# Script (.fsx)", ExportFormat.FSharpScript));

            ThemedContextMenuHelper.ApplyVsTheme(menu);
            menu.PlacementTarget = btn;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private MenuItem MakeExportMenuItem(string header, ExportFormat format)
        {
            var item = new MenuItem { Header = header };
            item.Click += (s, e) => ExportRequested?.Invoke(this, format);
            return item;
        }

        private static Button MakeButton(ImageMoniker moniker, string tooltip)
        {
            var btn = new Button
            {
                Content = new CrispImage { Moniker = moniker, Width = 16, Height = 16 },
                ToolTip = tooltip,
                Padding = new Thickness(4, 3, 4, 3),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            btn.SetResourceReference(Button.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            btn.SetResourceReference(Button.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
            return btn;
        }
    }
}
