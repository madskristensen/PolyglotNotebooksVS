using System;
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
    }
}
