using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace PolyglotNotebooks.Models
{
    public class DocumentOpenedEventArgs : EventArgs
    {
        public DocumentOpenedEventArgs(NotebookDocument document) => Document = document;
        public NotebookDocument Document { get; }
    }

    public class DocumentClosedEventArgs : EventArgs
    {
        public DocumentClosedEventArgs(string filePath) => FilePath = filePath;
        public string FilePath { get; }
    }

    public class DocumentDirtyChangedEventArgs : EventArgs
    {
        public DocumentDirtyChangedEventArgs(NotebookDocument document, bool isDirty)
        {
            Document = document;
            IsDirty = isDirty;
        }
        public NotebookDocument Document { get; }
        public bool IsDirty { get; }
    }

    public class NotebookDocumentManager
    {
        private readonly Dictionary<string, NotebookDocument> _openDocuments
            = new Dictionary<string, NotebookDocument>(StringComparer.OrdinalIgnoreCase);

        public event EventHandler<DocumentOpenedEventArgs>? DocumentOpened;
        public event EventHandler<DocumentClosedEventArgs>? DocumentClosed;
        public event EventHandler<DocumentDirtyChangedEventArgs>? DocumentDirtyChanged;

        public IReadOnlyDictionary<string, NotebookDocument> OpenDocuments => _openDocuments;

        public async Task<NotebookDocument> OpenAsync(string filePath)
        {
            string normalized = NormalizePath(filePath);

            if (_openDocuments.TryGetValue(normalized, out var existing))
                return existing;

            var doc = await Task.Run(() => NotebookDocument.Load(normalized)).ConfigureAwait(false);
            TrackDocument(normalized, doc);
            DocumentOpened?.Invoke(this, new DocumentOpenedEventArgs(doc));
            return doc;
        }

        public Task CloseAsync(string filePath)
        {
            string normalized = NormalizePath(filePath);

            if (_openDocuments.TryGetValue(normalized, out var doc))
            {
                UntrackDocument(normalized, doc);
                _openDocuments.Remove(normalized);
                DocumentClosed?.Invoke(this, new DocumentClosedEventArgs(normalized));
            }

            return Task.CompletedTask;
        }

        public NotebookDocument? GetDocument(string filePath)
        {
            _openDocuments.TryGetValue(NormalizePath(filePath), out var doc);
            return doc;
        }

        public bool IsOpen(string filePath)
            => _openDocuments.ContainsKey(NormalizePath(filePath));

        private void TrackDocument(string key, NotebookDocument doc)
        {
            _openDocuments[key] = doc;
            doc.PropertyChanged += OnDocumentPropertyChanged;
        }

        private void UntrackDocument(string key, NotebookDocument doc)
        {
            doc.PropertyChanged -= OnDocumentPropertyChanged;
        }

        private void OnDocumentPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(NotebookDocument.IsDirty) && sender is NotebookDocument doc)
                DocumentDirtyChanged?.Invoke(this, new DocumentDirtyChangedEventArgs(doc, doc.IsDirty));
        }

        /// <summary>
        /// Directly registers an already-loaded document without loading from disk.
        /// Used by the editor factory after a rename to re-register under a new path.
        /// </summary>
        public void RegisterDocument(string filePath, NotebookDocument document)
        {
            string normalized = NormalizePath(filePath);
            TrackDocument(normalized, document);
            DocumentOpened?.Invoke(this, new DocumentOpenedEventArgs(document));
        }

        private static string NormalizePath(string filePath)
            => Path.GetFullPath(filePath);
    }
}
