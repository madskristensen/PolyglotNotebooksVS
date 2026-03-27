using Microsoft.VisualStudio.Shell;
using PolyglotNotebooks.Models;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace PolyglotNotebooks.Editor
{
    /// <summary>
    /// Visual representation of a single notebook cell.
    /// Composes a CellToolbar (top), code TextBox (middle), and OutputControl (bottom).
    /// </summary>
    internal sealed class CellControl : Border
    {
        private readonly NotebookCell _cell;
        private readonly TextBox _editor;

        /// <summary>Raised when the user activates Run on this cell's toolbar.</summary>
        public event EventHandler? RunRequested;

        /// <summary>Raised when the user chooses "Run Cells Above" for this cell.</summary>
        public event EventHandler? RunAboveRequested;

        /// <summary>Raised when the user chooses "Run Cell and Below" for this cell.</summary>
        public event EventHandler? RunBelowRequested;

        /// <summary>Raised when the user chooses "Run Selection"; carries the selected text.</summary>
        public event EventHandler<RunSelectionEventArgs>? RunSelectionRequested;

        public CellControl(NotebookDocument document, NotebookCell cell)
        {
            _cell = cell;

            // Outer card border
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(4);
            Margin = new Thickness(0, 4, 0, 4);
            Padding = new Thickness(10);
            SetResourceReference(Border.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
            SetResourceReference(Border.BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // toolbar
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // editor
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // outputs

            // Toolbar
            var toolbar = new CellToolbar(document, cell);
            toolbar.RunRequested        += (s, e) => RunRequested?.Invoke(this, e);
            toolbar.RunAboveRequested   += (s, e) => RunAboveRequested?.Invoke(this, e);
            toolbar.RunBelowRequested   += (s, e) => RunBelowRequested?.Invoke(this, e);
            // RunSelectionRequested wired after _editor is assigned below
            Grid.SetRow(toolbar, 0);
            grid.Children.Add(toolbar);

            // Separator line between toolbar and editor
            var sep = new System.Windows.Shapes.Rectangle
            {
                Height = 1,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 4),
                Opacity = 0.3
            };
            sep.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, VsBrushes.ToolWindowBorderKey);
            Grid.SetRow(sep, 1);
            grid.Children.Add(sep);

            // Code editor (TextBox) — height auto-sizes to content, capped at 20 lines
            var fontFamily = new System.Windows.Media.FontFamily("Consolas, Courier New");
            double fontSize = 13;
            double lineHeight = fontFamily.LineSpacing * fontSize;
            double editorVerticalPadding = 4 + 4; // Padding top + bottom

            var editor = new TextBox
            {
                FontFamily = fontFamily,
                FontSize = fontSize,
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.NoWrap,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MinHeight = Math.Ceiling(lineHeight * 2 + editorVerticalPadding),
                MaxHeight = Math.Ceiling(lineHeight * 20 + editorVerticalPadding),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(4),
                IsUndoEnabled = true
            };
            editor.SetResourceReference(TextBox.BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
            editor.SetResourceReference(TextBox.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            editor.SetResourceReference(TextBox.SelectionBrushProperty, VsBrushes.HighlightKey);

            // Two-way binding: TextBox.Text ↔ NotebookCell.Contents
            editor.SetBinding(TextBox.TextProperty, new Binding(nameof(NotebookCell.Contents))
            {
                Source = cell,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });

            // Auto-grow + magic command detection on every text change.
            editor.TextChanged += (s, e) =>
            {
                AdjustEditorHeight(editor);
                ParseMagicCommand(editor, cell);
            };
            AdjustEditorHeight(editor);

            _editor = editor;

            // Wire RunSelectionRequested after _editor is assigned so the closure is safe.
            toolbar.RunSelectionRequested += (s, e) =>
            {
                var selected = _editor.SelectedText;
                if (!string.IsNullOrEmpty(selected))
                    RunSelectionRequested?.Invoke(this, new RunSelectionEventArgs(selected));
            };

            Grid.SetRow(editor, 1);
            grid.Children.Add(editor);

            // Output area
            var output = new OutputControl { Cell = cell };
            Grid.SetRow(output, 2);
            grid.Children.Add(output);

            Child = grid;
        }

        public NotebookCell Cell => _cell;

        internal TextBox CodeEditor => _editor;

        // ── Magic command parsing ─────────────────────────────────────────────

        /// <summary>
        /// Inspects the first line of the editor for a dotnet-interactive magic command
        /// (e.g. <c>#!csharp</c>) and updates <see cref="NotebookCell.KernelName"/> to match.
        /// Has no effect when the first line is not a magic command.
        /// </summary>
        private static void ParseMagicCommand(TextBox editor, NotebookCell cell)
        {
            var text = editor.Text;
            if (string.IsNullOrEmpty(text)) return;

            var newlineIdx = text.IndexOf('\n');
            var firstLine = (newlineIdx >= 0 ? text.Substring(0, newlineIdx) : text).Trim();

            if (!firstLine.StartsWith("#!")) return;

            var kernelHint = firstLine.Substring(2).Trim();
            if (string.IsNullOrEmpty(kernelHint)) return;

            // Only update when changed — avoids triggering PropertyChanged loops.
            if (!string.Equals(kernelHint, cell.KernelName, StringComparison.OrdinalIgnoreCase))
                cell.KernelName = kernelHint;
        }

        private static void AdjustEditorHeight(TextBox editor)
        {
            editor.Measure(new Size(editor.ActualWidth > 0 ? editor.ActualWidth : double.PositiveInfinity,
                                    double.PositiveInfinity));
            double desired = editor.DesiredSize.Height;
            if (desired < editor.MinHeight) desired = editor.MinHeight;
            if (desired > editor.MaxHeight) desired = editor.MaxHeight;
            editor.Height = desired;
        }
    }

    /// <summary>Event data carrying the selected text for "Run Selection" actions.</summary>
    public sealed class RunSelectionEventArgs : EventArgs
    {
        public RunSelectionEventArgs(string selectedText) => SelectedText = selectedText;
        public string SelectedText { get; }
    }
}
