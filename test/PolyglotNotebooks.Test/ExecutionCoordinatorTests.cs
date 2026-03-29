using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PolyglotNotebooks.Execution;
using PolyglotNotebooks.Kernel;
using PolyglotNotebooks.Models;

#pragma warning disable MSTEST0037
#pragma warning disable VSTHRD002

namespace PolyglotNotebooks.Test
{
    /// <summary>
    /// Tests for <see cref="ExecutionCoordinator"/>.
    ///
    /// The coordinator's main execution path requires a live kernel and VS thread
    /// infrastructure.  Tests here cover the constructor guard, disposal idempotency,
    /// and the null-document guard on RunAllCellsAsync — all exercisable without VS.
    /// </summary>
    [TestClass]
    public class ExecutionCoordinatorTests
    {
        // ── Constructor guard ─────────────────────────────────────────────────

        [TestMethod]
        public void Constructor_WhenNullKernelProcessManager_ThrowsArgumentNullException()
        {
            bool threw = false;
            try { _ = new ExecutionCoordinator(null!); }
            catch (ArgumentNullException) { threw = true; }
            Assert.IsTrue(threw, "Expected ArgumentNullException for null KernelProcessManager");
        }

        // ── Dispose idempotency ───────────────────────────────────────────────

        [TestMethod]
        public void Dispose_WhenCalledOnce_DoesNotThrow()
        {
            using var manager = new KernelProcessManager();
            var coordinator = new ExecutionCoordinator(manager);

            bool threw = false;
            try { coordinator.Dispose(); }
            catch { threw = true; }
            Assert.IsFalse(threw, "First Dispose() must not throw");
        }

        [TestMethod]
        public void Dispose_WhenCalledTwice_DoesNotThrow()
        {
            using var manager = new KernelProcessManager();
            var coordinator = new ExecutionCoordinator(manager);

            coordinator.Dispose();

            bool threw = false;
            try { coordinator.Dispose(); }
            catch { threw = true; }
            Assert.IsFalse(threw, "Second Dispose() must be a no-op");
        }

        // ── RunAllCellsAsync null guard ───────────────────────────────────────

        [TestMethod]
        public void RunAllCellsAsync_WhenNullDocument_ThrowsArgumentNullException()
        {
            // The null-check fires synchronously before the first await so this
            // is safely exercisable without a running kernel.
            using var manager = new KernelProcessManager();
            using var coordinator = new ExecutionCoordinator(manager);

            bool threw = false;
            try
            {
                // GetAwaiter().GetResult() unwraps the faulted Task without AggregateException.
                coordinator.RunAllCellsAsync(null!).GetAwaiter().GetResult();
            }
            catch (ArgumentNullException) { threw = true; }
            Assert.IsTrue(threw, "Expected ArgumentNullException for null document");
        }

        // ── HandleCellRunRequested null guard ─────────────────────────────────

        // HandleCellRunRequested is not unit-testable: its method body directly
        // references ThreadHelper.JoinableTaskFactory (VS SDK type).  JIT compilation
        // of the whole method fails in the unit-test runner before the null guard
        // can execute.  Behaviour is validated by integration/VS-hosted tests.

        // ── IsJavaScriptCell helper ───────────────────────────────────────────

        [TestMethod]
        public void IsJavaScriptCell_WhenJavascript_ReturnsTrue()
        {
            Assert.IsTrue(ExecutionCoordinator.IsJavaScriptCell("javascript"));
        }

        [TestMethod]
        public void IsJavaScriptCell_WhenJs_ReturnsTrue()
        {
            Assert.IsTrue(ExecutionCoordinator.IsJavaScriptCell("js"));
        }

        [TestMethod]
        public void IsJavaScriptCell_WhenJavaScript_CaseInsensitive_ReturnsTrue()
        {
            Assert.IsTrue(ExecutionCoordinator.IsJavaScriptCell("JavaScript"));
            Assert.IsTrue(ExecutionCoordinator.IsJavaScriptCell("JAVASCRIPT"));
            Assert.IsTrue(ExecutionCoordinator.IsJavaScriptCell("JS"));
            Assert.IsTrue(ExecutionCoordinator.IsJavaScriptCell("Js"));
        }

        [TestMethod]
        public void IsJavaScriptCell_WhenCsharp_ReturnsFalse()
        {
            Assert.IsFalse(ExecutionCoordinator.IsJavaScriptCell("csharp"));
        }

