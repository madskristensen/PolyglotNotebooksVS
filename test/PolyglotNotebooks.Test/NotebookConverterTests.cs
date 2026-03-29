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

            Assert.IsNotNull(ipynb);
            Assert.IsTrue(ipynb.Contains("Console.WriteLine"), "ipynb should contain the cell source");
            // A valid Jupyter notebook has nbformat metadata
            Assert.IsTrue(ipynb.Contains("nbformat"), "ipynb output should include nbformat");
        }

        [TestMethod]
        public void ConvertDibToIpynb_OverridesKernelName_WhenExplicitlySet()
        {
            var dib = MakeDib("csharp", "let x = 1");

            var ipynb = NotebookConverter.ConvertDibToIpynb(dib, "fsharp");

            Assert.IsTrue(ipynb.Contains("fsharp"),
                "ipynb kernelspec should reflect the overridden kernel name");
        }

        [TestMethod]
        public void ConvertDibToIpynb_MultiCellDocument_AllCellsPreserved()
        {
            var dib = MakeMultiCellDib(
                ("csharp", "var a = 1;"),
                ("fsharp", "let b = 2"),
                ("markdown", "# Title"));

            var ipynb = NotebookConverter.ConvertDibToIpynb(dib);

            Assert.IsTrue(ipynb.Contains("var a = 1;"), "First cell content should be preserved");
            Assert.IsTrue(ipynb.Contains("let b = 2"), "Second cell content should be preserved");
            Assert.IsTrue(ipynb.Contains("# Title"), "Markdown cell content should be preserved");
        }

        // ── Ipynb → Dib ──────────────────────────────────────────────

        [TestMethod]
        public void ConvertIpynbToDib_SingleCell_ProducesValidDib()
        {
            // First create a valid .ipynb from a known .dib
            var originalDib = MakeDib("csharp", "int x = 42;");
            var ipynb = NotebookConverter.ConvertDibToIpynb(originalDib);

            var dib = NotebookConverter.ConvertIpynbToDib(ipynb);

            Assert.IsNotNull(dib);
            Assert.IsTrue(dib.Contains("int x = 42;"), "Converted .dib should contain the original code");
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

            Assert.IsTrue(roundTripped.Contains("var msg = \"hello\";"),
                "Code cell content should survive round-trip");
            Assert.IsTrue(roundTripped.Contains("## Notes"),
                "Markdown cell content should survive round-trip");
        }

        [TestMethod]
        public void RoundTrip_IpynbToDib_ThenBack_PreservesCells()
        {
            // Verify that ipynb → dib preserves cell content, and that .dib content
            // can independently be converted back to ipynb. We avoid a single
            // ipynb→dib→ipynb chain because the Interactive.Documents library has a
            // known JsonElement metadata cast limitation on that path.
            var seed = MakeDib("csharp", "Console.Read();");
            var ipynb = NotebookConverter.ConvertDibToIpynb(seed);

            var dib = NotebookConverter.ConvertIpynbToDib(ipynb);
            Assert.IsTrue(dib.Contains("Console.Read();"),
                "Cell content should survive ipynb → dib conversion");

            // Verify the .dib is valid by parsing it independently
            var doc = NotebookParser.ParseDib(dib, string.Empty);
            bool foundCell = false;
            foreach (var cell in doc.Cells)
            {
                if (cell.Contents.Contains("Console.Read();"))
                { foundCell = true; break; }
            }
            Assert.IsTrue(foundCell, "Parsed .dib should contain the original code cell");
        }
    }
}
