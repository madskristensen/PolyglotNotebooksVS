using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;

namespace PolyglotNotebooks.Variables
{
    /// <summary>
    /// Code-only WPF control for the Variable Explorer tool window.
    /// Shows a DataGrid (Name / Type / Value / Kernel), a refresh button,
    /// and a detail pane that displays the full value for the selected row.
    /// </summary>
    internal sealed class VariableExplorerControl : UserControl
    {
        private readonly DataGrid _grid;
        private readonly TextBlock _detailBlock;
        private readonly Border _detailBorder;
        private readonly TextBlock _emptyState;
        private readonly VariableService? _service;

        public VariableExplorerControl(VariableService? service = null)
        {
            _service = service;

            // ── Root container ─────────────────────────────────────────────
            var root = new DockPanel { LastChildFill = true };
            Themes.SetUseVsTheme(root, true);
            root.SetResourceReference(BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);

            // ── Toolbar ────────────────────────────────────────────────────
            var toolbar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(4, 4, 4, 2)
            };
            toolbar.SetResourceReference(BackgroundProperty, VsBrushes.ButtonFaceKey);
            DockPanel.SetDock(toolbar, Dock.Top);

            var refreshIcon = new CrispImage
            {
                Moniker = KnownMonikers.Refresh,
                Width = 14,
                Height = 14,
                Margin = new Thickness(0, 0, 4, 0)
            };
            var refreshLabel = new TextBlock { Text = "Refresh", VerticalAlignment = VerticalAlignment.Center };
            var refreshPanel = new StackPanel { Orientation = Orientation.Horizontal };
            refreshPanel.Children.Add(refreshIcon);
            refreshPanel.Children.Add(refreshLabel);

            var refreshBtn = new Button
            {
                Content = refreshPanel,
                Padding = new Thickness(6, 3, 8, 3),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            refreshBtn.SetResourceReference(ForegroundProperty, VsBrushes.ToolWindowTextKey);
            refreshBtn.SetResourceReference(BackgroundProperty, VsBrushes.ButtonFaceKey);
            refreshBtn.Click += OnRefreshClicked;
            toolbar.Children.Add(refreshBtn);
            root.Children.Add(toolbar);

            // ── Detail pane (bottom) ───────────────────────────────────────
            _detailBorder = new Border
            {
                MinHeight = 36,
                MaxHeight = 120,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(6, 4, 6, 4),
                Visibility = Visibility.Collapsed
            };
            _detailBorder.SetResourceReference(BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
            _detailBorder.SetResourceReference(BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
            DockPanel.SetDock(_detailBorder, Dock.Bottom);

            _detailBlock = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12
            };
            _detailBlock.SetResourceReference(ForegroundProperty, VsBrushes.ToolWindowTextKey);
            _detailBorder.Child = _detailBlock;
            root.Children.Add(_detailBorder);

            // ── DataGrid ──────────────────────────────────────────────────
            _grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserSortColumns = true,
                CanUserResizeRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                SelectionMode = DataGridSelectionMode.Single,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                BorderThickness = new Thickness(0)
            };
            _grid.SetResourceReference(BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
            _grid.SetResourceReference(ForegroundProperty, VsBrushes.ToolWindowTextKey);
            _grid.SetResourceReference(DataGrid.RowBackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
            _grid.SetResourceReference(DataGrid.HorizontalGridLinesBrushProperty, VsBrushes.ToolWindowBorderKey);

            AddColumn("Name", "Name", new DataGridLength(1, DataGridLengthUnitType.Star));
            AddColumn("Type", "TypeName", new DataGridLength(1, DataGridLengthUnitType.Star));
            AddColumn("Value", "Value", new DataGridLength(2, DataGridLengthUnitType.Star));
            AddColumn("Kernel", "KernelName", new DataGridLength(80, DataGridLengthUnitType.Pixel));

            _grid.SelectionChanged += OnSelectionChanged;

            // ── Empty state overlay ───────────────────────────────────────
            _emptyState = new TextBlock
            {
                Text = "Run a cell to see variables",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 24, 0, 0),
                FontSize = 13
            };
            _emptyState.SetResourceReference(ForegroundProperty, VsBrushes.GrayTextKey);

            var gridHost = new Grid();
            gridHost.Children.Add(_grid);
            gridHost.Children.Add(_emptyState);
            root.Children.Add(gridHost);

            Content = root;

            // ── Bind to service ───────────────────────────────────────────
            if (_service != null)
            {
                _grid.ItemsSource = _service.Variables;
                _service.Variables.CollectionChanged += OnVariablesChanged;
                UpdateEmptyState();
            }

            this.Unloaded += OnUnloaded;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void AddColumn(string header, string binding, DataGridLength width)
        {
            _grid.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(binding),
                Width = width,
                SortMemberPath = binding
            });
        }

        private void UpdateEmptyState()
        {
            bool empty = _service == null || _service.Variables.Count == 0;
            _emptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── Event handlers ────────────────────────────────────────────────────

        private void OnVariablesChanged(object sender, NotifyCollectionChangedEventArgs e)
            => UpdateEmptyState();

        private void OnRefreshClicked(object sender, RoutedEventArgs e)
        {
            if (_service == null) return;
#pragma warning disable VSTHRD110, VSSDK007
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await _service.RefreshVariablesAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Diagnostics.ExtensionLogger.LogWarning(nameof(VariableExplorerControl),
                        $"Manual refresh failed: {ex.Message}");
                }
            });
#pragma warning restore VSTHRD110, VSSDK007
        }

        private void OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_grid.SelectedItem is VariableInfo info)
            {
                // Show truncated value immediately, then fetch full value.
                _detailBlock.Text = $"{info.Name} = {info.Value}";
                _detailBorder.Visibility = Visibility.Visible;

                if (_service != null)
                {
#pragma warning disable VSTHRD110, VSSDK007
                    _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                    {
                        try
                        {
                            var full = await _service
                                .GetFullValueAsync(info.Name, info.KernelName)
                                .ConfigureAwait(false);

                            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                            // Guard: selection may have changed while awaiting.
                            if (_grid.SelectedItem is VariableInfo current &&
                                current.Name == info.Name &&
                                current.KernelName == info.KernelName)
                            {
                                _detailBlock.Text = $"{info.Name} = {full}";
                            }
                        }
                        catch { /* truncated value already displayed */ }
                    });
#pragma warning restore VSTHRD110, VSSDK007
                }
            }
            else
            {
                _detailBorder.Visibility = Visibility.Collapsed;
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_service != null)
                _service.Variables.CollectionChanged -= OnVariablesChanged;
        }
    }
}
