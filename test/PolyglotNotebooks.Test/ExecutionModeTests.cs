using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PolyglotNotebooks.Execution;
using PolyglotNotebooks.Kernel;
using PolyglotNotebooks.Models;

#pragma warning disable MSTEST0037
#pragma warning disable VSTHRD002

namespace PolyglotNotebooks.Test
{
    /// <summary>
    /// Tests for execution mode logic (Phase 4).
    ///
    /// ExecutionCoordinator's core execution paths require a live kernel and VS thread
    /// infrastructure.  This file extends the existing ExecutionCoordinatorTests with:
    ///   - CancelCurrentExecution behavior (safe: no VS SDK references)
    ///   - KernelClientAvailable event subscription
    ///   - RunAllCellsAsync with a pre-cancelled token (throws before VS SDK is needed)
    ///   - RunAllCellsAsync with documents of varying cell kinds
    ///
    /// HandleCellRunRequested and HandleRunAllRequested are fire-and-forget wrappers
    /// that reference ThreadHelper.JoinableTaskFactory — they are JIT-unsafe in the
    /// test runner and covered by integration tests only.
    /// </summary>
    [TestClass]
    public class ExecutionModeTests
    {
        // ====================================================================
        // CancelCurrentExecution — safe to call (no VS SDK in method body)
        // ====================================================================

        [TestMethod]
        public void CancelCurrentExecution_WhenNoCtsActive_DoesNotThrow()
        {
            using var manager = new KernelProcessManager();
            using var coordinator = new ExecutionCoordinator(manager);

            bool threw = false;
            try { coordinator.CancelCurrentExecution(); }
            catch { threw = true; }
            Assert.IsFalse(threw, "CancelCurrentExecution() with no active cts must not throw");
        }

        [TestMethod]
        public void CancelCurrentExecution_WhenCalledTwiceSequentially_DoesNotThrow()
        {
            using var manager = new KernelProcessManager();
            using var coordinator = new ExecutionCoordinator(manager);

            bool threw = false;
            try
            {
                coordinator.CancelCurrentExecution();
                coordinator.CancelCurrentExecution();
            }
            catch { threw = true; }
            Assert.IsFalse(threw, "Repeated CancelCurrentExecution() calls must not throw");
        }

        [TestMethod]
        public void CancelCurrentExecution_AfterDispose_DoesNotThrow()
        {
            using var manager = new KernelProcessManager();
            var coordinator = new ExecutionCoordinator(manager);
            coordinator.Dispose();

            // After Dispose the cts is null; calling again must be a no-op
            bool threw = false;
            try { coordinator.CancelCurrentExecution(); }
            catch { threw = true; }
            Assert.IsFalse(threw, "CancelCurrentExecution() after Dispose must not throw");
        }

        // ====================================================================
        // KernelClientAvailable event — subscribable without VS SDK
        // ====================================================================

        [TestMethod]
        public void KernelClientAvailable_EventSubscription_DoesNotThrow()
        {
            using var manager = new KernelProcessManager();
            using var coordinator = new ExecutionCoordinator(manager);

            bool threw = false;
            try
            {
                coordinator.KernelClientAvailable += _ => { };
            }
            catch { threw = true; }
            Assert.IsFalse(threw, "Subscribing to KernelClientAvailable must not throw");
        }

        [TestMethod]
        public void KernelClientAvailable_EventUnsubscription_DoesNotThrow()
        {
            using var manager = new KernelProcessManager();
            using var coordinator = new ExecutionCoordinator(manager);

            Action<PolyglotNotebooks.Protocol.KernelClient> handler = _ => { };
            coordinator.KernelClientAvailable += handler;

            bool threw = false;
            try { coordinator.KernelClientAvailable -= handler; }
            catch { threw = true; }
            Assert.IsFalse(threw, "Unsubscribing KernelClientAvailable must not throw");
        }

        // ====================================================================
        // KernelClient property — returns null before first execution
        // ====================================================================

        [TestMethod]
        public void KernelClient_Property_IsNullBeforeAnyExecution()
        {
            using var manager = new KernelProcessManager();
            using var coordinator = new ExecutionCoordinator(manager);
            Assert.IsNull(coordinator.KernelClient, "KernelClient must be null until kernel is started");
        }

