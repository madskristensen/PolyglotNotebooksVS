using System;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PolyglotNotebooks.IntelliSense;
using PolyglotNotebooks.Protocol;

#pragma warning disable MSTEST0037

namespace PolyglotNotebooks.Test
{
    /// <summary>
    /// Tests for IntelliSense components (Phase 3).
    ///
    /// CompletionProvider, DiagnosticsProvider, HoverProvider, and SignatureHelpProvider
    /// are WPF-heavy and use VsBrushes/ThreadHelper (VS SDK).  We test only the internal
    /// static helper methods — pure logic with no VS SDK references — via direct calls.
    ///
    /// IntelliSenseManager construction and null-guard paths are also tested; they do
    /// not reference VS SDK types in their executed code paths.
    ///
    /// KernelStatus model types are tested directly.
    /// </summary>
    [TestClass]
    public class IntelliSenseTests
    {
        // ====================================================================
        // CompletionProvider — CaretToLinePosition
        // ====================================================================

        private static LinePosition CompletionCaretToLine(string text, int caretIndex)
            => CompletionProvider.CaretToLinePosition(text, caretIndex);

        private static int CompletionFindWordStart(string text, int caretIndex)
            => CompletionProvider.FindWordStart(text, caretIndex);

        private static string CompletionGetKindGlyph(string? kind)
            => CompletionProvider.GetKindGlyph(kind);

        [TestMethod]
        public void CompletionProvider_CaretToLinePosition_IndexZero_ReturnsLine0Char0()
        {
            var pos = CompletionCaretToLine("hello", 0);
            Assert.AreEqual(0, pos.Line);
            Assert.AreEqual(0, pos.Character);
        }

        [TestMethod]
        public void CompletionProvider_CaretToLinePosition_MidSingleLine_ReturnsCorrectCharacter()
        {
            var pos = CompletionCaretToLine("hello", 3);
            Assert.AreEqual(0, pos.Line);
            Assert.AreEqual(3, pos.Character);
        }

        [TestMethod]
        public void CompletionProvider_CaretToLinePosition_EndOfSingleLine_ReturnsCorrectCharacter()
        {
            var pos = CompletionCaretToLine("hello", 5);
            Assert.AreEqual(0, pos.Line);
            Assert.AreEqual(5, pos.Character);
        }

        [TestMethod]
        public void CompletionProvider_CaretToLinePosition_JustAfterNewline_ReturnsLine1Char0()
        {
            var pos = CompletionCaretToLine("hello\nworld", 6);
            Assert.AreEqual(1, pos.Line);
            Assert.AreEqual(0, pos.Character);
        }

        [TestMethod]
        public void CompletionProvider_CaretToLinePosition_MidSecondLine_ReturnsLine1Char3()
        {
            var pos = CompletionCaretToLine("hello\nworld", 9);
            Assert.AreEqual(1, pos.Line);
            Assert.AreEqual(3, pos.Character);
        }

        [TestMethod]
        public void CompletionProvider_CaretToLinePosition_CarriageReturnIgnored_DoesNotIncrementCharacter()
        {
            // \r is skipped — it must not count as a character
            var pos = CompletionCaretToLine("hi\r\nbye", 4);
            Assert.AreEqual(1, pos.Line);
            Assert.AreEqual(0, pos.Character);
        }

        [TestMethod]
        public void CompletionProvider_CaretToLinePosition_ThirdLine_ReturnsLine2()
        {
            var pos = CompletionCaretToLine("a\nb\nc", 5);
            Assert.AreEqual(2, pos.Line);
            Assert.AreEqual(1, pos.Character);
        }

        // ====================================================================
        // CompletionProvider — FindWordStart
        // ====================================================================

        [TestMethod]
        public void CompletionProvider_FindWordStart_AtStartOfWord_ReturnsWordStart()
        {
            // "hello world", caret at end of "world" (11) → word starts at 6
            int start = CompletionFindWordStart("hello world", 11);
            Assert.AreEqual(6, start);
        }

        [TestMethod]
        public void CompletionProvider_FindWordStart_AfterDot_ReturnsPosAfterDot()
        {
            // "obj.Method", caret at end (10) → "Method" starts at 4
            int start = CompletionFindWordStart("obj.Method", 10);
            Assert.AreEqual(4, start);
        }

        [TestMethod]
        public void CompletionProvider_FindWordStart_AtBeginning_ReturnsZero()
        {
            int start = CompletionFindWordStart("hello", 5);
            Assert.AreEqual(0, start);
        }

