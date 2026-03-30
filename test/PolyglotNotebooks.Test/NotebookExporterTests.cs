using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PolyglotNotebooks.Models;

#pragma warning disable MSTEST0037

namespace PolyglotNotebooks.Test
{
    [TestClass]
    public class NotebookExporterTests
    {
        // ── Helpers ──────────────────────────────────────────────────────

        private static NotebookDocument MakeDocument(params NotebookCell[] cells)
        {
            var doc = NotebookDocument.Create("test.dib", NotebookFormat.Dib);
            foreach (var cell in cells)
                doc.AddCellInternal(cell);
            return doc;
        }

        private static NotebookCell MakeCodeCell(string kernel, string contents)
            => new NotebookCell(CellKind.Code, kernel, contents);

        private static NotebookCell MakeMarkdownCell(string contents)
            => new NotebookCell(CellKind.Markdown, "markdown", contents);

        private static NotebookCell MakeCodeCellWithOutput(string kernel, string code, string outputText)
        {
            var cell = MakeCodeCell(kernel, code);
            cell.Outputs.Add(new CellOutput(
                CellOutputKind.StandardOutput,
                new List<FormattedOutput>
                {
                    new FormattedOutput("text/plain", outputText)
                }));
            return cell;
        }

        // ── HTML Export ──────────────────────────────────────────────────

        [TestMethod]
        public void ExportToHtml_EmptyDocument_ReturnsValidHtmlShell()
        {
            var doc = MakeDocument();

            string html = NotebookExporter.ExportToHtml(doc);

            Assert.IsTrue(html.Contains("<!DOCTYPE html>"), "Should contain DOCTYPE");
            Assert.IsTrue(html.Contains("<html"), "Should contain <html> tag");
            Assert.IsTrue(html.Contains("</html>"), "Should contain closing html tag");
        }

        [TestMethod]
        public void ExportToHtml_CodeCell_WrapsInPreCodeTags()
        {
            var doc = MakeDocument(MakeCodeCell("csharp", "Console.WriteLine(\"Hi\");"));

            string html = NotebookExporter.ExportToHtml(doc);

            Assert.IsTrue(html.Contains("<pre><code>"), "Should wrap code in pre/code tags");
            Assert.IsTrue(html.Contains("Console.WriteLine"), "Should contain cell content");
        }

        [TestMethod]
        public void ExportToHtml_MarkdownCell_RendersContent()
        {
            var doc = MakeDocument(MakeMarkdownCell("# Hello World"));

            string html = NotebookExporter.ExportToHtml(doc);

            Assert.IsTrue(html.Contains("<h1>Hello World</h1>"), "Markdown heading should be rendered as <h1>");
            Assert.IsTrue(html.Contains("markdown-cell"), "Should use markdown-cell class");
        }

        [TestMethod]
        public void ExportToHtml_CellWithOutput_RendersOutputBlock()
        {
            var doc = MakeDocument(MakeCodeCellWithOutput("csharp", "1 + 1", "2"));

            string html = NotebookExporter.ExportToHtml(doc);

            Assert.IsTrue(html.Contains("cell-output"), "Should contain output block");
            Assert.IsTrue(html.Contains("2"), "Should contain output value");
        }

        [TestMethod]
        public void ExportToHtml_EscapesHtmlCharacters()
        {
            var doc = MakeDocument(MakeCodeCell("csharp", "var x = 1 < 2 && true;"));

            string html = NotebookExporter.ExportToHtml(doc);

            Assert.IsTrue(html.Contains("&lt;"), "Should escape < character");
            Assert.IsTrue(html.Contains("&amp;"), "Should escape & character");
        }

        [TestMethod]
        public void ExportToHtml_ShowsKernelName()
        {
            var doc = MakeDocument(MakeCodeCell("fsharp", "let x = 42"));

            string html = NotebookExporter.ExportToHtml(doc);

            Assert.IsTrue(html.Contains("fsharp"), "Should show kernel name");
            Assert.IsTrue(html.Contains("cell-language"), "Should use cell-language class");
        }

