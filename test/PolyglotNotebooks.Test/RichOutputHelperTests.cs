using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PolyglotNotebooks.Editor;

#pragma warning disable MSTEST0037

namespace PolyglotNotebooks.Test
{
    /// <summary>
    /// Tests for the pure static helper methods on OutputControl and ImageOutputControl
    /// (Phase 3 / Phase 4 rich output rendering).
    ///
    /// OutputControl's UI methods (Rebuild, RenderOutput, etc.) reference VsBrushes and
    /// WebView2OutputHost and are not testable in the unit-test runner.  The internal static
    /// helpers (MarkdownToHtml, InlineMarkdown, CsvToHtmlTable, ParseCsvLine) are pure string
    /// processing with no VS SDK references and are called directly.
    ///
    /// ImageOutputControl.StripDataUri is likewise a pure internal static helper tested directly.
    /// </summary>
    [TestClass]
    public class RichOutputHelperTests
    {
        // ====================================================================
        // Direct call wrappers — OutputControl internal static helpers
        // ====================================================================

        private static string MarkdownToHtml(string markdown)
            => OutputControl.MarkdownToHtml(markdown);

        private static string InlineMarkdown(string text)
            => OutputControl.InlineMarkdown(text);

        private static string CsvToHtmlTable(string csv)
            => OutputControl.CsvToHtmlTable(csv);

        private static string[] ParseCsvLine(string line)
            => OutputControl.ParseCsvLine(line);

        // ====================================================================
        // MarkdownToHtml — ATX headings
        // ====================================================================

        [TestMethod]
        public void MarkdownToHtml_H1_ContainsH1Tag()
        {
            string html = MarkdownToHtml("# Title");
            StringAssert.Contains(html, "<h1>");
            StringAssert.Contains(html, "Title");
        }

        [TestMethod]
        public void MarkdownToHtml_H2_ContainsH2Tag()
        {
            string html = MarkdownToHtml("## Section");
            StringAssert.Contains(html, "<h2>");
            StringAssert.Contains(html, "Section");
        }

        [TestMethod]
        public void MarkdownToHtml_H3_ContainsH3Tag()
        {
            string html = MarkdownToHtml("### Sub");
            StringAssert.Contains(html, "<h3>");
            StringAssert.Contains(html, "Sub");
        }

        [TestMethod]
        public void MarkdownToHtml_RegularLine_ContainsParagraphTag()
        {
            string html = MarkdownToHtml("some text");
            StringAssert.Contains(html, "<p>");
            StringAssert.Contains(html, "some text");
        }

        [TestMethod]
        public void MarkdownToHtml_BlankLine_ContainsBrTag()
        {
            string html = MarkdownToHtml("\n");
            StringAssert.Contains(html, "<br>");
        }

        // ====================================================================
        // MarkdownToHtml — unordered lists
        // ====================================================================

        [TestMethod]
        public void MarkdownToHtml_DashListItem_ContainsUlAndLi()
        {
            string html = MarkdownToHtml("- item one");
            StringAssert.Contains(html, "<ul>");
            StringAssert.Contains(html, "<li>");
            StringAssert.Contains(html, "item one");
        }

        [TestMethod]
        public void MarkdownToHtml_AsteriskListItem_ContainsLi()
        {
            string html = MarkdownToHtml("* item two");
            StringAssert.Contains(html, "<li>");
            StringAssert.Contains(html, "item two");
        }

        [TestMethod]
        public void MarkdownToHtml_MultipleListItems_AllRenderedAsLi()
        {
            string html = MarkdownToHtml("- alpha\n- beta\n- gamma");
            StringAssert.Contains(html, "alpha");
            StringAssert.Contains(html, "beta");
            StringAssert.Contains(html, "gamma");
            StringAssert.Contains(html, "</ul>");
        }

        // ====================================================================
        // MarkdownToHtml — code blocks
        // ====================================================================

        [TestMethod]
        public void MarkdownToHtml_TripleBacktickCodeBlock_ContainsPreCode()
        {
            string html = MarkdownToHtml("```\nvar x = 1;\n```");
            StringAssert.Contains(html, "<pre><code>");
            StringAssert.Contains(html, "</code></pre>");
        }

