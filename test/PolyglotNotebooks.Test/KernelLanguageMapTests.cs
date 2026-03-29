using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PolyglotNotebooks.Editor;

#pragma warning disable MSTEST0037

namespace PolyglotNotebooks.Test
{
    [TestClass]
    public class KernelLanguageMapTests
    {
        // ── Content Type Mapping ──────────────────────────────────────

        [TestMethod]
        [DataRow("csharp", "CSharp")]
        [DataRow("fsharp", "F#")]
        [DataRow("javascript", "JavaScript")]
        [DataRow("typescript", "TypeScript")]
        [DataRow("python", "Python")]
        [DataRow("powershell", "PowerShell")]
        [DataRow("markdown", "markdown")]
        [DataRow("sql", "SQL Server Tools")]
        [DataRow("html", "HTML")]
        [DataRow("CSHARP", "CSharp")]
        [DataRow("CSharp", "CSharp")]
        [DataRow("SQL", "SQL Server Tools")]
        [DataRow("HTML", "HTML")]
        public void GetContentTypeName_ReturnsExpectedResult(string kernel, string expected)
            => Assert.AreEqual(expected, KernelLanguageMap.GetContentTypeName(kernel));

        // ── Unknown kernel returns null ──

        [TestMethod]
        public void GetContentTypeName_UnknownKernel_ReturnsNull()
            => Assert.IsNull(KernelLanguageMap.GetContentTypeName("ruby"));

        // ── File Extension Mapping ────────────────────────────────────

        [TestMethod]
        [DataRow("csharp", ".cs")]
        [DataRow("fsharp", ".fs")]
        [DataRow("javascript", ".js")]
        [DataRow("typescript", ".ts")]
        [DataRow("python", ".py")]
        [DataRow("powershell", ".ps1")]
        [DataRow("sql", ".sql")]
        [DataRow("html", ".html")]
        [DataRow("markdown", ".md")]
        [DataRow("ruby", ".txt")]
        [DataRow(null, ".txt")]
        public void GetFileExtension_ReturnsExpectedResult(string? kernel, string expected)
            => Assert.AreEqual(expected, KernelLanguageMap.GetFileExtension(kernel));

        // ── Fake File Name Generation ─────────────────────────────────

        [TestMethod]
        public void GetFakeFileName_CSharp_EndsWithCs()
        {
            var path = KernelLanguageMap.GetFakeFileName("csharp");
            Assert.IsTrue(path.EndsWith(".cs"), $"Expected .cs extension, got: {path}");
        }

        [TestMethod]
        public void GetFakeFileName_Sql_EndsWithSql()
        {
            var path = KernelLanguageMap.GetFakeFileName("sql");
            Assert.IsTrue(path.EndsWith(".sql"), $"Expected .sql extension, got: {path}");
        }

        [TestMethod]
        public void GetFakeFileName_Html_EndsWithHtml()
        {
            var path = KernelLanguageMap.GetFakeFileName("html");
            Assert.IsTrue(path.EndsWith(".html"), $"Expected .html extension, got: {path}");
        }

        [TestMethod]
        public void GetFakeFileName_Unknown_EndsWithTxt()
        {
            var path = KernelLanguageMap.GetFakeFileName("unknown");
            Assert.IsTrue(path.EndsWith(".txt"), $"Expected .txt extension, got: {path}");
        }

        [TestMethod]
        public void GetFakeFileName_Null_EndsWithTxt()
        {
            var path = KernelLanguageMap.GetFakeFileName(null);
            Assert.IsTrue(path.EndsWith(".txt"), $"Expected .txt extension, got: {path}");
        }

        [TestMethod]
        public void GetFakeFileName_ContainsPolyglotNotebooksDirectory()
        {
            var path = KernelLanguageMap.GetFakeFileName("csharp");
            Assert.IsTrue(path.Contains("PolyglotNotebooks"), $"Expected path to contain 'PolyglotNotebooks', got: {path}");
        }

        [TestMethod]
        public void GetFakeFileName_ReturnsFullPath()
        {
            var path = KernelLanguageMap.GetFakeFileName("csharp");
            Assert.IsTrue(Path.IsPathRooted(path), $"Expected rooted path, got: {path}");
        }

        [TestMethod]
        public void GetFakeFileName_ConsecutiveCallsReturnUniquePaths()
        {
            var path1 = KernelLanguageMap.GetFakeFileName("csharp");
            var path2 = KernelLanguageMap.GetFakeFileName("csharp");
            Assert.AreNotEqual(path1, path2);
        }

        [TestMethod]
        public void GetFakeFileName_AllKernels_ProduceCorrectExtensions()
        {
            var expectations = new (string kernel, string ext)[]
            {
                ("csharp", ".cs"),
                ("fsharp", ".fs"),
                ("javascript", ".js"),
                ("typescript", ".ts"),
                ("python", ".py"),
                ("powershell", ".ps1"),
                ("sql", ".sql"),
                ("html", ".html"),
                ("markdown", ".md"),
            };

            foreach (var (kernel, ext) in expectations)
            {
                var path = KernelLanguageMap.GetFakeFileName(kernel);
                Assert.IsTrue(path.EndsWith(ext), $"Kernel '{kernel}': expected {ext} extension, got: {path}");
            }
        }
    }
}