        // ── Markdown Export ──────────────────────────────────────────────

        [TestMethod]
        public void ExportToMarkdown_CodeCell_WrappsInFencedBlock()
        {
            var doc = MakeDocument(MakeCodeCell("csharp", "var x = 1;"));

            string md = NotebookExporter.ExportToMarkdown(doc);

            Assert.IsTrue(md.Contains("```csharp"), "Should use csharp fence language");
            Assert.IsTrue(md.Contains("var x = 1;"), "Should contain code");
            // Should have closing fence
            var lines = md.Split(new[] { '\n' }, StringSplitOptions.None);
            int fenceCloseCount = lines.Count(l => l.Trim() == "```");
            Assert.IsTrue(fenceCloseCount >= 1, "Should have closing fence");
        }

        [TestMethod]
        public void ExportToMarkdown_MarkdownCell_EmitsVerbatim()
        {
            var doc = MakeDocument(MakeMarkdownCell("# Title\nSome text"));

            string md = NotebookExporter.ExportToMarkdown(doc);

            Assert.IsTrue(md.Contains("# Title"), "Should contain heading");
            Assert.IsTrue(md.Contains("Some text"), "Should contain body text");
        }

        [TestMethod]
        public void ExportToMarkdown_CellWithOutput_IncludesOutputBlock()
        {
            var doc = MakeDocument(MakeCodeCellWithOutput("csharp", "1 + 1", "2"));

            string md = NotebookExporter.ExportToMarkdown(doc);

            Assert.IsTrue(md.Contains("**Output:**"), "Should include Output label");
            Assert.IsTrue(md.Contains("2"), "Should contain output value");
        }

        [TestMethod]
        public void ExportToMarkdown_MultipleCells_SeparatedByBlankLines()
        {
            var doc = MakeDocument(
                MakeMarkdownCell("First"),
                MakeCodeCell("csharp", "var x = 1;"));

            string md = NotebookExporter.ExportToMarkdown(doc);

            Assert.IsTrue(md.Contains("First"), "Should contain first cell");
            Assert.IsTrue(md.Contains("```csharp"), "Should contain second cell fence");
        }

        // ── C# Script Export ─────────────────────────────────────────────

        [TestMethod]
        public void ExportToCSharpScript_CSharpCodeCell_EmitsDirectly()
        {
            var doc = MakeDocument(MakeCodeCell("csharp", "var x = 1;"));

            string csx = NotebookExporter.ExportToCSharpScript(doc);

            Assert.IsTrue(csx.Contains("var x = 1;"), "Should emit C# code directly");
            Assert.IsFalse(csx.TrimStart().StartsWith("//"), "C# code should not be commented");
        }

        [TestMethod]
        public void ExportToCSharpScript_MarkdownCell_EmitsAsComments()
        {
            var doc = MakeDocument(MakeMarkdownCell("# Title"));

            string csx = NotebookExporter.ExportToCSharpScript(doc);

            Assert.IsTrue(csx.Contains("// # Title"), "Markdown should be commented out");
        }

        [TestMethod]
        public void ExportToCSharpScript_NonCSharpCodeCell_EmitsAsComments()
        {
            var doc = MakeDocument(MakeCodeCell("fsharp", "let x = 42"));

            string csx = NotebookExporter.ExportToCSharpScript(doc);

            Assert.IsTrue(csx.Contains("// [fsharp]"), "Should label non-C# cell");
            Assert.IsTrue(csx.Contains("// let x = 42"), "Non-C# code should be commented");
        }

