using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;

using PolyglotNotebooks.IntelliSense;
using PolyglotNotebooks.Models;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media; // FontFamily

namespace PolyglotNotebooks.Editor
{
    /// <summary>
    /// Cell-level toolbar: kernel selector dropdown, run buttons, execution counter,
    /// status indicator, clear-output button, and a "…" cell-options menu.
    /// </summary>
    internal sealed class CellToolbar : DockPanel
    {
        private readonly NotebookDocument _document;
        private readonly NotebookCell _cell;
        private readonly ComboBox _kernelCombo;
        private readonly TextBlock _executionCounter;
        private readonly TextBlock _statusIndicator;
        private readonly CrispImage _statusIcon;
        private Border? _splitRunButton;

        // Guard against re-entrant kernel combo ↔ cell property update loops.
        private bool _syncingKernelCombo;

        /// <summary>Raised when the user clicks ▶ or "Run Cell".</summary>
        public event EventHandler? RunRequested;

        /// <summary>Raised when the user chooses "Run Cells Above".</summary>
        public event EventHandler? RunAboveRequested;

        /// <summary>Raised when the user chooses "Run Cell and Below".</summary>
        public event EventHandler? RunBelowRequested;

        /// <summary>Raised when the user chooses "Run Selection".</summary>
        public event EventHandler? RunSelectionRequested;

        public CellToolbar(NotebookDocument document, NotebookCell cell)
        {
            _document = document;
            _cell = cell;

            LastChildFill = false;
            Margin = new Thickness(0, 0, 0, 4);

            bool isMarkdown = cell.Kind == CellKind.Markdown;

            // ── Left side: kernel selector or markdown label ──────────────────
            if (isMarkdown)
            {
                var mdLabel = new TextBlock
                {
                    Text = "Markdown",
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 1, 8, 1),
                    Padding = new Thickness(4, 1, 4, 1),
                };
                mdLabel.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
                var mdBorder = new Border
                {
                    Child = mdLabel,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                };
                mdBorder.SetResourceReference(Border.BackgroundProperty, VsBrushes.ButtonFaceKey);
                mdBorder.SetResourceReference(Border.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
                DockPanel.SetDock(mdBorder, Dock.Left);
                Children.Add(mdBorder);

                _kernelCombo = new ComboBox { Visibility = Visibility.Collapsed };
            }
            else
            {
                _kernelCombo = new ComboBox
                {
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Padding = new Thickness(4, 1, 4, 1),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                    BorderThickness = new Thickness(1),
                    IsEditable = false,
                    ToolTip = "Select kernel language"
                };
                _kernelCombo.SetResourceReference(ComboBox.BackgroundProperty, VsBrushes.ButtonFaceKey);
                _kernelCombo.SetResourceReference(ComboBox.ForegroundProperty, VsBrushes.ToolWindowTextKey);
                _kernelCombo.SetResourceReference(ComboBox.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);

                PopulateKernelComboItems();
                SyncKernelComboSelection();

                _kernelCombo.SelectionChanged += OnKernelComboSelectionChanged;

                DockPanel.SetDock(_kernelCombo, Dock.Left);
                Children.Add(_kernelCombo);

                // Subscribe to cache updates (fires on background thread → marshal to UI).
                KernelInfoCache.Default.KernelsChanged += OnKernelsCacheChanged;
                Unloaded += (s, e) => KernelInfoCache.Default.KernelsChanged -= OnKernelsCacheChanged;
            }

            // ── Right-side controls (added right-to-left via Dock.Right) ──────

            // Status indicator elements — code cells only
            _statusIndicator = new TextBlock
            {
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0),
                Visibility = Visibility.Collapsed
            };
            _statusIcon = new CrispImage
            {
                Moniker = KnownMonikers.TestCoveredPassing,
                Width = 20,
                Height = 20,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0),
                Visibility = Visibility.Collapsed
            };

