using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.Shell;
using PolyglotNotebooks.Diagnostics;
using PolyglotNotebooks.Protocol;

namespace PolyglotNotebooks.IntelliSense
{
    internal sealed class CompletionProvider : IDisposable
    {
        private readonly TextBox _textBox;
        private readonly DispatcherTimer _debounceTimer;
        private readonly Popup _popup;
        private readonly ListBox _listBox;
        private KernelClient? _kernelClient;
        private CancellationTokenSource? _requestCts;
        private List<CompletionItem> _currentItems = new List<CompletionItem>();
        private string _filterPrefix = string.Empty;
        private int _filterStart;
        private bool _disposed;

        public CompletionProvider(TextBox textBox)
        {
            _textBox = textBox ?? throw new ArgumentNullException(nameof(textBox));

            _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _debounceTimer.Tick += OnDebounceTimerTick;

            _listBox = new ListBox
            {
                MaxHeight = 200,
                MinWidth = 200,
                MaxWidth = 500,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0)
            };
            _listBox.SetResourceReference(Control.BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
            _listBox.SetResourceReference(Control.ForegroundProperty, VsBrushes.ToolWindowTextKey);

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
                    Child = _listBox
                }
            };
            var popupBorder = (Border)_popup.Child;
            popupBorder.SetResourceReference(Border.BorderBrushProperty, VsBrushes.ToolWindowBorderKey);
            popupBorder.SetResourceReference(Border.BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);

            _popup.Closed += (s, e) =>
            {
                _requestCts?.Cancel();
            };

            _textBox.TextChanged += OnTextChanged;
            _textBox.PreviewKeyDown += OnPreviewKeyDown;
            _textBox.LostFocus += OnLostFocus;
        }

        public void SetKernelClient(KernelClient? client)
        {
            _kernelClient = client;
        }

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_kernelClient == null) return;

            var text = _textBox.Text;
            var caretIndex = _textBox.CaretIndex;

            if (caretIndex <= 0 || caretIndex > text.Length)
            {
                ClosePopup();
                return;
            }

            char charBefore = text[caretIndex - 1];
            if (charBefore == '.' || (!char.IsWhiteSpace(charBefore) && !char.IsControl(charBefore)))
            {
                _debounceTimer.Stop();
                _debounceTimer.Start();
            }
            else
            {
                ClosePopup();
            }
        }

        private void OnDebounceTimerTick(object? sender, EventArgs e)
        {
            _debounceTimer.Stop();
            RequestCompletions();
        }

        private void RequestCompletions()
        {
            if (_kernelClient == null) return;

            var text = _textBox.Text;
            var caretIndex = _textBox.CaretIndex;
            if (string.IsNullOrEmpty(text) || caretIndex < 0) return;

            var position = CaretToLinePosition(text, caretIndex);
            _filterStart = FindWordStart(text, caretIndex);
            _filterPrefix = text.Substring(_filterStart, caretIndex - _filterStart);

            _requestCts?.Cancel();
            _requestCts = new CancellationTokenSource();
            var ct = _requestCts.Token;
            var client = _kernelClient;

#pragma warning disable VSTHRD110, VSSDK007
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    var result = await client.RequestCompletionsAsync(text, position, ct).ConfigureAwait(false);
                    if (ct.IsCancellationRequested) return;

                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);
                    ShowCompletions(result.Completions);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    ExtensionLogger.LogException(nameof(CompletionProvider),
                        "Error requesting completions", ex);
                }
            });