        // ====================================================================
        // RunAllCellsAsync — pre-cancelled token
        //
        // EnsureKernelStartedAsync calls _startupLock.WaitAsync(ct).
        // With a pre-cancelled CancellationToken, WaitAsync(ct) throws
        // OperationCanceledException before any kernel I/O is attempted.
        // ====================================================================

        [TestMethod]
        public void RunAllCellsAsync_WhenTokenAlreadyCancelled_ThrowsOperationCanceledException()
        {
            using var manager = new KernelProcessManager();
            using var coordinator = new ExecutionCoordinator(manager);
            var doc = NotebookDocument.Create("test.dib", NotebookFormat.Dib, "csharp");
            doc.AddCell(CellKind.Code, "csharp");

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            bool threw = false;
            try
            {
                coordinator.RunAllCellsAsync(doc, cts.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { threw = true; }
            Assert.IsTrue(threw, "RunAllCellsAsync with a pre-cancelled token must throw OperationCanceledException");
        }

        [TestMethod]
        public void RunAllCellsAsync_WhenNullDocument_ThrowsArgumentNullException()
        {
            using var manager = new KernelProcessManager();
            using var coordinator = new ExecutionCoordinator(manager);

            bool threw = false;
            try
            {
                coordinator.RunAllCellsAsync(null!).GetAwaiter().GetResult();
            }
            catch (ArgumentNullException) { threw = true; }
            Assert.IsTrue(threw, "RunAllCellsAsync(null) must throw ArgumentNullException");
        }

        // ====================================================================
        // NotebookDocument cell helpers used in tests above
        // ====================================================================

        // NotebookDocument.AddCell is the public API — verify it exists and works
        [TestMethod]
        public void NotebookDocument_AddCell_AddsCodeCell()
        {
            var doc = NotebookDocument.Create("x.dib", NotebookFormat.Dib, "csharp");
            doc.AddCell(CellKind.Code, "csharp");
            Assert.AreEqual(1, doc.Cells.Count);
            Assert.AreEqual(CellKind.Code, doc.Cells[0].Kind);
        }

        [TestMethod]
        public void NotebookDocument_AddCell_AddsMarkdownCell()
        {
            var doc = NotebookDocument.Create("x.dib", NotebookFormat.Dib, "csharp");
            doc.AddCell(CellKind.Markdown, "markdown");
            Assert.AreEqual(1, doc.Cells.Count);
            Assert.AreEqual(CellKind.Markdown, doc.Cells[0].Kind);
        }

        [TestMethod]
        public void NotebookDocument_AddMultipleCells_PreservesOrder()
        {
            var doc = NotebookDocument.Create("x.dib", NotebookFormat.Dib, "csharp");
            doc.AddCell(CellKind.Markdown, "markdown");
            doc.AddCell(CellKind.Code, "csharp");
            doc.AddCell(CellKind.Code, "csharp");

            Assert.AreEqual(3, doc.Cells.Count);
            Assert.AreEqual(CellKind.Markdown, doc.Cells[0].Kind);
            Assert.AreEqual(CellKind.Code, doc.Cells[1].Kind);
            Assert.AreEqual(CellKind.Code, doc.Cells[2].Kind);
        }

        // ====================================================================
        // CancellationToken propagation model test
        // ====================================================================

        [TestMethod]
        public void CancellationToken_CancelAfterCreation_IsCancelled()
        {
            // Verify the CancellationTokenSource → Token propagation model used
            // by HandleRunAllRequested / CancelCurrentExecution is correct.
            var cts = new CancellationTokenSource();
            Assert.IsFalse(cts.Token.IsCancellationRequested);
            cts.Cancel();
            Assert.IsTrue(cts.Token.IsCancellationRequested);
            cts.Dispose();
        }

        [TestMethod]
        public void CancellationToken_LinkedSource_PropagatesParentCancellation()
        {
            using var parent = new CancellationTokenSource();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(parent.Token);

            Assert.IsFalse(linked.Token.IsCancellationRequested);
            parent.Cancel();
            Assert.IsTrue(linked.Token.IsCancellationRequested);
        }
    }
}
