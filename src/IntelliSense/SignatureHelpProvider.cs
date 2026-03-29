using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.VisualStudio.Shell;
using PolyglotNotebooks.Diagnostics;
using PolyglotNotebooks.Protocol;

namespace PolyglotNotebooks.IntelliSense
{
    internal sealed class SignatureHelpProvider : IDisposable
    {
        private readonly TextBox _textBox;
        private readonly DispatcherTimer _debounceTimer;
        private readonly Popup _popup;
        private readonly TextBlock _signatureBlock;
        private KernelClient? _kernelClient;
        private CancellationTokenSource? _requestCts;
        private bool _disposed;

        public SignatureHelpProvider(TextBox textBox)
        {
            _textBox = textBox ?? throw new ArgumentNullException(nameof(textBox));

            _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _debounceTimer.Tick += OnDebounceTimerTick;

            _signatureBlock = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 600,
                Padding = new Thickness(6, 4, 6, 4),
                FontFamily = new System.Windows.Media.FontFamily("Consolas, Courier New"),
                FontSize = 12
            };
            _signatureBlock.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);

            _popup = new Popup
            {
                StaysOpen = true,
                AllowsTransparency = true,
                PlacementTarget = _textBox,
                Placement = PlacementMode.RelativePoint,
                IsOpen = false,
                Child = new Border
                {
                    BorderThickness = new Thickness(1),
                    Child = _signatureBlock
                }
            };
            var popupBorder = (Border)_popup.Child;
            popupBorder.SetResourceReference(Border.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
            popupBorder.SetResourceReference(Border.BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);

            _textBox.PreviewTextInput += OnPreviewTextInput;
            _textBox.PreviewKeyDown += OnPreviewKeyDown;
        }

        public void SetKernelClient(KernelClient? client)
        {
            _kernelClient = client;
        }

        private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (_kernelClient == null) return;

            if (e.Text == "(" || e.Text == ",")
            {
                _debounceTimer.Stop();
                _debounceTimer.Start();
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_popup.IsOpen) return;

            if (e.Key == Key.Escape)
            {
                ClosePopup();
                e.Handled = true;
            }
            else if (e.Key == Key.OemCloseBrackets ||
                     (e.Key == Key.D9 && (Keyboard.Modifiers & ModifierKeys.Shift) != 0))
            {
                // ')' key pressed — close signature help
                ClosePopup();
            }
        }

        private void OnDebounceTimerTick(object? sender, EventArgs e)
        {
            _debounceTimer.Stop();
            RequestSignatureHelp();
        }

        private void RequestSignatureHelp()
        {
            if (_kernelClient == null) return;

            var text = _textBox.Text;
            var caretIndex = _textBox.CaretIndex;
            if (string.IsNullOrEmpty(text) || caretIndex < 0) return;

            var position = CaretToLinePosition(text, caretIndex);

            _requestCts?.Cancel();
            _requestCts = new CancellationTokenSource();
            var ct = _requestCts.Token;
            var client = _kernelClient;

#pragma warning disable VSTHRD110, VSSDK007
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    var result = await client.RequestSignatureHelpAsync(text, position, ct).ConfigureAwait(false);
                    if (ct.IsCancellationRequested) return;

                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);
                    ShowSignatureHelp(result);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    ExtensionLogger.LogException(nameof(SignatureHelpProvider),
                        "Error requesting signature help", ex);
                }
            });
#pragma warning restore VSTHRD110, VSSDK007
        }

        private void ShowSignatureHelp(SignatureHelpProduced result)
        {
            if (result.Signatures == null || result.Signatures.Count == 0)
            {
                ClosePopup();
                return;
            }

            var sigIndex = Math.Max(0, Math.Min(result.ActiveSignatureIndex, result.Signatures.Count - 1));
            var sig = result.Signatures[sigIndex];
            var activeParam = result.ActiveParameterIndex;

            _signatureBlock.Inlines.Clear();

            // Build the signature display with the active parameter in bold
            if (sig.Parameters != null && sig.Parameters.Count > 0)
            {
                var label = sig.Label;
                int parenOpen = label.IndexOf('(');
                if (parenOpen >= 0)
                {
                    _signatureBlock.Inlines.Add(new Run(label.Substring(0, parenOpen + 1)));

                    for (int i = 0; i < sig.Parameters.Count; i++)
                    {
                        if (i > 0)
                            _signatureBlock.Inlines.Add(new Run(", "));

                        var paramLabel = sig.Parameters[i].Label;
                        if (i == activeParam)
                        {
                            _signatureBlock.Inlines.Add(new Run(paramLabel) { FontWeight = FontWeights.Bold });
                        }
                        else
                        {
                            _signatureBlock.Inlines.Add(new Run(paramLabel));
                        }
                    }

                    int parenClose = label.LastIndexOf(')');
                    if (parenClose >= 0)
                        _signatureBlock.Inlines.Add(new Run(label.Substring(parenClose)));
                    else
                        _signatureBlock.Inlines.Add(new Run(")"));
                }
                else
                {
                    _signatureBlock.Inlines.Add(new Run(label));
                }
            }
            else
            {
                _signatureBlock.Inlines.Add(new Run(sig.Label));
            }

            // Add documentation on a new line if available
            if (sig.Documentation != null && !string.IsNullOrWhiteSpace(sig.Documentation.Value))
            {
                _signatureBlock.Inlines.Add(new LineBreak());
                _signatureBlock.Inlines.Add(new Run(sig.Documentation.Value) { FontStyle = FontStyles.Italic });
            }

            try
            {
                var caretIndex = _textBox.CaretIndex;
                if (caretIndex >= 0 && caretIndex <= _textBox.Text.Length)
                {
                    var rect = _textBox.GetRectFromCharacterIndex(caretIndex, false);
                    _popup.HorizontalOffset = rect.Left;
                    _popup.VerticalOffset = rect.Top - 24;
                }
            }
            catch { /* layout may not be ready */ }

            _popup.IsOpen = true;
        }

        private void ClosePopup()
        {
            _popup.IsOpen = false;
        }

        internal static LinePosition CaretToLinePosition(string text, int caretIndex)
        {
            int line = 0, character = 0;
            for (int i = 0; i < caretIndex && i < text.Length; i++)
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

            _textBox.PreviewTextInput -= OnPreviewTextInput;
            _textBox.PreviewKeyDown -= OnPreviewKeyDown;

            _requestCts?.Cancel();
            _requestCts?.Dispose();

            ClosePopup();
        }
    }
}
