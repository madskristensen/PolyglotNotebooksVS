using System.ComponentModel.Composition;

using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;

using PolyglotNotebooks.Diagnostics;

namespace PolyglotNotebooks.Editor.SyntaxHighlighting
{
    /// <summary>
    /// MEF-exported <see cref="ITaggerProvider"/> that creates a
    /// <see cref="NotebookClassifier"/> for buffers that carry the
    /// <c>"PolyglotNotebook.KernelName"</c> property.  Registered for the
    /// "text" content type so it fires regardless of which language content
    /// type VS assigns to the hosted <see cref="IVsCodeWindow"/> buffer.
    /// </summary>
    [Export(typeof(ITaggerProvider))]
    [ContentType("text")]
    [TagType(typeof(ClassificationTag))]
    internal sealed class NotebookClassifierProvider : ITaggerProvider
    {
        [Import]
        internal IClassificationTypeRegistryService ClassificationRegistry { get; set; }

        public ITagger<T> CreateTagger<T>(ITextBuffer buffer) where T : ITag
        {
            ExtensionLogger.LogInfo("NotebookClassifierProvider",
                $"CreateTagger called for buffer with content type '{buffer?.ContentType.TypeName}'");

            if (buffer == null || typeof(T) != typeof(ClassificationTag))
                return null;

            ExtensionLogger.LogInfo("NotebookClassifierProvider",
                $"Buffer has PolyglotNotebook.KernelName property: {buffer.Properties.ContainsProperty("PolyglotNotebook.KernelName")}");

            if (!buffer.Properties.ContainsProperty("PolyglotNotebook.KernelName"))
                return null;

            return buffer.Properties.GetOrCreateSingletonProperty(
                () => new NotebookClassifier(buffer, ClassificationRegistry)) as ITagger<T>;
        }
    }
}
