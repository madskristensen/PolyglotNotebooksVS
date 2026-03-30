using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace PolyglotNotebooks.Models
{
    /// <summary>
    /// Provides export methods to convert a <see cref="NotebookDocument"/> into
    /// standalone HTML, Markdown, C# script (.csx), or F# script (.fsx) formats.
    /// All methods are pure functions that return the exported content as a string.
    /// </summary>
    public static class NotebookExporter
    {
        // Matches ANSI escape sequences (e.g. \x1b[32;1m, \x1b[0m) so they can be stripped from exported output.
        private static readonly Regex AnsiEscapePattern =
            new Regex(@"\x1b\[[0-9;]*[a-zA-Z]", RegexOptions.Compiled);
        /// <summary>
        /// Exports the notebook as a standalone HTML document with inline CSS.
        /// Markdown cells are rendered as escaped text blocks, code cells are
        /// wrapped in syntax-highlighted <c>&lt;pre&gt;&lt;code&gt;</c> blocks,
        /// and text/plain outputs are included beneath each code cell.
        /// </summary>
        public static string ExportToHtml(NotebookDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\">");
            sb.AppendLine("<head>");
            sb.AppendLine("<meta charset=\"utf-8\" />");
            sb.AppendLine($"<title>{WebUtility.HtmlEncode(document.FileName)}</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; max-width: 960px; margin: 0 auto; padding: 2rem; line-height: 1.6; color: #1e1e1e; }");
            sb.AppendLine(".cell { margin-bottom: 1.5rem; }");
            sb.AppendLine(".markdown-cell { }");
            sb.AppendLine(".code-cell pre { background: #f5f5f5; border: 1px solid #e0e0e0; border-radius: 4px; padding: 1rem; overflow-x: auto; }");
            sb.AppendLine(".code-cell code { font-family: 'Cascadia Code', 'Consolas', 'Courier New', monospace; font-size: 0.9rem; }");
            sb.AppendLine(".cell-language { color: #6a737d; font-size: 0.75rem; margin-bottom: 0.25rem; }");
            sb.AppendLine(".cell-output { background: #fffbe6; border-left: 3px solid #f0c000; padding: 0.75rem 1rem; margin-top: 0.5rem; white-space: pre-wrap; font-family: 'Cascadia Code', 'Consolas', monospace; font-size: 0.85rem; }");
            sb.AppendLine(".cell-output-error { background: #fff0f0; border-left-color: #e00; }");
            sb.AppendLine("</style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");

            foreach (var cell in document.Cells)
            {
                sb.AppendLine("<div class=\"cell\">");

                if (cell.Kind == CellKind.Markdown)
                {
                    sb.AppendLine("<div class=\"markdown-cell\">");
                    sb.AppendLine(ConvertMarkdownToHtml(cell.Contents));
                    sb.AppendLine("</div>");
                }
                else
                {
                    sb.AppendLine("<div class=\"code-cell\">");
                    sb.AppendLine($"<div class=\"cell-language\">{WebUtility.HtmlEncode(cell.KernelName)}</div>");
                    sb.AppendLine($"<pre><code>{WebUtility.HtmlEncode(cell.Contents)}</code></pre>");

                    AppendOutputsAsHtml(sb, cell);

                    sb.AppendLine("</div>");
                }

                sb.AppendLine("</div>");
            }

            sb.AppendLine("</body>");
            sb.AppendLine("</html>");

            return sb.ToString();
        }

        /// <summary>
        /// Exports the notebook as a Markdown document.
        /// Markdown cells are emitted verbatim. Code cells are wrapped in
        /// fenced code blocks with the kernel name as the language tag.
        /// Text outputs are appended as indented output blocks.
        /// </summary>
        public static string ExportToMarkdown(NotebookDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            var sb = new StringBuilder();

            for (int i = 0; i < document.Cells.Count; i++)
            {
                if (i > 0)
                    sb.AppendLine();

                var cell = document.Cells[i];

                if (cell.Kind == CellKind.Markdown)
                {
                    sb.AppendLine(cell.Contents);
                }
                else
                {
                    string lang = MapKernelToFenceLanguage(cell.KernelName);
                    sb.AppendLine($"```{lang}");
                    sb.AppendLine(cell.Contents);
                    sb.AppendLine("```");

                    string? outputText = GetPlainTextOutput(cell);
                    if (outputText != null)
                    {
                        sb.AppendLine();
                        sb.AppendLine("**Output:**");
                        sb.AppendLine("```");
                        sb.AppendLine(outputText);
                        sb.AppendLine("```");
                    }
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Exports the notebook as a C# script (.csx) file.
        /// Only C# code cells are included; markdown cells and cells in other
        /// languages are emitted as comments.
        /// </summary>
        public static string ExportToCSharpScript(NotebookDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            return ExportToScript(document, "csharp", "//");
        }

        /// <summary>
        /// Exports the notebook as an F# script (.fsx) file.
        /// Only F# code cells are included; markdown cells and cells in other
        /// languages are emitted as comments.
        /// </summary>
        public static string ExportToFSharpScript(NotebookDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            return ExportToScript(document, "fsharp", "//");
        }

        /// <summary>
        /// Returns the file extension (including leading dot) for the given export format.
        /// </summary>
        public static string GetFileExtension(ExportFormat format)
        {
            switch (format)
            {
                case ExportFormat.Html: return ".html";
                case ExportFormat.Pdf: return ".pdf";
                case ExportFormat.Markdown: return ".md";
                case ExportFormat.CSharpScript: return ".csx";
                case ExportFormat.FSharpScript: return ".fsx";
                default: throw new ArgumentOutOfRangeException(nameof(format));
            }
        }

        /// <summary>
        /// Returns a human-readable filter string for a Save File dialog.
        /// </summary>
        public static string GetFileFilter(ExportFormat format)
        {
            switch (format)
            {
                case ExportFormat.Html: return "HTML Files (*.html)|*.html";
                case ExportFormat.Pdf: return "PDF Files (*.pdf)|*.pdf";
                case ExportFormat.Markdown: return "Markdown Files (*.md)|*.md";
                case ExportFormat.CSharpScript: return "C# Script Files (*.csx)|*.csx";
                case ExportFormat.FSharpScript: return "F# Script Files (*.fsx)|*.fsx";
                default: throw new ArgumentOutOfRangeException(nameof(format));
            }
        }

        /// <summary>
        /// Exports the document content for the given format.
        /// </summary>
        public static string Export(NotebookDocument document, ExportFormat format)
        {
            switch (format)
            {
                case ExportFormat.Html: return ExportToHtml(document);
                case ExportFormat.Pdf: return ExportToHtml(document);
                case ExportFormat.Markdown: return ExportToMarkdown(document);
                case ExportFormat.CSharpScript: return ExportToCSharpScript(document);
                case ExportFormat.FSharpScript: return ExportToFSharpScript(document);
                default: throw new ArgumentOutOfRangeException(nameof(format));
            }
        }

        // ── Private helpers ──────────────────────────────────────────────

        private static string ExportToScript(NotebookDocument document, string targetKernel, string commentPrefix)
        {
            var sb = new StringBuilder();
            bool first = true;

            foreach (var cell in document.Cells)
            {
                if (!first)
                {
                    sb.AppendLine();
                }
                first = false;

                if (cell.Kind == CellKind.Markdown)
                {
                    // Emit markdown as comments
                    foreach (string line in cell.Contents.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                    {
                        sb.AppendLine($"{commentPrefix} {line}");
                    }
                }
                else if (string.Equals(cell.KernelName, targetKernel, StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine(cell.Contents);
                }
                else
                {
                    // Code cell in a different language — emit as a comment
                    sb.AppendLine($"{commentPrefix} [{cell.KernelName}]");
                    foreach (string line in cell.Contents.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                    {
                        sb.AppendLine($"{commentPrefix} {line}");
                    }
                }
            }

            return sb.ToString();
        }

        private static void AppendOutputsAsHtml(StringBuilder sb, NotebookCell cell)
        {
            foreach (var output in cell.Outputs)
            {
                bool isError = output.Kind == CellOutputKind.StandardError || output.Kind == CellOutputKind.Error;
                string errorClass = isError ? " cell-output-error" : "";

                // Prefer text/html, then text/plain
                var htmlOutput = output.FormattedValues.FirstOrDefault(f =>
                    string.Equals(f.MimeType, "text/html", StringComparison.OrdinalIgnoreCase));
                if (htmlOutput != null && !htmlOutput.SuppressDisplay)
                {
                    sb.AppendLine($"<div class=\"cell-output{errorClass}\">");
                    sb.AppendLine(htmlOutput.Value);
                    sb.AppendLine("</div>");
                    continue;
                }

                var textOutput = output.FormattedValues.FirstOrDefault(f =>
                    string.Equals(f.MimeType, "text/plain", StringComparison.OrdinalIgnoreCase));
                if (textOutput != null && !textOutput.SuppressDisplay)
                {
                    sb.AppendLine($"<div class=\"cell-output{errorClass}\">");
                    sb.AppendLine(WebUtility.HtmlEncode(StripAnsiEscapeCodes(textOutput.Value)));
                    sb.AppendLine("</div>");
                }
            }
        }

        private static string? GetPlainTextOutput(NotebookCell cell)
        {
            var parts = new List<string>();
            foreach (var output in cell.Outputs)
            {
                var textVal = output.FormattedValues.FirstOrDefault(f =>
                    string.Equals(f.MimeType, "text/plain", StringComparison.OrdinalIgnoreCase));
                if (textVal != null && !textVal.SuppressDisplay && !string.IsNullOrEmpty(textVal.Value))
                {
                    parts.Add(StripAnsiEscapeCodes(textVal.Value));
                }
            }
            return parts.Count > 0 ? string.Join(Environment.NewLine, parts) : null;
        }

        private static string StripAnsiEscapeCodes(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            return AnsiEscapePattern.Replace(text, string.Empty);
        }

        private static string MapKernelToFenceLanguage(string kernelName)
        {
            switch (kernelName?.ToLowerInvariant())
            {
                case "csharp": return "csharp";
                case "fsharp": return "fsharp";
                case "javascript": return "javascript";
                case "typescript": return "typescript";
                case "python": return "python";
                case "powershell":
                case "pwsh": return "powershell";
                case "sql": return "sql";
                case "kql": return "kql";
                case "html": return "html";
                default: return kernelName ?? "";
            }
        }

        // ── Markdown → HTML (lightweight converter) ──────────────────────

        private static string ConvertMarkdownToHtml(string markdown)
        {
            if (string.IsNullOrEmpty(markdown))
                return string.Empty;

            var sb = new StringBuilder();
            var lines = markdown.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            bool inCodeBlock = false;
            bool inList = false;
            string codeFence = string.Empty;

            foreach (var line in lines)
            {
                // Code fence detection (``` or ~~~)
                if (!inCodeBlock && (line.StartsWith("```") || line.StartsWith("~~~")))
                {
                    if (inList) { sb.AppendLine("</ul>"); inList = false; }
                    inCodeBlock = true;
                    codeFence = line.StartsWith("```") ? "```" : "~~~";
                    sb.AppendLine("<pre><code>");
                    continue;
                }
                if (inCodeBlock)
                {
                    if (line.StartsWith(codeFence)) { sb.AppendLine("</code></pre>"); inCodeBlock = false; }
                    else sb.AppendLine(WebUtility.HtmlEncode(line));
                    continue;
                }

                // Close list when encountering a non-list line
                if (inList && !line.StartsWith("- ") && !line.StartsWith("* ") && !line.StartsWith("+ "))
                {
                    sb.AppendLine("</ul>");
                    inList = false;
                }

                // ATX headings
                if (line.StartsWith("### ")) { sb.AppendLine($"<h3>{InlineMarkdown(line.Substring(4))}</h3>"); continue; }
                if (line.StartsWith("## "))  { sb.AppendLine($"<h2>{InlineMarkdown(line.Substring(3))}</h2>"); continue; }
                if (line.StartsWith("# "))   { sb.AppendLine($"<h1>{InlineMarkdown(line.Substring(2))}</h1>"); continue; }

                // Unordered lists
                if (line.StartsWith("- ") || line.StartsWith("* ") || line.StartsWith("+ "))
                {
                    if (!inList) { sb.AppendLine("<ul>"); inList = true; }
                    sb.AppendLine($"<li>{InlineMarkdown(line.Substring(2))}</li>");
                    continue;
                }

                // Blank line
                if (string.IsNullOrWhiteSpace(line)) { sb.AppendLine("<br>"); continue; }

                sb.AppendLine($"<p>{InlineMarkdown(line)}</p>");
            }

            if (inCodeBlock) sb.AppendLine("</code></pre>");
            if (inList)      sb.AppendLine("</ul>");

            return sb.ToString();
        }

        private static string InlineMarkdown(string text)
        {
            text = WebUtility.HtmlEncode(text);
            text = Regex.Replace(text, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
            text = Regex.Replace(text, @"__(.+?)__",     "<strong>$1</strong>");
            text = Regex.Replace(text, @"\*(.+?)\*",     "<em>$1</em>");
            text = Regex.Replace(text, @"_(.+?)_",       "<em>$1</em>");
            text = Regex.Replace(text, @"`(.+?)`",        "<code>$1</code>");
            text = Regex.Replace(text, @"\[(.+?)\]\((.+?)\)", "<a href='$2'>$1</a>");
            return text;
        }
    }
}
