using System;
using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PolyglotNotebooks.Execution;
using PolyglotNotebooks.Protocol;

#pragma warning disable MSTEST0037

namespace PolyglotNotebooks.Test
{
    /// <summary>
    /// Tests for <see cref="CellExecutionEngine"/>.
    ///
    /// The full execution pipeline (ExecuteCellAsync) requires a live kernel and VS
    /// thread infrastructure; those paths are covered by integration tests.  Here we
    /// test the two internal-static helpers (MapKernelName, IsTerminalEvent) that contain
    /// the pure business logic, plus the constructor guard and Dispose idempotency.
    /// </summary>
    [TestClass]
    public class CellExecutionEngineTests
    {
        // ── MapKernelName — C# variants ───────────────────────────────────────

        [TestMethod]
        public void MapKernelName_WhenCSharpSymbol_ReturnsCsharp()
            => Assert.AreEqual("csharp", CellExecutionEngine.MapKernelName("C#"));

        [TestMethod]
        public void MapKernelName_WhenCsharpLowerCase_ReturnsCsharp()
            => Assert.AreEqual("csharp", CellExecutionEngine.MapKernelName("csharp"));

        [TestMethod]
        public void MapKernelName_WhenCsharpMixedCase_ReturnsCsharp()
            => Assert.AreEqual("csharp", CellExecutionEngine.MapKernelName("CSharp"));

        [TestMethod]
        public void MapKernelName_WhenNull_DefaultsToCsharp()
            => Assert.AreEqual("csharp", CellExecutionEngine.MapKernelName(null));

        [TestMethod]
        public void MapKernelName_WhenEmpty_DefaultsToCsharp()
            => Assert.AreEqual("csharp", CellExecutionEngine.MapKernelName(""));

        // ── MapKernelName — F# variants ───────────────────────────────────────

        [TestMethod]
        public void MapKernelName_WhenFSharpSymbol_ReturnsFsharp()
            => Assert.AreEqual("fsharp", CellExecutionEngine.MapKernelName("F#"));

        [TestMethod]
        public void MapKernelName_WhenFsharpLowerCase_ReturnsFsharp()
            => Assert.AreEqual("fsharp", CellExecutionEngine.MapKernelName("fsharp"));

        // ── MapKernelName — JavaScript variants ──────────────────────────────

        [TestMethod]
        public void MapKernelName_WhenJavaScript_ReturnsJavascript()
            => Assert.AreEqual("javascript", CellExecutionEngine.MapKernelName("JavaScript"));

        [TestMethod]
        public void MapKernelName_WhenJsAlias_ReturnsJavascript()
            => Assert.AreEqual("javascript", CellExecutionEngine.MapKernelName("js"));

        [TestMethod]
        public void MapKernelName_WhenJavascriptLowerCase_ReturnsJavascript()
            => Assert.AreEqual("javascript", CellExecutionEngine.MapKernelName("javascript"));

        // ── MapKernelName — SQL ───────────────────────────────────────────────

        [TestMethod]
        public void MapKernelName_WhenSQLUpperCase_ReturnsSql()
            => Assert.AreEqual("sql", CellExecutionEngine.MapKernelName("SQL"));

        [TestMethod]
        public void MapKernelName_WhenSqlLowerCase_ReturnsSql()
            => Assert.AreEqual("sql", CellExecutionEngine.MapKernelName("sql"));

        // ── MapKernelName — PowerShell variants ──────────────────────────────

        [TestMethod]
        public void MapKernelName_WhenPwsh_ReturnsPwsh()
            => Assert.AreEqual("pwsh", CellExecutionEngine.MapKernelName("pwsh"));

        [TestMethod]
        public void MapKernelName_WhenPowershell_ReturnsPwsh()
            => Assert.AreEqual("pwsh", CellExecutionEngine.MapKernelName("powershell"));

        [TestMethod]
        public void MapKernelName_WhenPowerShellMixedCase_ReturnsPwsh()
            => Assert.AreEqual("pwsh", CellExecutionEngine.MapKernelName("PowerShell"));

        // ── MapKernelName — HTML / Markdown ───────────────────────────────────

        [TestMethod]
        public void MapKernelName_WhenHtml_ReturnsHtml()
            => Assert.AreEqual("html", CellExecutionEngine.MapKernelName("html"));

