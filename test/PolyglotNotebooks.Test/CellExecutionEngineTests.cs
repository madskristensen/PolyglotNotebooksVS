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
        // ── MapKernelName ─────────────────────────────────────────────────────

        [TestMethod]
        [DataRow("C#", "csharp")]
        [DataRow("csharp", "csharp")]
        [DataRow("CSharp", "csharp")]
        [DataRow(null, "csharp")]
        [DataRow("", "csharp")]
        [DataRow("F#", "fsharp")]
        [DataRow("fsharp", "fsharp")]
        [DataRow("JavaScript", "javascript")]
        [DataRow("js", "javascript")]
        [DataRow("javascript", "javascript")]
        [DataRow("SQL", "sql")]
        [DataRow("sql", "sql")]
        [DataRow("pwsh", "pwsh")]
        [DataRow("powershell", "pwsh")]
        [DataRow("PowerShell", "pwsh")]
        [DataRow("html", "html")]
        [DataRow("HTML", "html")]
        [DataRow("markdown", "markdown")]
        [DataRow("md", "markdown")]
        [DataRow("mycustomkernel", "mycustomkernel")]
        public void MapKernelName_ReturnsExpectedResult(string? input, string expected)
            => Assert.AreEqual(expected, CellExecutionEngine.MapKernelName(input));

        // ── IsTerminalEvent ───────────────────────────────────────────────────

        [TestMethod]
        [DataRow("CommandSucceeded", true)]
        [DataRow("CommandFailed", true)]
        [DataRow("ReturnValueProduced", false)]
        [DataRow("StandardOutputValueProduced", false)]
        [DataRow("StandardErrorValueProduced", false)]
        [DataRow("DisplayedValueProduced", false)]
        [DataRow("KernelReady", false)]
        [DataRow("ErrorProduced", false)]
        public void IsTerminalEvent_ReturnsExpectedResult(string eventType, bool expected)
            => Assert.AreEqual(expected, CellExecutionEngine.IsTerminalEvent(eventType));

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
