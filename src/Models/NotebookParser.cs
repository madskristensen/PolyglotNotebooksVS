using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.DotNet.Interactive.Documents;
using Microsoft.DotNet.Interactive.Documents.Jupyter;

namespace PolyglotNotebooks.Models
{
    // Microsoft.DotNet.Interactive.Documents targets netstandard2.0, so it is fully
    // compatible with net48. We use its CodeSubmission and Notebook types for all
    // parsing and serialization; our own model layer sits on top for WPF binding.
    public static class NotebookParser
    {
        private static readonly KernelInfoCollection DefaultKernels = BuildDefaultKernels();

        // Maps Jupyter / dotnet-interactive kernel name aliases to canonical kernel names.
        private static readonly Dictionary<string, string> KernelNameAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [".net-csharp"]  = "csharp",
                [".net-fsharp"]  = "fsharp",
                [".net-pwsh"]    = "pwsh",
                [".net-javascript"] = "javascript",
                [".net-sql"]     = "sql",
                [".net-kql"]     = "kql",
                ["kusto"]        = "kql",
                ["c#"]           = "csharp",
                ["f#"]           = "fsharp",
                ["powershell"]   = "pwsh",
                ["js"]           = "javascript",
            };

        private static KernelInfoCollection BuildDefaultKernels()
        {
            var kernels = new KernelInfoCollection { DefaultKernelName = "csharp" };
            kernels.Add(new KernelInfo("csharp", "C#", new[] { "cs", "c#" }));
            kernels.Add(new KernelInfo("fsharp", "F#", new[] { "fs", "f#" }));
            kernels.Add(new KernelInfo("pwsh", "PowerShell", new[] { "powershell" }));
            kernels.Add(new KernelInfo("javascript", "JavaScript", new[] { "js" }));
            kernels.Add(new KernelInfo("html", "HTML", Array.Empty<string>()));
            kernels.Add(new KernelInfo("sql", "SQL", Array.Empty<string>()));
            kernels.Add(new KernelInfo("kql", "KQL", new[] { "kusto" }));
            kernels.Add(new KernelInfo("markdown", "Markdown", Array.Empty<string>()));
            return kernels;
        }

        // Matches lines like "#!kql-Ddtelvsraw" or "#!sql-myServer" at the start of a line.
        private static readonly Regex MagicCommandPattern =
            new Regex(@"^#!([a-zA-Z][\w\-]*)", RegexOptions.Multiline | RegexOptions.Compiled);

        /// <summary>
        /// Builds a kernel collection that includes the default kernels plus any
        /// dynamically-connected kernel names found in the .dib content. This ensures
        /// that <c>#!kql-Ddtelvsraw</c> style directives are treated as cell boundaries
        /// by <see cref="CodeSubmission.Parse"/>.
        /// </summary>
        private static KernelInfoCollection BuildKernelsForContent(string content)
        {
            var knownNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ki in DefaultKernels)
                knownNames.Add(ki.Name);

            var extras = new List<string>();
            foreach (Match match in MagicCommandPattern.Matches(content))
            {
                var name = match.Groups[1].Value;
                if (!knownNames.Contains(name))
                {
                    knownNames.Add(name);
                    extras.Add(name);
                }
            }

            if (extras.Count == 0)
                return DefaultKernels;

            // Clone the defaults and add discovered dynamic kernels.
            var kernels = BuildDefaultKernels();
            foreach (var name in extras)
                kernels.Add(new KernelInfo(name));

