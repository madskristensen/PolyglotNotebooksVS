using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;

using PolyglotNotebooks.Diagnostics;
using PolyglotNotebooks.Models;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
        private TextBox? _editor;
        private IWpfTextViewHost? _textViewHost;
        private IVsTextView? _vsTextView;
        private IVsCodeWindow? _codeWindow;
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

        // Cached regex for inline markdown formatting (bold, code, italic)
        private static readonly Regex _inlineFormattingRegex =
            new Regex(@"\*\*(.+?)\*\*|__(.+?)__|`(.+?)`|\*(.+?)\*|_([^_\s](?:[^_]*[^_\s])?)_", RegexOptions.Compiled);

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
            toolbar.RunRequested += (s, e) => RunRequested?.Invoke(this, e);
            toolbar.RunAboveRequested += (s, e) => RunAboveRequested?.Invoke(this, e);
            toolbar.RunBelowRequested += (s, e) => RunBelowRequested?.Invoke(this, e);
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

        internal TextBox? CodeEditor => _editor;

        /// <summary>The hosted VS text view for code cells, or null for markdown cells.</summary>
        internal IWpfTextView? TextView { get; private set; }

        /// <summary>The hosted IVsTextView for command forwarding, or null for markdown/fallback cells.</summary>
        internal IVsTextView? VsTextView => _vsTextView;

        /// <summary>
        /// Returns true if this cell's hosted VS text view currently has aggregate keyboard focus.
        /// </summary>
        internal bool HasFocusedTextView()
            => (_textViewHost?.TextView?.VisualElement?.IsKeyboardFocusWithin ?? false)
               || (_textViewHost?.TextView?.HasAggregateFocus ?? false);

        /// <summary>
        /// Returns the hosted IWpfTextView if it currently has keyboard focus, otherwise null.
        /// </summary>
        internal IWpfTextView GetFocusedIWpfTextView()
        {
            if (_textViewHost?.TextView != null
                && (_textViewHost.TextView.VisualElement?.IsKeyboardFocusWithin == true
                    || _textViewHost.TextView.HasAggregateFocus))
            {
                return _textViewHost.TextView;
            }
            return null;
        }

        // ── Code cell construction ────────────────────────────────────────────

        private void BuildCodeCellContent(Grid grid, NotebookCell cell, CellToolbar toolbar)
        {
            var fontFamily = new FontFamily("Consolas, Courier New");
            double fontSize = 13;
            double lineHeight = fontFamily.LineSpacing * fontSize;
            double editorVerticalPadding = 4 + 4;
            double minH = Math.Ceiling(lineHeight * 1 + editorVerticalPadding);
            double maxH = Math.Ceiling(lineHeight * 25 + editorVerticalPadding);

            try
            {
                // Obtain MEF services for the VS editor
                var componentModel = (IComponentModel)Package.GetGlobalService(typeof(SComponentModel));
                var editorAdapterFactory = componentModel.GetService<IVsEditorAdaptersFactoryService>();
                var contentTypeRegistry = componentModel.GetService<IContentTypeRegistryService>();

                // Get OLE service provider (required by VS adapter factory)
                var oleServiceProvider = GetOleServiceProvider();

                // Resolve content type from kernel name
                var contentType = ResolveContentType(contentTypeRegistry, cell.KernelName);

                // Create VS text buffer adapter with the resolved content type
                var bufferAdapter = editorAdapterFactory.CreateVsTextBufferAdapter(oleServiceProvider, contentType);
                var initialText = cell.Contents ?? string.Empty;
                bufferAdapter.InitializeContent(initialText, initialText.Length);

                // Set language service ID on the COM buffer so VS's language
                // service infrastructure (classifiers, colorizers) engages
                Guid langServiceId = GetLanguageServiceGuid(cell.KernelName);
                if (langServiceId != Guid.Empty)
                {
                    ((IVsTextBuffer)bufferAdapter).SetLanguageServiceID(ref langServiceId);
                }

                // Get the MEF ITextBuffer for event subscription and content type changes
                var buffer = editorAdapterFactory.GetDataBuffer((IVsTextBuffer)bufferAdapter);

                // Mark buffer as a notebook cell so the MEF classifier engages
                buffer.Properties.AddProperty("PolyglotNotebook.KernelName", cell.KernelName ?? "text");

                // Ensure the data buffer has the correct content type
                // (CreateVsTextBufferAdapter may not always propagate to the data buffer)
                if (buffer.ContentType.TypeName != contentType.TypeName)
                {
                    ExtensionLogger.LogInfo(nameof(CellControl),
                        $"Data buffer content type mismatch: got '{buffer.ContentType.TypeName}', expected '{contentType.TypeName}'; forcing change");
                    buffer.ChangeContentType(contentType, null);
                }

                // Write temp file to disk so language services can find it
                var fakePath = GetFakeFileName(cell.KernelName);
                File.WriteAllText(fakePath, initialText, Encoding.UTF8);

                // Associate a file document so Roslyn/language services engage
                var documentFactory = componentModel.GetService<ITextDocumentFactoryService>();
                try
                {
                    documentFactory.CreateTextDocument(buffer, fakePath);
                }
                catch (Exception docEx)
                {
                    ExtensionLogger.LogWarning(nameof(CellControl),
                        $"ITextDocument creation failed for kernel '{cell.KernelName}': {docEx.Message}");
                }

                // Create code window adapter — has its own HWND and IOleCommandTarget chain
                var codeWindow = editorAdapterFactory.CreateVsCodeWindowAdapter(oleServiceProvider);

                // Disable splitter bar and dropdown/navigation bar to reduce chrome
                var codeWindowEx = (IVsCodeWindowEx)codeWindow;
                var initView = new INITVIEW[1];
                codeWindowEx.Initialize(
                    (uint)(_codewindowbehaviorflags.CWB_DISABLESPLITTER
                         | _codewindowbehaviorflags.CWB_DISABLEDROPDOWNBAR),
                    VSUSERCONTEXTATTRIBUTEUSAGE.VSUC_Usage_Filter,
                    szNameAuxUserContext: "",
                    szValueAuxUserContext: "",
                    InitViewFlags: 0,
                    pInitView: initView);

                // Associate buffer with the code window
                Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(
                    codeWindow.SetBuffer((IVsTextLines)bufferAdapter));

                // Get the primary text view (has its own HWND — keyboard input works natively)
                IVsTextView vsTextView;
                Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(
                    codeWindow.GetPrimaryView(out vsTextView));

                // Get the WPF host from the VS text view
                var textViewHost = editorAdapterFactory.GetWpfTextViewHost(vsTextView);
                var textView = textViewHost.TextView;

                // Configure editor display options — keep cells compact
                textView.Options.SetOptionValue(DefaultTextViewOptions.WordWrapStyleId, WordWrapStyles.None);
                textView.Options.SetOptionValue(DefaultTextViewHostOptions.LineNumberMarginId, false);
                textView.Options.SetOptionValue(DefaultTextViewHostOptions.GlyphMarginId, false);
                textView.Options.SetOptionValue(DefaultTextViewHostOptions.HorizontalScrollBarId, false);
                textView.Options.SetOptionValue(DefaultTextViewHostOptions.VerticalScrollBarId, false);
                textView.Options.SetOptionValue(DefaultTextViewHostOptions.ZoomControlId, false);
                textView.Options.SetOptionValue(DefaultTextViewHostOptions.SelectionMarginId, false);
                textView.Options.SetOptionValue(DefaultTextViewHostOptions.ChangeTrackingId, false);

                // Collapse the bottom margin container (status bar showing Ln/Ch/encoding/zoom)
                var bottomMargin = textViewHost.GetTextViewMargin("bottom") as IWpfTextViewMargin;
                if (bottomMargin?.VisualElement != null)
                    bottomMargin.VisualElement.Visibility = Visibility.Collapsed;

                // Collapse the left margin container (indicator/selection/line-number gutters)
                var leftMargin = textViewHost.GetTextViewMargin("left") as IWpfTextViewMargin;
                if (leftMargin?.VisualElement != null)
                    leftMargin.VisualElement.Visibility = Visibility.Collapsed;

                // Store references for cleanup and command forwarding
                _textViewHost = textViewHost;
                _vsTextView = vsTextView;
                _codeWindow = codeWindow;
                TextView = textView;

                var hostControl = textViewHost.HostControl;
                hostControl.MinHeight = minH;
                hostControl.MaxHeight = maxH;

                // Dynamically resize editor based on content line count.
                // Deferred via Dispatcher to avoid layout reentrancy during LayoutChanged.
                textView.LayoutChanged += (s, e) =>
                {
                    int lineCount = textView.TextSnapshot.LineCount;
                    double desired = Math.Ceiling(lineCount * lineHeight + editorVerticalPadding);
                    desired = Math.Max(minH, Math.Min(desired, maxH));

                    if (Math.Abs(hostControl.Height - desired) > 0.5)
                    {
#pragma warning disable VSTHRD110, VSTHRD001 // Dispatcher.BeginInvoke is intentionally fire-and-forget
                        hostControl.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            hostControl.Height = desired;

                            // Enable vertical scrollbar only when content overflows max height
                            bool hasOverflow = desired >= maxH;
                            textView.Options.SetOptionValue(DefaultTextViewHostOptions.VerticalScrollBarId, hasOverflow);
                        }), System.Windows.Threading.DispatcherPriority.Render);
#pragma warning restore VSTHRD110, VSTHRD001
                    }
                };

                // Set initial height based on content
                int initialLineCount = textView.TextSnapshot.LineCount;
                double initialHeight = Math.Ceiling(initialLineCount * lineHeight + editorVerticalPadding);
                hostControl.Height = Math.Max(minH, Math.Min(initialHeight, maxH));

                // Enable vertical scrollbar only when content overflows at initial load
                bool initialOverflow = hostControl.Height >= maxH;
                textView.Options.SetOptionValue(DefaultTextViewHostOptions.VerticalScrollBarId, initialOverflow);

                // Forward mouse wheel to notebook ScrollViewer only when cell has no overflow.
                // When content overflows (at max height), let the native editor scroll internally.
                textViewHost.HostControl.PreviewMouseWheel += (s, e) =>
                {
                    bool hasOverflow = hostControl.Height >= maxH;
                    if (!hasOverflow)
                    {
                        var scrollViewer = FindParentScrollViewer(textViewHost.HostControl);
                        if (scrollViewer != null)
                        {
                            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
                            e.Handled = true;
                        }
                    }
                    // When hasOverflow, don't handle — let the native editor scroll
                };

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
                    _codeWindow?.Close();
                    _codeWindow = null;
                    _vsTextView?.CloseView();
                    _vsTextView = null;
                };

                // The IVsCodeWindow may internally parent the HostControl.
                // Disconnect it before re-parenting to our Grid.
                if (hostControl.Parent is System.Windows.Controls.Panel parentPanel)
                    parentPanel.Children.Remove(hostControl);
                else if (hostControl.Parent is System.Windows.Controls.ContentControl parentCC)
                    parentCC.Content = null;
                else if (hostControl.Parent is System.Windows.Controls.Decorator parentDec)
                    parentDec.Child = null;

                Grid.SetRow(hostControl, 1);
                grid.Children.Add(hostControl);

                var output = new OutputControl { Cell = cell };
                Grid.SetRow(output, 2);
                grid.Children.Add(output);
            }
            catch (Exception ex)
            {
                ExtensionLogger.LogException(nameof(CellControl),
                    $"Failed to create IVsCodeWindow for cell (kernel: {cell.KernelName})", ex);
                ActivityLog.LogError(nameof(CellControl),
                    $"IVsCodeWindow creation failed: {ex.Message}. Falling back to TextBox.");

                BuildCodeCellFallback(grid, cell, toolbar, fontFamily, fontSize, minH, maxH);
            }
        }

        private static IContentType ResolveContentType(IContentTypeRegistryService registry, string? kernelName)
        {
            if (kernelName != null && _kernelContentTypeMap.TryGetValue(kernelName, out var vsContentType))
            {
                var ct = registry.GetContentType(vsContentType);
                if (ct != null)
                {
                    return ct;
                }

                ExtensionLogger.LogWarning(nameof(CellControl),
                    $"Content type '{vsContentType}' not found for kernel '{kernelName}'; falling back to 'text'");
            }

            var fallback = registry.GetContentType("text") ?? registry.GetContentType("plaintext");
            ExtensionLogger.LogInfo(nameof(CellControl),
                $"Using fallback content type '{fallback?.TypeName ?? "null"}' for kernel '{kernelName ?? "null"}'");
            return fallback;
        }

        /// <summary>
        /// Maps a kernel name to a fake file path with the right extension,
        /// used to associate an <see cref="ITextDocument"/> with the buffer
        /// so that language services (Roslyn, etc.) engage properly.
        /// Uses a full temp path so Roslyn's MiscellaneousFilesWorkspace picks it up.
        /// </summary>
        private static int _fakeFileCounter;
        private static string GetFakeFileName(string? kernelName)
        {
            var id = System.Threading.Interlocked.Increment(ref _fakeFileCounter);
            string ext;
            switch (kernelName?.ToLowerInvariant())
            {
                case "csharp": ext = ".cs"; break;
                case "fsharp": ext = ".fs"; break;
                case "javascript": ext = ".js"; break;
                case "typescript": ext = ".ts"; break;
                case "python": ext = ".py"; break;
                case "powershell": ext = ".ps1"; break;
                case "sql": ext = ".sql"; break;
                case "html": ext = ".html"; break;
                case "markdown": ext = ".md"; break;
                default: ext = ".txt"; break;
            }
            var dir = Path.Combine(Path.GetTempPath(), "PolyglotNotebooks");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"cell_{id}{ext}");
        }

        /// <summary>
        /// Retrieves the global OLE IServiceProvider, required by VS adapter factory methods.
        /// </summary>
        private static Microsoft.VisualStudio.OLE.Interop.IServiceProvider GetOleServiceProvider()
        {
            var objWithSite = (Microsoft.VisualStudio.OLE.Interop.IObjectWithSite)
                Microsoft.VisualStudio.Shell.ServiceProvider.GlobalProvider;
            Guid interfaceIID = typeof(Microsoft.VisualStudio.OLE.Interop.IServiceProvider).GUID;
            IntPtr rawSP;
            objWithSite.GetSite(ref interfaceIID, out rawSP);
            try
            {
                return (Microsoft.VisualStudio.OLE.Interop.IServiceProvider)
                    Marshal.GetObjectForIUnknown(rawSP);
            }
            finally
            {
                if (rawSP != IntPtr.Zero) Marshal.Release(rawSP);
            }
        }

        /// <summary>
        /// Returns the VS language service GUID for a given kernel name.
        /// This enables COM-level classifiers/colorizers for the buffer.
        /// </summary>
        private static Guid GetLanguageServiceGuid(string kernelName)
        {
            switch (kernelName?.ToLowerInvariant())
            {
                case "csharp": return new Guid("a6c744a8-0e4a-4fc6-886a-064283054674"); // Roslyn C#
                case "fsharp": return new Guid("BC6DD5A5-D4D6-4dab-A00D-A51242DBAF1B"); // F#
                case "javascript": return new Guid("71d61d27-9011-4b17-9469-d20f798fb5c0"); // JavaScript
                case "html": return new Guid("58e975a0-f8fe-11d2-a6ae-00104bcc7269"); // HTML
                case "sql": return new Guid("fa6e5f20-7e40-11d1-b60e-00a0c9083275"); // T-SQL
                default: return Guid.Empty;
            }
        }

        /// <summary>
        /// Fallback code cell construction using a plain TextBox.
        /// Used when IVsCodeWindow creation fails at runtime.
        /// </summary>
        private void BuildCodeCellFallback(Grid grid, NotebookCell cell, CellToolbar toolbar,
            FontFamily fontFamily, double fontSize, double minH, double maxH)
        {
            var editor = new TextBox
            {
                FontFamily = fontFamily,
                FontSize = fontSize,
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.NoWrap,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MinHeight = minH,
                MaxHeight = maxH,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(4),
                IsUndoEnabled = true,
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

            editor.TextChanged += (s, e) =>
            {
                ParseMagicCommand(editor, cell);
                AdjustEditorHeight(editor);
            };

            _editor = editor;

            toolbar.RunSelectionRequested += (s, e) =>
            {
                var selected = editor.SelectedText;
                if (!string.IsNullOrEmpty(selected))
                    RunSelectionRequested?.Invoke(this, new RunSelectionEventArgs(selected));
            };

            Grid.SetRow(editor, 1);
            grid.Children.Add(editor);

            var output = new OutputControl { Cell = cell };
            Grid.SetRow(output, 2);
            grid.Children.Add(output);
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
            if (_isMarkdownEditing || _editor == null) return;
            _isMarkdownEditing = true;
            _markdownDisplay.Visibility = Visibility.Collapsed;
            _editor.Visibility = Visibility.Visible;
            AdjustEditorHeight(_editor);
            _editor.Focus();
            _editor.CaretIndex = _editor.Text?.Length ?? 0;
        }

        private void ExitMarkdownEditMode()
        {
            if (!_isMarkdownEditing || _editor == null) return;
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
            int lastEnd = 0;

            foreach (Match m in _inlineFormattingRegex.Matches(text))
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

        private static ScrollViewer FindParentScrollViewer(DependencyObject child)
        {
            var parent = VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is ScrollViewer sv)
                    return sv;
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
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