            // Cell menu (···)
            var menuBtn = isMarkdown ? BuildMarkdownMenuButton() : BuildMenuButton();
            DockPanel.SetDock(menuBtn, Dock.Right);
            Children.Add(menuBtn);

            // Execution counter [N] — created for all cells, only added for code
            _executionCounter = new TextBlock
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            _executionCounter.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.GrayTextKey);

            if (!isMarkdown)
            {
                // Clear Output button — Dock.Right (2nd from right, after menu)
                var clearBtn = new Button
                {
                    Content = MakeCrispImage(KnownMonikers.ClearWindowContent),
                    Padding = new Thickness(4, 3, 4, 3),
                    BorderThickness = new Thickness(1),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 4, 0),
                    ToolTip = "Clear cell output (Ctrl+Shift+Backspace)"
                };
                clearBtn.SetResourceReference(Button.ForegroundProperty, VsBrushes.ToolWindowTextKey);
                clearBtn.SetResourceReference(Button.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
                clearBtn.Click += (s, e) => _cell.Outputs.Clear();
                DockPanel.SetDock(clearBtn, Dock.Right);
                Children.Add(clearBtn);

                // Split button: ▶ Run | ▼ dropdown — Dock.Right (3rd from right)
                var splitButton = BuildSplitRunButton();
                DockPanel.SetDock(splitButton, Dock.Right);
                Children.Add(splitButton);

                // Execution counter [N] — Dock.Right (4th from right)
                UpdateExecutionCounter();
                DockPanel.SetDock(_executionCounter, Dock.Right);
                Children.Add(_executionCounter);

                // Status container — Dock.Right (leftmost of right-docked items)
                var statusContainer = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    MinWidth = 80,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 0, 0),
                };
                statusContainer.Children.Add(_statusIcon);
                statusContainer.Children.Add(_statusIndicator);
                DockPanel.SetDock(statusContainer, Dock.Right);
                Children.Add(statusContainer);

                UpdateStatusIndicator();
            }

            _cell.PropertyChanged += OnCellPropertyChanged;
            Unloaded += (s, e) => _cell.PropertyChanged -= OnCellPropertyChanged;
        }

        /// <summary>
        /// Builds a VS-native split button: a single unified control with a ▶ Run
        /// area and a tiny ▾ chevron that opens a dropdown — no visible separator.
        /// </summary>
        private Border BuildSplitRunButton()
        {
            // Two-column grid: run icon area + narrow chevron area
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });

            // Run icon area
            var runArea = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(4, 3, 2, 3),
                Child = MakeCrispImage(KnownMonikers.Run),
                Cursor = Cursors.Hand,
                ToolTip = "Run cell (Shift+Enter)"
            };
            Grid.SetColumn(runArea, 0);

            // Chevron area
            var chevron = new TextBlock
            {
                Text = "▾",
                FontSize = 9,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            chevron.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);

            var chevronArea = new Border
            {
                Background = Brushes.Transparent,
                Child = chevron,
                Cursor = Cursors.Hand,
                ToolTip = "Run options"
            };
            Grid.SetColumn(chevronArea, 1);

            grid.Children.Add(runArea);
            grid.Children.Add(chevronArea);

            // Context menu for the chevron dropdown
            var menu = new ContextMenu();
            ThemedContextMenuHelper.ApplyVsTheme(menu);
            menu.Items.Add(MakeMenuItem("Run Cell", () => RunRequested?.Invoke(this, EventArgs.Empty), KnownMonikers.Run));
            menu.Items.Add(MakeMenuItem("Run Cells Above", () => RunAboveRequested?.Invoke(this, EventArgs.Empty), KnownMonikers.MoveUp));
            menu.Items.Add(MakeMenuItem("Run Cell and Below", () => RunBelowRequested?.Invoke(this, EventArgs.Empty), KnownMonikers.MoveDown));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Run Selection", () => RunSelectionRequested?.Invoke(this, EventArgs.Empty), KnownMonikers.RunOutline));

            // Wire mouse handlers
            runArea.MouseLeftButtonDown += (s, e) =>
            {
                RunRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            };

            chevronArea.MouseLeftButtonDown += (s, e) =>
            {
                menu.PlacementTarget = _splitRunButton;
                menu.Placement = PlacementMode.Bottom;
                menu.IsOpen = true;
                e.Handled = true;
            };

            // Hover feedback: highlight the whole button on mouse enter/leave
            void SetHoverBackground(bool hovering)
            {
                if (hovering)
                    grid.SetResourceReference(Grid.BackgroundProperty, VsBrushes.CommandBarMouseOverBackgroundGradientKey);
                else
                    grid.Background = Brushes.Transparent;
            }

            grid.MouseEnter += (s, e) => SetHoverBackground(true);
            grid.MouseLeave += (s, e) => SetHoverBackground(false);

            // Outer border — single unified control with rounded corners
            var splitBorder = new Border
            {
                Child = grid,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
                UseLayoutRounding = true,
            };
            splitBorder.SetResourceReference(Border.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
            splitBorder.SetResourceReference(Border.BackgroundProperty, VsBrushes.ButtonFaceKey);

            _splitRunButton = splitBorder;
            return splitBorder;
        }

        private Button BuildMenuButton()
        {
            var btn = new Button
            {
                Content = MakeCrispImage(KnownMonikers.Ellipsis),
                Padding = new Thickness(4, 3, 4, 3),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
                ToolTip = "Cell options"
            };
            btn.SetResourceReference(Button.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            btn.SetResourceReference(Button.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);

            var menu = new ContextMenu();
            ThemedContextMenuHelper.ApplyVsTheme(menu);
            menu.Items.Add(MakeMenuItem("Run Cell", () => RunRequested?.Invoke(this, EventArgs.Empty), KnownMonikers.Run));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Insert Code Cell Above", () => InsertCellAt(0, CellKind.Code), KnownMonikers.InsertClause));
            menu.Items.Add(MakeMenuItem("Insert Code Cell Below", () => InsertCellAt(1, CellKind.Code), KnownMonikers.InsertClause));
            menu.Items.Add(MakeMenuItem("Insert Markdown Cell Above", () => InsertCellAt(0, CellKind.Markdown), KnownMonikers.InsertClause));
            menu.Items.Add(MakeMenuItem("Insert Markdown Cell Below", () => InsertCellAt(1, CellKind.Markdown), KnownMonikers.InsertClause));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Move Up", MoveUp, KnownMonikers.MoveUp));
            menu.Items.Add(MakeMenuItem("Move Down", MoveDown, KnownMonikers.MoveDown));
            menu.Items.Add(new Separator());

            // Change Language submenu
            var langMenu = new MenuItem
            {
                Header = "Change Language",
                Icon = MakeCrispImage(KnownMonikers.LocalVariable)
            };
            foreach (var pair in LanguagePairs)
                langMenu.Items.Add(MakeMenuItem(pair.Display, () => _cell.KernelName = pair.Kernel));
            menu.Items.Add(langMenu);

            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Clear Output", () => _cell.Outputs.Clear(), KnownMonikers.ClearWindowContent));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Delete Cell", DeleteCell, KnownMonikers.DeleteListItem));

            btn.Click += (s, e) =>
            {
                menu.PlacementTarget = btn;
                menu.Placement = PlacementMode.Bottom;
                menu.IsOpen = true;
            };

            return btn;
        }

        private Button BuildMarkdownMenuButton()
        {
            var btn = new Button
            {
                Content = MakeCrispImage(KnownMonikers.Ellipsis),
                Padding = new Thickness(4, 3, 4, 3),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
                ToolTip = "Cell options"
            };
            btn.SetResourceReference(Button.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            btn.SetResourceReference(Button.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);

            var menu = new ContextMenu();
            ThemedContextMenuHelper.ApplyVsTheme(menu);
            menu.Items.Add(MakeMenuItem("Insert Code Cell Above", () => InsertCellAt(0, CellKind.Code), KnownMonikers.InsertClause));
            menu.Items.Add(MakeMenuItem("Insert Code Cell Below", () => InsertCellAt(1, CellKind.Code), KnownMonikers.InsertClause));
            menu.Items.Add(MakeMenuItem("Insert Markdown Cell Above", () => InsertCellAt(0, CellKind.Markdown), KnownMonikers.InsertClause));
            menu.Items.Add(MakeMenuItem("Insert Markdown Cell Below", () => InsertCellAt(1, CellKind.Markdown), KnownMonikers.InsertClause));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Move Up", MoveUp, KnownMonikers.MoveUp));
            menu.Items.Add(MakeMenuItem("Move Down", MoveDown, KnownMonikers.MoveDown));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Delete Cell", DeleteCell, KnownMonikers.DeleteListItem));

            btn.Click += (s, e) =>
            {
                menu.PlacementTarget = btn;
                menu.Placement = PlacementMode.Bottom;
                menu.IsOpen = true;
            };

            return btn;
        }

        // ── Kernel ComboBox helpers ────────────────────────────────────────────

        private sealed class KernelComboItem
        {
            public KernelComboItem(string kernelName, string displayName)
            {
                KernelName = kernelName;
                DisplayName = displayName;
            }
            public string KernelName { get; }
            public string DisplayName { get; }
            public override string ToString() => DisplayName;
        }

        private void PopulateKernelComboItems()
        {
            _kernelCombo.Items.Clear();
            foreach (var kernelName in KernelInfoCache.Default.GetAvailableKernels())
                _kernelCombo.Items.Add(new KernelComboItem(kernelName, GetLanguageDisplayName(kernelName)));
        }

        private void SyncKernelComboSelection()
        {
            _syncingKernelCombo = true;
            try
            {
                var currentKernel = _cell.KernelName ?? "csharp";
                foreach (KernelComboItem item in _kernelCombo.Items)
                {
                    if (string.Equals(item.KernelName, currentKernel, StringComparison.OrdinalIgnoreCase))
                    {
                        _kernelCombo.SelectedItem = item;
                        return;
                    }
                }
                // Current kernel not in list — add an ad-hoc entry
                var custom = new KernelComboItem(currentKernel, GetLanguageDisplayName(currentKernel));
                _kernelCombo.Items.Add(custom);
                _kernelCombo.SelectedItem = custom;
            }
            finally
            {
                _syncingKernelCombo = false;
            }
        }

        private void OnKernelComboSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_syncingKernelCombo) return;
            if (_kernelCombo.SelectedItem is KernelComboItem item)
                _cell.KernelName = item.KernelName;
        }

        private void OnKernelsCacheChanged()
        {
            // KernelsChanged fires on a background thread; use JoinableTaskFactory to marshal to UI.
#pragma warning disable VSTHRD110, VSSDK007
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                PopulateKernelComboItems();
                SyncKernelComboSelection();
            });
