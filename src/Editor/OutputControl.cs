using Microsoft.VisualStudio.Shell;
using PolyglotNotebooks.Models;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PolyglotNotebooks.Editor
{
    /// <summary>
    /// Renders the output(s) of a single notebook cell.
    /// Routes each output by MIME type and supports expand/collapse.
    /// Supported MIME types: text/plain, text/html, text/markdown, text/csv,
    /// application/json, image/png, image/jpeg, image/gif, image/svg+xml.
    /// </summary>
    internal sealed class OutputControl : Border
    {
        private readonly StackPanel _root;
        private NotebookCell? _cell;
        private bool _isExpanded = true;

        // The live container that holds per-output panels (may be null when collapsed).
        private StackPanel? _outputContainer;

        // Tracks WebView2OutputHost instances so we can dispose them on rebuild.
        private readonly List<IDisposable> _disposables = new List<IDisposable>();

        public OutputControl()
        {
            _root = new StackPanel { Orientation = Orientation.Vertical };
            Child = _root;
            Visibility = Visibility.Collapsed;

            this.Unloaded += (s, e) =>
            {
                if (_cell != null)
                    _cell.Outputs.CollectionChanged -= OnOutputsChanged;

                DisposeTrackedControls();
            };
        }

        public NotebookCell? Cell
        {
            get => _cell;
            set
            {
                if (_cell != null)
                    _cell.Outputs.CollectionChanged -= OnOutputsChanged;

                _cell = value;

                if (_cell != null)
                    _cell.Outputs.CollectionChanged += OnOutputsChanged;

                Rebuild();
            }
        }

        // -----------------------------------------------------------------------
        // Collection change handling
        // -----------------------------------------------------------------------

        private void OnOutputsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // For Replace (e.g. DisplayedValueUpdated live-updating a chart), update only
            // the affected slot to avoid destroying and re-initialising WebView2 instances.
            if (e.Action == NotifyCollectionChangedAction.Replace
                && _outputContainer != null
                && e.NewStartingIndex >= 0
                && e.NewStartingIndex < _outputContainer.Children.Count
                && e.NewItems?.Count == 1)
            {
                ReplaceOutputAt(e.NewStartingIndex, (CellOutput)e.NewItems[0]!);
            }
            else
            {
                Rebuild();
            }
        }

        private void ReplaceOutputAt(int index, CellOutput newOutput)
        {
            if (_outputContainer == null)
            {
                Rebuild();
                return;
            }

            // Dispose any WebView2OutputHost sitting at this slot.
            var oldElement = _outputContainer.Children[index];
            if (oldElement is IDisposable d)
            {
                _disposables.Remove(d);
                d.Dispose();
            }

            var newElement = RenderOutput(newOutput);
            _outputContainer.Children.RemoveAt(index);
            _outputContainer.Children.Insert(index, newElement);
        }

        // -----------------------------------------------------------------------
        // Full rebuild
        // -----------------------------------------------------------------------

        private void Rebuild()
        {
            DisposeTrackedControls();
            _root.Children.Clear();
            _outputContainer = null;

            if (_cell == null || _cell.Outputs.Count == 0)
            {
                Visibility = Visibility.Collapsed;
                return;
            }

            Visibility = Visibility.Visible;

            // Collapse/expand header
            var headerPanel = new DockPanel
            {
                LastChildFill = false,
                Margin = new Thickness(0, 4, 0, 0)
            };

            var toggleBtn = new Button
            {
                Content = _isExpanded ? "▼ Output" : "▶ Output",
                Padding = new Thickness(4, 1, 4, 1),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Toggle output visibility"
            };
            toggleBtn.SetResourceReference(Button.ForegroundProperty, VsBrushes.GrayTextKey);
            toggleBtn.Click += (s, e) =>
            {
                _isExpanded = !_isExpanded;
                toggleBtn.Content = _isExpanded ? "▼ Output" : "▶ Output";
                Rebuild();
            };
            DockPanel.SetDock(toggleBtn, Dock.Left);
            headerPanel.Children.Add(toggleBtn);
            _root.Children.Add(headerPanel);

            if (!_isExpanded)
                return;

            _outputContainer = new StackPanel { Orientation = Orientation.Vertical };
            _outputContainer.SetResourceReference(StackPanel.BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);

            foreach (var output in _cell.Outputs)
                _outputContainer.Children.Add(RenderOutput(output));

            // Cap total output height at 500px; add a scrollbar beyond that.
            var scroll = new ScrollViewer
            {
                MaxHeight = 500,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _outputContainer,
            };
            _root.Children.Add(scroll);
        }

        // -----------------------------------------------------------------------
        // Per-output rendering
        // -----------------------------------------------------------------------

        private UIElement RenderOutput(CellOutput output)
        {
            bool isError = output.Kind == CellOutputKind.Error
                        || output.Kind == CellOutputKind.StandardError;

            var container = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0, 2, 0, 2)
            };

            if (output.FormattedValues.Count == 0)
            {
                var empty = new TextBlock
                {
                    Text = $"[{output.Kind}]",
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    Padding = new Thickness(4),
                    FontStyle = FontStyles.Italic,
                };
                empty.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.GrayTextKey);
                container.Children.Add(empty);
                return container;
            }

            foreach (var formatted in output.FormattedValues)
            {
                if (formatted.SuppressDisplay)
                    continue;

                container.Children.Add(CreateElementForMimeType(formatted.MimeType, formatted.Value, isError));
            }

            return container;
        }

        private UIElement CreateElementForMimeType(string mimeType, string value, bool isError)
        {
            // All image/* types are handled by ImageOutputControl (supports raster + SVG).
            if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return ImageOutputControl.Create(mimeType, value);

            switch (mimeType.ToLowerInvariant())
            {
                case "text/html":
                {
                    var host = new WebView2OutputHost();
                    host.SetHtmlContent(value);
                    _disposables.Add(host);
                    return host;
                }

                case "text/markdown":
                {
                    var host = new WebView2OutputHost();
                    host.SetHtmlContent(MarkdownToHtml(value));
                    _disposables.Add(host);
                    return host;
                }

                case "application/json":
                    return RenderJson(value);

                case "text/csv":
                {
                    var host = new WebView2OutputHost();
                    host.SetHtmlContent(CsvToHtmlTable(value));
                    _disposables.Add(host);
                    return host;
                }

                default:
                    return RenderText(value, isError);
            }
        }

        // -----------------------------------------------------------------------
        // MIME-specific renderers
        // -----------------------------------------------------------------------

        private static UIElement RenderText(string text, bool isError)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Consolas, Courier New"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Padding = new Thickness(4),
            };

            if (isError)
                tb.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.VizSurfaceRedMediumKey);
            else
                tb.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);

            return tb;
        }

        private static UIElement RenderJson(string json)
        {
            string formatted = json;
            try
            {
                var doc = System.Text.Json.JsonDocument.Parse(json);
                formatted = System.Text.Json.JsonSerializer.Serialize(
                    doc.RootElement,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
            catch { /* leave raw json if parsing fails */ }

            var tb = new TextBlock
            {
                Text = formatted,
                FontFamily = new FontFamily("Consolas, Courier New"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Padding = new Thickness(4),
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            return tb;
        }

        // -----------------------------------------------------------------------
        // Markdown → HTML (lightweight converter for common constructs)
        // -----------------------------------------------------------------------

        private static string MarkdownToHtml(string markdown)
        {
            var sb = new StringBuilder();
            var lines = markdown.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            bool inCodeBlock = false;
            bool inList = false;
            string codeFence = string.Empty;

            foreach (var line in lines)
            {
                // Code fence detection (``` or ~~~)
                if (!inCodeBlock && (line.StartsWith("```") || line.StartsWith("~~~")))
                {
                    if (inList) { sb.AppendLine("</ul>"); inList = false; }
                    inCodeBlock = true;
                    codeFence = line.StartsWith("```") ? "```" : "~~~";
                    sb.AppendLine("<pre><code>");
                    continue;
                }
                if (inCodeBlock)
                {
                    if (line.StartsWith(codeFence)) { sb.AppendLine("</code></pre>"); inCodeBlock = false; }
                    else sb.AppendLine(WebUtility.HtmlEncode(line));
                    continue;
                }

                // Close list when encountering a non-list line
                if (inList && !line.StartsWith("- ") && !line.StartsWith("* ") && !line.StartsWith("+ "))
                {
                    sb.AppendLine("</ul>");
                    inList = false;
                }

                // ATX headings
                if (line.StartsWith("### ")) { sb.AppendLine($"<h3>{InlineMarkdown(line.Substring(4))}</h3>"); continue; }
                if (line.StartsWith("## "))  { sb.AppendLine($"<h2>{InlineMarkdown(line.Substring(3))}</h2>"); continue; }
                if (line.StartsWith("# "))   { sb.AppendLine($"<h1>{InlineMarkdown(line.Substring(2))}</h1>"); continue; }

                // Unordered lists
                if (line.StartsWith("- ") || line.StartsWith("* ") || line.StartsWith("+ "))
                {
                    if (!inList) { sb.AppendLine("<ul>"); inList = true; }
                    sb.AppendLine($"<li>{InlineMarkdown(line.Substring(2))}</li>");
                    continue;
                }

                // Blank line
                if (string.IsNullOrWhiteSpace(line)) { sb.AppendLine("<br>"); continue; }

                sb.AppendLine($"<p>{InlineMarkdown(line)}</p>");
            }

            if (inCodeBlock) sb.AppendLine("</code></pre>");
            if (inList)      sb.AppendLine("</ul>");

            return sb.ToString();
        }

        private static string InlineMarkdown(string text)
        {
            // HTML-encode first to avoid injections, then apply markdown patterns.
            text = WebUtility.HtmlEncode(text);
            text = Regex.Replace(text, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
            text = Regex.Replace(text, @"__(.+?)__",     "<strong>$1</strong>");
            text = Regex.Replace(text, @"\*(.+?)\*",     "<em>$1</em>");
            text = Regex.Replace(text, @"_(.+?)_",       "<em>$1</em>");
            text = Regex.Replace(text, @"`(.+?)`",        "<code>$1</code>");
            text = Regex.Replace(text, @"\[(.+?)\]\((.+?)\)", "<a href='$2'>$1</a>");
            return text;
        }

        // -----------------------------------------------------------------------
        // CSV → HTML table
        // -----------------------------------------------------------------------

        private static string CsvToHtmlTable(string csv)
        {
            var lines = csv.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return "<p><em>Empty CSV</em></p>";

            var sb = new StringBuilder();
            sb.AppendLine("<table>");

            for (int i = 0; i < lines.Length; i++)
            {
                var cells = ParseCsvLine(lines[i]);
                sb.Append(i == 0 ? "<thead><tr>" : "<tr>");
                string tag = i == 0 ? "th" : "td";
                foreach (var cell in cells)
                    sb.Append($"<{tag}>{WebUtility.HtmlEncode(cell)}</{tag}>");
                sb.AppendLine(i == 0 ? "</tr></thead><tbody>" : "</tr>");
            }

            sb.AppendLine("</tbody></table>");
            return sb.ToString();
        }

        private static string[] ParseCsvLine(string line)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    { current.Append('"'); i++; }
                    else
                        inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                { result.Add(current.ToString()); current.Clear(); }
                else
                    current.Append(c);
            }
            result.Add(current.ToString());
            return result.ToArray();
        }

        // -----------------------------------------------------------------------
        // Disposal
        // -----------------------------------------------------------------------

        private void DisposeTrackedControls()
        {
            foreach (var d in _disposables)
            {
                try { d.Dispose(); }
                catch { }
            }
            _disposables.Clear();
        }
    }
}

