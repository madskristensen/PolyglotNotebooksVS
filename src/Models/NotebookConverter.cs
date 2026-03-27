namespace PolyglotNotebooks.Models
{
    /// <summary>
    /// Utility methods for converting between .dib and .ipynb notebook formats.
    /// Both formats are supported by <see cref="NotebookParser"/>; these helpers
    /// operate purely on string content without touching the file system.
    /// </summary>
    public static class NotebookConverter
    {
        /// <summary>
        /// Converts .dib (dotnet-interactive code submission) content to .ipynb JSON.
        /// </summary>
        /// <param name="dibContent">Raw .dib file content.</param>
        /// <param name="defaultKernelName">
        ///   The kernel name to embed in the Jupyter kernelspec metadata.
        ///   Defaults to <c>"csharp"</c>.
        /// </param>
        /// <returns>Jupyter notebook JSON string.</returns>
        public static string ConvertDibToIpynb(string dibContent, string defaultKernelName = "csharp")
        {
            var doc = NotebookParser.ParseDib(dibContent, string.Empty);
            // Override default kernel if explicitly specified by the caller.
            if (!string.IsNullOrEmpty(defaultKernelName))
                doc.DefaultKernelName = defaultKernelName;
            return NotebookParser.SerializeIpynb(doc);
        }

        /// <summary>
        /// Converts .ipynb JSON content to .dib (dotnet-interactive code submission) format.
        /// </summary>
        /// <param name="ipynbJson">Raw .ipynb JSON string.</param>
        /// <returns>.dib content string.</returns>
        public static string ConvertIpynbToDib(string ipynbJson)
        {
            var doc = NotebookParser.ParseIpynb(ipynbJson, string.Empty);
            return NotebookParser.SerializeDib(doc);
        }
    }
}
