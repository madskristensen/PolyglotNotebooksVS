using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PolyglotNotebooks.Models;

#pragma warning disable MSTEST0037

namespace PolyglotNotebooks.Test
{
    [TestClass]
    public class NotebookConverterTests
    {
        // ── Helper: minimal .dib content ─────────────────────────────

        private static string MakeDib(string kernelName, string code)
        {
            return string.Join("\r\n", new[]
            {
                $"#!{kernelName}",
                code,
            });
        }

        private static string MakeMultiCellDib(params (string kernel, string code)[] cells)
        {
            var lines = new System.Collections.Generic.List<string>();
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0) lines.Add("");
                lines.Add($"#!{cells[i].kernel}");
                lines.Add(cells[i].code);
            }
            return string.Join("\r\n", lines);
        }

        // ── Dib → Ipynb ──────────────────────────────────────────────

        [TestMethod]
        public void ConvertDibToIpynb_SingleCSharpCell_ProducesValidIpynb()
        {
            var dib = MakeDib("csharp", "Console.WriteLine(\"Hi\");");

            var ipynb = NotebookConverter.ConvertDibToIpynb(dib);

            // Structurally verify by parsing the output
            var doc = NotebookParser.ParseIpynb(ipynb, "test.ipynb");
            Assert.AreEqual(1, doc.Cells.Count, "Should produce exactly one cell");
            Assert.AreEqual(CellKind.Code, doc.Cells[0].Kind, "Cell should be a code cell");
            Assert.IsTrue(doc.Cells[0].Contents.Contains("Console.WriteLine(\"Hi\");"),
                "Cell content should be preserved");
        }

        [TestMethod]
        public void ConvertDibToIpynb_OverridesKernelName_WhenExplicitlySet()
        {
            var dib = MakeDib("csharp", "let x = 1");

            var ipynb = NotebookConverter.ConvertDibToIpynb(dib, "fsharp");

            var doc = NotebookParser.ParseIpynb(ipynb, "test.ipynb");
            Assert.AreEqual("fsharp", doc.DefaultKernelName,
                "Default kernel should reflect the overridden kernel name");
        }

        [TestMethod]
        public void ConvertDibToIpynb_MultiCellDocument_AllCellsPreserved()
        {
            var dib = MakeMultiCellDib(
                ("csharp", "var a = 1;"),
                ("fsharp", "let b = 2"),
                ("markdown", "# Title"));

            var ipynb = NotebookConverter.ConvertDibToIpynb(dib);

            var doc = NotebookParser.ParseIpynb(ipynb, "test.ipynb");
            Assert.AreEqual(3, doc.Cells.Count, "All three cells should be present");

            // Verify ordering and content
            Assert.AreEqual(CellKind.Code, doc.Cells[0].Kind);
            Assert.IsTrue(doc.Cells[0].Contents.Contains("var a = 1;"), "First cell content");

            Assert.AreEqual(CellKind.Code, doc.Cells[1].Kind);
            Assert.AreEqual("fsharp", doc.Cells[1].KernelName, "Second cell kernel");
            Assert.IsTrue(doc.Cells[1].Contents.Contains("let b = 2"), "Second cell content");

            Assert.AreEqual(CellKind.Markdown, doc.Cells[2].Kind);
            Assert.IsTrue(doc.Cells[2].Contents.Contains("# Title"), "Third cell content");
        }

        // ── Ipynb → Dib ──────────────────────────────────────────────

        [TestMethod]
        public void ConvertIpynbToDib_SingleCell_ProducesValidDib()
        {
            var originalDib = MakeDib("csharp", "int x = 42;");
            var ipynb = NotebookConverter.ConvertDibToIpynb(originalDib);

            var dib = NotebookConverter.ConvertIpynbToDib(ipynb);

            // Structurally verify by parsing the dib output
            var doc = NotebookParser.ParseDib(dib, "test.dib");
            Assert.AreEqual(1, doc.Cells.Count, "Should produce exactly one cell");
            Assert.AreEqual(CellKind.Code, doc.Cells[0].Kind);
            Assert.IsTrue(doc.Cells[0].Contents.Contains("int x = 42;"),
                "Cell content should be preserved");
        }

        // ── Round-trips ──────────────────────────────────────────────

        [TestMethod]
        public void RoundTrip_DibToIpynb_ThenBack_PreservesCells()
        {
            var originalDib = MakeMultiCellDib(
                ("csharp", "var msg = \"hello\";"),
                ("markdown", "## Notes"));

            var ipynb = NotebookConverter.ConvertDibToIpynb(originalDib);
            var roundTripped = NotebookConverter.ConvertIpynbToDib(ipynb);

            // Structurally verify the round-tripped content
            var doc = NotebookParser.ParseDib(roundTripped, "test.dib");
            Assert.AreEqual(2, doc.Cells.Count, "Both cells should survive round-trip");
            Assert.AreEqual(CellKind.Code, doc.Cells[0].Kind);
            Assert.IsTrue(doc.Cells[0].Contents.Contains("var msg = \"hello\";"),
                "Code cell content should survive round-trip");
            Assert.AreEqual(CellKind.Markdown, doc.Cells[1].Kind);
            Assert.IsTrue(doc.Cells[1].Contents.Contains("## Notes"),
                "Markdown cell content should survive round-trip");
        }

        [TestMethod]
        public void RoundTrip_IpynbToDib_ThenBack_PreservesCells()
        {
            var seed = MakeDib("csharp", "Console.Read();");
            var ipynb = NotebookConverter.ConvertDibToIpynb(seed);

            var dib = NotebookConverter.ConvertIpynbToDib(ipynb);

            // Structurally verify the round-tripped content
            var doc = NotebookParser.ParseDib(dib, string.Empty);
            Assert.AreEqual(1, doc.Cells.Count, "Cell count should be preserved");
            Assert.AreEqual(CellKind.Code, doc.Cells[0].Kind, "Cell kind should be code");
            Assert.AreEqual("csharp", doc.Cells[0].KernelName, "Kernel name should be csharp");
            Assert.IsTrue(doc.Cells[0].Contents.Contains("Console.Read();"),
                "Cell content should survive ipynb → dib conversion");
        }
    }
}