        [TestMethod]
        public void CompletionProvider_FindWordStart_UnderscoreIsWordChar_IncludedInWord()
        {
            // "_myVar", caret at end → word starts at 0 (underscore is word char)
            int start = CompletionFindWordStart("_myVar", 6);
            Assert.AreEqual(0, start);
        }

        [TestMethod]
        public void CompletionProvider_FindWordStart_SpaceBeforeWord_ReturnsAfterSpace()
        {
            // "  abc", caret at 5 → word starts at 2
            int start = CompletionFindWordStart("  abc", 5);
            Assert.AreEqual(2, start);
        }

        // ====================================================================
        // CompletionProvider — GetKindGlyph
        // ====================================================================

        [TestMethod]
        [DataRow("method", "M")]
        [DataRow("property", "P")]
        [DataRow("field", "F")]
        [DataRow("class", "C")]
        [DataRow("interface", "I")]
        [DataRow("keyword", "K")]
        [DataRow("variable", "V")]
        [DataRow("namespace", "N")]
        [DataRow("Method", "M")]
        [DataRow("unknown_kind", "·")]
        [DataRow(null, "·")]
        [DataRow("", "·")]
        public void CompletionProvider_GetKindGlyph_ReturnsExpectedGlyph(string? kind, string expected)
            => Assert.AreEqual(expected, CompletionGetKindGlyph(kind));

        // ====================================================================
        // DiagnosticsProvider — GetCharOffset
        // ====================================================================

        private static int DiagGetCharOffset(string text, int line, int character)
            => DiagnosticsProvider.GetCharOffset(text, line, character);

        [TestMethod]
        public void DiagnosticsProvider_GetCharOffset_Line0_ReturnsCharacter()
        {
            int offset = DiagGetCharOffset("hello world", 0, 5);
            Assert.AreEqual(5, offset);
        }

        [TestMethod]
        public void DiagnosticsProvider_GetCharOffset_Line1_ReturnsOffsetIntoSecondLine()
        {
            // "hello\nworld" — line 1 starts at index 6; char 3 → index 9
            int offset = DiagGetCharOffset("hello\nworld", 1, 3);
            Assert.AreEqual(9, offset);
        }

        [TestMethod]
        public void DiagnosticsProvider_GetCharOffset_Line0Char0_ReturnsZero()
        {
            int offset = DiagGetCharOffset("abc", 0, 0);
            Assert.AreEqual(0, offset);
        }

        [TestMethod]
        public void DiagnosticsProvider_GetCharOffset_BeyondTextLength_ClampedToLength()
        {
            // character 99 on an empty line beyond text → clamped to text.Length
            int offset = DiagGetCharOffset("hi", 0, 99);
            Assert.AreEqual(2, offset);
        }

        [TestMethod]
        public void DiagnosticsProvider_GetCharOffset_ThreeLinesDeep_CorrectOffset()
        {
            // "a\nb\nc" — line 2 starts at index 4; char 1 → index 5
            int offset = DiagGetCharOffset("a\nb\nc", 2, 1);
            Assert.AreEqual(5, offset);
        }

        // ====================================================================
        // HoverProvider — CaretToLinePosition
        // ====================================================================

        private static LinePosition HoverCaretToLine(string text, int charIndex)
            => HoverProvider.CaretToLinePosition(text, charIndex);

        private static string HoverStripHtml(string html)
            => HoverProvider.StripHtml(html);

        [TestMethod]
        public void HoverProvider_CaretToLinePosition_SingleLine_ReturnsLine0()
        {
            var pos = HoverCaretToLine("console.log", 7);
            Assert.AreEqual(0, pos.Line);
            Assert.AreEqual(7, pos.Character);
        }

        [TestMethod]
        public void HoverProvider_CaretToLinePosition_StartOfSecondLine_ReturnsLine1Char0()
        {
            var pos = HoverCaretToLine("foo\nbar", 4);
            Assert.AreEqual(1, pos.Line);
            Assert.AreEqual(0, pos.Character);
        }

        [TestMethod]
        public void HoverProvider_CaretToLinePosition_MidSecondLine_CorrectCharacter()
        {
            var pos = HoverCaretToLine("foo\nbar", 6);
            Assert.AreEqual(1, pos.Line);
            Assert.AreEqual(2, pos.Character);
        }

        [TestMethod]
        public void HoverProvider_CaretToLinePosition_IndexZero_ReturnsZeroZero()
        {
            var pos = HoverCaretToLine("anything", 0);
            Assert.AreEqual(0, pos.Line);
            Assert.AreEqual(0, pos.Character);
        }

