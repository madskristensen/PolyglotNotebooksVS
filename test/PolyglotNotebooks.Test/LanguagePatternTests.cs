using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PolyglotNotebooks.Editor.SyntaxHighlighting;

#pragma warning disable MSTEST0037

namespace PolyglotNotebooks.Test
{
    /// <summary>
    /// Tests for <see cref="LanguagePattern"/> — the per-language regex registry
    /// used by <see cref="NotebookClassifier"/> for syntax highlighting.
    /// </summary>
    [TestClass]
    public class LanguagePatternTests
    {
        // ════════════════════════════════════════════════════════════════════════
        // Helpers
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns true if the named capture group fires for the given input.
        /// </summary>
        private static bool HasGroupMatch(Regex pattern, string input, string groupName)
        {
            foreach (Match m in pattern.Matches(input))
            {
                if (m.Groups[groupName].Success)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Returns the first captured value for the named group, or null.
        /// </summary>
        private static string? FirstGroupValue(Regex pattern, string input, string groupName)
        {
            foreach (Match m in pattern.Matches(input))
            {
                if (m.Groups[groupName].Success)
                    return m.Groups[groupName].Value;
            }
            return null;
        }

        // ════════════════════════════════════════════════════════════════════════
        // Lookup / resolution tests
        // ════════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void Get_NullKernelName_ReturnsNull()
        {
            Assert.IsNull(LanguagePattern.Get(null!));
        }

        [TestMethod]
        public void Get_EmptyKernelName_ReturnsNull()
        {
            Assert.IsNull(LanguagePattern.Get(string.Empty));
        }

        [TestMethod]
        public void Get_UnknownLanguage_ReturnsNull()
        {
            Assert.IsNull(LanguagePattern.Get("brainfuck"));
        }

        [TestMethod]
        public void Get_CSharp_ReturnsNonNullPattern()
        {
            var lp = LanguagePattern.Get("csharp");
            Assert.IsNotNull(lp);
            Assert.IsNotNull(lp.Pattern);
        }

        [TestMethod]
        public void Get_CSharpAlias_ReturnsPattern()
        {
            var lp = LanguagePattern.Get("c#");
            Assert.IsNotNull(lp);
            Assert.IsNotNull(lp.Pattern);
        }

        [TestMethod]
        public void Get_FSharp_ReturnsPattern()
        {
            Assert.IsNotNull(LanguagePattern.Get("fsharp"));
            Assert.IsNotNull(LanguagePattern.Get("f#"));
        }

        [TestMethod]
        public void Get_JavaScript_ReturnsPattern()
        {
            Assert.IsNotNull(LanguagePattern.Get("javascript"));
            Assert.IsNotNull(LanguagePattern.Get("js"));
        }

        [TestMethod]
        public void Get_TypeScript_ReturnsPattern()
        {
            Assert.IsNotNull(LanguagePattern.Get("typescript"));
            Assert.IsNotNull(LanguagePattern.Get("ts"));
        }

        [TestMethod]
        public void Get_Python_ReturnsPattern()
        {
            Assert.IsNotNull(LanguagePattern.Get("python"));
        }

        [TestMethod]
        public void Get_Sql_ReturnsPattern()
        {
            Assert.IsNotNull(LanguagePattern.Get("sql"));
        }

        [TestMethod]
        public void Get_PowerShell_ReturnsPattern()
        {
            Assert.IsNotNull(LanguagePattern.Get("powershell"));
            Assert.IsNotNull(LanguagePattern.Get("pwsh"));
        }

        [TestMethod]
        public void Get_Kql_ReturnsPattern()
        {
            Assert.IsNotNull(LanguagePattern.Get("kql"));
            Assert.IsNotNull(LanguagePattern.Get("kusto"));
        }

        [TestMethod]
        public void Get_Html_ReturnsPattern()
        {
            Assert.IsNotNull(LanguagePattern.Get("html"));
        }

        [TestMethod]
        public void Get_CompositeKqlKernel_ReturnsKqlPattern()
        {
            var pattern = LanguagePattern.Get("kql-Ddtelvsraw");
            Assert.IsNotNull(pattern, "kql-Ddtelvsraw should resolve to the KQL pattern");
        }

        [TestMethod]
        public void Get_CompositeSqlKernel_ReturnsSqlPattern()
        {
            var pattern = LanguagePattern.Get("sql-myServer");
            Assert.IsNotNull(pattern, "sql-myServer should resolve to the SQL pattern");
        }

        [TestMethod]
        public void Get_CompositeUnknownBase_ReturnsNull()
        {
            Assert.IsNull(LanguagePattern.Get("unknown-connection"));
        }

        [TestMethod]
        public void Get_IsCaseInsensitive()
        {
            var lower = LanguagePattern.Get("csharp");
            var upper = LanguagePattern.Get("CSHARP");
            var mixed = LanguagePattern.Get("CSharp");

            Assert.IsNotNull(lower);
            Assert.IsNotNull(upper);
            Assert.IsNotNull(mixed);
        }

        // ════════════════════════════════════════════════════════════════════════
        // C# regex matching
        // ════════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void CSharpPattern_MatchesKeyword_class()
        {
            var regex = LanguagePattern.Get("csharp")!.Pattern;
            Assert.IsTrue(HasGroupMatch(regex, "class Foo {}", "keyword"));
            Assert.AreEqual("class", FirstGroupValue(regex, "class Foo {}", "keyword"));
        }

        [TestMethod]
        public void CSharpPattern_MatchesKeyword_void()
        {
            var regex = LanguagePattern.Get("csharp")!.Pattern;
            Assert.IsTrue(HasGroupMatch(regex, "public void Run()", "keyword"));
            Assert.AreEqual("void", FirstGroupValue(regex, "void Run()", "keyword"));
        }

        [TestMethod]
        public void CSharpPattern_MatchesKeyword_async_await()
        {
            var regex = LanguagePattern.Get("csharp")!.Pattern;
            Assert.IsTrue(HasGroupMatch(regex, "async Task Go()", "keyword"));
            Assert.IsTrue(HasGroupMatch(regex, "await task;", "keyword"));
        }

        [TestMethod]
        public void CSharpPattern_MatchesString_DoubleQuoted()
        {
            var regex = LanguagePattern.Get("csharp")!.Pattern;
            Assert.IsTrue(HasGroupMatch(regex, "var s = \"hello\";", "string"));
            Assert.AreEqual("\"hello\"", FirstGroupValue(regex, "var s = \"hello\";", "string"));
        }

        [TestMethod]
        public void CSharpPattern_MatchesString_VerbatimString()
        {
            var regex = LanguagePattern.Get("csharp")!.Pattern;
            Assert.IsTrue(HasGroupMatch(regex, "var s = @\"path\\to\";", "string"));
        }

        [TestMethod]
        public void CSharpPattern_MatchesLineComment()
        {
            var regex = LanguagePattern.Get("csharp")!.Pattern;
            Assert.IsTrue(HasGroupMatch(regex, "// this is a comment", "comment"));
        }

        [TestMethod]
        public void CSharpPattern_MatchesBlockComment()
        {
            var regex = LanguagePattern.Get("csharp")!.Pattern;
            Assert.IsTrue(HasGroupMatch(regex, "/* block */", "comment"));
        }

        [TestMethod]
        public void CSharpPattern_MatchesNumber()
        {
            var regex = LanguagePattern.Get("csharp")!.Pattern;
            Assert.IsTrue(HasGroupMatch(regex, "int x = 42;", "number"));
            Assert.AreEqual("42", FirstGroupValue(regex, "int x = 42;", "number"));
        }

        [TestMethod]
        public void CSharpPattern_MatchesHexNumber()
        {
            var regex = LanguagePattern.Get("csharp")!.Pattern;
            Assert.IsTrue(HasGroupMatch(regex, "int x = 0xFF;", "number"));
        }

        [TestMethod]
        public void CSharpPattern_MatchesType()
        {
            var regex = LanguagePattern.Get("csharp")!.Pattern;
            Assert.IsTrue(HasGroupMatch(regex, "int x = 5;", "type"));
            Assert.AreEqual("int", FirstGroupValue(regex, "int x = 5;", "type"));
        }

        // ════════════════════════════════════════════════════════════════════════
        // Python regex matching
        // ════════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void PythonPattern_MatchesHashComment()
        {
            var regex = LanguagePattern.Get("python")!.Pattern;
            Assert.IsTrue(HasGroupMatch(regex, "# this is a comment", "comment"));
        }

        [TestMethod]
        public void PythonPattern_MatchesKeyword_def()
        {
            var regex = LanguagePattern.Get("python")!.Pattern;
            Assert.IsTrue(HasGroupMatch(regex, "def foo():", "keyword"));
            Assert.AreEqual("def", FirstGroupValue(regex, "def foo():", "keyword"));
        }

        [TestMethod]
        public void PythonPattern_MatchesKeyword_import()
        {
            var regex = LanguagePattern.Get("python")!.Pattern;
            Assert.IsTrue(HasGroupMatch(regex, "import os", "keyword"));
        }

        [TestMethod]
        public void PythonPattern_MatchesString_DoubleQuoted()
        {
            var regex = LanguagePattern.Get("python")!.Pattern;
            Assert.IsTrue(HasGroupMatch(regex, "x = \"hello\"", "string"));
        }

        [TestMethod]
        public void PythonPattern_MatchesTripleQuotedString()
        {
            var regex = LanguagePattern.Get("python")!.Pattern;
            Assert.IsTrue(HasGroupMatch(regex, "x = \"\"\"docstring\"\"\"", "string"));
        }

        [TestMethod]
        public void PythonPattern_MatchesType()
        {
            var regex = LanguagePattern.Get("python")!.Pattern;
            Assert.IsTrue(HasGroupMatch(regex, "x: int = 5", "type"));
            Assert.AreEqual("int", FirstGroupValue(regex, "x: int = 5", "type"));
        }

        // ════════════════════════════════════════════════════════════════════════
        // SQL regex matching
        // ════════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void SqlPattern_MatchesCaseInsensitiveKeyword_SELECT()
        {
            var regex = LanguagePattern.Get("sql")!.Pattern;
            Assert.IsTrue(HasGroupMatch(regex, "SELECT * FROM t", "keyword"));
            Assert.IsTrue(HasGroupMatch(regex, "select * from t", "keyword"));
        }

        [TestMethod]
        public void SqlPattern_MatchesLineComment()
        {
            var regex = LanguagePattern.Get("sql")!.Pattern;
            Assert.IsTrue(HasGroupMatch(regex, "-- comment here", "comment"));
        }

        [TestMethod]
        public void SqlPattern_MatchesSingleQuotedString()
        {
            var regex = LanguagePattern.Get("sql")!.Pattern;
            Assert.IsTrue(HasGroupMatch(regex, "WHERE name = 'Alice'", "string"));
        }

        [TestMethod]
        public void SqlPattern_MatchesNumber()
        {
            var regex = LanguagePattern.Get("sql")!.Pattern;
            Assert.IsTrue(HasGroupMatch(regex, "WHERE id = 123", "number"));
        }

        // ════════════════════════════════════════════════════════════════════════
        // JavaScript regex matching
        // ════════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void JavaScriptPattern_MatchesKeyword_function()
        {
            var regex = LanguagePattern.Get("javascript")!.Pattern;
            Assert.IsTrue(HasGroupMatch(regex, "function foo() {}", "keyword"));
            Assert.AreEqual("function", FirstGroupValue(regex, "function foo() {}", "keyword"));
        }

        [TestMethod]
        public void JavaScriptPattern_MatchesKeyword_const()
        {
            var regex = LanguagePattern.Get("javascript")!.Pattern;
            Assert.IsTrue(HasGroupMatch(regex, "const x = 1;", "keyword"));
        }

        [TestMethod]
        public void JavaScriptPattern_MatchesTemplateString()
        {
            var regex = LanguagePattern.Get("javascript")!.Pattern;
            Assert.IsTrue(HasGroupMatch(regex, "let s = `hello`;", "string"));
        }

        [TestMethod]
        public void JavaScriptPattern_MatchesLineComment()
        {
            var regex = LanguagePattern.Get("javascript")!.Pattern;
            Assert.IsTrue(HasGroupMatch(regex, "// comment", "comment"));
        }

        [TestMethod]
        public void JavaScriptPattern_MatchesNumber()
        {
            var regex = LanguagePattern.Get("javascript")!.Pattern;
            Assert.IsTrue(HasGroupMatch(regex, "let x = 3.14;", "number"));
        }

        // ════════════════════════════════════════════════════════════════════════
        // F# regex matching
        // ════════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void FSharpPattern_MatchesKeyword_let()
        {
            var regex = LanguagePattern.Get("fsharp")!.Pattern;
            Assert.IsTrue(HasGroupMatch(regex, "let x = 5", "keyword"));
            Assert.AreEqual("let", FirstGroupValue(regex, "let x = 5", "keyword"));
        }

        [TestMethod]
        public void FSharpPattern_MatchesParenStarComment()
        {
            var regex = LanguagePattern.Get("fsharp")!.Pattern;
            Assert.IsTrue(HasGroupMatch(regex, "(* comment *)", "comment"));
        }

        // ════════════════════════════════════════════════════════════════════════
        // PowerShell regex matching
        // ════════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void PowerShellPattern_MatchesHashComment()
        {
            var regex = LanguagePattern.Get("powershell")!.Pattern;
            Assert.IsTrue(HasGroupMatch(regex, "# comment", "comment"));
        }

        [TestMethod]
        public void PowerShellPattern_MatchesVariable()
        {
            var regex = LanguagePattern.Get("powershell")!.Pattern;
            // PowerShell variables ($var) are classified as "type" group
            Assert.IsTrue(HasGroupMatch(regex, "$myVar = 1", "type"));
        }

        [TestMethod]
        public void PowerShellPattern_MatchesKeyword_function()
        {
            var regex = LanguagePattern.Get("powershell")!.Pattern;
            Assert.IsTrue(HasGroupMatch(regex, "function Get-Data {}", "keyword"));
        }

        // ════════════════════════════════════════════════════════════════════════
        // HTML regex matching
        // ════════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void HtmlPattern_MatchesComment()
        {
            var regex = LanguagePattern.Get("html")!.Pattern;
            Assert.IsTrue(HasGroupMatch(regex, "<!-- comment -->", "comment"));
        }

        [TestMethod]
        public void HtmlPattern_MatchesTag()
        {
            var regex = LanguagePattern.Get("html")!.Pattern;
            // Tags match as "keyword" group
            Assert.IsTrue(HasGroupMatch(regex, "<div class=\"x\">", "keyword"));
        }

        // ════════════════════════════════════════════════════════════════════════
        // KQL regex matching
        // ════════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void KqlPattern_MatchesKeyword_where()
        {
            var regex = LanguagePattern.Get("kql")!.Pattern;
            Assert.IsTrue(HasGroupMatch(regex, "| where status == 200", "keyword"));
        }

        [TestMethod]
        public void KqlPattern_MatchesLineComment()
        {
            var regex = LanguagePattern.Get("kql")!.Pattern;
            Assert.IsTrue(HasGroupMatch(regex, "// comment line", "comment"));
        }
    }
}