        [TestMethod]
        public void MarkdownToHtml_TildeCodeBlock_ContainsPreCode()
        {
            string html = MarkdownToHtml("~~~\ncode here\n~~~");
            StringAssert.Contains(html, "<pre><code>");
            StringAssert.Contains(html, "</code></pre>");
        }

        [TestMethod]
        public void MarkdownToHtml_CodeBlockContent_HtmlEncoded()
        {
            // Content inside a code block should be HTML-encoded to prevent injection
            string html = MarkdownToHtml("```\n<script>alert(1)</script>\n```");
            StringAssert.Contains(html, "&lt;script&gt;");
        }

        // ====================================================================
        // InlineMarkdown — bold, italic, inline code, links
        // ====================================================================

        [TestMethod]
        public void InlineMarkdown_DoubleAsteriskBold_WrapsInStrong()
        {
            string html = InlineMarkdown("**bold text**");
            StringAssert.Contains(html, "<strong>");
            StringAssert.Contains(html, "bold text");
        }

        [TestMethod]
        public void InlineMarkdown_DoubleUnderscoreBold_WrapsInStrong()
        {
            string html = InlineMarkdown("__bold__");
            StringAssert.Contains(html, "<strong>");
            StringAssert.Contains(html, "bold");
        }

        [TestMethod]
        public void InlineMarkdown_SingleAsteriskItalic_WrapsInEm()
        {
            string html = InlineMarkdown("*italic*");
            StringAssert.Contains(html, "<em>");
            StringAssert.Contains(html, "italic");
        }

        [TestMethod]
        public void InlineMarkdown_BacktickInlineCode_WrapsInCode()
        {
            string html = InlineMarkdown("`myVar`");
            StringAssert.Contains(html, "<code>");
            StringAssert.Contains(html, "myVar");
        }

        [TestMethod]
        public void InlineMarkdown_LinkSyntax_RendersAnchorTag()
        {
            string html = InlineMarkdown("[GitHub](https://github.com)");
            StringAssert.Contains(html, "<a href=");
            StringAssert.Contains(html, "GitHub");
            StringAssert.Contains(html, "https://github.com");
        }

        [TestMethod]
        public void InlineMarkdown_HtmlSpecialChars_AreEncoded()
        {
            // Input with < > & should be HTML-encoded before markdown patterns run
            string html = InlineMarkdown("<script>");
            StringAssert.Contains(html, "&lt;script&gt;");
            Assert.IsFalse(html.Contains("<script>"), "Raw <script> tag must not appear in output");
        }

        [TestMethod]
        public void InlineMarkdown_PlainText_ReturnedWithHtmlEncoding()
        {
            string html = InlineMarkdown("hello world");
            StringAssert.Contains(html, "hello world");
        }

        // ====================================================================
        // CsvToHtmlTable
        // ====================================================================

        [TestMethod]
        public void CsvToHtmlTable_EmptyCsv_ReturnsEmptyParagraph()
        {
            string html = CsvToHtmlTable("");
            StringAssert.Contains(html, "Empty CSV");
        }

        [TestMethod]
        public void CsvToHtmlTable_HeaderRow_WrappedInThead()
        {
            string html = CsvToHtmlTable("Name,Age\nAlice,30");
            StringAssert.Contains(html, "<thead>");
            StringAssert.Contains(html, "<th>");
            StringAssert.Contains(html, "Name");
            StringAssert.Contains(html, "Age");
        }

        [TestMethod]
        public void CsvToHtmlTable_DataRow_WrappedInTd()
        {
            string html = CsvToHtmlTable("Name,Age\nAlice,30");
            StringAssert.Contains(html, "<td>");
            StringAssert.Contains(html, "Alice");
            StringAssert.Contains(html, "30");
        }

        [TestMethod]
        public void CsvToHtmlTable_MultipleDataRows_AllPresent()
        {
            string html = CsvToHtmlTable("A,B\n1,2\n3,4");
            StringAssert.Contains(html, "1");
            StringAssert.Contains(html, "2");
            StringAssert.Contains(html, "3");
            StringAssert.Contains(html, "4");
        }

