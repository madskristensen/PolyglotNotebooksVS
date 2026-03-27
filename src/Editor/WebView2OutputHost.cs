using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PolyglotNotebooks.Editor
{
    /// <summary>
    /// Hosts a WebView2 control for rich HTML output rendering.
    /// Handles lazy initialization, VS theme injection, content auto-resize, and graceful
    /// fallback when the WebView2 runtime is not installed.
    /// </summary>
    internal sealed class WebView2OutputHost : Border, IDisposable
    {
        private WebView2? _webView;

        // Raw content to render — stored so it can be applied once init completes.
        private string? _pendingContent;
        private bool _pendingIsFullDocument;

        private bool _isInitialized;
        private bool _initFailed;
        private bool _disposed;

        private const double DefaultHeight = 200;
        private const double MaxContentHeight = 480; // inner cap; outer scroll wrapper adds its own cap

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

        // -----------------------------------------------------------------------
        // Initialisation
        // -----------------------------------------------------------------------

        private void InitializeWebView()
        {
            try
            {
                _webView = new WebView2
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
                }
                catch { /* non-critical — default height remains */ }
            }).FileAndForget(nameof(WebView2OutputHost));
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
                    _webView.NavigationCompleted -= OnNavigationCompleted;
                _webView.Dispose();
                _webView = null;
            }
        }
    }
}