            return kernels;
        }

        public static NotebookDocument Load(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Notebook file not found.", filePath);

            string content = File.ReadAllText(filePath);
            string ext = Path.GetExtension(filePath).ToLowerInvariant();

            return ext == ".ipynb"
                ? ParseIpynb(content, filePath)
                : ParseDib(content, filePath);
        }

        public static void Save(NotebookDocument doc)
        {
            string content = doc.Format == NotebookFormat.Ipynb
                ? SerializeIpynb(doc)
                : SerializeDib(doc);

            File.WriteAllText(doc.FilePath, content);
        }

        public static NotebookDocument ParseDib(string content, string filePath)
        {
            var kernels = BuildKernelsForContent(content);
            var interactive = CodeSubmission.Parse(content, kernels);
            return MapToDocument(interactive, filePath, NotebookFormat.Dib);
        }

        public static NotebookDocument ParseIpynb(string content, string filePath)
        {
            var interactive = Notebook.Parse(content, DefaultKernels);
            return MapToDocument(interactive, filePath, NotebookFormat.Ipynb);
        }

        public static string SerializeDib(NotebookDocument doc)
        {
            var interactive = MapToInteractive(doc);
            return CodeSubmission.ToCodeSubmissionContent(interactive, "\r\n");
        }

        public static string SerializeIpynb(NotebookDocument doc)
        {
            var interactive = MapToInteractive(doc);
            return Notebook.ToJupyterJson(interactive, doc.DefaultKernelName);
        }

        private static NotebookDocument MapToDocument(
            InteractiveDocument interactive,
            string filePath,
            NotebookFormat format)
        {
            string rawDefault = interactive.GetDefaultKernelName() ?? "csharp";
            string defaultKernel = NormalizeKernelName(rawDefault);
            var doc = NotebookDocument.Create(filePath, format, defaultKernel);

            // Preserve document-level metadata (kernelspec, language_info, nbformat, etc.)
            if (interactive.Metadata != null)
                foreach (var kv in interactive.Metadata)
                    doc.Metadata[kv.Key] = kv.Value;

            foreach (var element in interactive.Elements)
            {
                bool isMarkdown = string.Equals(element.KernelName, "markdown",
                    StringComparison.OrdinalIgnoreCase);
                var kind = isMarkdown ? CellKind.Markdown : CellKind.Code;

                string kernelName = NormalizeKernelName(element.KernelName ?? defaultKernel);
                var cell = new NotebookCell(element.Id ?? Guid.NewGuid().ToString(), kind, kernelName, element.Contents ?? "");
                cell.ExecutionOrder = element.ExecutionOrder == 0 ? (int?)null : element.ExecutionOrder;

                if (element.Metadata != null)
                    foreach (var kv in element.Metadata)
                        cell.Metadata[kv.Key] = kv.Value;

                foreach (var output in element.Outputs)
                    cell.Outputs.Add(MapOutput(output));

                doc.AddCellInternal(cell);
            }

            doc.MarkClean();
            return doc;
        }

        private static InteractiveDocument MapToInteractive(NotebookDocument doc)
        {
            var elements = new List<InteractiveDocumentElement>();

            foreach (var cell in doc.Cells)
            {
                var outputs = cell.Outputs
                    .Select(MapOutputToElement)
                    .Where(o => o != null)
                    .Select(o => o!)
                    .ToList();

                var element = new InteractiveDocumentElement(
                    cell.Contents,
                    cell.KernelName,
                    outputs);

                element.Id = cell.Id;
                if (cell.ExecutionOrder.HasValue)
                    element.ExecutionOrder = cell.ExecutionOrder.Value;

                if (cell.Metadata.Count > 0)
                    element.Metadata = cell.Metadata;

                elements.Add(element);
            }

            var interactive = new InteractiveDocument(elements);

            // Restore document-level metadata for round-trip fidelity.
            if (doc.Metadata.Count > 0)
                foreach (var kv in doc.Metadata)
                    interactive.Metadata[kv.Key] = kv.Value;

            var kernelInfo = interactive.GetKernelInfo();
            if (kernelInfo != null)
                kernelInfo.DefaultKernelName = doc.DefaultKernelName;

            return interactive;
        }

        private static CellOutput MapOutput(InteractiveDocumentOutputElement output)
        {
            switch (output)
            {
                case ReturnValueElement rv:
                    return new CellOutput(CellOutputKind.ReturnValue, MapDataToFormatted(rv.Data));

                case DisplayElement disp:
                    string? valueId = null;
                    if (disp.Metadata?.TryGetValue("transient", out var transient) == true
                        && transient is IDictionary<string, object> transientDict
                        && transientDict.TryGetValue("display_id", out var displayId))
                    {
                        valueId = displayId?.ToString();
                    }
                    return new CellOutput(CellOutputKind.Display, MapDataToFormatted(disp.Data), valueId);

                case ErrorElement err:
                    var errText = $"{err.ErrorName}: {err.ErrorValue}";
                    if (err.StackTrace?.Length > 0)
                        errText += "\n" + string.Join("\n", err.StackTrace);
                    return new CellOutput(CellOutputKind.Error, new List<FormattedOutput>
                    {
                        new FormattedOutput("text/plain", errText)
                    });

                case TextElement txt:
                    var kind = txt.Name == "stderr" ? CellOutputKind.StandardError : CellOutputKind.StandardOutput;
                    return new CellOutput(kind, new List<FormattedOutput>
                    {
                        new FormattedOutput("text/plain", txt.Text)
                    });

                default:
                    return new CellOutput(CellOutputKind.StandardOutput, new List<FormattedOutput>());
            }
        }

        private static List<FormattedOutput> MapDataToFormatted(IDictionary<string, object>? data)
        {
            if (data == null)
                return new List<FormattedOutput>();

            var result = new List<FormattedOutput>();
            foreach (var kv in data)
            {
                string value = kv.Value is string s ? s
                    : kv.Value is IEnumerable<string> lines ? string.Concat(lines)
                    : kv.Value?.ToString() ?? "";
                result.Add(new FormattedOutput(kv.Key, value));
            }
            return result;
        }

        private static InteractiveDocumentOutputElement? MapOutputToElement(CellOutput output)
        {
            switch (output.Kind)
            {
                case CellOutputKind.ReturnValue:
                    return CreateReturnValueElement(BuildData(output));

                case CellOutputKind.Display:
                    return new DisplayElement(BuildData(output));

                case CellOutputKind.Error:
                    var plain = output.FormattedValues.FirstOrDefault(f => f.MimeType == "text/plain")?.Value ?? "";
                    var parts = plain.Split(new[] { ": " }, 2, StringSplitOptions.None);
                    return new ErrorElement(
                        parts.Length > 1 ? parts[1] : plain,
                        parts.Length > 1 ? parts[0] : "Error",
                        Array.Empty<string>());

                case CellOutputKind.StandardOutput:
                    var outText = output.FormattedValues.FirstOrDefault()?.Value ?? "";
                    return new TextElement(outText, "stdout");

                case CellOutputKind.StandardError:
                    var errText = output.FormattedValues.FirstOrDefault()?.Value ?? "";
                    return new TextElement(errText, "stderr");

                default:
                    return null;
            }
        }

        // ReturnValueElement.Data has no public setter; set via the backing field.
        private static readonly FieldInfo? _returnValueDataField =
            typeof(ReturnValueElement).GetField(
                "<Data>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static ReturnValueElement CreateReturnValueElement(IDictionary<string, object> data)
        {
            var element = new ReturnValueElement();
            _returnValueDataField?.SetValue(element, data);
            return element;
        }

        private static IDictionary<string, object> BuildData(CellOutput output)
        {
            var data = new Dictionary<string, object>();
            foreach (var fv in output.FormattedValues)
                data[fv.MimeType] = fv.Value;
            return data;
        }

        /// <summary>
        /// Maps Jupyter / .net-interactive kernel name aliases to the canonical kernel name
        /// used throughout this extension. Unrecognized names are returned unchanged.
        /// </summary>
        public static string NormalizeKernelName(string kernelName)
        {
            if (string.IsNullOrEmpty(kernelName))
                return kernelName;
            return KernelNameAliases.TryGetValue(kernelName, out var normalized)
                ? normalized
                : kernelName;
        }
    }
}
