using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;

using PolyglotNotebooks.Models;

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace PolyglotNotebooks.Editor
{
    /// <summary>
    /// WPF control that displays a navigable flat list of notebook cells,
    /// shown inside the VS Document Outline tool window.
    /// </summary>
    internal sealed class DocumentOutlineControl : UserControl
    {
        private static readonly Dictionary<string, string> _displayNameMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["csharp"] = "C#",
                ["fsharp"] = "F#",
                ["javascript"] = "JavaScript",
                ["typescript"] = "TypeScript",
                ["python"] = "Python",
                ["powershell"] = "PowerShell",
                ["sql"] = "SQL",
                ["kql"] = "KQL",
                ["html"] = "HTML",
                ["markdown"] = "Markdown",
                ["mermaid"] = "Mermaid",
                ["http"] = "HTTP"
            };

        private readonly NotebookDocument _document;
        private readonly NotebookControl _notebookControl;
        private readonly TreeView _treeView;
        private readonly TextBlock _emptyMessage;
        private bool _suppressSelectionSync;

        public DocumentOutlineControl(NotebookDocument document, NotebookControl notebookControl)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _notebookControl = notebookControl ?? throw new ArgumentNullException(nameof(notebookControl));

            SetResourceReference(BackgroundProperty, TreeViewColors.BackgroundBrushKey);

            _emptyMessage = new TextBlock
            {
                Text = "No cells in notebook.",
                Margin = new Thickness(8, 6, 8, 6),
                Visibility = Visibility.Collapsed
            };
            _emptyMessage.SetResourceReference(TextBlock.ForegroundProperty, TreeViewColors.BackgroundTextBrushKey);

            _treeView = new TreeView
            {
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                ItemContainerStyle = CreateTreeViewItemStyle()
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(_treeView, ScrollBarVisibility.Disabled);
            _treeView.MouseDoubleClick += OnTreeViewDoubleClick;
            _treeView.KeyDown += OnTreeViewKeyDown;
            _treeView.SelectedItemChanged += OnTreeViewSelectedItemChanged;

            var grid = new Grid();
            grid.Children.Add(_treeView);
            grid.Children.Add(_emptyMessage);

            Content = grid;

            // CrispImage theming: inform the image service of our background color
            Loaded += (s, e) => UpdateImageThemeColors();
            VSColorTheme.ThemeChanged += OnThemeChanged;

            _document.Cells.CollectionChanged += OnCellsCollectionChanged;
            _notebookControl.FocusedCellChanged += OnFocusedCellChanged;

            RebuildItems();
        }

        /// <summary>
        /// Unsubscribes from document and control events. Called when the outline is released.
        /// </summary>
        public void Cleanup()
        {
            VSColorTheme.ThemeChanged -= OnThemeChanged;
            _document.Cells.CollectionChanged -= OnCellsCollectionChanged;
            _notebookControl.FocusedCellChanged -= OnFocusedCellChanged;

            foreach (var item in GetAllItems())
                item.Cleanup();
        }

        // ── Image theming ────────────────────────────────────────────────────────

        private void UpdateImageThemeColors()
        {
            var drawingColor = VSColorTheme.GetThemedColor(TreeViewColors.BackgroundColorKey);
            ImageThemingUtilities.SetImageBackgroundColor(this,
                Color.FromArgb(drawingColor.A, drawingColor.R, drawingColor.G, drawingColor.B));
        }

        private void OnThemeChanged(ThemeChangedEventArgs e)
        {
#pragma warning disable VSSDK007 // Intentional fire-and-forget
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                UpdateImageThemeColors();
            }).FileAndForget(nameof(DocumentOutlineControl));