#pragma warning restore VSTHRD110, VSSDK007
        }

        private void ShowCompletions(List<CompletionItem> completions)
        {
            if (completions == null || completions.Count == 0)
            {
                ClosePopup();
                return;
            }

            _currentItems = completions;
            _listBox.Items.Clear();

            foreach (var item in completions)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                var kindText = new TextBlock
                {
                    Text = GetKindGlyph(item.Kind),
                    Width = 18,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center
                };
                kindText.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
                sp.Children.Add(kindText);

                var displayText = new TextBlock
                {
                    Text = item.DisplayText,
                    VerticalAlignment = VerticalAlignment.Center
                };
                displayText.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
                sp.Children.Add(displayText);

                var lbi = new ListBoxItem
                {
                    Content = sp,
                    Padding = new Thickness(4, 2, 4, 2),
                    ToolTip = string.IsNullOrEmpty(item.Documentation) ? null : item.Documentation
                };
                _listBox.Items.Add(lbi);
            }

            _listBox.SelectedIndex = 0;

            try
            {
                var caretIndex = _textBox.CaretIndex;
                if (caretIndex >= 0 && caretIndex <= _textBox.Text.Length)
                {
                    var rect = _textBox.GetRectFromCharacterIndex(caretIndex, false);
                    _popup.HorizontalOffset = rect.Left;
                    _popup.VerticalOffset = rect.Bottom + 2;
                }
            }
            catch { /* GetRectFromCharacterIndex can fail if layout not ready */ }

            _popup.IsOpen = true;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_popup.IsOpen) return;

            switch (e.Key)
            {
                case Key.Down:
                    if (_listBox.SelectedIndex < _listBox.Items.Count - 1)
                        _listBox.SelectedIndex++;
                    e.Handled = true;
                    break;

                case Key.Up:
                    if (_listBox.SelectedIndex > 0)
                        _listBox.SelectedIndex--;
                    e.Handled = true;
                    break;

                case Key.Enter:
                case Key.Tab:
                    AcceptCompletion();
                    e.Handled = true;
                    break;

                case Key.Escape:
                    ClosePopup();
                    e.Handled = true;
                    break;
            }
        }

        private void AcceptCompletion()
        {
            if (_listBox.SelectedIndex < 0 || _listBox.SelectedIndex >= _currentItems.Count)
            {
                ClosePopup();
                return;
            }

            var item = _currentItems[_listBox.SelectedIndex];
            var insertText = item.InsertText ?? item.DisplayText;

            var text = _textBox.Text;
            var caretIndex = _textBox.CaretIndex;

            // Replace the filter prefix with the insert text
            var newText = text.Substring(0, _filterStart) + insertText + text.Substring(caretIndex);
            _textBox.Text = newText;
            _textBox.CaretIndex = _filterStart + insertText.Length;

            ClosePopup();
        }

        private void ClosePopup()
        {
            _popup.IsOpen = false;
            _currentItems.Clear();
        }

        private void OnLostFocus(object sender, RoutedEventArgs e)
        {
            // Small delay to allow click on popup item
#pragma warning disable VSTHRD110, VSTHRD001
            _textBox.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (!_textBox.IsFocused && !_listBox.IsKeyboardFocusWithin)
                    ClosePopup();
            }));
#pragma warning restore VSTHRD110, VSTHRD001
        }

        private static string GetKindGlyph(string? kind)
        {
            if (string.IsNullOrEmpty(kind)) return "·";
            switch (kind!.ToLowerInvariant())
            {
                case "method": return "M";
                case "property": return "P";
                case "field": return "F";
                case "class": return "C";
                case "interface": return "I";
                case "struct": return "S";
                case "enum": return "E";
                case "keyword": return "K";
                case "variable": return "V";
                case "namespace": return "N";
                case "event": return "Ev";
                default: return "·";
            }
        }

        private static LinePosition CaretToLinePosition(string text, int caretIndex)
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

        private static int FindWordStart(string text, int caretIndex)
        {
            int i = caretIndex - 1;
            while (i >= 0 && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                i--;
            return i + 1;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _debounceTimer.Stop();
            _debounceTimer.Tick -= OnDebounceTimerTick;

            _textBox.TextChanged -= OnTextChanged;
            _textBox.PreviewKeyDown -= OnPreviewKeyDown;
            _textBox.LostFocus -= OnLostFocus;

            _requestCts?.Cancel();
            _requestCts?.Dispose();

            ClosePopup();
        }
    }
}