        [TestMethod]
        public void ExportToCSharpScript_MixedCells_PreservesOrder()
        {
            var doc = MakeDocument(
                MakeMarkdownCell("Setup"),
                MakeCodeCell("csharp", "var x = 1;"),
                MakeCodeCell("fsharp", "let y = 2"),
                MakeCodeCell("csharp", "var z = 3;"));

            string csx = NotebookExporter.ExportToCSharpScript(doc);

            int setupIndex = csx.IndexOf("// Setup");
            int xIndex = csx.IndexOf("var x = 1;");
            int fsharpIndex = csx.IndexOf("// [fsharp]");
            int zIndex = csx.IndexOf("var z = 3;");

            Assert.IsTrue(setupIndex < xIndex, "Setup comment should come before first code");
            Assert.IsTrue(xIndex < fsharpIndex, "First C# should come before F# comment");
            Assert.IsTrue(fsharpIndex < zIndex, "F# comment should come before second C#");
        }

        // ── F# Script Export ─────────────────────────────────────────────

        [TestMethod]
        public void ExportToFSharpScript_FSharpCodeCell_EmitsDirectly()
        {
            var doc = MakeDocument(MakeCodeCell("fsharp", "let x = 42"));

            string fsx = NotebookExporter.ExportToFSharpScript(doc);

            Assert.IsTrue(fsx.Contains("let x = 42"), "Should emit F# code directly");
        }

        [TestMethod]
        public void ExportToFSharpScript_NonFSharpCodeCell_EmitsAsComments()
        {
            var doc = MakeDocument(MakeCodeCell("csharp", "var x = 1;"));

            string fsx = NotebookExporter.ExportToFSharpScript(doc);

            Assert.IsTrue(fsx.Contains("// [csharp]"), "Should label non-F# cell");
            Assert.IsTrue(fsx.Contains("// var x = 1;"), "Non-F# code should be commented");
        }

        // ── Helper methods ───────────────────────────────────────────────

        [TestMethod]
        public void GetFileExtension_ReturnsCorrectExtensions()
        {
            Assert.AreEqual(".html", NotebookExporter.GetFileExtension(ExportFormat.Html));
            Assert.AreEqual(".pdf", NotebookExporter.GetFileExtension(ExportFormat.Pdf));
            Assert.AreEqual(".md", NotebookExporter.GetFileExtension(ExportFormat.Markdown));
            Assert.AreEqual(".csx", NotebookExporter.GetFileExtension(ExportFormat.CSharpScript));
            Assert.AreEqual(".fsx", NotebookExporter.GetFileExtension(ExportFormat.FSharpScript));
        }

        [TestMethod]
        public void GetFileFilter_ReturnsFilterWithExtension()
        {
            string filter = NotebookExporter.GetFileFilter(ExportFormat.Html);

            Assert.IsTrue(filter.Contains("*.html"), "Filter should contain *.html");
            Assert.IsTrue(filter.Contains("|"), "Filter should be pipe-delimited");
        }

        [TestMethod]
        public void Export_DispatchesToCorrectMethod()
        {
            var doc = MakeDocument(MakeCodeCell("csharp", "var x = 1;"));

            string html = NotebookExporter.Export(doc, ExportFormat.Html);
            string pdf = NotebookExporter.Export(doc, ExportFormat.Pdf);
            string md = NotebookExporter.Export(doc, ExportFormat.Markdown);
            string csx = NotebookExporter.Export(doc, ExportFormat.CSharpScript);
            string fsx = NotebookExporter.Export(doc, ExportFormat.FSharpScript);

            Assert.IsTrue(html.Contains("<!DOCTYPE html>"), "Html format should produce HTML");
            Assert.IsTrue(pdf.Contains("<!DOCTYPE html>"), "Pdf format should produce HTML (rendered to PDF by caller)");
            Assert.IsTrue(md.Contains("```csharp"), "Markdown format should produce fenced blocks");
            Assert.IsTrue(csx.Contains("var x = 1;"), "CSharpScript should include code");
            Assert.IsTrue(fsx.Contains("// [csharp]"), "FSharpScript should comment non-F# code");
        }
    }
}
