using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.Shell;
using PolyglotNotebooks.Diagnostics;
using PolyglotNotebooks.Models;
using PolyglotNotebooks.Protocol;

namespace PolyglotNotebooks.IntelliSense
{
    internal sealed class DiagnosticsProvider : IDisposable
    {
        private readonly TextBox _textBox;
        private readonly NotebookCell _cell;
        private readonly DispatcherTimer _debounceTimer;
        private KernelClient? _kernelClient;
        private CancellationTokenSource? _requestCts;
        private DiagnosticAdorner? _adorner;
        private List<DiagnosticInfo> _diagnosticInfos = new List<DiagnosticInfo>();
        private bool _disposed;

        public DiagnosticsProvider(TextBox textBox, NotebookCell cell)
        {
            _textBox = textBox ?? throw new ArgumentNullException(nameof(textBox));
            _cell = cell ?? throw new ArgumentNullException(nameof(cell));

            _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _debounceTimer.Tick += OnDebounceTimerTick;

            _textBox.TextChanged += OnTextChanged;
            _textBox.MouseMove += OnMouseMove;

            if (_textBox.IsLoaded)
                SetupAdorner();
            else
                _textBox.Loaded += OnTextBoxLoaded;
        }

        public void SetKernelClient(KernelClient? client)
        {
            _kernelClient = client;
        }

        private void OnTextBoxLoaded(object sender, RoutedEventArgs e)
        {
            _textBox.Loaded -= OnTextBoxLoaded;
            SetupAdorner();
        }

        private void SetupAdorner()
        {
            var layer = AdornerLayer.GetAdornerLayer(_textBox);
            if (layer != null)
            {
                _adorner = new DiagnosticAdorner(_textBox);
                layer.Add(_adorner);
            }
        }

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_kernelClient == null) return;

            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        private void OnDebounceTimerTick(object? sender, EventArgs e)
        {
            _debounceTimer.Stop();
            RequestDiagnostics();
        }

        private void RequestDiagnostics()
        {
            if (_kernelClient == null) return;

            var text = _textBox.Text;
            if (string.IsNullOrEmpty(text))
            {
                ClearDiagnostics();
                return;
            }

            _requestCts?.Cancel();
            _requestCts = new CancellationTokenSource();
            var ct = _requestCts.Token;
            var client = _kernelClient;
            var kernelName = _cell.KernelName;

#pragma warning disable VSTHRD110, VSSDK007
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    var result = await client.RequestDiagnosticsAsync(text, kernelName, ct).ConfigureAwait(false);
                    if (ct.IsCancellationRequested) return;

                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);
                    ApplyDiagnostics(text, result.Diagnostics);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    ExtensionLogger.LogException(nameof(DiagnosticsProvider),
                        "Error requesting diagnostics", ex);
                }
            });
