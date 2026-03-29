using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace PolyglotNotebooks.Variables
{
    /// <summary>
    /// Code-only WPF control for the Variable Explorer tool window.
    /// Shows a DataGrid (Name / Type / Value / Kernel)
    /// and a detail pane that displays the full value for the selected row.
    /// </summary>
    internal sealed class VariableExplorerControl : UserControl
    {
        private readonly DataGrid _grid;
        private readonly TextBlock _detailBlock;
        private readonly Border _detailBorder;
        private readonly TextBlock _emptyState;
        private readonly VariableService? _service;
        private string _filterText = string.Empty;

        public VariableExplorerControl(VariableService? service = null)
        {
            _service = service;

            var root = new DockPanel { LastChildFill = true };
            Themes.SetUseVsTheme(root, true);
            root.SetResourceReference(BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);

            // Detail pane (bottom)
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

            // DataGrid
            _grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserSortColumns = true,
                CanUserResizeRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.None,
                SelectionMode = DataGridSelectionMode.Single,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                BorderThickness = new Thickness(0),
                ColumnHeaderStyle = CreateColumnHeaderStyle(),
                RowStyle = CreateRowStyle(),
                CellStyle = CreateCellStyle()
            };
            _grid.SetResourceReference(BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
            _grid.SetResourceReference(ForegroundProperty, VsBrushes.ToolWindowTextKey);
            _grid.SetResourceReference(DataGrid.RowBackgroundProperty, VsBrushes.ToolWindowBackgroundKey);

            AddColumn("Name", "Name", new DataGridLength(1, DataGridLengthUnitType.Star));
            AddColumn("Type", "TypeName", new DataGridLength(1, DataGridLengthUnitType.Star));
            AddColumn("Value", "Value", new DataGridLength(2, DataGridLengthUnitType.Star));
            AddColumn("Kernel", "KernelName", new DataGridLength(80, DataGridLengthUnitType.Pixel));

            _grid.SelectionChanged += OnSelectionChanged;

            // Empty state overlay
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

            // Bind to service
            if (_service != null)
            {
                _grid.ItemsSource = _service.Variables;
                _service.Variables.CollectionChanged += OnVariablesChanged;
                UpdateEmptyState();
            }

            this.Unloaded += OnUnloaded;
        }

        // ── Filter support ─────────────────────────────────────────────

        /// <summary>Filters the DataGrid to show only variables matching the search text.</summary>
        internal void ApplyFilter(string searchText)
        {
            _filterText = searchText ?? string.Empty;
            RefreshFilter();
        }

        /// <summary>Clears the current filter and shows all variables.</summary>
        internal void ClearFilter()
        {
            _filterText = string.Empty;
            RefreshFilter();
        }

        private void RefreshFilter()
        {
            if (_service == null) return;

            if (string.IsNullOrWhiteSpace(_filterText))
            {
                _grid.ItemsSource = _service.Variables;
            }
            else
            {
                var filter = _filterText;
                var filtered = _service.Variables
                    .Where(v => MatchesFilter(v, filter))
                    .ToList();
                _grid.ItemsSource = filtered;
            }

            UpdateEmptyState();
        }

        private static bool MatchesFilter(VariableInfo variable, string filter)
        {
            return variable.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                || variable.TypeName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                || variable.Value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                || variable.KernelName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ── Helpers ────────────────────────────────────────────────────

        private void AddColumn(string header, string binding, DataGridLength width)
        {
            var col = new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(binding),
                Width = width,
                SortMemberPath = binding,
                ElementStyle = CreateCellTextStyle()
            };
            _grid.Columns.Add(col);
        }

        private void UpdateEmptyState()
        {
            var source = _grid.ItemsSource as System.Collections.ICollection;
            bool empty = source == null || source.Count == 0;
            _emptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── Style factories ────────────────────────────────────────────

        /// <summary>Creates a VS-themed column header style with a full ControlTemplate override.</summary>
        private static Style CreateColumnHeaderStyle()
        {
            var style = new Style(typeof(DataGridColumnHeader));
            style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            style.Setters.Add(new Setter(Control.SnapsToDevicePixelsProperty, true));

            var template = new ControlTemplate(typeof(DataGridColumnHeader));

            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "HeaderBorder";
            border.SetValue(Border.PaddingProperty, new Thickness(8, 6, 8, 6));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 1, 1));
            border.SetResourceReference(Border.BackgroundProperty, VsBrushes.CommandBarGradientBeginKey);
            border.SetResourceReference(Border.BorderBrushProperty, VsBrushes.CommandBarGradientEndKey);

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            border.AppendChild(content);

            template.VisualTree = border;

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
                new DynamicResourceExtension(EnvironmentColors.CommandBarMenuItemMouseOverBrushKey), "HeaderBorder"));
            template.Triggers.Add(hoverTrigger);

            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            style.Seal();
            return style;
        }

        /// <summary>Creates a VS-themed row style with selection and hover highlighting.</summary>
        private static Style CreateRowStyle()
        {
            var style = new Style(typeof(DataGridRow));
            style.Setters.Add(new Setter(Control.BackgroundProperty,
                new DynamicResourceExtension(VsBrushes.ToolWindowBackgroundKey)));
            style.Setters.Add(new Setter(Control.ForegroundProperty,
                new DynamicResourceExtension(VsBrushes.ToolWindowTextKey)));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.SnapsToDevicePixelsProperty, true));

            var selectedTrigger = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true };
            selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty,
                new DynamicResourceExtension(VsBrushes.HighlightKey)));
            selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty,
                new DynamicResourceExtension(VsBrushes.HighlightTextKey)));
            style.Triggers.Add(selectedTrigger);

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty,
                new DynamicResourceExtension(EnvironmentColors.CommandBarMenuItemMouseOverBrushKey)));
            style.Triggers.Add(hoverTrigger);

            style.Seal();
            return style;
        }

        /// <summary>Creates a VS-themed cell style with transparent background so row colors show through.</summary>
        private static Style CreateCellStyle()
        {
            var style = new Style(typeof(DataGridCell));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.ForegroundProperty,
                new DynamicResourceExtension(VsBrushes.ToolWindowTextKey)));

            var selectedTrigger = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
            selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty,
                new DynamicResourceExtension(VsBrushes.HighlightTextKey)));
            style.Triggers.Add(selectedTrigger);

            style.Seal();
            return style;
        }

        /// <summary>Creates a style for TextBlock elements inside DataGrid cells (padding + ellipsis).</summary>
        private static Style CreateCellTextStyle()
        {
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(6, 4, 6, 4)));
            style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            style.Seal();
            return style;
        }

        // ── Event handlers ─────────────────────────────────────────────

        private void OnVariablesChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // Re-apply filter when the underlying collection changes
            if (!string.IsNullOrWhiteSpace(_filterText))
                RefreshFilter();
            else
                UpdateEmptyState();
        }

        private void OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_grid.SelectedItem is VariableInfo info)
            {
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
