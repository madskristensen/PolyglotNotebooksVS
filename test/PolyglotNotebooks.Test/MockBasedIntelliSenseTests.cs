using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PolyglotNotebooks.IntelliSense;
using PolyglotNotebooks.Protocol;

#pragma warning disable MSTEST0037

namespace PolyglotNotebooks.Test
{
    /// <summary>
    /// Mock-based IntelliSense tests.
    ///
    /// NOTE ON MOCK LIMITATIONS:
    /// CompletionProvider, HoverProvider, DiagnosticsProvider, and SignatureHelpProvider
    /// are tightly coupled to WPF (TextBox, Popup, Adorner) and VS SDK (ThreadHelper,
    /// VsBrushes).  KernelClient is a sealed concrete class with no interface abstraction,
    /// so Moq cannot create substitutes for it.
    ///
    /// Full provider flow testing would require either:
    ///   1) Extracting an IKernelClient interface, or
    ///   2) A WPF test host with STA threading.
    ///
    /// These tests cover the pure-logic internal static helpers that were previously
    /// private and are now exposed for testing.  The existing IntelliSenseTests cover
    /// CompletionProvider, HoverProvider, and DiagnosticsProvider helpers; this file
    /// covers SignatureHelpProvider.CaretToLinePosition (newly extracted).
    /// </summary>
    [TestClass]
    public class MockBasedIntelliSenseTests
    {
        // ====================================================================
        // SignatureHelpProvider — CaretToLinePosition (promoted from private)
        // ====================================================================

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static LinePosition SigCaretToLine(string text, int caretIndex)
            => SignatureHelpProvider.CaretToLinePosition(text, caretIndex);

        [TestMethod]
        public void SignatureHelpProvider_CaretToLinePosition_IndexZero_ReturnsLine0Char0()
        {
            var pos = SigCaretToLine("hello", 0);
            Assert.AreEqual(0, pos.Line);
            Assert.AreEqual(0, pos.Character);
        }

        [TestMethod]
        public void SignatureHelpProvider_CaretToLinePosition_MidSingleLine_ReturnsCorrectCharacter()
        {
            var pos = SigCaretToLine("Console.Write(", 14);
            Assert.AreEqual(0, pos.Line);
            Assert.AreEqual(14, pos.Character);
        }

        [TestMethod]
        public void SignatureHelpProvider_CaretToLinePosition_EndOfSingleLine_ReturnsCorrectCharacter()
        {
            var pos = SigCaretToLine("abc", 3);
            Assert.AreEqual(0, pos.Line);
            Assert.AreEqual(3, pos.Character);
        }

        [TestMethod]
        public void SignatureHelpProvider_CaretToLinePosition_JustAfterNewline_ReturnsLine1Char0()
        {
            var pos = SigCaretToLine("hello\nworld", 6);
            Assert.AreEqual(1, pos.Line);
            Assert.AreEqual(0, pos.Character);
        }

        [TestMethod]
        public void SignatureHelpProvider_CaretToLinePosition_MidSecondLine_ReturnsCorrectPosition()
        {
            var pos = SigCaretToLine("var x = 1;\nConsole.Write(x,", 25);
            Assert.AreEqual(1, pos.Line);
            Assert.AreEqual(14, pos.Character);
        }

        [TestMethod]
        public void SignatureHelpProvider_CaretToLinePosition_CarriageReturnIgnored()
        {
            // \r should not increment character count
            var pos = SigCaretToLine("hi\r\nbye", 4);
            Assert.AreEqual(1, pos.Line);
            Assert.AreEqual(0, pos.Character);
        }

        [TestMethod]
        public void SignatureHelpProvider_CaretToLinePosition_ThirdLine_ReturnsLine2()
        {
            var pos = SigCaretToLine("a\nb\nc", 5);
            Assert.AreEqual(2, pos.Line);
            Assert.AreEqual(1, pos.Character);
        }

        [TestMethod]
        public void SignatureHelpProvider_CaretToLinePosition_EmptyString_ReturnsZeroZero()
        {
            var pos = SigCaretToLine("", 0);
            Assert.AreEqual(0, pos.Line);
            Assert.AreEqual(0, pos.Character);
        }

        [TestMethod]
        public void SignatureHelpProvider_CaretToLinePosition_CaretBeyondText_HandledGracefully()
        {
            // Caret past end of text — loop stops at text.Length
            var pos = SigCaretToLine("ab", 5);
            Assert.AreEqual(0, pos.Line);
            Assert.AreEqual(2, pos.Character);
        }

        [TestMethod]
        public void SignatureHelpProvider_CaretToLinePosition_MultipleNewlines_TracksLinesCorrectly()
        {
            // Three empty lines then "x"
            var pos = SigCaretToLine("\n\n\nx", 4);
            Assert.AreEqual(3, pos.Line);
            Assert.AreEqual(1, pos.Character);
        }

        [TestMethod]
        public void SignatureHelpProvider_CaretToLinePosition_WindowsNewlines_CorrectLine()
        {
            // "line1\r\nline2" — caret at start of "line2" (index 7)
            var pos = SigCaretToLine("line1\r\nline2", 7);
            Assert.AreEqual(1, pos.Line);
            Assert.AreEqual(0, pos.Character);
        }
    }
}