        [TestMethod]
        public void IsJavaScriptCell_WhenNull_ReturnsFalse()
        {
            Assert.IsFalse(ExecutionCoordinator.IsJavaScriptCell((string?)null));
        }

        [TestMethod]
        public void IsJavaScriptCell_WhenEmpty_ReturnsFalse()
        {
            Assert.IsFalse(ExecutionCoordinator.IsJavaScriptCell(""));
        }

        [TestMethod]
        public void IsJavaScriptCell_WhenPython_ReturnsFalse()
        {
            Assert.IsFalse(ExecutionCoordinator.IsJavaScriptCell("python"));
        }

        // ── SelectCellsAbove / SelectCellsBelow helpers ───────────────────────

        [TestMethod]
        public void SelectCellsAbove_WhenCurrentIsFirst_ReturnsEmpty()
        {
            var cells = new List<NotebookCell>
            {
                new NotebookCell(CellKind.Code, "csharp", "first"),
                new NotebookCell(CellKind.Code, "csharp", "second"),
            };

            var result = ExecutionCoordinator.SelectCellsAbove(cells, cells[0]);

            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void SelectCellsAbove_WhenCurrentIsLast_ReturnsAllButLast()
        {
            var cells = new List<NotebookCell>
            {
                new NotebookCell(CellKind.Code, "csharp", "first"),
                new NotebookCell(CellKind.Code, "csharp", "second"),
                new NotebookCell(CellKind.Code, "csharp", "third"),
            };

            var result = ExecutionCoordinator.SelectCellsAbove(cells, cells[2]);

            Assert.AreEqual(2, result.Count);
            Assert.AreSame(cells[0], result[0]);
            Assert.AreSame(cells[1], result[1]);
        }

        [TestMethod]
        public void SelectCellsAbove_WhenCurrentIsMiddle_ReturnsCellsBefore()
        {
            var cells = new List<NotebookCell>
            {
                new NotebookCell(CellKind.Code, "csharp", "a"),
                new NotebookCell(CellKind.Code, "csharp", "b"),
                new NotebookCell(CellKind.Code, "csharp", "c"),
            };

            var result = ExecutionCoordinator.SelectCellsAbove(cells, cells[1]);

            Assert.AreEqual(1, result.Count);
            Assert.AreSame(cells[0], result[0]);
        }

        [TestMethod]
        public void SelectCellsAbove_WhenCurrentNotInList_ReturnsAll()
        {
            var cells = new List<NotebookCell>
            {
                new NotebookCell(CellKind.Code, "csharp", "a"),
                new NotebookCell(CellKind.Code, "csharp", "b"),
            };
            var orphan = new NotebookCell(CellKind.Code, "csharp", "orphan");

            var result = ExecutionCoordinator.SelectCellsAbove(cells, orphan);

            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void SelectCellsBelow_WhenCurrentIsLast_ReturnsOnlyCurrent()
        {
            var cells = new List<NotebookCell>
            {
                new NotebookCell(CellKind.Code, "csharp", "first"),
                new NotebookCell(CellKind.Code, "csharp", "second"),
            };

            var result = ExecutionCoordinator.SelectCellsBelow(cells, cells[1]);

            Assert.AreEqual(1, result.Count);
            Assert.AreSame(cells[1], result[0]);
        }

        [TestMethod]
        public void SelectCellsBelow_WhenCurrentIsFirst_ReturnsAll()
        {
            var cells = new List<NotebookCell>
            {
                new NotebookCell(CellKind.Code, "csharp", "first"),
                new NotebookCell(CellKind.Code, "csharp", "second"),
                new NotebookCell(CellKind.Code, "csharp", "third"),
            };

            var result = ExecutionCoordinator.SelectCellsBelow(cells, cells[0]);

            Assert.AreEqual(3, result.Count);
            Assert.AreSame(cells[0], result[0]);
            Assert.AreSame(cells[2], result[2]);
        }

        [TestMethod]
        public void SelectCellsBelow_WhenCurrentNotInList_ReturnsEmpty()
        {
            var cells = new List<NotebookCell>
            {
                new NotebookCell(CellKind.Code, "csharp", "a"),
            };
            var orphan = new NotebookCell(CellKind.Code, "csharp", "orphan");

            var result = ExecutionCoordinator.SelectCellsBelow(cells, orphan);

            Assert.AreEqual(0, result.Count);
        }
    }
}
