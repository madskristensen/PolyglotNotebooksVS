using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using PolyglotNotebooks.IntelliSense;
using PolyglotNotebooks.Kernel;
using PolyglotNotebooks.Models;
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PolyglotNotebooks.Editor
{
    /// <summary>
    /// Main notebook editor control.
    /// Hosts a <see cref="NotebookToolbar"/> at the top, then a scrollable list of
    /// <see cref="CellControl"/> instances, one per notebook cell.
    /// </summary>
    internal sealed class NotebookControl : ContentControl
    {
        private NotebookDocument? _document;
        private readonly StackPanel _cellStack;
        private readonly TextBlock _titleText;
        private readonly NotebookToolbar _toolbar;
        private IntelliSenseManager? _intelliSenseManager;
        private CellControl? _focusedCell;

        public NotebookControl(NotebookDocument? document)
        {
            var rootBorder = new Border();
            Themes.SetUseVsTheme(rootBorder, true);
            rootBorder.SetResourceReference(Border.BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);

            var rootGrid = new Grid();
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                    // row 0: header
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                    // row 1: toolbar
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // row 2: cells

            // Header
            var headerPanel = new DockPanel
            {
                Margin = new Thickness(16, 10, 16, 6),
                LastChildFill = true
            };
            _titleText = new TextBlock
            {
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _titleText.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            headerPanel.Children.Add(_titleText);
            Grid.SetRow(headerPanel, 0);
            rootGrid.Children.Add(headerPanel);

            // Toolbar
            _toolbar = new NotebookToolbar();
            _toolbar.RunAllRequested              += (s, e) => RunAllRequested?.Invoke(this, EventArgs.Empty);
            _toolbar.RestartAndRunAllRequested    += (s, e) => RestartAndRunAllRequested?.Invoke(this, EventArgs.Empty);
            _toolbar.InterruptRequested           += (s, e) => InterruptRequested?.Invoke(this, EventArgs.Empty);
            _toolbar.RestartKernelRequested       += (s, e) => RestartKernelRequested?.Invoke(this, EventArgs.Empty);
            _toolbar.ClearAllOutputsRequested     += (s, e) => ClearAllOutputsRequested?.Invoke(this, EventArgs.Empty);
            Grid.SetRow(_toolbar, 1);
            rootGrid.Children.Add(_toolbar);

            // Scrollable cell list
            _cellStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(16, 4, 16, 16)
            };
            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _cellStack
            };
            Grid.SetRow(scrollViewer, 2);
            rootGrid.Children.Add(scrollViewer);

            rootBorder.Child = rootGrid;
            Content = rootBorder;

            Document = document;
        }

        public NotebookDocument? Document
        {
            get => _document;
            set
            {
                if (_document != null)
                {
                    _document.PropertyChanged -= OnDocumentPropertyChanged;
                    _document.Cells.CollectionChanged -= OnCellsChanged;
                }

                _document = value;

                if (_document != null)
                {
                    _document.PropertyChanged += OnDocumentPropertyChanged;
                    _document.Cells.CollectionChanged += OnCellsChanged;
                }

                RebuildCells();
                UpdateTitle();
            }
        }

        public IntelliSenseManager? IntelliSenseManager
        {
            get => _intelliSenseManager;
            set
            {
                _intelliSenseManager = value;
                RebuildCells();
            }
        }

        /// <summary>
        /// Raised when the user clicks Run (▶) on any cell.
        /// </summary>
        public event EventHandler<CellRunEventArgs>? CellRunRequested;

        /// <summary>Raised when the user chooses "Run Cells Above" for a cell.</summary>
        public event EventHandler<CellRunEventArgs>? RunCellAboveRequested;

        /// <summary>Raised when the user chooses "Run Cell and Below" for a cell.</summary>
        public event EventHandler<CellRunEventArgs>? RunCellBelowRequested;

        /// <summary>Raised when the user chooses "Run Selection" for a cell.</summary>
        public event EventHandler<CellRunSelectionEventArgs>? RunSelectionRequested;

        /// <summary>Raised by the toolbar Run All button or Ctrl+Shift+Enter.</summary>
        public event EventHandler? RunAllRequested;

        /// <summary>Raised by the toolbar Restart+Run All button.</summary>
        public event EventHandler? RestartAndRunAllRequested;

        /// <summary>Raised by the toolbar Interrupt button or Ctrl+.</summary>
        public event EventHandler? InterruptRequested;

        /// <summary>Raised by the toolbar Restart Kernel button.</summary>
        public event EventHandler? RestartKernelRequested;

        /// <summary>Raised by the toolbar Clear All Outputs button.</summary>
        public event EventHandler? ClearAllOutputsRequested;

        /// <summary>
        /// Updates the kernel status indicator in the toolbar.
        /// Safe to call only from the UI thread.
        /// </summary>
        public void UpdateKernelStatus(KernelStatus status)
            => _toolbar.UpdateKernelStatus(status);

        // Keyboard shortcuts
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);

            var mods = e.KeyboardDevice.Modifiers;

            if (mods == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Enter)
            {
                // Ctrl+Shift+Enter → Run All
                RunAllRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
            else if (mods == ModifierKeys.Shift && e.Key == Key.Enter)
            {
                // Shift+Enter → Run current cell and advance focus to next cell
                if (_focusedCell != null)
                {
                    CellRunRequested?.Invoke(this, new CellRunEventArgs(_focusedCell.Cell));
                    AdvanceFocusToNextCell(_focusedCell);
                    e.Handled = true;
                }
            }
            else if (mods == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Back)
            {
                // Ctrl+Shift+Backspace → Clear current cell output
                _focusedCell?.Cell.Outputs.Clear();
                e.Handled = true;
            }
            else if (mods == ModifierKeys.Control && e.Key == Key.OemPeriod)
            {
                // Ctrl+. → Interrupt
                InterruptRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
        }

        private void AdvanceFocusToNextCell(CellControl current)
        {
            bool found = false;
            foreach (var child in _cellStack.Children)
            {
                if (found && child is CellControl next)
                {
                    next.CodeEditor.Focus();
                    return;
                }
                if (ReferenceEquals(child, current))
                    found = true;
            }
        }

        private void OnDocumentPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(NotebookDocument.FileName) ||
                e.PropertyName == nameof(NotebookDocument.IsDirty))
                UpdateTitle();
        }

        private void OnCellsChanged(object sender, NotifyCollectionChangedEventArgs e) => RebuildCells();

        private void UpdateTitle()
        {
            if (_document == null)
            {
                _titleText.Text = "Polyglot Notebook";
                return;
            }
            string dirty = _document.IsDirty ? " •" : string.Empty;
            _titleText.Text = $"{_document.FileName}{dirty}";
        }

        private void RebuildCells()
        {
            // Detach IntelliSense from existing cells before clearing
            foreach (var child in _cellStack.Children)
            {
                if (child is CellControl cc)
                    _intelliSenseManager?.DetachFromCell(cc);
            }

            _cellStack.Children.Clear();
            _focusedCell = null;

            if (_document == null)
                return;

            if (_document.Cells.Count == 0)
            {
                _cellStack.Children.Add(MakeAddCellButton(0));
                return;
            }

            for (int i = 0; i < _document.Cells.Count; i++)
            {
                // Insert button above first cell
                if (i == 0)
                    _cellStack.Children.Add(MakeAddCellButton(0));

                var cell = _document.Cells[i];
                var cellControl = new CellControl(_document, cell);

                // Bubble run events with cell context
                cellControl.RunRequested      += (s, e) => CellRunRequested?.Invoke(this, new CellRunEventArgs(cell));
                cellControl.RunAboveRequested += (s, e) => RunCellAboveRequested?.Invoke(this, new CellRunEventArgs(cell));
                cellControl.RunBelowRequested += (s, e) => RunCellBelowRequested?.Invoke(this, new CellRunEventArgs(cell));
                cellControl.RunSelectionRequested += (s, e) =>
                    RunSelectionRequested?.Invoke(this, new CellRunSelectionEventArgs(cell, e.SelectedText));

                // Track which cell currently has keyboard focus
                cellControl.GotFocus  += (s, e) => _focusedCell = cellControl;
                cellControl.LostFocus += (s, e) => { if (ReferenceEquals(_focusedCell, cellControl)) _focusedCell = null; };

                _intelliSenseManager?.AttachToCell(cellControl);
                _cellStack.Children.Add(cellControl);

                // Insert button below every cell
                _cellStack.Children.Add(MakeAddCellButton(i + 1));
            }
        }

        private Button MakeAddCellButton(int insertIndex)
        {
            var icon = new CrispImage
            {
                Moniker = KnownMonikers.AddItem,
                Width = 14,
                Height = 14,
                Margin = new Thickness(0, 0, 4, 0)
            };
            var label = new System.Windows.Controls.TextBlock { Text = "Add Cell", VerticalAlignment = VerticalAlignment.Center };
            var panel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            panel.Children.Add(icon);
            panel.Children.Add(label);

            var btn = new Button
            {
                Content = panel,
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(6, 3, 8, 3),
                Margin = new Thickness(0, 2, 0, 2),
                FontSize = 11,
                BorderThickness = new Thickness(1),
                Opacity = 0.5,
                ToolTip = "Insert a new code cell"
            };
            btn.SetResourceReference(Button.BackgroundProperty, VsBrushes.ButtonFaceKey);
            btn.SetResourceReference(Button.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            btn.SetResourceReference(Button.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
            btn.MouseEnter += (s, e) => btn.Opacity = 1.0;
            btn.MouseLeave += (s, e) => btn.Opacity = 0.5;

            var capturedIndex = insertIndex;
            btn.Click += (s, e) =>
            {
                if (_document == null) return;
                _document.AddCell(CellKind.Code, _document.DefaultKernelName, capturedIndex);
            };

            return btn;
        }
    }

    /// <summary>Event data for <see cref="NotebookControl.CellRunRequested"/>.</summary>
    public sealed class CellRunEventArgs : EventArgs
    {
        public CellRunEventArgs(NotebookCell cell) => Cell = cell;
        public NotebookCell Cell { get; }
    }

    /// <summary>Event data for <see cref="NotebookControl.RunSelectionRequested"/>.</summary>
    public sealed class CellRunSelectionEventArgs : EventArgs
    {
        public CellRunSelectionEventArgs(NotebookCell cell, string selectedText)
        {
            Cell = cell;
            SelectedText = selectedText;
        }
        public NotebookCell Cell { get; }
        public string SelectedText { get; }
    }
}
