using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PolyglotNotebooks.Models;

#pragma warning disable MSTEST0037

namespace PolyglotNotebooks.Test
{
    [TestClass]
    public class NotebookParserIpynbTests
    {
        // ── Helpers ──────────────────────────────────────────────────

        private static string MinimalIpynb(string cellType, string source, string kernelName = ".net-csharp")
        {
            string outputsOrNothing = cellType == "code" ? @"""outputs"": []," : "";
            return @"{
  ""cells"": [
    {
      ""cell_type"": """ + cellType + @""",
      " + outputsOrNothing + @"
      ""source"": [""" + EscapeJson(source) + @"""],
      ""metadata"": {}
    }
  ],
  ""metadata"": {
    ""kernelspec"": {
      ""display_name"": "".NET (C#)"",
      ""language"": ""C#"",
      ""name"": """ + kernelName + @"""
    },
    ""language_info"": {
      ""name"": ""C#""
    }
  },
  ""nbformat"": 4,
  ""nbformat_minor"": 2
}";
        }

        private static string EscapeJson(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        // ── NormalizeKernelName ───────────────────────────────────────

        [TestMethod]
        [DataRow(".net-csharp", "csharp", DisplayName = ".net-csharp → csharp")]
        [DataRow(".net-fsharp", "fsharp", DisplayName = ".net-fsharp → fsharp")]
        [DataRow(".net-pwsh", "pwsh", DisplayName = ".net-pwsh → pwsh")]
        [DataRow(".net-javascript", "javascript", DisplayName = ".net-javascript → javascript")]
        [DataRow(".net-sql", "sql", DisplayName = ".net-sql → sql")]
        [DataRow(".net-kql", "kql", DisplayName = ".net-kql → kql")]
        [DataRow("kusto", "kql", DisplayName = "kusto → kql")]
        [DataRow("c#", "csharp", DisplayName = "c# → csharp")]
        [DataRow("f#", "fsharp", DisplayName = "f# → fsharp")]
        [DataRow("powershell", "pwsh", DisplayName = "powershell → pwsh")]
        [DataRow("js", "javascript", DisplayName = "js → javascript")]
        public void NormalizeKernelName_KnownAlias_MapsToCanonical(string input, string expected)
        {
            var result = NotebookParser.NormalizeKernelName(input);

            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        [DataRow("csharp", DisplayName = "csharp passes through")]
        [DataRow("fsharp", DisplayName = "fsharp passes through")]
        [DataRow("python", DisplayName = "unknown kernel passes through")]
        [DataRow("rust", DisplayName = "rust passes through")]
        public void NormalizeKernelName_UnknownKernel_ReturnsUnchanged(string input)
        {
            var result = NotebookParser.NormalizeKernelName(input);

            Assert.AreEqual(input, result);
        }

        [TestMethod]
        public void NormalizeKernelName_NullInput_ReturnsNull()
        {
            var result = NotebookParser.NormalizeKernelName(null);

            Assert.IsNull(result);
        }

        [TestMethod]
        public void NormalizeKernelName_EmptyInput_ReturnsEmpty()
        {
            var result = NotebookParser.NormalizeKernelName(string.Empty);

            Assert.AreEqual(string.Empty, result);
        }

        [TestMethod]
        [DataRow("C#", "csharp", DisplayName = "C# (uppercase) → csharp")]
        [DataRow("F#", "fsharp", DisplayName = "F# (uppercase) → fsharp")]
        [DataRow("POWERSHELL", "pwsh", DisplayName = "POWERSHELL → pwsh")]
        [DataRow("Js", "javascript", DisplayName = "Js (mixed case) → javascript")]
        public void NormalizeKernelName_CaseInsensitive_MapsCorrectly(string input, string expected)
        {
            var result = NotebookParser.NormalizeKernelName(input);

            Assert.AreEqual(expected, result);
        }

        // ── ParseIpynb ───────────────────────────────────────────────

        [TestMethod]
        public void ParseIpynb_MinimalCodeCell_ParsesCorrectly()
        {
            var json = MinimalIpynb("code", "Console.WriteLine(42);");

            var doc = NotebookParser.ParseIpynb(json, "test.ipynb");

            Assert.AreEqual(1, doc.Cells.Count);
            Assert.AreEqual(CellKind.Code, doc.Cells[0].Kind);
            Assert.IsTrue(doc.Cells[0].Contents.Contains("Console.WriteLine(42);"));
        }

        [TestMethod]
        public void ParseIpynb_MarkdownCell_ParsedAsMarkdownKind()
        {
            var json = MinimalIpynb("markdown", "# Hello World");

            var doc = NotebookParser.ParseIpynb(json, "test.ipynb");

            Assert.AreEqual(1, doc.Cells.Count);
            Assert.AreEqual(CellKind.Markdown, doc.Cells[0].Kind);
            Assert.AreEqual("markdown", doc.Cells[0].KernelName);
            Assert.IsTrue(doc.Cells[0].Contents.Contains("# Hello World"));
        }

        [TestMethod]
        public void ParseIpynb_MultipleCells_PreservesOrderAndTypes()
        {
            var json = @"{
  ""cells"": [
    { ""cell_type"": ""code"", ""source"": [""var x = 1;""], ""metadata"": {}, ""outputs"": [] },
    { ""cell_type"": ""markdown"", ""source"": [""# Title""], ""metadata"": {} },
    { ""cell_type"": ""code"", ""source"": [""var y = 2;""], ""metadata"": {}, ""outputs"": [] }
  ],
  ""metadata"": {
    ""kernelspec"": { ""display_name"": "".NET (C#)"", ""language"": ""C#"", ""name"": "".net-csharp"" },
    ""language_info"": { ""name"": ""C#"" }
  },
  ""nbformat"": 4,
  ""nbformat_minor"": 2
}";

            var doc = NotebookParser.ParseIpynb(json, "test.ipynb");

            Assert.AreEqual(3, doc.Cells.Count);
            Assert.AreEqual(CellKind.Code, doc.Cells[0].Kind);
            Assert.IsTrue(doc.Cells[0].Contents.Contains("var x = 1;"));
            Assert.AreEqual(CellKind.Markdown, doc.Cells[1].Kind);
            Assert.IsTrue(doc.Cells[1].Contents.Contains("# Title"));
            Assert.AreEqual(CellKind.Code, doc.Cells[2].Kind);
            Assert.IsTrue(doc.Cells[2].Contents.Contains("var y = 2;"));
        }

        [TestMethod]
        public void ParseIpynb_WithKernelMetadata_PreservesMetadata()
        {
            var json = MinimalIpynb("code", "let x = 1", ".net-csharp");

            var doc = NotebookParser.ParseIpynb(json, "test.ipynb");

            // Document-level metadata should include kernelspec information
            Assert.IsTrue(doc.Metadata.Count > 0, "Document metadata should be preserved");
            Assert.AreEqual(1, doc.Cells.Count);
            Assert.AreEqual("csharp", doc.Cells[0].KernelName,
                "Cell kernel should be the normalized default kernel");
        }

        [TestMethod]
        public void ParseIpynb_WithTextOutput_CapturesOutput()
        {
            var json = @"{
  ""cells"": [
    {
      ""cell_type"": ""code"",
      ""source"": [""print('hello')""],
      ""metadata"": {},
      ""outputs"": [
        {
          ""output_type"": ""stream"",
          ""name"": ""stdout"",
          ""text"": ""hello\n""
        }
      ]
    }
  ],
  ""metadata"": {
    ""kernelspec"": { ""display_name"": "".NET (C#)"", ""language"": ""C#"", ""name"": "".net-csharp"" },
    ""language_info"": { ""name"": ""C#"" }
  },
  ""nbformat"": 4,
  ""nbformat_minor"": 2
}";

            var doc = NotebookParser.ParseIpynb(json, "test.ipynb");

            Assert.AreEqual(1, doc.Cells.Count);
            Assert.AreEqual(1, doc.Cells[0].Outputs.Count);
            Assert.AreEqual(CellOutputKind.StandardOutput, doc.Cells[0].Outputs[0].Kind);
        }

        [TestMethod]
        public void ParseIpynb_WithErrorOutput_CapturesErrorOutput()
        {
            var json = @"{
  ""cells"": [
    {
      ""cell_type"": ""code"",
      ""source"": [""throw new Exception();""],
      ""metadata"": {},
      ""outputs"": [
        {
          ""output_type"": ""error"",
          ""ename"": ""Exception"",
          ""evalue"": ""Something went wrong"",
          ""traceback"": [""at Main()""]
        }
      ]
    }
  ],
  ""metadata"": {
    ""kernelspec"": { ""display_name"": "".NET (C#)"", ""language"": ""C#"", ""name"": "".net-csharp"" },
    ""language_info"": { ""name"": ""C#"" }
  },
  ""nbformat"": 4,
  ""nbformat_minor"": 2
}";

            var doc = NotebookParser.ParseIpynb(json, "test.ipynb");

            Assert.AreEqual(1, doc.Cells[0].Outputs.Count);
            Assert.AreEqual(CellOutputKind.Error, doc.Cells[0].Outputs[0].Kind);
            var errorText = doc.Cells[0].Outputs[0].FormattedValues[0].Value;
            Assert.IsTrue(errorText.Contains("Exception"), "Error output should contain the error name");
        }

        [TestMethod]
        public void ParseIpynb_WithDisplayDataOutput_CapturesDisplayOutput()
        {
            var json = @"{
  ""cells"": [
    {
      ""cell_type"": ""code"",
      ""source"": [""display(42)""],
      ""metadata"": {},
      ""outputs"": [
        {
          ""output_type"": ""display_data"",
          ""data"": { ""text/plain"": ""42"" },
          ""metadata"": {}
        }
      ]
    }
  ],
  ""metadata"": {
    ""kernelspec"": { ""display_name"": "".NET (C#)"", ""language"": ""C#"", ""name"": "".net-csharp"" },
    ""language_info"": { ""name"": ""C#"" }
  },
  ""nbformat"": 4,
  ""nbformat_minor"": 2
}";

            var doc = NotebookParser.ParseIpynb(json, "test.ipynb");

            Assert.AreEqual(1, doc.Cells[0].Outputs.Count);
            Assert.AreEqual(CellOutputKind.Display, doc.Cells[0].Outputs[0].Kind);
        }

        [TestMethod]
        public void ParseIpynb_EmptyNotebook_ProducesDocumentWithNoCells()
        {
            var json = @"{
  ""cells"": [],
  ""metadata"": {
    ""kernelspec"": { ""display_name"": "".NET (C#)"", ""language"": ""C#"", ""name"": "".net-csharp"" },
    ""language_info"": { ""name"": ""C#"" }
  },
  ""nbformat"": 4,
  ""nbformat_minor"": 2
}";

            var doc = NotebookParser.ParseIpynb(json, "test.ipynb");

            Assert.AreEqual(0, doc.Cells.Count);
            Assert.AreEqual(NotebookFormat.Ipynb, doc.Format);
        }

        [TestMethod]
        public void ParseIpynb_MalformedJson_ThrowsException()
        {
            var badJson = "{ this is not valid JSON }}}";
            bool threw = false;

            try
            {
                NotebookParser.ParseIpynb(badJson, "bad.ipynb");
            }
            catch (Exception)
            {
                threw = true;
            }

            Assert.IsTrue(threw, "Malformed JSON should throw an exception");
        }

        [TestMethod]
        public void ParseIpynb_NoMetadata_StillParsesSuccessfully()
        {
            var json = @"{
  ""cells"": [
    { ""cell_type"": ""code"", ""source"": [""1+1""], ""metadata"": {}, ""outputs"": [] }
  ],
  ""metadata"": {},
  ""nbformat"": 4,
  ""nbformat_minor"": 2
}";

            var doc = NotebookParser.ParseIpynb(json, "test.ipynb");

            Assert.AreEqual(1, doc.Cells.Count);
            Assert.AreEqual(NotebookFormat.Ipynb, doc.Format);
        }

        // ── SerializeIpynb ───────────────────────────────────────────

        [TestMethod]
        public void SerializeIpynb_RoundTrip_ProducesEquivalentDocument()
        {
            var json = @"{
  ""cells"": [
    { ""cell_type"": ""code"", ""source"": [""var a = 1;""], ""metadata"": {}, ""outputs"": [] },
    { ""cell_type"": ""markdown"", ""source"": [""## Notes""], ""metadata"": {} }
  ],
  ""metadata"": {
    ""kernelspec"": { ""display_name"": "".NET (C#)"", ""language"": ""C#"", ""name"": "".net-csharp"" },
    ""language_info"": { ""name"": ""C#"" }
  },
  ""nbformat"": 4,
  ""nbformat_minor"": 2
}";

            var original = NotebookParser.ParseIpynb(json, "test.ipynb");
            var serialized = NotebookParser.SerializeIpynb(original);
            var reparsed = NotebookParser.ParseIpynb(serialized, "test.ipynb");

            Assert.AreEqual(original.Cells.Count, reparsed.Cells.Count);
            for (int i = 0; i < original.Cells.Count; i++)
            {
                Assert.AreEqual(original.Cells[i].Kind, reparsed.Cells[i].Kind, $"Cell {i} kind mismatch");
                Assert.AreEqual(original.Cells[i].Contents.Trim(), reparsed.Cells[i].Contents.Trim(), $"Cell {i} content mismatch");
            }
        }

        [TestMethod]
        public void SerializeIpynb_OutputPreservation_ThroughRoundTrip()
        {
            var json = @"{
  ""cells"": [
    {
      ""cell_type"": ""code"",
      ""source"": [""print(42)""],
      ""metadata"": {},
      ""outputs"": [
        {
          ""output_type"": ""stream"",
          ""name"": ""stdout"",
          ""text"": ""42\n""
        }
      ]
    }
  ],
  ""metadata"": {
    ""kernelspec"": { ""display_name"": "".NET (C#)"", ""language"": ""C#"", ""name"": "".net-csharp"" },
    ""language_info"": { ""name"": ""C#"" }
  },
  ""nbformat"": 4,
  ""nbformat_minor"": 2
}";

            var original = NotebookParser.ParseIpynb(json, "test.ipynb");
            var serialized = NotebookParser.SerializeIpynb(original);
            var reparsed = NotebookParser.ParseIpynb(serialized, "test.ipynb");

            Assert.AreEqual(1, reparsed.Cells[0].Outputs.Count, "Output should survive round-trip");
            Assert.AreEqual(CellOutputKind.StandardOutput, reparsed.Cells[0].Outputs[0].Kind);
        }

        [TestMethod]
        public void SerializeIpynb_MetadataPreservation_ThroughRoundTrip()
        {
            var json = MinimalIpynb("code", "1+1", ".net-csharp");

            var original = NotebookParser.ParseIpynb(json, "test.ipynb");
            var serialized = NotebookParser.SerializeIpynb(original);
            var reparsed = NotebookParser.ParseIpynb(serialized, "test.ipynb");

            Assert.AreEqual(original.DefaultKernelName, reparsed.DefaultKernelName,
                "Default kernel name should be preserved");
        }

        [TestMethod]
        public void SerializeIpynb_EmptyDocument_ProducesValidJson()
        {
            var doc = NotebookDocument.Create("empty.ipynb", NotebookFormat.Ipynb);

            var json = NotebookParser.SerializeIpynb(doc);

            Assert.IsNotNull(json);
            Assert.IsTrue(json.Contains("nbformat"), "Serialized ipynb should contain nbformat");
            // Verify it can be re-parsed
            var reparsed = NotebookParser.ParseIpynb(json, "empty.ipynb");
            Assert.AreEqual(0, reparsed.Cells.Count);
        }

        [TestMethod]
        public void SerializeIpynb_CellOrdering_IsPreserved()
        {
            var doc = NotebookDocument.Create("order.ipynb", NotebookFormat.Ipynb);
            doc.AddCell(CellKind.Code, "csharp");
            doc.Cells[0].Contents = "// first";
            doc.AddCell(CellKind.Markdown, "markdown");
            doc.Cells[1].Contents = "# second";
            doc.AddCell(CellKind.Code, "csharp");
            doc.Cells[2].Contents = "// third";

            var json = NotebookParser.SerializeIpynb(doc);
            var reparsed = NotebookParser.ParseIpynb(json, "order.ipynb");

            Assert.AreEqual(3, reparsed.Cells.Count);
            Assert.IsTrue(reparsed.Cells[0].Contents.Contains("// first"), "First cell should be first");
            Assert.IsTrue(reparsed.Cells[1].Contents.Contains("# second"), "Second cell should be second");
            Assert.IsTrue(reparsed.Cells[2].Contents.Contains("// third"), "Third cell should be third");
        }

        [TestMethod]
        public void SerializeIpynb_KernelNameInOutput_IsCanonical()
        {
            var json = MinimalIpynb("code", "let x = 1", ".net-fsharp");

            var doc = NotebookParser.ParseIpynb(json, "test.ipynb");
            var serialized = NotebookParser.SerializeIpynb(doc);

            // The serialized output should contain the canonical kernel name in the JSON
            Assert.IsTrue(serialized.Contains("fsharp"),
                "Serialized notebook should contain the canonical kernel name");
        }
    }
}
