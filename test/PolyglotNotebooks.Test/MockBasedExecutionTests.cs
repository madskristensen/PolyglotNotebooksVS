using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using PolyglotNotebooks.Execution;
using PolyglotNotebooks.Kernel;
using PolyglotNotebooks.Models;
using PolyglotNotebooks.Protocol;

#pragma warning disable MSTEST0037
#pragma warning disable VSTHRD002

namespace PolyglotNotebooks.Test
{
    /// <summary>
    /// Mock-based tests for <see cref="ExecutionCoordinator"/>,
    /// <see cref="CellExecutionEngine"/>, and <see cref="KernelProcessManager"/>.
    ///
    /// These tests use Moq to inject fake dependencies through the internal
    /// constructor overloads added for testability. They exercise code paths
    /// that were previously untestable without a live kernel or VS infrastructure.
    /// </summary>
    [TestClass]
    public class MockBasedExecutionTests
    {
        // ══════════════════════════════════════════════════════════════════════
        // ExecutionCoordinator — construction and disposal with mocked deps
        // ══════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void Coordinator_InternalConstructor_WithMockedProcessManager_Succeeds()
        {
            var mockPM = new Mock<IKernelProcessManager>();

            using var coordinator = new ExecutionCoordinator(mockPM.Object);

            Assert.IsNull(coordinator.KernelClient);
        }

        [TestMethod]
        public void Coordinator_InternalConstructor_NullProcessManager_ThrowsArgumentNull()
        {
            bool threw = false;
            try
            {
                _ = new ExecutionCoordinator((IKernelProcessManager)null!);
            }
            catch (ArgumentNullException) { threw = true; }
            Assert.IsTrue(threw, "Expected ArgumentNullException for null IKernelProcessManager");
        }

        [TestMethod]
        public void Coordinator_Dispose_WithMockedProcessManager_DoesNotThrow()
        {
            var mockPM = new Mock<IKernelProcessManager>();
            var coordinator = new ExecutionCoordinator(mockPM.Object);

            bool threw = false;
            try { coordinator.Dispose(); }
            catch { threw = true; }
            Assert.IsFalse(threw, "Dispose with mocked dependencies must not throw");
        }

        [TestMethod]
        public void Coordinator_Dispose_CalledTwice_WithMockedProcessManager_DoesNotThrow()
        {
            var mockPM = new Mock<IKernelProcessManager>();
            var coordinator = new ExecutionCoordinator(mockPM.Object);

            coordinator.Dispose();

            bool threw = false;
            try { coordinator.Dispose(); }
            catch { threw = true; }
            Assert.IsFalse(threw, "Second Dispose() must be a no-op");
        }

        [TestMethod]
        public void Coordinator_CancelCurrentExecution_WhenNoExecution_DoesNotThrow()
        {
            var mockPM = new Mock<IKernelProcessManager>();
            using var coordinator = new ExecutionCoordinator(mockPM.Object);

            bool threw = false;
            try { coordinator.CancelCurrentExecution(); }
            catch { threw = true; }
            Assert.IsFalse(threw, "CancelCurrentExecution with no active execution must not throw");
        }

        // ══════════════════════════════════════════════════════════════════════
        // ExecutionCoordinator — RestartAndRunAllAsync with mocked process mgr
        // ══════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void RestartAndRunAllAsync_NullDocument_ThrowsArgumentNull()
        {
            var mockPM = new Mock<IKernelProcessManager>();
            using var coordinator = new ExecutionCoordinator(mockPM.Object);

            bool threw = false;
            try
            {
                coordinator.RestartAndRunAllAsync(null!).GetAwaiter().GetResult();
            }
            catch (ArgumentNullException) { threw = true; }
            Assert.IsTrue(threw, "Expected ArgumentNullException for null document");
        }

        [TestMethod]
        public void RestartAndRunAllAsync_WhenProcessManagerRunning_CallsStopAsync()
        {
            var mockPM = new Mock<IKernelProcessManager>();
            mockPM.Setup(pm => pm.IsRunning).Returns(true);
            mockPM.Setup(pm => pm.StopAsync()).Returns(Task.CompletedTask);

            using var coordinator = new ExecutionCoordinator(mockPM.Object);

            // Empty document — RunAllCellsAsync will iterate zero code cells,
            // avoiding any JTF / kernel calls.
            var doc = NotebookDocument.Create("test.dib", NotebookFormat.Dib);

            coordinator.RestartAndRunAllAsync(doc).GetAwaiter().GetResult();

            mockPM.Verify(pm => pm.StopAsync(), Times.Once,
                "StopAsync should be called when the process manager reports IsRunning=true");
        }

