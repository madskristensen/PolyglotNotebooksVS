using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;

using PolyglotNotebooks.IntelliSense;
using PolyglotNotebooks.Models;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

            // ── Kernel selector (left) ─────────────────────────────────────────
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

            // ── Right-side controls (added right-to-left via Dock.Right) ──────

            // Status indicator (rightmost)
            _statusIndicator = new TextBlock
            {
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0),
                Visibility = Visibility.Collapsed
            };
            DockPanel.SetDock(_statusIndicator, Dock.Right);
            Children.Add(_statusIndicator);

            // Cell menu (···)
            var menuBtn = BuildMenuButton();
            DockPanel.SetDock(menuBtn, Dock.Right);
            Children.Add(menuBtn);

            // Clear Output button
            var clearBtn = new Button
            {
                Content = MakeCrispImage(KnownMonikers.ClearWindowContent),
                Padding = new Thickness(4, 2, 4, 2),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
                ToolTip = "Clear cell output (Ctrl+Shift+Backspace)"
            };
            clearBtn.SetResourceReference(Button.BackgroundProperty, VsBrushes.ButtonFaceKey);
            clearBtn.SetResourceReference(Button.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            clearBtn.SetResourceReference(Button.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
            clearBtn.Click += (s, e) => _cell.Outputs.Clear();
            DockPanel.SetDock(clearBtn, Dock.Right);
            Children.Add(clearBtn);

            // Run-mode dropdown button (▼) — opens run-mode context menu
            var runDropdownBtn = BuildRunDropdownButton();
            DockPanel.SetDock(runDropdownBtn, Dock.Right);
            Children.Add(runDropdownBtn);

            // Run button
            var runBtn = new Button
            {
                Content = MakeCrispImage(KnownMonikers.Run),
                Padding = new Thickness(4, 2, 4, 2),
                BorderThickness = new Thickness(1, 1, 0, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 0),
                ToolTip = "Run cell (Shift+Enter)"
            };
            runBtn.SetResourceReference(Button.BackgroundProperty, VsBrushes.ButtonFaceKey);
            runBtn.SetResourceReference(Button.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            runBtn.SetResourceReference(Button.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
            runBtn.Click += (s, e) => RunRequested?.Invoke(this, EventArgs.Empty);
            DockPanel.SetDock(runBtn, Dock.Right);
            Children.Add(runBtn);

            // Execution counter [N]
            _executionCounter = new TextBlock
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            _executionCounter.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.GrayTextKey);
            UpdateExecutionCounter();
            DockPanel.SetDock(_executionCounter, Dock.Right);
            Children.Add(_executionCounter);

            UpdateStatusIndicator();

            _cell.PropertyChanged += OnCellPropertyChanged;
            Unloaded += (s, e) => _cell.PropertyChanged -= OnCellPropertyChanged;
        }

        private Button BuildRunDropdownButton()
        {
            var btn = new Button
            {
                Content = MakeCrispImage(KnownMonikers.Expand),
                Padding = new Thickness(2, 2, 2, 2),
                BorderThickness = new Thickness(0, 1, 1, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
                ToolTip = "Run options"
            };
            btn.SetResourceReference(Button.BackgroundProperty, VsBrushes.ButtonFaceKey);
            btn.SetResourceReference(Button.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            btn.SetResourceReference(Button.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);

            var menu = new ContextMenu();
            ThemedContextMenuHelper.ApplyVsTheme(menu);
            menu.Items.Add(MakeMenuItem("Run Cell", () => RunRequested?.Invoke(this, EventArgs.Empty), KnownMonikers.Run));
            menu.Items.Add(MakeMenuItem("Run Cells Above", () => RunAboveRequested?.Invoke(this, EventArgs.Empty), KnownMonikers.MoveUp));
            menu.Items.Add(MakeMenuItem("Run Cell and Below", () => RunBelowRequested?.Invoke(this, EventArgs.Empty), KnownMonikers.MoveDown));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Run Selection", () => RunSelectionRequested?.Invoke(this, EventArgs.Empty), KnownMonikers.RunOutline));

            btn.Click += (s, e) =>
            {
                menu.PlacementTarget = btn;
                menu.Placement = PlacementMode.Bottom;
                menu.IsOpen = true;
            };

            return btn;
        }

        private Button BuildMenuButton()
        {
            var btn = new Button
            {
                Content = MakeCrispImage(KnownMonikers.Ellipsis),
                Padding = new Thickness(4, 2, 4, 2),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
                ToolTip = "Cell options"
            };
            btn.SetResourceReference(Button.BackgroundProperty, VsBrushes.ButtonFaceKey);
            btn.SetResourceReference(Button.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            btn.SetResourceReference(Button.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);

            var menu = new ContextMenu();
            ThemedContextMenuHelper.ApplyVsTheme(menu);
            menu.Items.Add(MakeMenuItem("Run Cell", () => RunRequested?.Invoke(this, EventArgs.Empty), KnownMonikers.Run));
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeMenuItem("Insert Cell Above", () => InsertCellAt(0), KnownMonikers.InsertClause));
            menu.Items.Add(MakeMenuItem("Insert Cell Below", () => InsertCellAt(1), KnownMonikers.InsertClause));
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
                    _statusIndicator.Text = "⟳ Running";
                    _statusIndicator.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.VizSurfaceGoldMediumKey);
                    _statusIndicator.Visibility = Visibility.Visible;
                    break;
                case CellExecutionStatus.Succeeded:
                    _statusIndicator.Text = "✓";
                    _statusIndicator.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.VizSurfaceGreenMediumKey);
                    _statusIndicator.Visibility = Visibility.Visible;
                    break;
                case CellExecutionStatus.Failed:
                    _statusIndicator.Text = "✗ Error";
                    _statusIndicator.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.VizSurfaceRedMediumKey);
                    _statusIndicator.Visibility = Visibility.Visible;
                    break;
                default:
                    _statusIndicator.Text = string.Empty;
                    _statusIndicator.Visibility = Visibility.Collapsed;
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

        private void InsertCellAt(int offset)
        {
            int idx = _document.Cells.IndexOf(_cell);
            if (idx < 0) return;
            _document.AddCell(CellKind.Code, _document.DefaultKernelName, idx + offset);
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
            ("pwsh",       "PowerShell"),
            ("python",     "Python")
        };
    }
}
