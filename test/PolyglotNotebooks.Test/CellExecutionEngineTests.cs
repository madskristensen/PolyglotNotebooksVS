using System;
using System.Diagnostics;
using System.Reflection;
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
    /// test the two private-static helpers (MapKernelName, IsTerminalEvent) that contain
    /// the pure business logic, plus the constructor guard and Dispose idempotency.
    /// </summary>
    [TestClass]
    public class CellExecutionEngineTests
    {
        // ── Reflection handles ────────────────────────────────────────────────

        private static readonly MethodInfo _mapKernelName =
            typeof(CellExecutionEngine)
                .GetMethod("MapKernelName", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("MapKernelName not found via reflection.");

        private static readonly MethodInfo _isTerminalEvent =
            typeof(CellExecutionEngine)
                .GetMethod("IsTerminalEvent", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("IsTerminalEvent not found via reflection.");

        private static string MapKernelName(string? input)
            => (string)_mapKernelName.Invoke(null, new object?[] { input })!;

        private static bool IsTerminalEvent(string eventType)
            => (bool)_isTerminalEvent.Invoke(null, new object[] { eventType })!;

        // ── MapKernelName — C# variants ───────────────────────────────────────

        [TestMethod]
        public void MapKernelName_WhenCSharpSymbol_ReturnsCsharp()
            => Assert.AreEqual("csharp", MapKernelName("C#"));

        [TestMethod]
        public void MapKernelName_WhenCsharpLowerCase_ReturnsCsharp()
            => Assert.AreEqual("csharp", MapKernelName("csharp"));

        [TestMethod]
        public void MapKernelName_WhenCsharpMixedCase_ReturnsCsharp()
            => Assert.AreEqual("csharp", MapKernelName("CSharp"));

        [TestMethod]
        public void MapKernelName_WhenNull_DefaultsToCsharp()
            => Assert.AreEqual("csharp", MapKernelName(null));

        [TestMethod]
        public void MapKernelName_WhenEmpty_DefaultsToCsharp()
            => Assert.AreEqual("csharp", MapKernelName(""));

        // ── MapKernelName — F# variants ───────────────────────────────────────

        [TestMethod]
        public void MapKernelName_WhenFSharpSymbol_ReturnsFsharp()
            => Assert.AreEqual("fsharp", MapKernelName("F#"));

        [TestMethod]
        public void MapKernelName_WhenFsharpLowerCase_ReturnsFsharp()
            => Assert.AreEqual("fsharp", MapKernelName("fsharp"));

        // ── MapKernelName — JavaScript variants ──────────────────────────────

        [TestMethod]
        public void MapKernelName_WhenJavaScript_ReturnsJavascript()
            => Assert.AreEqual("javascript", MapKernelName("JavaScript"));

        [TestMethod]
        public void MapKernelName_WhenJsAlias_ReturnsJavascript()
            => Assert.AreEqual("javascript", MapKernelName("js"));

        [TestMethod]
        public void MapKernelName_WhenJavascriptLowerCase_ReturnsJavascript()
            => Assert.AreEqual("javascript", MapKernelName("javascript"));

        // ── MapKernelName — SQL ───────────────────────────────────────────────

        [TestMethod]
        public void MapKernelName_WhenSQLUpperCase_ReturnsSql()
            => Assert.AreEqual("sql", MapKernelName("SQL"));

        [TestMethod]
        public void MapKernelName_WhenSqlLowerCase_ReturnsSql()
            => Assert.AreEqual("sql", MapKernelName("sql"));

        // ── MapKernelName — PowerShell variants ──────────────────────────────

        [TestMethod]
        public void MapKernelName_WhenPwsh_ReturnsPwsh()
            => Assert.AreEqual("pwsh", MapKernelName("pwsh"));

        [TestMethod]
        public void MapKernelName_WhenPowershell_ReturnsPwsh()
            => Assert.AreEqual("pwsh", MapKernelName("powershell"));

        [TestMethod]
        public void MapKernelName_WhenPowerShellMixedCase_ReturnsPwsh()
            => Assert.AreEqual("pwsh", MapKernelName("PowerShell"));

        // ── MapKernelName — HTML / Markdown ───────────────────────────────────

        [TestMethod]
        public void MapKernelName_WhenHtml_ReturnsHtml()
            => Assert.AreEqual("html", MapKernelName("html"));

        [TestMethod]
        public void MapKernelName_WhenHtmlUpperCase_ReturnsHtml()
            => Assert.AreEqual("html", MapKernelName("HTML"));

        [TestMethod]
        public void MapKernelName_WhenMarkdown_ReturnsMarkdown()
            => Assert.AreEqual("markdown", MapKernelName("markdown"));

        [TestMethod]
        public void MapKernelName_WhenMdAlias_ReturnsMarkdown()
            => Assert.AreEqual("markdown", MapKernelName("md"));

        // ── MapKernelName — unknown passthrough ───────────────────────────────

        [TestMethod]
        public void MapKernelName_WhenUnknownKernel_PassesThrough()
            => Assert.AreEqual("mycustomkernel", MapKernelName("mycustomkernel"));

        // ── IsTerminalEvent ───────────────────────────────────────────────────

        [TestMethod]
        public void IsTerminalEvent_WhenCommandSucceeded_ReturnsTrue()
            => Assert.IsTrue(IsTerminalEvent(KernelEventTypes.CommandSucceeded));

        [TestMethod]
        public void IsTerminalEvent_WhenCommandFailed_ReturnsTrue()
            => Assert.IsTrue(IsTerminalEvent(KernelEventTypes.CommandFailed));

        [TestMethod]
        public void IsTerminalEvent_WhenReturnValueProduced_ReturnsFalse()
            => Assert.IsFalse(IsTerminalEvent(KernelEventTypes.ReturnValueProduced));

        [TestMethod]
        public void IsTerminalEvent_WhenStandardOutputProduced_ReturnsFalse()
            => Assert.IsFalse(IsTerminalEvent(KernelEventTypes.StandardOutputValueProduced));

        [TestMethod]
        public void IsTerminalEvent_WhenStandardErrorProduced_ReturnsFalse()
            => Assert.IsFalse(IsTerminalEvent(KernelEventTypes.StandardErrorValueProduced));

        [TestMethod]
        public void IsTerminalEvent_WhenDisplayedValueProduced_ReturnsFalse()
            => Assert.IsFalse(IsTerminalEvent(KernelEventTypes.DisplayedValueProduced));

        [TestMethod]
        public void IsTerminalEvent_WhenKernelReady_ReturnsFalse()
            => Assert.IsFalse(IsTerminalEvent(KernelEventTypes.KernelReady));

        [TestMethod]
        public void IsTerminalEvent_WhenErrorProduced_ReturnsFalse()
            => Assert.IsFalse(IsTerminalEvent(KernelEventTypes.ErrorProduced));

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