        [TestMethod]
        public void RestartAndRunAllAsync_WhenProcessManagerNotRunning_SkipsStopAsync()
        {
            var mockPM = new Mock<IKernelProcessManager>();
            mockPM.Setup(pm => pm.IsRunning).Returns(false);

            using var coordinator = new ExecutionCoordinator(mockPM.Object);

            var doc = NotebookDocument.Create("test.dib", NotebookFormat.Dib);

            coordinator.RestartAndRunAllAsync(doc).GetAwaiter().GetResult();

            mockPM.Verify(pm => pm.StopAsync(), Times.Never,
                "StopAsync should NOT be called when the process manager is not running");
        }

        [TestMethod]
        public void RestartAndRunAllAsync_WithMarkdownOnlyCells_DoesNotStartKernel()
        {
            var mockPM = new Mock<IKernelProcessManager>();
            mockPM.Setup(pm => pm.IsRunning).Returns(false);

            using var coordinator = new ExecutionCoordinator(mockPM.Object);

            var doc = NotebookDocument.Create("test.dib", NotebookFormat.Dib);
            doc.Cells.Add(new NotebookCell(CellKind.Markdown, "markdown", "# Hello"));
            doc.Cells.Add(new NotebookCell(CellKind.Markdown, "markdown", "Some text"));

            coordinator.RestartAndRunAllAsync(doc).GetAwaiter().GetResult();

            // StartAsync should NOT be called because RunAllCellsAsync skips non-code cells,
            // so EnsureKernelStartedAsync is never invoked.
            mockPM.Verify(pm => pm.StartAsync(It.IsAny<CancellationToken>()), Times.Never,
                "Kernel should not start when document has no code cells");
        }

        // ══════════════════════════════════════════════════════════════════════
        // ExecutionCoordinator — RunAllCellsAsync / RunCellsAboveAsync guards
        // ══════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void RunCellsAboveAsync_NullDocument_ThrowsArgumentNull()
        {
            var mockPM = new Mock<IKernelProcessManager>();
            using var coordinator = new ExecutionCoordinator(mockPM.Object);

            bool threw = false;
            try
            {
                coordinator.RunCellsAboveAsync(null!, new NotebookCell(CellKind.Code, "csharp"))
                    .GetAwaiter().GetResult();
            }
            catch (ArgumentNullException) { threw = true; }
            Assert.IsTrue(threw);
        }

        [TestMethod]
        public void RunCellsAboveAsync_NullCurrentCell_ThrowsArgumentNull()
        {
            var mockPM = new Mock<IKernelProcessManager>();
            using var coordinator = new ExecutionCoordinator(mockPM.Object);
            var doc = NotebookDocument.Create("test.dib", NotebookFormat.Dib);

            bool threw = false;
            try
            {
                coordinator.RunCellsAboveAsync(doc, null!)
                    .GetAwaiter().GetResult();
            }
            catch (ArgumentNullException) { threw = true; }
            Assert.IsTrue(threw);
        }

        [TestMethod]
        public void RunCellsBelowAsync_NullDocument_ThrowsArgumentNull()
        {
            var mockPM = new Mock<IKernelProcessManager>();
            using var coordinator = new ExecutionCoordinator(mockPM.Object);

            bool threw = false;
            try
            {
                coordinator.RunCellsBelowAsync(null!, new NotebookCell(CellKind.Code, "csharp"))
                    .GetAwaiter().GetResult();
            }
            catch (ArgumentNullException) { threw = true; }
            Assert.IsTrue(threw);
        }

        [TestMethod]
        public void RunSelectionAsync_NullCell_ThrowsArgumentNull()
        {
            var mockPM = new Mock<IKernelProcessManager>();
            using var coordinator = new ExecutionCoordinator(mockPM.Object);

            bool threw = false;
            try
            {
                coordinator.RunSelectionAsync(null!, "code")
                    .GetAwaiter().GetResult();
            }
            catch (ArgumentNullException) { threw = true; }
            Assert.IsTrue(threw);
        }