#pragma warning restore VSSDK007
        }

        // ── Item creation ────────────────────────────────────────────────────────

        private void RebuildItems()
        {
            foreach (var item in GetAllItems())
                item.Cleanup();

            _treeView.Items.Clear();

            CellOutlineItem? currentMarkdownParent = null;

            foreach (var cell in _document.Cells)
            {
                if (cell.Kind == CellKind.Markdown)
                {
                    var item = new CellOutlineItem(cell, CreateItemPanel(cell, false));
                    item.ItemContainerStyle = _treeView.ItemContainerStyle;
                    _treeView.Items.Add(item);
                    currentMarkdownParent = item;
                    item.IsExpanded = true;
                }
                else if (currentMarkdownParent != null)
                {
                    var item = new CellOutlineItem(cell, CreateItemPanel(cell, true));
                    currentMarkdownParent.Items.Add(item);
                }
                else
                {
                    var item = new CellOutlineItem(cell, CreateItemPanel(cell, false));
                    _treeView.Items.Add(item);
                }
            }

            UpdateEmptyState();
        }

        private IEnumerable<CellOutlineItem> GetAllItems()
        {
            foreach (var item in _treeView.Items.OfType<CellOutlineItem>())
            {
                yield return item;
                foreach (var child in item.Items.OfType<CellOutlineItem>())
                    yield return child;
            }
        }

        private StackPanel CreateItemPanel(NotebookCell cell, bool isNested)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(isNested ? 20 : 4, 2, 4, 2)
            };

            // Cell type icon (16x16)
            var typeIcon = new CrispImage
            {
                Moniker = cell.Kind == CellKind.Markdown ? KnownMonikers.Class : KnownMonikers.Field,
                Width = 16,
                Height = 16
            };
            panel.Children.Add(typeIcon);

            // 4px gap
            panel.Children.Add(new FrameworkElement { Width = 4 });

            // Display text
            var textBlock = new TextBlock
            {
                Text = GetDisplayText(cell),
                FontWeight = GetFontWeight(cell),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            panel.Children.Add(textBlock);

            // Status icon for code cells
            if (cell.Kind == CellKind.Code)
            {
                // 4px gap
                var gap = new FrameworkElement { Width = 4 };
                panel.Children.Add(gap);

                var statusIcon = new CrispImage
                {
                    Width = 12,
                    Height = 12,
                    VerticalAlignment = VerticalAlignment.Center
                };
                UpdateStatusIcon(statusIcon, cell.ExecutionStatus);
                panel.Children.Add(statusIcon);
            }

            return panel;
        }

        // ── Display text logic ───────────────────────────────────────────────────

        private static string GetDisplayText(NotebookCell cell)
        {
            if (cell.Kind == CellKind.Markdown)
                return GetMarkdownDisplayText(cell.Contents);
            else
                return GetCodeDisplayText(cell.KernelName, cell.Contents);
        }

        private static string GetMarkdownDisplayText(string contents)
        {
            if (string.IsNullOrWhiteSpace(contents))
                return "Empty Markdown Cell";

            var lines = contents.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            // Look for the first heading
            foreach (var line in lines)
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("#"))
                {
                    // Strip leading # characters and whitespace
                    int i = 0;
                    while (i < trimmed.Length && trimmed[i] == '#') i++;
                    return trimmed.Substring(i).Trim();
                }
            }

            // No heading found — use first non-empty line
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0)
                    return trimmed;
            }

            return "Empty Markdown Cell";
        }

        private static string GetCodeDisplayText(string kernelName, string contents)
        {
            string displayName = GetKernelDisplayName(kernelName);
            string prefix = $"[{displayName}]";

            if (string.IsNullOrWhiteSpace(contents))
                return $"{prefix} Empty Code Cell";

            var lines = contents.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0)
                    return $"{prefix} {trimmed}";
            }

            return $"{prefix} Empty Code Cell";
        }

        private static string GetKernelDisplayName(string kernelName)
        {
            if (kernelName != null && _displayNameMap.TryGetValue(kernelName, out var displayName))
                return displayName;
            return kernelName ?? "Code";
        }

        private static FontWeight GetFontWeight(NotebookCell cell)
        {
            if (cell.Kind != CellKind.Markdown || string.IsNullOrWhiteSpace(cell.Contents))
                return FontWeights.Normal;

            var lines = cell.Contents.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.TrimStart().StartsWith("#"))
                    return FontWeights.Bold;
            }

            return FontWeights.Normal;
        }

        // ── Status icon ──────────────────────────────────────────────────────────

        private static void UpdateStatusIcon(CrispImage icon, CellExecutionStatus status)
        {
            switch (status)
            {
                case CellExecutionStatus.Running:
                    icon.Moniker = KnownMonikers.StatusRunning;
                    icon.Visibility = Visibility.Visible;
                    break;
                case CellExecutionStatus.Succeeded:
                    icon.Moniker = KnownMonikers.StatusOK;
                    icon.Visibility = Visibility.Visible;
                    break;
                case CellExecutionStatus.Failed:
                    icon.Moniker = KnownMonikers.StatusError;
                    icon.Visibility = Visibility.Visible;
                    break;
                default:
                    icon.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        // ── Theming ──────────────────────────────────────────────────────────────

        private static Style CreateTreeViewItemStyle()
        {
            var style = new Style(typeof(TreeViewItem));
            style.Setters.Add(new Setter(TreeViewItem.IsExpandedProperty, true));
            style.Setters.Add(new Setter(TreeViewItem.PaddingProperty, new Thickness(4, 2, 4, 2)));
            style.Setters.Add(new Setter(TreeViewItem.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(TreeViewItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            style.Setters.Add(new Setter(TreeViewItem.TemplateProperty, CreateTreeViewItemTemplate()));
            return style;
        }

        private static ControlTemplate CreateTreeViewItemTemplate()
        {
            string xaml = @"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
                 xmlns:platform='clr-namespace:Microsoft.VisualStudio.PlatformUI;assembly=Microsoft.VisualStudio.Shell.15.0'
                 TargetType='TreeViewItem'>
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height='Auto'/>
            <RowDefinition Height='Auto'/>
        </Grid.RowDefinitions>
        <Border x:Name='Bd'
                Grid.Row='0'
                Background='Transparent'
                Padding='{TemplateBinding Padding}'
                SnapsToDevicePixels='True'>
            <ContentPresenter x:Name='PART_Header'
                              ContentSource='Header'
                              HorizontalAlignment='Stretch'
                              SnapsToDevicePixels='{TemplateBinding SnapsToDevicePixels}'/>
        </Border>
        <ItemsPresenter x:Name='ItemsHost' Grid.Row='1'/>
    </Grid>
    <ControlTemplate.Triggers>
        <Trigger Property='IsSelected' Value='True'>
            <Setter TargetName='Bd' Property='Background'
                    Value='{DynamicResource {x:Static platform:TreeViewColors.SelectedItemActiveBrushKey}}'/>
            <Setter Property='Foreground'
                    Value='{DynamicResource {x:Static platform:TreeViewColors.SelectedItemActiveTextBrushKey}}'/>
        </Trigger>
        <MultiTrigger>
            <MultiTrigger.Conditions>
                <Condition Property='IsSelected' Value='True'/>
                <Condition Property='IsSelectionActive' Value='False'/>
            </MultiTrigger.Conditions>
            <Setter TargetName='Bd' Property='Background'
                    Value='{DynamicResource {x:Static platform:TreeViewColors.SelectedItemInactiveBrushKey}}'/>
            <Setter Property='Foreground'
                    Value='{DynamicResource {x:Static platform:TreeViewColors.SelectedItemInactiveTextBrushKey}}'/>
        </MultiTrigger>
        <Trigger Property='HasItems' Value='False'>
            <Setter TargetName='ItemsHost' Property='Visibility' Value='Collapsed'/>
        </Trigger>
    </ControlTemplate.Triggers>
</ControlTemplate>";
            return (ControlTemplate)XamlReader.Parse(xaml);
        }

        // ── Navigation ───────────────────────────────────────────────────────────

        private void OnTreeViewDoubleClick(object sender, MouseButtonEventArgs e)
        {
            NavigateToSelectedCell();
        }

        private void OnTreeViewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                NavigateToSelectedCell();
                e.Handled = true;
            }
        }

        private void NavigateToSelectedCell()
        {
            if (_treeView.SelectedItem is CellOutlineItem item)
                _notebookControl.ScrollToCell(item.Cell);
        }

        // ── Caret sync ───────────────────────────────────────────────────────────

        private void OnTreeViewSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            // Intentionally empty — selection is driven by user clicks.
        }

        private void OnFocusedCellChanged(object sender, NotebookCell cell)
        {
            if (_suppressSelectionSync || cell == null)
                return;

            foreach (var item in GetAllItems())
            {
                if (item.Cell == cell)
                {
                    _suppressSelectionSync = true;
                    try
                    {
                        item.IsSelected = true;
                        item.BringIntoView();
                    }
                    finally
                    {
                        _suppressSelectionSync = false;
                    }
                    return;
                }
            }
        }

        // ── Live updates ─────────────────────────────────────────────────────────

        private void OnCellsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // Rebuild the entire list for simplicity — the outline is lightweight.
            RebuildItems();
        }

        private void UpdateEmptyState()
        {
            bool empty = _treeView.Items.Count == 0;
            _emptyMessage.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            _treeView.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        }

        // ── CellOutlineItem ──────────────────────────────────────────────────────

        /// <summary>
        /// A TreeViewItem that tracks a <see cref="NotebookCell"/> and updates
        /// its display text and status icon when the cell changes.
        /// </summary>
        private sealed class CellOutlineItem : TreeViewItem
        {
            private readonly PropertyChangedEventHandler _propertyChangedHandler;

            public NotebookCell Cell { get; }

            public CellOutlineItem(NotebookCell cell, StackPanel content)
            {
                Cell = cell;
                Header = content;
                SetResourceReference(ForegroundProperty, TreeViewColors.BackgroundTextBrushKey);

                _propertyChangedHandler = OnCellPropertyChanged;
                cell.PropertyChanged += _propertyChangedHandler;
            }

            public void Cleanup()
            {
                Cell.PropertyChanged -= _propertyChangedHandler;
            }

            private void OnCellPropertyChanged(object sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(NotebookCell.Contents) ||
                    e.PropertyName == nameof(NotebookCell.KernelName) ||
                    e.PropertyName == nameof(NotebookCell.ExecutionStatus))
                {
                    UpdateDisplay();
                }
            }

            private void UpdateDisplay()
            {
                if (Header is StackPanel panel)
                {
                    // Update display text (index 2 in the panel)
                    if (panel.Children.Count > 2 && panel.Children[2] is TextBlock tb)
                    {
                        tb.Text = GetDisplayText(Cell);
                        tb.FontWeight = GetFontWeight(Cell);
                    }

                    // Update status icon for code cells (last child in panel)
                    if (Cell.Kind == CellKind.Code &&
                        panel.Children.Count > 4 &&
                        panel.Children[panel.Children.Count - 1] is CrispImage statusIcon)
                    {
                        UpdateStatusIcon(statusIcon, Cell.ExecutionStatus);
                    }
                }
            }
        }
    }
}