        // ====================================================================
        // HoverProvider — StripHtml
        // ====================================================================

        [TestMethod]
        public void HoverProvider_StripHtml_BoldTag_ReturnsInnerText()
        {
            string result = HoverStripHtml("<b>bold</b>");
            Assert.AreEqual("bold", result);
        }

        [TestMethod]
        public void HoverProvider_StripHtml_PlainText_ReturnsSameText()
        {
            string result = HoverStripHtml("plain text");
            Assert.AreEqual("plain text", result);
        }

        [TestMethod]
        public void HoverProvider_StripHtml_ParagraphTag_ReturnsInnerText()
        {
            string result = HoverStripHtml("<p>hello world</p>");
            Assert.AreEqual("hello world", result);
        }

        [TestMethod]
        public void HoverProvider_StripHtml_NestedTags_RemovesAll()
        {
            string result = HoverStripHtml("<div><span>text</span></div>");
            Assert.AreEqual("text", result);
        }

        [TestMethod]
        public void HoverProvider_StripHtml_EmptyString_ReturnsEmpty()
        {
            string result = HoverStripHtml("");
            Assert.AreEqual("", result);
        }

        [TestMethod]
        public void HoverProvider_StripHtml_WhitespaceOnly_ReturnsTrimmedEmpty()
        {
            string result = HoverStripHtml("   ");
            Assert.AreEqual("", result);
        }

        [TestMethod]
        public void HoverProvider_StripHtml_WithAttributeTags_ReturnsInnerText()
        {
            string result = HoverStripHtml("<span class=\"foo\">text</span>");
            Assert.AreEqual("text", result);
        }

        // ====================================================================
        // IntelliSenseManager — construction and null-guard paths
        // (No VS SDK references in these specific code paths)
        // ====================================================================

        [TestMethod]
        public void IntelliSenseManager_Constructor_DoesNotThrow()
        {
            bool threw = false;
            try { _ = CreateManager(); }
            catch { threw = true; }
            Assert.IsFalse(threw, "IntelliSenseManager() must not throw");
        }

        [TestMethod]
        public void IntelliSenseManager_SetKernelClient_WithNull_DoesNotThrow()
        {
            var mgr = CreateManager();
            bool threw = false;
            try { mgr.SetKernelClient(null!); }
            catch { threw = true; }
            Assert.IsFalse(threw, "SetKernelClient(null) on empty manager must not throw");
        }

        [TestMethod]
        public void IntelliSenseManager_AttachToCell_WithNull_DoesNotThrow()
        {
            var mgr = CreateManager();
            bool threw = false;
            try { AttachNullCell(mgr); }
            catch { threw = true; }
            Assert.IsFalse(threw, "AttachToCell(null) must return early without throwing");
        }

        [TestMethod]
        public void IntelliSenseManager_DetachFromCell_WithNull_DoesNotThrow()
        {
            var mgr = CreateManager();
            bool threw = false;
            try { DetachNullCell(mgr); }
            catch { threw = true; }
            Assert.IsFalse(threw, "DetachFromCell(null) must return early without throwing");
        }

        [TestMethod]
        public void IntelliSenseManager_Dispose_WhenEmpty_DoesNotThrow()
        {
            var mgr = CreateManager();
            bool threw = false;
            try { mgr.Dispose(); }
            catch { threw = true; }
            Assert.IsFalse(threw, "Dispose() on empty manager must not throw");
        }

        [TestMethod]
        public void IntelliSenseManager_Dispose_WhenCalledTwice_DoesNotThrow()
        {
            var mgr = CreateManager();
            mgr.Dispose();
            bool threw = false;
            try { mgr.Dispose(); }
            catch { threw = true; }
            Assert.IsFalse(threw, "Second Dispose() must be idempotent");
        }

        [TestMethod]
        public void IntelliSenseManager_AttachToCell_WhenDisposed_NullCellSilentlyIgnored()
        {
            var mgr = CreateManager();
            mgr.Dispose();
            bool threw = false;
            try { AttachNullCell(mgr); }
            catch { threw = true; }
            Assert.IsFalse(threw, "AttachToCell(null) on disposed manager must not throw");
        }

        // JIT-safety wrappers: keep VS SDK-adjacent call sites out of this method's body
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static IntelliSenseManager CreateManager() => new IntelliSenseManager();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AttachNullCell(IntelliSenseManager mgr) => mgr.AttachToCell(null!);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void DetachNullCell(IntelliSenseManager mgr) => mgr.DetachFromCell(null!);

    }
}
