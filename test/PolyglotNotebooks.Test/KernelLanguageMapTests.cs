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
        public void GetContentTypeName_CSharp_ReturnsCSharp()
            => Assert.AreEqual("CSharp", KernelLanguageMap.GetContentTypeName("csharp"));

        [TestMethod]
        public void GetContentTypeName_FSharp_ReturnsFSharp()
            => Assert.AreEqual("F#", KernelLanguageMap.GetContentTypeName("fsharp"));

        [TestMethod]
        public void GetContentTypeName_JavaScript_ReturnsJavaScript()
            => Assert.AreEqual("JavaScript", KernelLanguageMap.GetContentTypeName("javascript"));

        [TestMethod]
        public void GetContentTypeName_TypeScript_ReturnsTypeScript()
            => Assert.AreEqual("TypeScript", KernelLanguageMap.GetContentTypeName("typescript"));

        [TestMethod]
        public void GetContentTypeName_Python_ReturnsPython()
            => Assert.AreEqual("Python", KernelLanguageMap.GetContentTypeName("python"));

        [TestMethod]
        public void GetContentTypeName_PowerShell_ReturnsPowerShell()
            => Assert.AreEqual("PowerShell", KernelLanguageMap.GetContentTypeName("powershell"));

        [TestMethod]
        public void GetContentTypeName_Markdown_ReturnsMarkdown()
            => Assert.AreEqual("markdown", KernelLanguageMap.GetContentTypeName("markdown"));

        // ── Regression: "sql" must map to "SQL Server Tools", NOT "SQL" ──

        [TestMethod]
        public void GetContentTypeName_Sql_ReturnsSqlServerTools()
            => Assert.AreEqual("SQL Server Tools", KernelLanguageMap.GetContentTypeName("sql"));

        [TestMethod]
        public void GetContentTypeName_Sql_IsNotBareSQL()
            => Assert.AreNotEqual("SQL", KernelLanguageMap.GetContentTypeName("sql"));

        // ── Regression: "html" must map to "HTML", NOT "html" or "htmlx" ──

        [TestMethod]
        public void GetContentTypeName_Html_ReturnsHTML()
            => Assert.AreEqual("HTML", KernelLanguageMap.GetContentTypeName("html"));

        [TestMethod]
        public void GetContentTypeName_Html_IsNotLowercase()
            => Assert.AreNotEqual("html", KernelLanguageMap.GetContentTypeName("html"));

        [TestMethod]
        public void GetContentTypeName_Html_IsNotHtmlx()
            => Assert.AreNotEqual("htmlx", KernelLanguageMap.GetContentTypeName("html"));

        // ── Unknown kernel returns null ──

        [TestMethod]
        public void GetContentTypeName_UnknownKernel_ReturnsNull()
            => Assert.IsNull(KernelLanguageMap.GetContentTypeName("ruby"));

        // ── Case insensitivity ──

        [TestMethod]
        public void GetContentTypeName_IsCaseInsensitive_UpperCase()
            => Assert.AreEqual("CSharp", KernelLanguageMap.GetContentTypeName("CSHARP"));

        [TestMethod]
        public void GetContentTypeName_IsCaseInsensitive_MixedCase()
            => Assert.AreEqual("CSharp", KernelLanguageMap.GetContentTypeName("CSharp"));

        [TestMethod]
        public void GetContentTypeName_Sql_IsCaseInsensitive()
            => Assert.AreEqual("SQL Server Tools", KernelLanguageMap.GetContentTypeName("SQL"));

        [TestMethod]
        public void GetContentTypeName_Html_IsCaseInsensitive()
            => Assert.AreEqual("HTML", KernelLanguageMap.GetContentTypeName("HTML"));

        // ── File Extension Mapping ────────────────────────────────────

        [TestMethod]
        public void GetFileExtension_CSharp_ReturnsCs()
            => Assert.AreEqual(".cs", KernelLanguageMap.GetFileExtension("csharp"));

        [TestMethod]
        public void GetFileExtension_FSharp_ReturnsFs()
            => Assert.AreEqual(".fs", KernelLanguageMap.GetFileExtension("fsharp"));

        [TestMethod]
        public void GetFileExtension_JavaScript_ReturnsJs()
            => Assert.AreEqual(".js", KernelLanguageMap.GetFileExtension("javascript"));

        [TestMethod]
        public void GetFileExtension_TypeScript_ReturnsTs()
            => Assert.AreEqual(".ts", KernelLanguageMap.GetFileExtension("typescript"));

        [TestMethod]
        public void GetFileExtension_Python_ReturnsPy()
            => Assert.AreEqual(".py", KernelLanguageMap.GetFileExtension("python"));

        [TestMethod]
        public void GetFileExtension_PowerShell_ReturnsPs1()
            => Assert.AreEqual(".ps1", KernelLanguageMap.GetFileExtension("powershell"));

        [TestMethod]
        public void GetFileExtension_Sql_ReturnsSql()
            => Assert.AreEqual(".sql", KernelLanguageMap.GetFileExtension("sql"));

        [TestMethod]
        public void GetFileExtension_Html_ReturnsHtml()
            => Assert.AreEqual(".html", KernelLanguageMap.GetFileExtension("html"));

        [TestMethod]
        public void GetFileExtension_Markdown_ReturnsMd()
            => Assert.AreEqual(".md", KernelLanguageMap.GetFileExtension("markdown"));

        [TestMethod]
        public void GetFileExtension_UnknownKernel_ReturnsTxt()
            => Assert.AreEqual(".txt", KernelLanguageMap.GetFileExtension("ruby"));

        [TestMethod]
        public void GetFileExtension_NullKernel_ReturnsTxt()
            => Assert.AreEqual(".txt", KernelLanguageMap.GetFileExtension(null));

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