        [TestMethod]
        public void RunSelectionAsync_EmptySelectedText_ReturnsImmediately()
        {
            var mockPM = new Mock<IKernelProcessManager>();
            using var coordinator = new ExecutionCoordinator(mockPM.Object);
            var cell = new NotebookCell(CellKind.Code, "csharp", "var x = 1;");

            // Should return without starting the kernel
            coordinator.RunSelectionAsync(cell, "").GetAwaiter().GetResult();

            mockPM.Verify(pm => pm.StartAsync(It.IsAny<CancellationToken>()), Times.Never,
                "Kernel should not start when selected text is empty");
        }

        // ══════════════════════════════════════════════════════════════════════
        // ExecutionCoordinator — cancellation behavior
        // ══════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void RunAllCellsAsync_WhenCancelled_ThrowsOperationCanceled()
        {
            var mockPM = new Mock<IKernelProcessManager>();
            using var coordinator = new ExecutionCoordinator(mockPM.Object);

            var doc = NotebookDocument.Create("test.dib", NotebookFormat.Dib);
            doc.Cells.Add(new NotebookCell(CellKind.Code, "csharp", "var x = 1;"));

            using var cts = new CancellationTokenSource();
            cts.Cancel(); // pre-cancel

            bool threw = false;
            try
            {
                coordinator.RunAllCellsAsync(doc, cts.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { threw = true; }
            Assert.IsTrue(threw, "Pre-cancelled token should cause OperationCanceledException");
        }

        // ══════════════════════════════════════════════════════════════════════
        // CellExecutionEngine — construction with mocked IKernelClient
        // ══════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void CellExecutionEngine_InternalConstructor_WithMockedKernelClient_Succeeds()
        {
            var mockClient = new Mock<IKernelClient>();

            using var engine = new CellExecutionEngine(mockClient.Object);

            // Just verifying construction succeeds with a mock
        }

        [TestMethod]
        public void CellExecutionEngine_InternalConstructor_NullKernelClient_ThrowsArgumentNull()
        {
            bool threw = false;
            try
            {
                _ = new CellExecutionEngine((IKernelClient)null!);
            }
            catch (ArgumentNullException) { threw = true; }
            Assert.IsTrue(threw, "Expected ArgumentNullException for null IKernelClient");
        }

        [TestMethod]
        public void CellExecutionEngine_Dispose_WithMockedKernelClient_DoesNotThrow()
        {
            var mockClient = new Mock<IKernelClient>();
            var engine = new CellExecutionEngine(mockClient.Object);

            bool threw = false;
            try { engine.Dispose(); }
            catch { threw = true; }
            Assert.IsFalse(threw, "Dispose with mocked KernelClient must not throw");
        }

        [TestMethod]
        public void CellExecutionEngine_Dispose_Twice_WithMockedKernelClient_DoesNotThrow()
        {
            var mockClient = new Mock<IKernelClient>();
            var engine = new CellExecutionEngine(mockClient.Object);

            engine.Dispose();

            bool threw = false;
            try { engine.Dispose(); }
            catch { threw = true; }
            Assert.IsFalse(threw, "Second Dispose() must be a no-op");
        }

        // ══════════════════════════════════════════════════════════════════════
        // CellExecutionEngine — CancelExecutionAsync with mocked client
        // ══════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void CancelExecutionAsync_SendsCancelCommandToKernel()
        {
            var mockClient = new Mock<IKernelClient>();
            mockClient
                .Setup(c => c.SendCommandAsync(
                    It.IsAny<string>(),
                    It.IsAny<CancelCommand>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(new KernelCommandEnvelope()));

            using var engine = new CellExecutionEngine(mockClient.Object);

            engine.CancelExecutionAsync().GetAwaiter().GetResult();

            mockClient.Verify(
                c => c.SendCommandAsync(
                    CommandTypes.CancelCommand,
                    It.IsAny<CancelCommand>(),
                    It.IsAny<CancellationToken>()),
                Times.Once,
                "CancelExecutionAsync should forward a Cancel command to the kernel");
        }

        [TestMethod]
        public void CancelExecutionAsync_WhenSendFails_DoesNotThrow()
        {
            var mockClient = new Mock<IKernelClient>();
            mockClient
                .Setup(c => c.SendCommandAsync(
                    It.IsAny<string>(),
                    It.IsAny<CancelCommand>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Kernel is gone"));

            using var engine = new CellExecutionEngine(mockClient.Object);

            bool threw = false;
            try
            {
                engine.CancelExecutionAsync().GetAwaiter().GetResult();
            }
            catch { threw = true; }
            Assert.IsFalse(threw,
                "CancelExecutionAsync should swallow exceptions from the kernel client");
        }

        // ══════════════════════════════════════════════════════════════════════
        // KernelProcessManager — StatusChanged event verification
        // ══════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void ProcessManager_StopAsync_WhenNotStarted_FiresStatusChangedToStopped()
        {
            var manager = new KernelProcessManager();
            var statusChanges = new List<KernelStatusChangedEventArgs>();
            manager.StatusChanged += (s, e) => statusChanges.Add(e);

            manager.StopAsync().GetAwaiter().GetResult();

            Assert.AreEqual(1, statusChanges.Count, "Expected one StatusChanged event");
            Assert.AreEqual(KernelStatus.NotStarted, statusChanges[0].OldStatus);
            Assert.AreEqual(KernelStatus.Stopped, statusChanges[0].NewStatus);

            manager.Dispose();
        }

        [TestMethod]
        public void ProcessManager_StopAsync_CalledTwice_FiresStatusChangedOnlyOnce()
        {
            var manager = new KernelProcessManager();
            var statusChanges = new List<KernelStatusChangedEventArgs>();
            manager.StatusChanged += (s, e) => statusChanges.Add(e);

            manager.StopAsync().GetAwaiter().GetResult();
            manager.StopAsync().GetAwaiter().GetResult();

            // Second call should be a no-op since status is already Stopped
            Assert.AreEqual(1, statusChanges.Count,
                "StatusChanged should only fire once when status doesn't change");

            manager.Dispose();
        }

        [TestMethod]
        public void ProcessManager_MaxRestartAttempts_IsThree()
        {
            Assert.AreEqual(3, KernelProcessManager.MaxRestartAttempts);
        }

        [TestMethod]
        public void ProcessManager_CanReRunCellsAfterRestart_IsTrue()
        {
            var manager = new KernelProcessManager();
            Assert.IsTrue(manager.CanReRunCellsAfterRestart);
            manager.Dispose();
        }

        // ══════════════════════════════════════════════════════════════════════
        // IKernelProcessManager mock — status and lifecycle
        // ══════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void MockProcessManager_CanSimulateStatusTransitions()
        {
            var mockPM = new Mock<IKernelProcessManager>();
            mockPM.SetupSequence(pm => pm.Status)
                .Returns(KernelStatus.NotStarted)
                .Returns(KernelStatus.Starting)
                .Returns(KernelStatus.Ready);

            Assert.AreEqual(KernelStatus.NotStarted, mockPM.Object.Status);
            Assert.AreEqual(KernelStatus.Starting, mockPM.Object.Status);
            Assert.AreEqual(KernelStatus.Ready, mockPM.Object.Status);
        }

        [TestMethod]
        public void MockProcessManager_CanSimulateConnectionInfo()
        {
            var mockPM = new Mock<IKernelProcessManager>();
            mockPM.Setup(pm => pm.ConnectionInfo)
                .Returns(new KernelConnectionInfo
                {
                    ProcessId = 42,
                    WorkingDirectory = @"C:\test"
                });

            var info = mockPM.Object.ConnectionInfo;
            Assert.IsNotNull(info);
            Assert.AreEqual(42, info!.ProcessId);
            Assert.AreEqual(@"C:\test", info.WorkingDirectory);
        }

        [TestMethod]
        public void MockProcessManager_CanSimulateCrashEvent()
        {
            var mockPM = new Mock<IKernelProcessManager>();
            KernelCrashedEventArgs? capturedArgs = null;

            mockPM.Object.KernelCrashed += (s, e) => capturedArgs = e;
            mockPM.Raise(pm => pm.KernelCrashed += null,
                new KernelCrashedEventArgs(exitCode: -1, stderrOutput: "Segfault", attemptNumber: 0, willRetry: true));

            Assert.IsNotNull(capturedArgs);
            Assert.AreEqual(-1, capturedArgs!.ExitCode);
            Assert.AreEqual("Segfault", capturedArgs.StderrOutput);
            Assert.AreEqual(0, capturedArgs.AttemptNumber);
            Assert.IsTrue(capturedArgs.WillRetry);
        }

        [TestMethod]
        public void MockProcessManager_CanSimulateUnexpectedProcessExit()
        {
            var mockPM = new Mock<IKernelProcessManager>();
            ProcessExitedEventArgs? capturedArgs = null;

            mockPM.Object.ProcessExited += (s, e) => capturedArgs = e;
            mockPM.Raise(pm => pm.ProcessExited += null,
                new ProcessExitedEventArgs(exitCode: 1, stderrOutput: "OOM killed", wasUnexpected: true));

            Assert.IsNotNull(capturedArgs);
            Assert.AreEqual(1, capturedArgs!.ExitCode);
            Assert.AreEqual("OOM killed", capturedArgs.StderrOutput);
            Assert.IsTrue(capturedArgs.WasUnexpected);
        }

        [TestMethod]
        public void MockProcessManager_CanSimulateStatusChangedEvent()
        {
            var mockPM = new Mock<IKernelProcessManager>();
            KernelStatusChangedEventArgs? capturedArgs = null;

            mockPM.Object.StatusChanged += (s, e) => capturedArgs = e;
            mockPM.Raise(pm => pm.StatusChanged += null,
                new KernelStatusChangedEventArgs(KernelStatus.Ready, KernelStatus.Error));

            Assert.IsNotNull(capturedArgs);
            Assert.AreEqual(KernelStatus.Ready, capturedArgs!.OldStatus);
            Assert.AreEqual(KernelStatus.Error, capturedArgs.NewStatus);
        }

        // ══════════════════════════════════════════════════════════════════════
        // IKernelClient mock — verifying command routing
        // ══════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void MockKernelClient_CanSetupSendCommandAsync()
        {
            var mockClient = new Mock<IKernelClient>();
            var expectedEnvelope = new KernelCommandEnvelope { CommandType = "SubmitCode", Token = "abc" };

            mockClient
                .Setup(c => c.SendCommandAsync(It.IsAny<KernelCommandEnvelope>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            // Verify the mock is callable
            mockClient.Object.SendCommandAsync(expectedEnvelope, CancellationToken.None)
                .GetAwaiter().GetResult();

            mockClient.Verify(c => c.SendCommandAsync(
                It.Is<KernelCommandEnvelope>(e => e.Token == "abc"),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [TestMethod]
        public void MockKernelClient_CanSimulateEventsObservable()
        {
            var mockClient = new Mock<IKernelClient>();
            var subject = new Subject<KernelEventEnvelope>();
            mockClient.Setup(c => c.Events).Returns(subject);

            var received = new List<KernelEventEnvelope>();
            using var sub = mockClient.Object.Events.Subscribe(
                new ActionObserver<KernelEventEnvelope>(e => received.Add(e)));

            var envelope = new KernelEventEnvelope { EventType = KernelEventTypes.KernelReady };
            subject.OnNext(envelope);

            Assert.AreEqual(1, received.Count);
            Assert.AreEqual(KernelEventTypes.KernelReady, received[0].EventType);
        }

        [TestMethod]
        public void MockKernelClient_CanSimulateSubmitCodeFailure()
        {
            var mockClient = new Mock<IKernelClient>();
            mockClient
                .Setup(c => c.SubmitCodeAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new KernelCommandException(new KernelEventEnvelope
                {
                    EventType = KernelEventTypes.CommandFailed
                }));

            bool threw = false;
            try
            {
                mockClient.Object.SubmitCodeAsync("invalid code").GetAwaiter().GetResult();
            }
            catch (KernelCommandException) { threw = true; }
            Assert.IsTrue(threw, "Mocked SubmitCodeAsync should throw KernelCommandException");
        }

        [TestMethod]
        public void MockKernelClient_CanSimulateTimeout()
        {
            var mockClient = new Mock<IKernelClient>();
            mockClient
                .Setup(c => c.WaitForReadyAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new TimeoutException("Kernel did not respond within timeout"));

            bool threw = false;
            try
            {
                mockClient.Object.WaitForReadyAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (TimeoutException) { threw = true; }
            Assert.IsTrue(threw, "Mocked WaitForReadyAsync should throw TimeoutException");
        }

        // ══════════════════════════════════════════════════════════════════════
        // Integration-style: coordinator + mocked process manager lifecycle
        // ══════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void Coordinator_RestartAndRunAll_ResetsKernelState()
        {
            var mockPM = new Mock<IKernelProcessManager>();
            var stopCalled = false;
            mockPM.Setup(pm => pm.IsRunning).Returns(true);
            mockPM.Setup(pm => pm.StopAsync()).Callback(() => stopCalled = true).Returns(Task.CompletedTask);

            using var coordinator = new ExecutionCoordinator(mockPM.Object);
            var doc = NotebookDocument.Create("test.dib", NotebookFormat.Dib);

            coordinator.RestartAndRunAllAsync(doc).GetAwaiter().GetResult();

            Assert.IsTrue(stopCalled, "Process manager StopAsync should have been called");
            Assert.IsNull(coordinator.KernelClient, "KernelClient should be null after restart reset");
        }

        [TestMethod]
        public void Coordinator_RunAllCellsAsync_WithEmptyDocument_CompletesWithoutKernelStart()
        {
            var mockPM = new Mock<IKernelProcessManager>();
            using var coordinator = new ExecutionCoordinator(mockPM.Object);
            var doc = NotebookDocument.Create("empty.dib", NotebookFormat.Dib);

            coordinator.RunAllCellsAsync(doc).GetAwaiter().GetResult();

            mockPM.Verify(pm => pm.StartAsync(It.IsAny<CancellationToken>()), Times.Never,
                "No code cells means no kernel start");
        }

        [TestMethod]
        public void Coordinator_ExecutionCompletedEvent_NotFiredByRunAllAsync()
        {
            // RunAllCellsAsync is the direct async path — ExecutionCompleted is only
            // fired by the fire-and-forget wrapper (HandleRunAllRequested).
            var mockPM = new Mock<IKernelProcessManager>();
            using var coordinator = new ExecutionCoordinator(mockPM.Object);
            var doc = NotebookDocument.Create("test.dib", NotebookFormat.Dib);

            bool eventFired = false;
            coordinator.ExecutionCompleted += (s, e) => eventFired = true;

            coordinator.RunAllCellsAsync(doc).GetAwaiter().GetResult();

            Assert.IsFalse(eventFired,
                "ExecutionCompleted should not fire from the direct async method");
        }

        // ══════════════════════════════════════════════════════════════════════
        // KernelProcessManager — interface implementation correctness
        // ══════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void ProcessManager_ImplementsIKernelProcessManager()
        {
            var manager = new KernelProcessManager();

            Assert.IsInstanceOfType(manager, typeof(IKernelProcessManager));
            manager.Dispose();
        }

        [TestMethod]
        public void ProcessManager_AsInterface_StatusMatchesConcrete()
        {
            var manager = new KernelProcessManager();
            IKernelProcessManager iface = manager;

            Assert.AreEqual(manager.Status, iface.Status);
            Assert.AreEqual(manager.IsRunning, iface.IsRunning);
            Assert.AreEqual(manager.Process, iface.Process);
            Assert.AreEqual(manager.ConnectionInfo, iface.ConnectionInfo);

            manager.Dispose();
        }

        // ══════════════════════════════════════════════════════════════════════
        // KernelClient — interface implementation correctness
        // ══════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void KernelClient_ImplementsIKernelClient()
        {
            var process = Process.GetCurrentProcess();
            var client = new KernelClient(process);

            Assert.IsInstanceOfType(client, typeof(IKernelClient));
            client.Dispose();
        }

        [TestMethod]
        public void KernelClient_AsInterface_EventsIsNotNull()
        {
            var process = Process.GetCurrentProcess();
            var client = new KernelClient(process);
            IKernelClient iface = client;

            Assert.IsNotNull(iface.Events);

            client.Dispose();
        }

        [TestMethod]
        public void KernelClient_AsInterface_CommandTimeoutMs_DefaultIsPositive()
        {
            var process = Process.GetCurrentProcess();
            var client = new KernelClient(process);
            IKernelClient iface = client;

            Assert.IsTrue(iface.CommandTimeoutMs > 0, "Default timeout should be positive");

            client.Dispose();
        }
    }
}
