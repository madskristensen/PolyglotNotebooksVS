using System.Collections.Generic;
using System.IO;

namespace PolyglotNotebooks.Editor
{
    /// <summary>
    /// Maps Polyglot Notebooks kernel names to VS content type names and file extensions.
    /// Extracted from CellControl so the mappings can be unit-tested independently.
    /// </summary>
    internal static class KernelLanguageMap
    {
        private static readonly Dictionary<string, string> _contentTypeMap =
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["csharp"] = "CSharp",
                ["fsharp"] = "F#",
                ["javascript"] = "JavaScript",
                ["typescript"] = "TypeScript",
                ["python"] = "Python",
                ["powershell"] = "PowerShell",
                ["sql"] = "SQL Server Tools",
                ["kql"] = "plaintext",
                ["html"] = "HTML",
                ["markdown"] = "markdown",
                ["mermaid"] = "markdown"
            };

        private static readonly Dictionary<string, string> _extensionMap =
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["csharp"] = ".cs",
                ["fsharp"] = ".fs",
                ["javascript"] = ".js",
                ["typescript"] = ".ts",
                ["python"] = ".py",
                ["powershell"] = ".ps1",
                ["sql"] = ".sql",
                ["kql"] = ".kql",
                ["html"] = ".html",
                ["markdown"] = ".md",
                ["mermaid"] = ".md"
            };

        /// <summary>
        /// Returns the VS content type name for a given kernel name, or null if unknown.
        /// Lookup is case-insensitive.
        /// </summary>
        internal static string? GetContentTypeName(string kernelName)
        {
            return _contentTypeMap.TryGetValue(kernelName, out var ct) ? ct : null;
        }

        /// <summary>
        /// Returns the file extension (including leading dot) for a given kernel name.
        /// Falls back to ".txt" for unknown/null kernels.
        /// </summary>
        internal static string GetFileExtension(string? kernelName)
        {
            if (kernelName != null && _extensionMap.TryGetValue(kernelName, out var ext))
                return ext;
            return ".txt";
        }

        private static int _fakeFileCounter;

        /// <summary>
        /// Generates a unique fake file path under the temp directory with the correct
        /// extension for the given kernel, so VS language services engage properly.
        /// </summary>
        internal static string GetFakeFileName(string? kernelName)
        {
            var id = System.Threading.Interlocked.Increment(ref _fakeFileCounter);
            var ext = GetFileExtension(kernelName);
            var dir = Path.Combine(Path.GetTempPath(), "PolyglotNotebooks");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"cell_{id}{ext}");
        }
    }
}
