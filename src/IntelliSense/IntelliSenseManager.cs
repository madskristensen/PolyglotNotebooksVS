using System;
using System.Collections.Generic;
using PolyglotNotebooks.Editor;
using PolyglotNotebooks.Protocol;

namespace PolyglotNotebooks.IntelliSense
{
    internal sealed class IntelliSenseManager : IDisposable
    {
        private KernelClient? _kernelClient;
        private readonly Dictionary<CellControl, CellProviders> _attached = new Dictionary<CellControl, CellProviders>();
        private bool _disposed;

        public void SetKernelClient(KernelClient client)
        {
            _kernelClient = client;
            foreach (var kvp in _attached)
            {
                kvp.Value.Completion.SetKernelClient(client);
                kvp.Value.Hover.SetKernelClient(client);
                kvp.Value.SignatureHelp.SetKernelClient(client);
                kvp.Value.Diagnostics.SetKernelClient(client);
            }
        }

        public void AttachToCell(CellControl cell)
        {
            if (cell == null || _disposed) return;
            if (_attached.ContainsKey(cell)) return;

            var editor = cell.CodeEditor;
            var providers = new CellProviders
            {
                Completion = new CompletionProvider(editor),
                Hover = new HoverProvider(editor),
                SignatureHelp = new SignatureHelpProvider(editor),
                Diagnostics = new DiagnosticsProvider(editor, cell.Cell)
            };

            if (_kernelClient != null)
            {
                providers.Completion.SetKernelClient(_kernelClient);
                providers.Hover.SetKernelClient(_kernelClient);
                providers.SignatureHelp.SetKernelClient(_kernelClient);
                providers.Diagnostics.SetKernelClient(_kernelClient);
            }

            _attached[cell] = providers;
        }

        public void DetachFromCell(CellControl cell)
        {
            if (cell == null) return;
            if (_attached.TryGetValue(cell, out var providers))
            {
                providers.Completion.Dispose();
                providers.Hover.Dispose();
                providers.SignatureHelp.Dispose();
                providers.Diagnostics.Dispose();
                _attached.Remove(cell);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var kvp in _attached)
            {
                kvp.Value.Completion.Dispose();
                kvp.Value.Hover.Dispose();
                kvp.Value.SignatureHelp.Dispose();
                kvp.Value.Diagnostics.Dispose();
            }
            _attached.Clear();
        }

        private class CellProviders
        {
            public CompletionProvider Completion { get; set; } = null!;
            public HoverProvider Hover { get; set; } = null!;
            public SignatureHelpProvider SignatureHelp { get; set; } = null!;
            public DiagnosticsProvider Diagnostics { get; set; } = null!;
        }
    }
}
