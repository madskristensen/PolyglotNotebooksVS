using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

using System.ComponentModel.Composition;

namespace PolyglotNotebooks.Editor.SyntaxHighlighting
{
    /// <summary>
    /// MEF-exported <see cref="IClassifierProvider"/> that creates a
    /// <see cref="NotebookClassifier"/> for buffers carrying the
    /// <c>"PolyglotNotebook.KernelName"</c> property.  Registered for the
    /// "text" content type so it fires regardless of which language content
    /// type VS assigns to the hosted <see cref="IVsCodeWindow"/> buffer.
    /// Using IClassifierProvider instead of ITaggerProvider as it engages
    /// more reliably for embedded code windows.
    /// </summary>
    [Export(typeof(IClassifierProvider))]
    [ContentType("text")]
    internal sealed class NotebookClassifierProvider : IClassifierProvider
    {
        [Import]
        internal IClassificationTypeRegistryService ClassificationRegistry { get; set; }

        public IClassifier GetClassifier(ITextBuffer buffer)
        {
            if (buffer == null)
                return null;

            if (!buffer.Properties.ContainsProperty("PolyglotNotebook.KernelName"))
                return null;

            return buffer.Properties.GetOrCreateSingletonProperty(
                () => new NotebookClassifier(buffer, ClassificationRegistry));
        }
    }
}