        [TestMethod]
        public void MapKernelName_WhenHtmlUpperCase_ReturnsHtml()
            => Assert.AreEqual("html", CellExecutionEngine.MapKernelName("HTML"));

        [TestMethod]
        public void MapKernelName_WhenMarkdown_ReturnsMarkdown()
            => Assert.AreEqual("markdown", CellExecutionEngine.MapKernelName("markdown"));

        [TestMethod]
        public void MapKernelName_WhenMdAlias_ReturnsMarkdown()
            => Assert.AreEqual("markdown", CellExecutionEngine.MapKernelName("md"));

        // ── MapKernelName — unknown passthrough ───────────────────────────────

        [TestMethod]
        public void MapKernelName_WhenUnknownKernel_PassesThrough()
            => Assert.AreEqual("mycustomkernel", CellExecutionEngine.MapKernelName("mycustomkernel"));

        // ── IsTerminalEvent ───────────────────────────────────────────────────

        [TestMethod]
        public void IsTerminalEvent_WhenCommandSucceeded_ReturnsTrue()
            => Assert.IsTrue(CellExecutionEngine.IsTerminalEvent(KernelEventTypes.CommandSucceeded));

        [TestMethod]
        public void IsTerminalEvent_WhenCommandFailed_ReturnsTrue()
            => Assert.IsTrue(CellExecutionEngine.IsTerminalEvent(KernelEventTypes.CommandFailed));

        [TestMethod]
        public void IsTerminalEvent_WhenReturnValueProduced_ReturnsFalse()
            => Assert.IsFalse(CellExecutionEngine.IsTerminalEvent(KernelEventTypes.ReturnValueProduced));

        [TestMethod]
        public void IsTerminalEvent_WhenStandardOutputProduced_ReturnsFalse()
            => Assert.IsFalse(CellExecutionEngine.IsTerminalEvent(KernelEventTypes.StandardOutputValueProduced));

        [TestMethod]
        public void IsTerminalEvent_WhenStandardErrorProduced_ReturnsFalse()
            => Assert.IsFalse(CellExecutionEngine.IsTerminalEvent(KernelEventTypes.StandardErrorValueProduced));

        [TestMethod]
        public void IsTerminalEvent_WhenDisplayedValueProduced_ReturnsFalse()
            => Assert.IsFalse(CellExecutionEngine.IsTerminalEvent(KernelEventTypes.DisplayedValueProduced));

        [TestMethod]
        public void IsTerminalEvent_WhenKernelReady_ReturnsFalse()
            => Assert.IsFalse(CellExecutionEngine.IsTerminalEvent(KernelEventTypes.KernelReady));

        [TestMethod]
        public void IsTerminalEvent_WhenErrorProduced_ReturnsFalse()
            => Assert.IsFalse(CellExecutionEngine.IsTerminalEvent(KernelEventTypes.ErrorProduced));

        // ── Constructor guard ─────────────────────────────────────────────────

        [TestMethod]
        public void Constructor_WhenNullKernelClient_ThrowsArgumentNullException()
        {
            bool threw = false;
            try { _ = new CellExecutionEngine(null!); }
            catch (ArgumentNullException) { threw = true; }
            Assert.IsTrue(threw, "Expected ArgumentNullException for null KernelClient");
        }

        // ── Dispose idempotency ───────────────────────────────────────────────

        [TestMethod]
        public void Dispose_WhenCalledTwice_DoesNotThrow()
        {
            // Use the current process as a stub; we are not sending commands so
            // the process's stdin/stdout are irrelevant here.
            var process = Process.GetCurrentProcess();
            var client = new KernelClient(process);
            var engine = new CellExecutionEngine(client);

            engine.Dispose();

            bool threw = false;
            try { engine.Dispose(); }
            catch { threw = true; }
            Assert.IsFalse(threw, "Second Dispose() must be a no-op");
        }

        [TestMethod]
        public void Dispose_WhenNeverStarted_DoesNotThrow()
        {
            var process = Process.GetCurrentProcess();
            var client = new KernelClient(process);
            var engine = new CellExecutionEngine(client);

            bool threw = false;
            try { engine.Dispose(); }
            catch { threw = true; }
            Assert.IsFalse(threw, "Dispose on fresh instance must not throw");
        }
    }
}
