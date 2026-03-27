using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.VisualStudio.Shell;
using PolyglotNotebooks.Diagnostics;
using PolyglotNotebooks.Protocol;

namespace PolyglotNotebooks.IntelliSense
{
    internal sealed class HoverProvider : IDisposable
    {
        private readonly TextBox _textBox;
        private readonly DispatcherTimer _debounceTimer;
        private readonly Popup _popup;
        private readonly TextBlock _contentBlock;
        private KernelClient? _kernelClient;
        private CancellationTokenSource? _requestCts;
        private int _lastHoverCharIndex = -1;
        private bool _disposed;

        public HoverProvider(TextBox textBox)
        {
            _textBox = textBox ?? throw new ArgumentNullException(nameof(textBox));

            _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _debounceTimer.Tick += OnDebounceTimerTick;

            _contentBlock = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 500,
                Padding = new Thickness(6, 4, 6, 4),
                FontFamily = new System.Windows.Media.FontFamily("Consolas, Courier New"),
                FontSize = 12
            };
            _contentBlock.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);

            _popup = new Popup
            {
                StaysOpen = false,
                AllowsTransparency = true,
                PlacementTarget = _textBox,
                Placement = PlacementMode.RelativePoint,
                IsOpen = false,
                Child = new Border
                {
                    BorderThickness = new Thickness(1),
                    Child = _contentBlock
                }
            };
            var popupBorder = (Border)_popup.Child;
            popupBorder.SetResourceReference(Border.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
            popupBorder.SetResourceReference(Border.BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);

            _textBox.MouseMove += OnMouseMove;
            _textBox.MouseLeave += OnMouseLeave;
        }

        public void SetKernelClient(KernelClient? client)
        {
            _kernelClient = client;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_kernelClient == null) return;

            var point = e.GetPosition(_textBox);
            int charIndex;
            try
            {
                charIndex = _textBox.GetCharacterIndexFromPoint(point, true);
            }
            catch
            {
                return;
            }

            if (charIndex < 0 || charIndex == _lastHoverCharIndex) return;
            _lastHoverCharIndex = charIndex;

            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        private void OnMouseLeave(object sender, MouseEventArgs e)
        {
            _debounceTimer.Stop();
            _lastHoverCharIndex = -1;
            ClosePopup();
        }

        private void OnDebounceTimerTick(object? sender, EventArgs e)
        {
            _debounceTimer.Stop();
            RequestHover();
        }

        private void RequestHover()
        {
            if (_kernelClient == null || _lastHoverCharIndex < 0) return;

            var text = _textBox.Text;
            if (string.IsNullOrEmpty(text) || _lastHoverCharIndex >= text.Length) return;

            var position = CaretToLinePosition(text, _lastHoverCharIndex);

            _requestCts?.Cancel();
            _requestCts = new CancellationTokenSource();
            var ct = _requestCts.Token;
            var client = _kernelClient;

#pragma warning disable VSTHRD110, VSSDK007
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    var result = await client.RequestHoverTextAsync(text, position, ct).ConfigureAwait(false);
                    if (ct.IsCancellationRequested) return;

                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);
                    ShowHover(result);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    ExtensionLogger.LogException(nameof(HoverProvider),
                        "Error requesting hover text", ex);
                }
            });
#pragma warning restore VSTHRD110, VSSDK007
        }

        private void ShowHover(HoverTextProduced result)
        {
            if (result.Content == null || result.Content.Count == 0)
            {
                ClosePopup();
                return;
            }

            // Prefer plain text, fall back to stripping HTML
            var plainContent = result.Content.FirstOrDefault(c => c.MimeType == "text/plain");
            string displayText;
            if (plainContent != null)
            {
                displayText = plainContent.Value;
            }
            else
            {
                var htmlContent = result.Content.FirstOrDefault(c => c.MimeType == "text/html");
                displayText = htmlContent != null ? StripHtml(htmlContent.Value) : result.Content[0].Value;
            }

            if (string.IsNullOrWhiteSpace(displayText))
            {
                ClosePopup();
                return;
            }

            _contentBlock.Text = displayText;

            try
            {
                if (_lastHoverCharIndex >= 0 && _lastHoverCharIndex <= _textBox.Text.Length)
                {
                    var rect = _textBox.GetRectFromCharacterIndex(_lastHoverCharIndex, false);
                    _popup.HorizontalOffset = rect.Left;
                    _popup.VerticalOffset = rect.Top - 20;
                }
            }
            catch { /* layout may not be ready */ }

            _popup.IsOpen = true;
        }

        private void ClosePopup()
        {
            _popup.IsOpen = false;
        }

        private static string StripHtml(string html)
        {
            return Regex.Replace(html, "<[^>]+>", string.Empty).Trim();
        }

        private static LinePosition CaretToLinePosition(string text, int charIndex)
        {
            int line = 0, character = 0;
            for (int i = 0; i < charIndex && i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                    character = 0;
                }
                else if (text[i] != '\r')
                {
                    character++;
                }
            }
            return new LinePosition { Line = line, Character = character };
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _debounceTimer.Stop();
            _debounceTimer.Tick -= OnDebounceTimerTick;

            _textBox.MouseMove -= OnMouseMove;
            _textBox.MouseLeave -= OnMouseLeave;

            _requestCts?.Cancel();
            _requestCts?.Dispose();

            ClosePopup();
        }
    }
}