#pragma warning restore VSTHRD110, VSSDK007
        }

        // ── Cell property change listener ─────────────────────────────────────

        private void OnCellPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(NotebookCell.KernelName):
                    SyncKernelComboSelection();
                    break;
                case nameof(NotebookCell.ExecutionOrder):
                    UpdateExecutionCounter();
                    break;
                case nameof(NotebookCell.ExecutionStatus):
                    UpdateStatusIndicator();
                    break;
            }
        }

        private void UpdateExecutionCounter()
        {
            _executionCounter.Text = _cell.ExecutionOrder.HasValue
                ? $"[{_cell.ExecutionOrder}]"
                : "[ ]";
        }

        private void UpdateStatusIndicator()
        {
            switch (_cell.ExecutionStatus)
            {
                case CellExecutionStatus.Running:
                    _statusIcon.Visibility = Visibility.Collapsed;
                    _statusIndicator.Text = "⟳ Running";
                    _statusIndicator.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
                    _statusIndicator.Visibility = Visibility.Visible;
                    if (_splitRunButton != null) _splitRunButton.IsEnabled = false;
                    break;
                case CellExecutionStatus.Succeeded:
                    _statusIndicator.Visibility = Visibility.Collapsed;
                    _statusIcon.Moniker = KnownMonikers.TestCoveredPassing;
                    _statusIcon.Visibility = Visibility.Visible;
                    if (_splitRunButton != null) _splitRunButton.IsEnabled = true;
                    break;
                case CellExecutionStatus.Failed:
                    _statusIcon.Visibility = Visibility.Collapsed;
                    _statusIndicator.Text = "✗ Error";
                    _statusIndicator.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
                    _statusIndicator.Visibility = Visibility.Visible;
                    if (_splitRunButton != null) _splitRunButton.IsEnabled = true;
                    break;
                default:
                    _statusIcon.Visibility = Visibility.Collapsed;
                    _statusIndicator.Text = string.Empty;
                    _statusIndicator.Visibility = Visibility.Collapsed;
                    if (_splitRunButton != null) _splitRunButton.IsEnabled = true;
                    break;
            }
        }

        // ── Cell manipulation helpers ─────────────────────────────────────────

        private static MenuItem MakeMenuItem(string header, Action action, ImageMoniker moniker = default)
        {
            var item = new MenuItem { Header = header };
            if (moniker.Id != 0)
            {
                item.Icon = MakeCrispImage(moniker);
            }
            item.Click += (s, e) => action();
            return item;
        }

        private static CrispImage MakeCrispImage(ImageMoniker moniker)
        {
            return new CrispImage
            {
                Moniker = moniker,
                Width = 16,
                Height = 16
            };
        }

        private void InsertCellAt(int offset, CellKind kind)
        {
            int idx = _document.Cells.IndexOf(_cell);
            if (idx < 0) return;
            var kernelName = kind == CellKind.Markdown ? "markdown" : _document.DefaultKernelName;
            _document.AddCell(kind, kernelName, idx + offset);
        }

        private void MoveUp()
        {
            int idx = _document.Cells.IndexOf(_cell);
            if (idx > 0) _document.MoveCell(_cell, idx - 1);
        }

        private void MoveDown()
        {
            int idx = _document.Cells.IndexOf(_cell);
            if (idx >= 0 && idx < _document.Cells.Count - 1)
                _document.MoveCell(_cell, idx + 1);
        }

        private void DeleteCell()
        {
            _cell.PropertyChanged -= OnCellPropertyChanged;
            _document.RemoveCell(_cell);
        }

        // ── Static helpers ────────────────────────────────────────────────────

        private static string GetLanguageDisplayName(string kernelName)
        {
            if (kernelName == null) return "?";
            switch (kernelName.ToLowerInvariant())
            {
                case "csharp": return "C#";
                case "fsharp": return "F#";
                case "javascript": return "JS";
                case "typescript": return "TS";
                case "sql": return "SQL";
                case "kql": return "KQL";
                case "pwsh":
                case "powershell": return "PS";
                case "html": return "HTML";
                case "markdown": return "MD";
                case "python": return "PY";
                default: return kernelName;
            }
        }

        private static readonly (string Kernel, string Display)[] LanguagePairs =
        {
            ("csharp",     "C#"),
            ("fsharp",     "F#"),
            ("javascript", "JavaScript"),
            ("typescript", "TypeScript"),
            ("sql",        "SQL"),
            ("kql",        "KQL"),
            ("pwsh",       "PowerShell"),
            ("python",     "Python")
        };
    }
}