        [TestMethod]
        public void CsvToHtmlTable_HtmlSpecialCharsInValue_AreEscaped()
        {
            string html = CsvToHtmlTable("Col\n<b>bold</b>");
            StringAssert.Contains(html, "&lt;b&gt;");
            Assert.IsFalse(html.Contains("<b>bold</b>"), "Raw HTML in CSV must be escaped");
        }

        [TestMethod]
        public void CsvToHtmlTable_ClosingTags_Present()
        {
            string html = CsvToHtmlTable("X\n1");
            StringAssert.Contains(html, "</table>");
            StringAssert.Contains(html, "</tbody>");
        }

        // ====================================================================
        // ParseCsvLine
        // ====================================================================

        [TestMethod]
        public void ParseCsvLine_SimpleFields_ReturnsSplitArray()
        {
            string[] fields = ParseCsvLine("a,b,c");
            Assert.AreEqual(3, fields.Length);
            Assert.AreEqual("a", fields[0]);
            Assert.AreEqual("b", fields[1]);
            Assert.AreEqual("c", fields[2]);
        }

        [TestMethod]
        public void ParseCsvLine_QuotedField_PreservesCommaInside()
        {
            string[] fields = ParseCsvLine("\"hello, world\",second");
            Assert.AreEqual(2, fields.Length);
            Assert.AreEqual("hello, world", fields[0]);
            Assert.AreEqual("second", fields[1]);
        }

        [TestMethod]
        public void ParseCsvLine_EscapedQuoteInsideQuotedField_UnescapedCorrectly()
        {
            // RFC 4180: "" inside a quoted field represents a single "
            string[] fields = ParseCsvLine("\"say \"\"hi\"\"\"");
            Assert.AreEqual(1, fields.Length);
            Assert.AreEqual("say \"hi\"", fields[0]);
        }

        [TestMethod]
        public void ParseCsvLine_SingleField_ReturnsSingleElement()
        {
            string[] fields = ParseCsvLine("only");
            Assert.AreEqual(1, fields.Length);
            Assert.AreEqual("only", fields[0]);
        }

        [TestMethod]
        public void ParseCsvLine_EmptyField_ReturnsEmptyString()
        {
            string[] fields = ParseCsvLine("a,,c");
            Assert.AreEqual(3, fields.Length);
            Assert.AreEqual("", fields[1]);
        }

        [TestMethod]
        public void ParseCsvLine_TrailingComma_ProducesEmptyLastField()
        {
            string[] fields = ParseCsvLine("a,b,");
            Assert.AreEqual(3, fields.Length);
            Assert.AreEqual("", fields[2]);
        }

        // ====================================================================
        // ImageOutputControl — StripDataUri (static private helper)
        // ====================================================================

        private static string StripDataUri(string data)
            => ImageOutputControl.StripDataUri(data);

        [TestMethod]
        public void ImageOutputControl_StripDataUri_WithDataPrefix_ReturnsPayload()
        {
            // "data:image/png;base64,ABC123" → "ABC123"
            string result = StripDataUri("data:image/png;base64,ABC123");
            Assert.AreEqual("ABC123", result);
        }

        [TestMethod]
        public void ImageOutputControl_StripDataUri_WithoutPrefix_ReturnsSameString()
        {
            string result = StripDataUri("ABC123");
            Assert.AreEqual("ABC123", result);
        }

        [TestMethod]
        public void ImageOutputControl_StripDataUri_EmptyString_ReturnsEmpty()
        {
            string result = StripDataUri("");
            Assert.AreEqual("", result);
        }

        [TestMethod]
        public void ImageOutputControl_StripDataUri_OnlyComma_ReturnsEmptyAfterComma()
        {
            // Input is ",X" → after the comma is "X"
            string result = StripDataUri(",payload");
            Assert.AreEqual("payload", result);
        }

        [TestMethod]
        public void ImageOutputControl_StripDataUri_MultipleCommas_SplitsOnFirst()
        {
            // "data:text/plain;base64,foo,bar" → "foo,bar"
            string result = StripDataUri("data:text/plain;base64,foo,bar");
            Assert.AreEqual("foo,bar", result);
        }
    }
}
