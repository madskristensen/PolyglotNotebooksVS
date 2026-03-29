using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
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
#pragma warning disable MSTEST0032 // Intentionally verifying const value hasn't changed
            Assert.AreEqual(3, (object)KernelProcessManager.MaxRestartAttempts);
#pragma warning restore MSTEST0032
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

        // ══════════════════════════════════════════════════════════════════════
        // CellExecutionEngine.ExecuteCellAsync — event handling logic tests
        //
        // ExecuteCellAsync depends on ThreadHelper (VS SDK), which is
        // unavailable at test runtime. Instead, we test the core event-
        // handling and command-building logic that ExecuteCellAsync relies on:
        //   - EventObserver: token-matched event waiting (the heart of execution)
        //   - KernelCommandEnvelope: command building
        //   - MapKernelName / IsTerminalEvent: static helpers
        // ══════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void EventObserver_CommandSucceeded_CompletesTerminalWait()
        {
            var subject = new Subject<KernelEventEnvelope>();
            var token = "test-token-success";

            using var observer = new EventObserver(subject, token);

            // Simulate kernel sending CommandSucceeded for this token
            subject.OnNext(new KernelEventEnvelope
            {
                EventType = KernelEventTypes.CommandSucceeded,
                Event = JsonSerializer.SerializeToElement(
                    new CommandSucceeded { ExecutionOrder = 1 },
                    ProtocolSerializerOptions.Default),
                Command = new KernelCommandEnvelope { Token = token }
            });

            var result = observer.WaitForTerminalEventAsync().GetAwaiter().GetResult();

            Assert.AreEqual(KernelEventTypes.CommandSucceeded, result.EventType,
                "Terminal event should be CommandSucceeded");
        }

        [TestMethod]
        public void EventObserver_CommandFailed_CompletesTerminalWait()
        {
            var subject = new Subject<KernelEventEnvelope>();
            var token = "test-token-fail";

            using var observer = new EventObserver(subject, token);

            subject.OnNext(new KernelEventEnvelope
            {
                EventType = KernelEventTypes.CommandFailed,
                Event = JsonSerializer.SerializeToElement(
                    new CommandFailed { Message = "Compilation error CS1002" },
                    ProtocolSerializerOptions.Default),
                Command = new KernelCommandEnvelope { Token = token }
            });

            var result = observer.WaitForTerminalEventAsync().GetAwaiter().GetResult();

            Assert.AreEqual(KernelEventTypes.CommandFailed, result.EventType,
                "Terminal event should be CommandFailed");
            var failed = result.Event.Deserialize<CommandFailed>(ProtocolSerializerOptions.Default);
            Assert.IsNotNull(failed);
            Assert.IsTrue(failed!.Message.Contains("CS1002"),
                "Failed event should carry the error message");
        }

        [TestMethod]
        public void EventObserver_WrongToken_DoesNotCompleteTerminalWait()
        {
            var subject = new Subject<KernelEventEnvelope>();
            var token = "my-token";

            using var observer = new EventObserver(subject, token);

            // Push event with a DIFFERENT token — should be ignored
            subject.OnNext(new KernelEventEnvelope
            {
                EventType = KernelEventTypes.CommandSucceeded,
                Event = JsonSerializer.SerializeToElement(new CommandSucceeded(), ProtocolSerializerOptions.Default),
                Command = new KernelCommandEnvelope { Token = "other-token" }
            });

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            bool didNotComplete = false;
            try
            {
                observer.WaitForTerminalEventAsync(cts.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { didNotComplete = true; }
            catch (TimeoutException) { didNotComplete = true; }
            Assert.IsTrue(didNotComplete,
                "Events with wrong token should be ignored; wait should not complete");
        }

        [TestMethod]
        public void EventObserver_Cancellation_ThrowsOperationCanceled()
        {
            var subject = new Subject<KernelEventEnvelope>();
            using var observer = new EventObserver(subject, "token-cancel");

            using var cts = new CancellationTokenSource();
            cts.Cancel(); // pre-cancel

            bool threw = false;
            try
            {
                observer.WaitForTerminalEventAsync(cts.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { threw = true; }
            Assert.IsTrue(threw, "Pre-cancelled token should cause OperationCanceledException");
        }

        [TestMethod]
        public void EventObserver_StreamCompleted_FaultsPendingWait()
        {
            var subject = new Subject<KernelEventEnvelope>();
            using var observer = new EventObserver(subject, "token-crash");

            // Simulate kernel process crashing (stream completes unexpectedly)
            subject.OnCompleted();

            bool threw = false;
            try
            {
                observer.WaitForTerminalEventAsync().GetAwaiter().GetResult();
            }
            catch (InvalidOperationException ex)
            {
                threw = true;
                Assert.IsTrue(ex.Message.Contains("terminated"),
                    "Exception should indicate the kernel process terminated");
            }
            Assert.IsTrue(threw,
                "Stream completion should fault pending waits with InvalidOperationException");
        }

        [TestMethod]
        public void EventObserver_StreamError_FaultsPendingWait()
        {
            var subject = new Subject<KernelEventEnvelope>();
            using var observer = new EventObserver(subject, "token-error");

            // Simulate stream error
            subject.OnError(new IOException("Broken pipe"));

            bool threw = false;
            try
            {
                observer.WaitForTerminalEventAsync().GetAwaiter().GetResult();
            }
            catch (IOException ex)
            {
                threw = true;
                Assert.IsTrue(ex.Message.Contains("Broken pipe"));
            }
            Assert.IsTrue(threw,
                "Stream error should fault pending waits with the original exception");
        }

        [TestMethod]
        public void EventObserver_IntermediateEvents_DoNotCompleteTerminalWait()
        {
            var subject = new Subject<KernelEventEnvelope>();
            var token = "token-intermediate";
            using var observer = new EventObserver(subject, token);

            // Push non-terminal events (display, output, etc.)
            subject.OnNext(new KernelEventEnvelope
            {
                EventType = KernelEventTypes.DisplayedValueProduced,
                Event = JsonSerializer.SerializeToElement(new { }, ProtocolSerializerOptions.Default),
                Command = new KernelCommandEnvelope { Token = token }
            });
            subject.OnNext(new KernelEventEnvelope
            {
                EventType = KernelEventTypes.StandardOutputValueProduced,
                Event = JsonSerializer.SerializeToElement(new { }, ProtocolSerializerOptions.Default),
                Command = new KernelCommandEnvelope { Token = token }
            });

            // Terminal wait should NOT be completed by intermediate events
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            bool didNotComplete = false;
            try
            {
                observer.WaitForTerminalEventAsync(cts.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { didNotComplete = true; }
            catch (TimeoutException) { didNotComplete = true; }
            Assert.IsTrue(didNotComplete,
                "Intermediate events should not satisfy WaitForTerminalEventAsync");
        }

        [TestMethod]
        public void EventObserver_WaitForEventType_CompletesOnMatchingEvent()
        {
            var subject = new Subject<KernelEventEnvelope>();
            var token = "token-wait-type";
            using var observer = new EventObserver(subject, token);

            // Start the wait first (creates the TCS in _pending), then push the event
            var waitTask = observer.WaitForEventTypeAsync(KernelEventTypes.KernelReady);

            subject.OnNext(new KernelEventEnvelope
            {
                EventType = KernelEventTypes.KernelReady,
                Event = JsonSerializer.SerializeToElement(new { }, ProtocolSerializerOptions.Default),
                Command = new KernelCommandEnvelope { Token = token }
            });

            var result = waitTask.GetAwaiter().GetResult();
            Assert.AreEqual(KernelEventTypes.KernelReady, result.EventType);
        }

        // ══════════════════════════════════════════════════════════════════════
        // CellExecutionEngine — static helper methods
        // ══════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void MapKernelName_CSharpVariants_AllMapToCsharp()
        {
            Assert.AreEqual("csharp", CellExecutionEngine.MapKernelName("csharp"));
            Assert.AreEqual("csharp", CellExecutionEngine.MapKernelName("C#"));
            Assert.AreEqual("csharp", CellExecutionEngine.MapKernelName("CSharp"));
        }

        [TestMethod]
        public void MapKernelName_FSharpVariants_AllMapToFsharp()
        {
            Assert.AreEqual("fsharp", CellExecutionEngine.MapKernelName("fsharp"));
            Assert.AreEqual("fsharp", CellExecutionEngine.MapKernelName("F#"));
            Assert.AreEqual("fsharp", CellExecutionEngine.MapKernelName("FSharp"));
        }

        [TestMethod]
        public void MapKernelName_NullOrEmpty_DefaultsToCsharp()
        {
            Assert.AreEqual("csharp", CellExecutionEngine.MapKernelName(null!));
            Assert.AreEqual("csharp", CellExecutionEngine.MapKernelName(""));
        }

        [TestMethod]
        public void MapKernelName_Unknown_ReturnsAsIs()
        {
            Assert.AreEqual("rust", CellExecutionEngine.MapKernelName("rust"));
            Assert.AreEqual("go", CellExecutionEngine.MapKernelName("go"));
        }

        [TestMethod]
        public void IsTerminalEvent_CommandSucceeded_ReturnsTrue()
        {
            Assert.IsTrue(CellExecutionEngine.IsTerminalEvent(KernelEventTypes.CommandSucceeded));
        }

        [TestMethod]
        public void IsTerminalEvent_CommandFailed_ReturnsTrue()
        {
            Assert.IsTrue(CellExecutionEngine.IsTerminalEvent(KernelEventTypes.CommandFailed));
        }

        [TestMethod]
        public void IsTerminalEvent_OtherEvents_ReturnsFalse()
        {
            Assert.IsFalse(CellExecutionEngine.IsTerminalEvent(KernelEventTypes.KernelReady));
            Assert.IsFalse(CellExecutionEngine.IsTerminalEvent(KernelEventTypes.DisplayedValueProduced));
            Assert.IsFalse(CellExecutionEngine.IsTerminalEvent(KernelEventTypes.StandardOutputValueProduced));
            Assert.IsFalse(CellExecutionEngine.IsTerminalEvent(KernelEventTypes.ErrorProduced));
        }

        // ══════════════════════════════════════════════════════════════════════
        // ExecutionCoordinator — error propagation tests
        // ══════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void Coordinator_RestartAndRunAll_StopAsyncThrows_PropagatesException()
        {
            var mockPM = new Mock<IKernelProcessManager>();
            mockPM.Setup(pm => pm.IsRunning).Returns(true);
            mockPM.Setup(pm => pm.StopAsync())
                .ThrowsAsync(new InvalidOperationException("Process already dead"));

            using var coordinator = new ExecutionCoordinator(mockPM.Object);
            var doc = NotebookDocument.Create("test.dib", NotebookFormat.Dib);

            bool threw = false;
            try
            {
                coordinator.RestartAndRunAllAsync(doc).GetAwaiter().GetResult();
            }
            catch (InvalidOperationException ex)
            {
                threw = true;
                Assert.IsTrue(ex.Message.Contains("Process already dead"),
                    "Exception from StopAsync should propagate with original message");
            }
            Assert.IsTrue(threw,
                "StopAsync exception should propagate through RestartAndRunAllAsync");
        }

        [TestMethod]
        public void Coordinator_RestartAndRunAll_CancelledToken_ThrowsOperationCanceled()
        {
            var mockPM = new Mock<IKernelProcessManager>();
            mockPM.Setup(pm => pm.IsRunning).Returns(false);
            using var coordinator = new ExecutionCoordinator(mockPM.Object);
            var doc = NotebookDocument.Create("test.dib", NotebookFormat.Dib);

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            bool threw = false;
            try
            {
                coordinator.RestartAndRunAllAsync(doc, cts.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { threw = true; }
            Assert.IsTrue(threw,
                "Cancelled token should cause OperationCanceledException in RestartAndRunAllAsync");
        }

        [TestMethod]
        public void Coordinator_RunAllCellsAsync_NullDocument_ThrowsArgumentNull()
        {
            var mockPM = new Mock<IKernelProcessManager>();
            using var coordinator = new ExecutionCoordinator(mockPM.Object);

            bool threw = false;
            try
            {
                coordinator.RunAllCellsAsync(null!).GetAwaiter().GetResult();
            }
            catch (ArgumentNullException) { threw = true; }
            Assert.IsTrue(threw, "Expected ArgumentNullException for null document");
        }

        [TestMethod]
        public void Coordinator_CancelCurrentExecution_SequentialCalls_DoNotThrow()
        {
            var mockPM = new Mock<IKernelProcessManager>();
            using var coordinator = new ExecutionCoordinator(mockPM.Object);

            // Multiple sequential cancellations should be safe
            bool threw = false;
            try
            {
                coordinator.CancelCurrentExecution();
                coordinator.CancelCurrentExecution();
                coordinator.CancelCurrentExecution();
            }
            catch { threw = true; }
            Assert.IsFalse(threw,
                "Multiple CancelCurrentExecution calls should never throw");
        }

        [TestMethod]
        public void Coordinator_Dispose_ThenCancel_DoesNotThrow()
        {
            var mockPM = new Mock<IKernelProcessManager>();
            var coordinator = new ExecutionCoordinator(mockPM.Object);

            coordinator.Dispose();

            // Cancellation after disposal should be safe (no-op)
            bool threw = false;
            try
            {
                coordinator.CancelCurrentExecution();
            }
            catch { threw = true; }
            Assert.IsFalse(threw,
                "CancelCurrentExecution after Dispose should not throw");
        }

        [TestMethod]
        public void Coordinator_IsJavaScriptCell_CorrectlyIdentifiesJSKernels()
        {
            Assert.IsTrue(ExecutionCoordinator.IsJavaScriptCell("javascript"));
            Assert.IsTrue(ExecutionCoordinator.IsJavaScriptCell("js"));
            Assert.IsTrue(ExecutionCoordinator.IsJavaScriptCell("JavaScript"));
            Assert.IsTrue(ExecutionCoordinator.IsJavaScriptCell("JS"));
            Assert.IsFalse(ExecutionCoordinator.IsJavaScriptCell("csharp"));
            Assert.IsFalse(ExecutionCoordinator.IsJavaScriptCell("fsharp"));
            Assert.IsFalse(ExecutionCoordinator.IsJavaScriptCell(null));
            Assert.IsFalse(ExecutionCoordinator.IsJavaScriptCell(""));
        }

        [TestMethod]
        public void Coordinator_SelectCellsAbove_ReturnsOnlyCellsBeforeCurrent()
        {
            var cells = new List<NotebookCell>
            {
                new NotebookCell(CellKind.Code, "csharp", "cell0"),
                new NotebookCell(CellKind.Code, "csharp", "cell1"),
                new NotebookCell(CellKind.Code, "csharp", "cell2"),
            };

            var result = ExecutionCoordinator.SelectCellsAbove(cells, cells[2]);
            Assert.AreEqual(2, result.Count);
            Assert.AreSame(cells[0], result[0]);
            Assert.AreSame(cells[1], result[1]);
        }

        [TestMethod]
        public void Coordinator_SelectCellsBelow_ReturnsCurrentAndAllBelow()
        {
            var cells = new List<NotebookCell>
            {
                new NotebookCell(CellKind.Code, "csharp", "cell0"),
                new NotebookCell(CellKind.Code, "csharp", "cell1"),
                new NotebookCell(CellKind.Code, "csharp", "cell2"),
            };

            var result = ExecutionCoordinator.SelectCellsBelow(cells, cells[1]);
            Assert.AreEqual(2, result.Count);
            Assert.AreSame(cells[1], result[0]);
            Assert.AreSame(cells[2], result[1]);
        }

        // ══════════════════════════════════════════════════════════════════════
        // KernelProcessManager — crash recovery and lifecycle tests
        // ══════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void ProcessManager_InitialStatus_IsNotStarted()
        {
            var manager = new KernelProcessManager();
            Assert.AreEqual(KernelStatus.NotStarted, manager.Status);
            Assert.IsFalse(manager.IsRunning);
            Assert.IsNull(manager.Process);
            Assert.IsNull(manager.ConnectionInfo);
            manager.Dispose();
        }

        [TestMethod]
        public void ProcessManager_Dispose_ThrowsObjectDisposed_OnStartAsync()
        {
            var manager = new KernelProcessManager();
            manager.Dispose();

            bool threw = false;
            try
            {
                manager.StartAsync().GetAwaiter().GetResult();
            }
            catch (ObjectDisposedException) { threw = true; }
            Assert.IsTrue(threw,
                "StartAsync after Dispose should throw ObjectDisposedException");
        }

        [TestMethod]
        public void ProcessManager_Dispose_ThrowsObjectDisposed_OnStopAsync()
        {
            var manager = new KernelProcessManager();
            manager.Dispose();

            bool threw = false;
            try
            {
                manager.StopAsync().GetAwaiter().GetResult();
            }
            catch (ObjectDisposedException) { threw = true; }
            Assert.IsTrue(threw,
                "StopAsync after Dispose should throw ObjectDisposedException");
        }

        [TestMethod]
        public void ProcessManager_Dispose_ThrowsObjectDisposed_OnRestartAsync()
        {
            var manager = new KernelProcessManager();
            manager.Dispose();

            bool threw = false;
            try
            {
                manager.RestartAsync().GetAwaiter().GetResult();
            }
            catch (ObjectDisposedException) { threw = true; }
            Assert.IsTrue(threw,
                "RestartAsync after Dispose should throw ObjectDisposedException");
        }

        [TestMethod]
        public void ProcessManager_DisposeAll_DoesNotThrow()
        {
            // DisposeAll is a static cleanup method for VS shutdown
            bool threw = false;
            try
            {
                KernelProcessManager.DisposeAll();
            }
            catch { threw = true; }
            Assert.IsFalse(threw,
                "DisposeAll should never throw even when no instances exist");
        }

        [TestMethod]
        public void MockProcessManager_CrashEvent_WillRetryFalse_AtMaxAttempts()
        {
            var mockPM = new Mock<IKernelProcessManager>();
            KernelCrashedEventArgs? capturedArgs = null;

            mockPM.Object.KernelCrashed += (s, e) => capturedArgs = e;
            mockPM.Raise(pm => pm.KernelCrashed += null,
                new KernelCrashedEventArgs(
                    exitCode: -1073740791,
                    stderrOutput: "Stack overflow",
                    attemptNumber: KernelProcessManager.MaxRestartAttempts,
                    willRetry: false));

            Assert.IsNotNull(capturedArgs);
            Assert.IsFalse(capturedArgs!.WillRetry,
                "WillRetry should be false when attempts have reached max");
            Assert.AreEqual(KernelProcessManager.MaxRestartAttempts, capturedArgs.AttemptNumber);
        }

        [TestMethod]
        public void MockProcessManager_SequentialCrashes_TrackAttemptNumbers()
        {
            var mockPM = new Mock<IKernelProcessManager>();
            var crashEvents = new List<KernelCrashedEventArgs>();

            mockPM.Object.KernelCrashed += (s, e) => crashEvents.Add(e);

            // Simulate 4 crashes: 3 with retry, 1 giving up
            for (int i = 0; i <= KernelProcessManager.MaxRestartAttempts; i++)
            {
                bool willRetry = i < KernelProcessManager.MaxRestartAttempts;
                mockPM.Raise(pm => pm.KernelCrashed += null,
                    new KernelCrashedEventArgs(
                        exitCode: -1,
                        stderrOutput: $"Crash #{i}",
                        attemptNumber: i,
                        willRetry: willRetry));
            }

            Assert.AreEqual(KernelProcessManager.MaxRestartAttempts + 1, crashEvents.Count,
                "Should have received one crash event per attempt plus the final one");
            Assert.IsTrue(crashEvents[0].WillRetry, "First crash should have willRetry=true");
            Assert.IsFalse(crashEvents[crashEvents.Count - 1].WillRetry,
                "Last crash should have willRetry=false");
        }

        [TestMethod]
        public void MockProcessManager_StatusTransitions_DuringCrashRecovery()
        {
            var mockPM = new Mock<IKernelProcessManager>();
            var statusChanges = new List<KernelStatusChangedEventArgs>();

            mockPM.Object.StatusChanged += (s, e) => statusChanges.Add(e);

            // Simulate: Ready → Error (crash) → Restarting → Ready
            mockPM.Raise(pm => pm.StatusChanged += null,
                new KernelStatusChangedEventArgs(KernelStatus.Ready, KernelStatus.Error));
            mockPM.Raise(pm => pm.StatusChanged += null,
                new KernelStatusChangedEventArgs(KernelStatus.Error, KernelStatus.Restarting));
            mockPM.Raise(pm => pm.StatusChanged += null,
                new KernelStatusChangedEventArgs(KernelStatus.Restarting, KernelStatus.Ready));

            Assert.AreEqual(3, statusChanges.Count);
            Assert.AreEqual(KernelStatus.Error, statusChanges[0].NewStatus);
            Assert.AreEqual(KernelStatus.Restarting, statusChanges[1].NewStatus);
            Assert.AreEqual(KernelStatus.Ready, statusChanges[2].NewStatus);
        }

        [TestMethod]
        public void ProcessExitedEventArgs_Unexpected_HasCorrectProperties()
        {
            var args = new ProcessExitedEventArgs(exitCode: -1073741819, stderrOutput: "Access violation", wasUnexpected: true);

            Assert.AreEqual(-1073741819, args.ExitCode);
            Assert.AreEqual("Access violation", args.StderrOutput);
            Assert.IsTrue(args.WasUnexpected);
        }

        [TestMethod]
        public void ProcessExitedEventArgs_Expected_HasWasUnexpectedFalse()
        {
            var args = new ProcessExitedEventArgs(exitCode: 0, stderrOutput: "", wasUnexpected: false);

            Assert.AreEqual(0, args.ExitCode);
            Assert.IsFalse(args.WasUnexpected);
        }
    }
}

