using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Net;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PolyglotNotebooks.Editor
{
    /// <summary>
    /// Hosts a WebView2CompositionControl for rich HTML output rendering.
    /// Uses composition-based rendering (Direct3D into WPF visual tree) to avoid
    /// HWND airspace issues with scrollbars, popups, and context menus.
    /// Handles lazy initialization, VS theme injection, content auto-resize, and graceful
    /// fallback when the WebView2 runtime is not installed.
    /// </summary>
    internal sealed class WebView2OutputHost : Border, IDisposable
    {
        private WebView2CompositionControl? _webView;

        // Raw content to render — stored so it can be applied once init completes.
        private string? _pendingContent;
        private bool _pendingIsFullDocument;

        private bool _isInitialized;
        private bool _initFailed;
        private bool _disposed;

        private const double DefaultHeight = 200;
        private const double MaxContentHeight = 800; // inner cap; outer scroll wrapper adds its own cap

        public WebView2OutputHost()
        {
            Height = DefaultHeight;
        }

        // -----------------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------------

        /// <summary>
        /// Renders <paramref name="htmlContent"/> inside a VS-themed HTML shell.
        /// If <paramref name="htmlContent"/> is already a full HTML document it is used as-is.
        /// Initialises WebView2 lazily on first call.
        /// </summary>
        public void SetHtmlContent(string htmlContent)
        {
            if (_initFailed) return;

            _pendingIsFullDocument = IsFullHtmlDocument(htmlContent);
            _pendingContent = htmlContent;

            if (_webView == null)
                InitializeWebView();
            else if (_isInitialized)
                NavigateToContent();
        }

        /// <summary>
        /// Renders a Mermaid diagram inside a VS-themed HTML shell with mermaid.js.
        /// Automatically selects dark/light theme based on the current VS theme.
        /// Shows the raw source with an error message if the diagram is invalid.
        /// </summary>
        public void SetMermaidContent(string mermaidCode)
        {
            if (_initFailed) return;

            _pendingContent = BuildMermaidHtml(mermaidCode);
            _pendingIsFullDocument = true;

            if (_webView == null)
                InitializeWebView();
            else if (_isInitialized)
                NavigateToContent();
        }

        // -----------------------------------------------------------------------
        // Initialisation
        // -----------------------------------------------------------------------

        private void InitializeWebView()
        {
            try
            {
                _webView = new WebView2CompositionControl
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                };
                Child = _webView;

                _webView.CoreWebView2InitializationCompleted += OnWebViewInitialized;

                ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    try
                    {
                        string userDataFolder = System.IO.Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "PolyglotNotebooksVS", "WebView2Cache");

                        var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                        await _webView.EnsureCoreWebView2Async(env);
                    }
                    catch
                    {
                        ShowFallback();
                        _initFailed = true;
                    }
                }).FileAndForget(nameof(WebView2OutputHost));
            }
            catch
            {
                ShowFallback();
                _initFailed = true;
            }
        }

        private void OnWebViewInitialized(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
            {
                ShowFallback();
                _initFailed = true;
                return;
            }

            _isInitialized = true;
            _webView!.NavigationCompleted += OnNavigationCompleted;
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            if (_pendingContent != null)
                NavigateToContent();
        }

        // -----------------------------------------------------------------------
        // Navigation & auto-resize
        // -----------------------------------------------------------------------

        private void NavigateToContent()
        {
            if (_webView == null || !_isInitialized || _pendingContent == null) return;

            string html = _pendingIsFullDocument
                ? _pendingContent
                : BuildThemedHtml(_pendingContent);

            _webView.NavigateToString(html);
        }

        private void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                try
                {
                    if (_webView?.CoreWebView2 == null) return;

                    // Query the actual rendered content height and resize the host.
                    string result = await _webView.CoreWebView2.ExecuteScriptAsync(
                        "document.body ? document.body.scrollHeight : 0");

                    if (double.TryParse(result, out double contentHeight) && contentHeight > 0)
                        Height = Math.Min(contentHeight + 20, MaxContentHeight);

                    // Inject overflow-aware wheel forwarding: only forward to notebook
                    // when the WebView content can't scroll further in that direction.
                    await _webView.CoreWebView2.ExecuteScriptAsync(@"
                        if (!window.__polyglotWheelHooked) {
                            window.__polyglotWheelHooked = true;
                            document.addEventListener('wheel', function(e) {
                                var el = document.documentElement;
                                var canScrollDown = el.scrollTop + el.clientHeight < el.scrollHeight;
                                var canScrollUp = el.scrollTop > 0;
                                var scrollingDown = e.deltaY > 0;
                                var scrollingUp = e.deltaY < 0;

                                if ((scrollingDown && canScrollDown) || (scrollingUp && canScrollUp)) {
                                    return;
                                }
                                window.chrome.webview.postMessage(JSON.stringify({ type: 'scroll', deltaY: e.deltaY }));
                                e.preventDefault();
                            }, { passive: false });
                        }");
                }
                catch { /* non-critical — default height remains */ }
            }).FileAndForget(nameof(WebView2OutputHost));
        }

        private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var json = e.WebMessageAsJson;
                if (json == null) return;

                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("type", out var typeEl))
                {
                    string msgType = typeEl.GetString();
                    if (msgType == "scroll"
                        && doc.RootElement.TryGetProperty("deltaY", out var deltaEl))
                    {
                        double deltaY = deltaEl.GetDouble();
                        var scrollViewer = FindParentScrollViewer(this);
                        if (scrollViewer != null)
                            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + deltaY);
                    }
                    else if (msgType == "resize"
                        && doc.RootElement.TryGetProperty("height", out var heightEl))
                    {
                        double h = heightEl.GetDouble();
                        if (h > 0)
                            Height = Math.Min(h + 20, MaxContentHeight);
                    }
                }
            }
            catch { /* non-critical */ }
        }

        // -----------------------------------------------------------------------
        // HTML construction with VS theme colours
        // -----------------------------------------------------------------------

        private string BuildThemedHtml(string bodyContent)
        {
            string bg = GetCssColor(VsBrushes.ToolWindowBackgroundKey, "#1e1e1e");
            string fg = GetCssColor(VsBrushes.ToolWindowTextKey, "#d4d4d4");
            string border = GetCssColor(VsBrushes.ToolWindowBorderKey, "#3f3f46");

            return $@"<!DOCTYPE html>
<html><head><meta charset='utf-8'>
<style>
  * {{ box-sizing: border-box; }}
  body {{
    background: {bg};
    color: {fg};
    font-family: 'Segoe UI', Arial, sans-serif;
    font-size: 13px;
    margin: 8px;
    padding: 0;
    word-wrap: break-word;
  }}
  pre, code {{
    font-family: Consolas, 'Courier New', monospace;
    font-size: 12px;
  }}
  pre {{
    background: rgba(128,128,128,0.12);
    border: 1px solid {border};
    padding: 8px;
    border-radius: 3px;
    overflow-x: auto;
    white-space: pre-wrap;
  }}
  table {{
    border-collapse: collapse;
    width: 100%;
    margin: 4px 0;
  }}
  th, td {{
    border: 1px solid {border};
    padding: 4px 8px;
    text-align: left;
  }}
  th {{
    background: rgba(128,128,128,0.15);
    font-weight: 600;
  }}
  tr:nth-child(even) td {{ background: rgba(128,128,128,0.05); }}
  img {{ max-width: 100%; }}
  h1, h2, h3, h4, h5, h6 {{ margin: 8px 0 4px 0; }}
  ul, ol {{ padding-left: 20px; }}
  a {{ color: #4ec9b0; }}
</style>
</head><body>
{bodyContent}
</body></html>";
        }

        private static string GetCssColor(object vsResourceKey, string fallback)
        {
            try
            {
                var brush = Application.Current?.TryFindResource(vsResourceKey) as SolidColorBrush;
                if (brush != null)
                {
                    var c = brush.Color;
                    return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                }
            }
            catch { }
            return fallback;
        }

        // -----------------------------------------------------------------------
        // Mermaid HTML construction
        // -----------------------------------------------------------------------

        private string BuildMermaidHtml(string mermaidCode)
        {
            string bg = GetCssColor(VsBrushes.ToolWindowBackgroundKey, "#1e1e1e");
            string fg = GetCssColor(VsBrushes.ToolWindowTextKey, "#d4d4d4");
            bool isDark = IsDarkBackground();
            string mermaidTheme = isDark ? "dark" : "default";
            string errorColor = isDark ? "#f48771" : "#d32f2f";
            string escapedContent = WebUtility.HtmlEncode(mermaidCode);

            string themeVarsJs = isDark
                ? $@",
        themeVariables: {{
            primaryColor: '#2d2d2d',
            primaryTextColor: '{fg}',
            primaryBorderColor: '#555',
            lineColor: '#888',
            secondaryColor: '#252525',
            tertiaryColor: '#333'
        }}"
                : "";

            return $@"<!DOCTYPE html>
<html><head><meta charset='utf-8'>
<script src=""https://cdn.jsdelivr.net/npm/mermaid/dist/mermaid.min.js""></script>
<style>
  body {{ margin: 0; padding: 8px; background: {bg}; color: {fg}; }}
  .mermaid-output {{ text-align: center; }}
  .mermaid-error {{
    font-family: Consolas, 'Courier New', monospace;
    font-size: 12px;
    color: {errorColor};
    padding: 8px;
    white-space: pre-wrap;
    border: 1px solid {errorColor};
    border-radius: 3px;
    margin: 4px 0;
  }}
  .mermaid-source {{
    font-family: Consolas, 'Courier New', monospace;
    font-size: 12px;
    color: {fg};
    padding: 8px;
    white-space: pre-wrap;
    opacity: 0.7;
  }}
  .mermaid-output svg {{ background: transparent !important; }}
</style>
</head><body>
<pre id=""mermaid-source"" style=""display:none"">{escapedContent}</pre>
<div id=""mermaid-container"" class=""mermaid-output""></div>
<script>
  mermaid.initialize({{
    startOnLoad: false,
    theme: '{mermaidTheme}',
    securityLevel: 'strict'{themeVarsJs}
  }});

  (async function() {{
    var source = document.getElementById('mermaid-source').textContent;
    var container = document.getElementById('mermaid-container');
    try {{
      var result = await mermaid.render('mermaid-svg', source);
      container.innerHTML = result.svg;
      await new Promise(r => setTimeout(r, 100));
      window.chrome.webview.postMessage(JSON.stringify({{ type: 'resize', height: document.body.scrollHeight }}));
    }} catch (err) {{
      var safeMsg = (err.message || String(err)).replace(/</g, '&lt;').replace(/>/g, '&gt;');
      var safeSource = source.replace(/</g, '&lt;').replace(/>/g, '&gt;');
      container.innerHTML =
        '<div class=""mermaid-error"">\u26a0 Mermaid rendering error: ' + safeMsg + '</div>' +
        '<div class=""mermaid-source"">' + safeSource + '</div>';
    }}
  }})();
</script>
</body></html>";
        }

        private static bool IsDarkBackground()
        {
            try
            {
                var brush = Application.Current?.TryFindResource(VsBrushes.ToolWindowBackgroundKey) as SolidColorBrush;
                if (brush != null)
                {
                    var c = brush.Color;
                    double luminance = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
                    return luminance < 0.5;
                }
            }
            catch { }
            return true; // default to dark
        }

        // -----------------------------------------------------------------------
        // Fallback
        // -----------------------------------------------------------------------

        private void ShowFallback()
        {
            var panel = new StackPanel { Orientation = Orientation.Vertical };

            var tb = new TextBlock
            {
                Text = "HTML output — WebView2 runtime not available.",
                TextWrapping = TextWrapping.Wrap,
                Padding = new Thickness(4),
                FontStyle = FontStyles.Italic,
                FontSize = 12,
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.GrayTextKey);

            var link = new TextBlock
            {
                Text = "Install WebView2 runtime: https://go.microsoft.com/fwlink/p/?LinkId=2124703",
                TextWrapping = TextWrapping.Wrap,
                Padding = new Thickness(4, 0, 4, 4),
                FontSize = 11,
            };
            link.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.GrayTextKey);

            panel.Children.Add(tb);
            panel.Children.Add(link);
            Child = panel;
            Height = double.NaN; // auto-size to content
        }

        // -----------------------------------------------------------------------
        // Utilities
        // -----------------------------------------------------------------------

        private static bool IsFullHtmlDocument(string html)
        {
            var trimmed = html.TrimStart();
            return trimmed.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
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

        // -----------------------------------------------------------------------
        // IDisposable
        // -----------------------------------------------------------------------

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_webView != null)
            {
                _webView.CoreWebView2InitializationCompleted -= OnWebViewInitialized;
                if (_isInitialized)
                {
                    _webView.NavigationCompleted -= OnNavigationCompleted;
                    if (_webView.CoreWebView2 != null)
                        _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
                }
                _webView.Dispose();
                _webView = null;
            }
        }
    }
}
