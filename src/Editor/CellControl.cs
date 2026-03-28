using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using PolyglotNotebooks.Editor.SyntaxHighlighting;
using PolyglotNotebooks.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace PolyglotNotebooks.Editor
{
    /// <summary>
    /// Visual representation of a single notebook cell.
    /// Composes a CellToolbar (top), code TextBox (middle), and OutputControl (bottom).
    /// </summary>
    internal sealed class CellControl : Border
    {
        private readonly NotebookCell _cell;
        private TextBox _editor;
        private SyntaxHighlightAdorner? _syntaxAdorner;
        private IWpfTextViewHost? _textViewHost;
        private bool _suppressBufferSync;

        // Kernel name → VS content type mapping
        private static readonly Dictionary<string, string> _kernelContentTypeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["csharp"] = "CSharp",
            ["fsharp"] = "F#",
            ["javascript"] = "JavaScript",
            ["typescript"] = "TypeScript",
            ["python"] = "Python",
            ["powershell"] = "PowerShell",
            ["sql"] = "SQL",
            ["html"] = "html",
            ["markdown"] = "markdown"
        };

        // Markdown cell state
        private StackPanel _markdownDisplay;
        private bool _isMarkdownEditing;
        private Grid _contentGrid;

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
            _contentGrid = grid;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // toolbar
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // editor
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // outputs

            // Toolbar
            var toolbar = new CellToolbar(document, cell);
            toolbar.RunRequested        += (s, e) => RunRequested?.Invoke(this, e);
            toolbar.RunAboveRequested   += (s, e) => RunAboveRequested?.Invoke(this, e);
            toolbar.RunBelowRequested   += (s, e) => RunBelowRequested?.Invoke(this, e);
            Grid.SetRow(toolbar, 0);
            grid.Children.Add(toolbar);

            // Separator line between toolbar and content
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

            if (cell.Kind == CellKind.Markdown)
            {
                BuildMarkdownCellContent(grid, cell);
            }
            else
            {
                BuildCodeCellContent(grid, cell, toolbar);
            }

            Child = grid;
        }

        public NotebookCell Cell => _cell;

        internal TextBox CodeEditor => _editor;

        /// <summary>The hosted VS text view for code cells, or null for markdown cells.</summary>
        internal IWpfTextView? TextView { get; private set; }

        // ── Code cell construction ────────────────────────────────────────────

        private void BuildCodeCellContent(Grid grid, NotebookCell cell, CellToolbar toolbar)
        {
            var fontFamily = new FontFamily("Consolas, Courier New");
            double fontSize = 13;
            double lineHeight = fontFamily.LineSpacing * fontSize;
            double editorVerticalPadding = 4 + 4;
            double minH = Math.Ceiling(lineHeight * 2 + editorVerticalPadding);
            double maxH = Math.Ceiling(lineHeight * 20 + editorVerticalPadding);

            // Obtain MEF services for the VS editor
            var componentModel = (IComponentModel)Package.GetGlobalService(typeof(SComponentModel));
            var textEditorFactory = componentModel.GetService<ITextEditorFactoryService>();
            var textBufferFactory = componentModel.GetService<ITextBufferFactoryService>();
            var contentTypeRegistry = componentModel.GetService<IContentTypeRegistryService>();

            // Resolve content type from kernel name
            var contentType = ResolveContentType(contentTypeRegistry, cell.KernelName);

            // Create text buffer and populate it
            var buffer = textBufferFactory.CreateTextBuffer(cell.Contents ?? string.Empty, contentType);

            // Create IWpfTextView with desired options
            var textView = textEditorFactory.CreateTextView(buffer, textEditorFactory.DefaultRoles);
            textView.Options.SetOptionValue(DefaultTextViewOptions.WordWrapStyleId, WordWrapStyles.None);
            textView.Options.SetOptionValue(DefaultTextViewHostOptions.LineNumberMarginId, false);
            textView.Options.SetOptionValue(DefaultTextViewHostOptions.GlyphMarginId, false);

            var host = textEditorFactory.CreateTextViewHost(textView, setFocus: false);
            _textViewHost = host;
            TextView = textView;

            var hostControl = host.HostControl;
            hostControl.MinHeight = minH;
            hostControl.MaxHeight = maxH;

            // Two-way sync: Buffer → Model
            buffer.Changed += (s, e) =>
            {
                if (_suppressBufferSync) return;
                cell.Contents = buffer.CurrentSnapshot.GetText();
                ParseMagicCommand(buffer.CurrentSnapshot.GetText(), cell);
            };

            // Two-way sync: Model → Buffer (for external changes)
            cell.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(NotebookCell.Contents))
                {
                    var currentText = buffer.CurrentSnapshot.GetText();
                    if (currentText != cell.Contents)
                    {
                        _suppressBufferSync = true;
                        try
                        {
                            using (var edit = buffer.CreateEdit())
                            {
                                edit.Replace(new Microsoft.VisualStudio.Text.Span(0, buffer.CurrentSnapshot.Length), cell.Contents ?? string.Empty);
                                edit.Apply();
                            }
                        }
                        finally
                        {
                            _suppressBufferSync = false;
                        }
                    }
                }
                else if (e.PropertyName == nameof(NotebookCell.KernelName))
                {
                    // Update content type when kernel changes
                    var newContentType = ResolveContentType(contentTypeRegistry, cell.KernelName);
                    if (buffer.ContentType != newContentType)
                        buffer.ChangeContentType(newContentType, editTag: null);
                }
            };

            toolbar.RunSelectionRequested += (s, e) =>
            {
                var selectedSpans = textView.Selection.SelectedSpans;
                var selected = string.Join(string.Empty, selectedSpans.Select(span => span.GetText()));
                if (!string.IsNullOrEmpty(selected))
                    RunSelectionRequested?.Invoke(this, new RunSelectionEventArgs(selected));
            };

            // Clean up when removed from visual tree
            hostControl.Unloaded += (s, e) =>
            {
                host.Close();
            };

            Grid.SetRow(hostControl, 1);
            grid.Children.Add(hostControl);

            var output = new OutputControl { Cell = cell };
            Grid.SetRow(output, 2);
            grid.Children.Add(output);
        }

        private static IContentType ResolveContentType(IContentTypeRegistryService registry, string? kernelName)
        {
            if (kernelName != null && _kernelContentTypeMap.TryGetValue(kernelName, out var vsContentType))
            {
                var ct = registry.GetContentType(vsContentType);
                if (ct != null)
                    return ct;
            }
            return registry.GetContentType("text") ?? registry.GetContentType("plaintext");
        }

        // ── Markdown cell construction ────────────────────────────────────────

        private void BuildMarkdownCellContent(Grid grid, NotebookCell cell)
        {
            Focusable = true;

            // Editor for raw markdown editing (initially hidden)
            var fontFamily = new FontFamily("Segoe UI, Verdana");
            double fontSize = 13;
            double lineHeight = fontFamily.LineSpacing * fontSize;
            double editorVerticalPadding = 4 + 4;

            var editor = new TextBox
            {
                FontFamily = fontFamily,
                FontSize = fontSize,
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.Wrap,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MinHeight = Math.Ceiling(lineHeight * 2 + editorVerticalPadding),
                MaxHeight = Math.Ceiling(lineHeight * 20 + editorVerticalPadding),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(4),
                IsUndoEnabled = true,
                Visibility = Visibility.Collapsed
            };
            editor.SetResourceReference(TextBox.BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
            editor.SetResourceReference(TextBox.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            editor.SetResourceReference(TextBox.SelectionBrushProperty, VsBrushes.HighlightKey);

            editor.SetBinding(TextBox.TextProperty, new Binding(nameof(NotebookCell.Contents))
            {
                Source = cell,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });

            editor.TextChanged += (s, e) => AdjustEditorHeight(editor);
            editor.LostFocus += (s, e) => ExitMarkdownEditMode();

            _editor = editor;

            Grid.SetRow(editor, 1);
            grid.Children.Add(editor);

            // Rendered markdown display (initially visible)
            _markdownDisplay = RenderMarkdownToPanel(cell.Contents);
            _markdownDisplay.MouseLeftButtonDown += OnMarkdownDisplayClicked;
            Grid.SetRow(_markdownDisplay, 1);
            grid.Children.Add(_markdownDisplay);

            // Re-render when contents change externally
            cell.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(NotebookCell.Contents) && !_isMarkdownEditing)
                    RefreshMarkdownDisplay();
            };
        }

        private void OnMarkdownDisplayClicked(object sender, MouseButtonEventArgs e)
        {
            Focus();
            if (e.ClickCount >= 2)
                EnterMarkdownEditMode();
        }

        private void EnterMarkdownEditMode()
        {
            if (_isMarkdownEditing) return;
            _isMarkdownEditing = true;
            _markdownDisplay.Visibility = Visibility.Collapsed;
            _editor.Visibility = Visibility.Visible;
            AdjustEditorHeight(_editor);
            _editor.Focus();
            _editor.CaretIndex = _editor.Text?.Length ?? 0;
        }

        private void ExitMarkdownEditMode()
        {
            if (!_isMarkdownEditing) return;
            _isMarkdownEditing = false;
            _editor.Visibility = Visibility.Collapsed;
            RefreshMarkdownDisplay();
            _markdownDisplay.Visibility = Visibility.Visible;
        }

        private void RefreshMarkdownDisplay()
        {
            if (_markdownDisplay == null || _contentGrid == null) return;
            var row = Grid.GetRow(_markdownDisplay);
            _contentGrid.Children.Remove(_markdownDisplay);
            _markdownDisplay = RenderMarkdownToPanel(_cell.Contents);
            _markdownDisplay.MouseLeftButtonDown += OnMarkdownDisplayClicked;
            Grid.SetRow(_markdownDisplay, row);
            _contentGrid.Children.Add(_markdownDisplay);
        }

        // ── Markdown WPF rendering ────────────────────────────────────────────

        private static StackPanel RenderMarkdownToPanel(string markdown)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(4),
                Cursor = Cursors.Hand,
                MinHeight = 30
            };
            panel.ToolTip = "Double-click to edit";

            if (string.IsNullOrWhiteSpace(markdown))
            {
                var placeholder = new TextBlock
                {
                    Text = "Double-click to edit markdown\u2026",
                    FontStyle = FontStyles.Italic,
                    Padding = new Thickness(4),
                };
                placeholder.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.GrayTextKey);
                panel.Children.Add(placeholder);
                return panel;
            }

            var lines = markdown.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            bool inCodeBlock = false;
            var codeLines = new StringBuilder();

            foreach (var line in lines)
            {
                if (!inCodeBlock && (line.TrimStart().StartsWith("```") || line.TrimStart().StartsWith("~~~")))
                {
                    inCodeBlock = true;
                    codeLines.Clear();
                    continue;
                }
                if (inCodeBlock)
                {
                    if (line.TrimStart().StartsWith("```") || line.TrimStart().StartsWith("~~~"))
                    {
                        panel.Children.Add(CreateCodeBlock(codeLines.ToString().TrimEnd('\r', '\n')));
                        inCodeBlock = false;
                    }
                    else
                    {
                        codeLines.AppendLine(line);
                    }
                    continue;
                }

                // ATX headings
                if (line.StartsWith("### "))
                { panel.Children.Add(CreateHeadingBlock(line.Substring(4), 16, FontWeights.SemiBold)); continue; }
                if (line.StartsWith("## "))
                { panel.Children.Add(CreateHeadingBlock(line.Substring(3), 18, FontWeights.SemiBold)); continue; }
                if (line.StartsWith("# "))
                { panel.Children.Add(CreateHeadingBlock(line.Substring(2), 22, FontWeights.Bold)); continue; }

                // Unordered lists
                if (line.StartsWith("- ") || line.StartsWith("* ") || line.StartsWith("+ "))
                {
                    var tb = new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 13,
                        Margin = new Thickness(16, 1, 0, 1),
                    };
                    tb.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
                    tb.Inlines.Add(new Run("\u2022 "));
                    AddInlineFormatting(tb.Inlines, line.Substring(2));
                    panel.Children.Add(tb);
                    continue;
                }

                // Blank line
                if (string.IsNullOrWhiteSpace(line))
                { panel.Children.Add(new Border { Height = 8 }); continue; }

                // Regular paragraph
                var para = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 13,
                    Margin = new Thickness(0, 1, 0, 1),
                };
                para.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
                AddInlineFormatting(para.Inlines, line);
                panel.Children.Add(para);
            }

            if (inCodeBlock)
                panel.Children.Add(CreateCodeBlock(codeLines.ToString().TrimEnd('\r', '\n')));

            return panel;
        }

        private static Border CreateCodeBlock(string text)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Consolas, Courier New"),
                FontSize = 12,
                TextWrapping = TextWrapping.NoWrap,
                Padding = new Thickness(8, 4, 8, 4),
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            var border = new Border
            {
                Child = tb,
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 2, 0, 2),
            };
            border.SetResourceReference(Border.BackgroundProperty, VsBrushes.ButtonFaceKey);
            border.SetResourceReference(Border.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
            return border;
        }

        private static TextBlock CreateHeadingBlock(string text, double fontSize, FontWeight weight)
        {
            var tb = new TextBlock
            {
                FontSize = fontSize,
                FontWeight = weight,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 4),
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            AddInlineFormatting(tb.Inlines, text);
            return tb;
        }

        private static void AddInlineFormatting(InlineCollection inlines, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                inlines.Add(new Run(text ?? string.Empty));
                return;
            }

            // Priority: bold (**/__) before code (`), then italic (*/_)
            var regex = new Regex(@"\*\*(.+?)\*\*|__(.+?)__|`(.+?)`|\*(.+?)\*|_([^_\s](?:[^_]*[^_\s])?)_");
            int lastEnd = 0;

            foreach (Match m in regex.Matches(text))
            {
                if (m.Index > lastEnd)
                    inlines.Add(new Run(text.Substring(lastEnd, m.Index - lastEnd)));

                if (m.Groups[1].Success)       // **bold**
                    inlines.Add(new Bold(new Run(m.Groups[1].Value)));
                else if (m.Groups[2].Success)  // __bold__
                    inlines.Add(new Bold(new Run(m.Groups[2].Value)));
                else if (m.Groups[3].Success)  // `code`
                    inlines.Add(new Run(m.Groups[3].Value) { FontFamily = new FontFamily("Consolas, Courier New") });
                else if (m.Groups[4].Success)  // *italic*
                    inlines.Add(new Italic(new Run(m.Groups[4].Value)));
                else if (m.Groups[5].Success)  // _italic_
                    inlines.Add(new Italic(new Run(m.Groups[5].Value)));

                lastEnd = m.Index + m.Length;
            }

            if (lastEnd < text.Length)
                inlines.Add(new Run(text.Substring(lastEnd)));
            else if (lastEnd == 0)
                inlines.Add(new Run(text));
        }

        // ── Syntax highlighting ───────────────────────────────────────────────

        private void SetupSyntaxAdorner()
        {
            if (_syntaxAdorner != null || _editor == null) return;
            var layer = AdornerLayer.GetAdornerLayer(_editor);
            if (layer == null) return;

            _syntaxAdorner = new SyntaxHighlightAdorner(_editor);
            layer.Add(_syntaxAdorner);
            _syntaxAdorner.SetLanguage(_cell.KernelName);
        }

        private void OnCellPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(NotebookCell.KernelName))
                _syntaxAdorner?.SetLanguage(_cell.KernelName);
        }

        // ── Magic command parsing ─────────────────────────────────────────────

        /// <summary>
        /// Inspects the first line of the editor for a dotnet-interactive magic command
        /// (e.g. <c>#!csharp</c>) and updates <see cref="NotebookCell.KernelName"/> to match.
        /// Has no effect when the first line is not a magic command.
        /// </summary>
        private static void ParseMagicCommand(string text, NotebookCell cell)
        {
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

        /// <summary>
        /// Overload for TextBox-based markdown cells.
        /// </summary>
        private static void ParseMagicCommand(TextBox editor, NotebookCell cell)
        {
            ParseMagicCommand(editor.Text, cell);
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