#pragma warning restore VSTHRD110, VSSDK007
        }

        private void ApplyDiagnostics(string text, List<Protocol.Diagnostic> diagnostics)
        {
            _diagnosticInfos.Clear();

            if (diagnostics != null)
            {
                foreach (var diag in diagnostics)
                {
                    int start = GetCharOffset(text, diag.LinePositionSpan.Start.Line, diag.LinePositionSpan.Start.Character);
                    int end = GetCharOffset(text, diag.LinePositionSpan.End.Line, diag.LinePositionSpan.End.Character);
                    _diagnosticInfos.Add(new DiagnosticInfo
                    {
                        StartOffset = start,
                        EndOffset = end,
                        Severity = diag.Severity,
                        Message = diag.Message
                    });
                }
            }

            if (_adorner != null)
            {
                _adorner.SetDiagnostics(_diagnosticInfos);
                _adorner.InvalidateVisual();
            }
        }

        private void ClearDiagnostics()
        {
            _diagnosticInfos.Clear();
            if (_adorner != null)
            {
                _adorner.SetDiagnostics(_diagnosticInfos);
                _adorner.InvalidateVisual();
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_diagnosticInfos.Count == 0)
            {
                _textBox.ToolTip = null;
                return;
            }

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

            if (charIndex < 0) return;

            var matchedDiag = _diagnosticInfos.FirstOrDefault(
                d => charIndex >= d.StartOffset && charIndex < d.EndOffset);

            _textBox.ToolTip = matchedDiag != null
                ? $"[{matchedDiag.Severity}] {matchedDiag.Message}"
                : null;
        }

        private static int GetCharOffset(string text, int line, int character)
        {
            int currentLine = 0;
            int i = 0;
            while (i < text.Length && currentLine < line)
            {
                if (text[i] == '\n') currentLine++;
                i++;
            }
            return Math.Min(i + character, text.Length);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _debounceTimer.Stop();
            _debounceTimer.Tick -= OnDebounceTimerTick;

            _textBox.TextChanged -= OnTextChanged;
            _textBox.MouseMove -= OnMouseMove;
            _textBox.Loaded -= OnTextBoxLoaded;

            _requestCts?.Cancel();
            _requestCts?.Dispose();

            if (_adorner != null)
            {
                var layer = AdornerLayer.GetAdornerLayer(_textBox);
                if (layer != null)
                {
                    try { layer.Remove(_adorner); } catch { }
                }
            }

            _diagnosticInfos.Clear();
        }

        internal class DiagnosticInfo
        {
            public int StartOffset { get; set; }
            public int EndOffset { get; set; }
            public string Severity { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
        }

        private sealed class DiagnosticAdorner : Adorner
        {
            private readonly TextBox _textBox;
            private List<DiagnosticInfo> _diagnostics = new List<DiagnosticInfo>();
            private static readonly Pen ErrorPen = CreatePen(Colors.Red);
            private static readonly Pen WarningPen = CreatePen(Color.FromRgb(255, 215, 0)); // Gold

            public DiagnosticAdorner(TextBox textBox) : base(textBox)
            {
                _textBox = textBox;
                IsHitTestVisible = false;
            }

            public void SetDiagnostics(List<DiagnosticInfo> diagnostics)
            {
                _diagnostics = diagnostics ?? new List<DiagnosticInfo>();
            }

            private static Pen CreatePen(Color color)
            {
                var pen = new Pen(new SolidColorBrush(color), 1.2);
                pen.Freeze();
                return pen;
            }

            protected override void OnRender(DrawingContext drawingContext)
            {
                foreach (var diag in _diagnostics)
                {
                    var pen = string.Equals(diag.Severity, "error", StringComparison.OrdinalIgnoreCase)
                        ? ErrorPen : WarningPen;

                    DrawSquiggly(drawingContext, diag.StartOffset, diag.EndOffset, pen);
                }
            }

            private void DrawSquiggly(DrawingContext dc, int startOffset, int endOffset, Pen pen)
            {
                if (startOffset >= endOffset) return;

                var text = _textBox.Text;
                if (startOffset >= text.Length) return;
                endOffset = Math.Min(endOffset, text.Length);

                Rect startRect, endRect;
                try
                {
                    startRect = _textBox.GetRectFromCharacterIndex(startOffset, false);
                    endRect = _textBox.GetRectFromCharacterIndex(endOffset > 0 ? endOffset - 1 : 0, true);
                }
                catch
                {
                    return;
                }

                if (startRect.IsEmpty || endRect.IsEmpty) return;

                double y = startRect.Bottom;
                double x1 = startRect.Left;
                double x2 = endRect.Right;
                if (x2 <= x1) x2 = x1 + 6;

                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    ctx.BeginFigure(new Point(x1, y), false, false);
                    double waveHeight = 1.5;
                    double waveLength = 3;
                    bool up = true;
                    for (double x = x1 + waveLength; x <= x2; x += waveLength)
                    {
                        ctx.LineTo(new Point(x, y + (up ? -waveHeight : waveHeight)), true, false);
                        up = !up;
                    }
                }
                geometry.Freeze();
                dc.DrawGeometry(null, pen, geometry);
            }
        }
    }
}
