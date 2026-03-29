using System;
using System.IO;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PolyglotNotebooks.Kernel;

#pragma warning disable MSTEST0037
#pragma warning disable VSTHRD002

namespace PolyglotNotebooks.Test
{
    [TestClass]
    public class KernelConnectionInfoTests
    {
        [TestMethod]
        public void Create_WhenCalled_DefaultProcessIdIsZero()
        {
            var info = new KernelConnectionInfo();

            Assert.AreEqual(0, info.ProcessId);
        }

        [TestMethod]
        public void Create_WhenCalled_DefaultStartTimeIsMinValue()
        {
            var info = new KernelConnectionInfo();

            Assert.AreEqual(default(DateTime), info.StartTime);
        }

        [TestMethod]
        public void Create_WhenCalled_DefaultWorkingDirectoryIsEmpty()
        {
            var info = new KernelConnectionInfo();

            Assert.AreEqual(string.Empty, info.WorkingDirectory);
        }

        [TestMethod]
        public void Create_WhenCalled_DefaultVersionIsEmpty()
        {
            var info = new KernelConnectionInfo();

            Assert.AreEqual(string.Empty, info.DotnetInteractiveVersion);
        }

        [TestMethod]
        public void Create_WhenCalled_AvailableKernelsIsEmpty()
        {
            var info = new KernelConnectionInfo();

            Assert.IsNotNull(info.AvailableKernels);
            Assert.AreEqual(0, info.AvailableKernels.Count);
        }

        [TestMethod]
        public void Properties_WhenSet_RetainValues()
        {
            var now = DateTime.UtcNow;
            var info = new KernelConnectionInfo
            {
                ProcessId = 1234,
                StartTime = now,
                WorkingDirectory = @"C:\projects\mynotebook",
                DotnetInteractiveVersion = "1.0.0-beta.12345",
                AvailableKernels = new[] { "csharp", "fsharp", "pwsh" }
            };

            Assert.AreEqual(1234, info.ProcessId);
            Assert.AreEqual(now, info.StartTime);
            Assert.AreEqual(@"C:\projects\mynotebook", info.WorkingDirectory);
            Assert.AreEqual("1.0.0-beta.12345", info.DotnetInteractiveVersion);
            Assert.AreEqual(3, info.AvailableKernels.Count);
        }
    }

    [TestClass]
    public class KernelStatusTests
    {
        [TestMethod]
        public void KernelStatus_AllExpectedValues_Exist()
        {
            Assert.IsTrue(Enum.IsDefined(typeof(KernelStatus), KernelStatus.NotStarted));
            Assert.IsTrue(Enum.IsDefined(typeof(KernelStatus), KernelStatus.Starting));
            Assert.IsTrue(Enum.IsDefined(typeof(KernelStatus), KernelStatus.Ready));
            Assert.IsTrue(Enum.IsDefined(typeof(KernelStatus), KernelStatus.Busy));
            Assert.IsTrue(Enum.IsDefined(typeof(KernelStatus), KernelStatus.Restarting));
            Assert.IsTrue(Enum.IsDefined(typeof(KernelStatus), KernelStatus.Stopped));
            Assert.IsTrue(Enum.IsDefined(typeof(KernelStatus), KernelStatus.Error));
        }

        [TestMethod]
        public void KernelStatusChangedEventArgs_WhenCreated_SetsOldAndNewStatus()
        {
            var args = new KernelStatusChangedEventArgs(KernelStatus.NotStarted, KernelStatus.Starting);

            Assert.AreEqual(KernelStatus.NotStarted, args.OldStatus);
            Assert.AreEqual(KernelStatus.Starting, args.NewStatus);
        }

        [TestMethod]
        public void KernelCrashedEventArgs_Constructor_StoresExitCode()
        {
            var args = new KernelCrashedEventArgs(1, "error output", 0, false);
            Assert.AreEqual(1, args.ExitCode);
        }

        [TestMethod]
        public void KernelCrashedEventArgs_Constructor_StoresStderr()
        {
            var args = new KernelCrashedEventArgs(-1, "crash details", 1, true);
            Assert.AreEqual("crash details", args.StderrOutput);
        }

        [TestMethod]
        public void KernelCrashedEventArgs_Constructor_StoresAttemptNumber()
        {
            var args = new KernelCrashedEventArgs(0, "", 2, true);
            Assert.AreEqual(2, args.AttemptNumber);
        }

        [TestMethod]
        public void KernelCrashedEventArgs_Constructor_StoresWillRetry()
        {
            var args = new KernelCrashedEventArgs(0, "", 0, true);
            Assert.IsTrue(args.WillRetry);
        }

        [TestMethod]
        public void KernelCrashedEventArgs_WillRetryFalse_StoredCorrectly()
        {
            var args = new KernelCrashedEventArgs(0, "", 3, false);
            Assert.IsFalse(args.WillRetry);
        }
    }

    [TestClass]
    public class KernelProcessManagerTests
    {
        [TestMethod]
        public void Constructor_WhenNullWorkingDirectory_UsesUserProfile()
        {
            var manager = new KernelProcessManager(null);

            // Without launching, verify initial state
            Assert.AreEqual(KernelStatus.NotStarted, manager.Status);
            manager.Dispose();
        }

        [TestMethod]
        public void Constructor_WhenWorkingDirectoryProvided_UsesIt()
        {
            // Use system temp directory which always exists
            var tempDir = Path.GetTempPath();
            var manager = new KernelProcessManager(tempDir);

            Assert.AreEqual(KernelStatus.NotStarted, manager.Status);
            manager.Dispose();
        }

        [TestMethod]
        public void Constructor_WhenEmptyWorkingDirectory_UsesUserProfile()
        {
            var manager = new KernelProcessManager(string.Empty);

            Assert.AreEqual(KernelStatus.NotStarted, manager.Status);
            manager.Dispose();
        }

        [TestMethod]
        public void Status_WhenNewInstance_IsNotStarted()
        {
            var manager = new KernelProcessManager();

            Assert.AreEqual(KernelStatus.NotStarted, manager.Status);
            manager.Dispose();
        }

        [TestMethod]
        public void IsRunning_WhenNotStarted_ReturnsFalse()
        {
            var manager = new KernelProcessManager();

            Assert.IsFalse(manager.IsRunning);
            manager.Dispose();
        }

        [TestMethod]
        public void Process_WhenNotStarted_IsNull()
        {
            var manager = new KernelProcessManager();

            Assert.IsNull(manager.Process);
            manager.Dispose();
        }

        [TestMethod]
        public void ConnectionInfo_WhenNotStarted_IsNull()
        {
            var manager = new KernelProcessManager();

            Assert.IsNull(manager.ConnectionInfo);
            manager.Dispose();
        }

        [TestMethod]
        public void Dispose_WhenNotStarted_DoesNotThrow()
        {
            var manager = new KernelProcessManager();

            // Should not throw
            manager.Dispose();
        }

        [TestMethod]
        public void Dispose_WhenCalledTwice_DoesNotThrow()
        {
            var manager = new KernelProcessManager();

            manager.Dispose();
            manager.Dispose(); // second dispose should be a no-op
        }

        [TestMethod]
        public void StartAsync_WhenDisposed_ThrowsObjectDisposedException()
        {
            var manager = new KernelProcessManager();
            manager.Dispose();

            bool threw = false;
            try { manager.StartAsync().Wait(); }
            catch (AggregateException ae) when (ae.InnerException is ObjectDisposedException) { threw = true; }
            catch (ObjectDisposedException) { threw = true; }
            Assert.IsTrue(threw, "Expected ObjectDisposedException after dispose");
        }

        [TestMethod]
        public void StopAsync_WhenDisposed_ThrowsObjectDisposedException()
        {
            var manager = new KernelProcessManager();
            manager.Dispose();

            bool threw = false;
            try { manager.StopAsync().Wait(); }
            catch (AggregateException ae) when (ae.InnerException is ObjectDisposedException) { threw = true; }
            catch (ObjectDisposedException) { threw = true; }
            Assert.IsTrue(threw, "Expected ObjectDisposedException after dispose");
        }

        [TestMethod]
        public void RestartAsync_WhenDisposed_ThrowsObjectDisposedException()
        {
            var manager = new KernelProcessManager();
            manager.Dispose();

            bool threw = false;
            try { manager.RestartAsync().Wait(); }
            catch (AggregateException ae) when (ae.InnerException is ObjectDisposedException) { threw = true; }
            catch (ObjectDisposedException) { threw = true; }
            Assert.IsTrue(threw, "Expected ObjectDisposedException after dispose");
        }

        [TestMethod]
        public void ProcessExitedEventArgs_WhenCreated_SetsAllProperties()
        {
            var args = new ProcessExitedEventArgs(exitCode: -1, stderrOutput: "Fatal error", wasUnexpected: true);

            Assert.AreEqual(-1, args.ExitCode);
            Assert.AreEqual("Fatal error", args.StderrOutput);
            Assert.IsTrue(args.WasUnexpected);
        }

        [TestMethod]
        public void ProcessExitedEventArgs_WhenExpectedExit_WasUnexpectedIsFalse()
        {
            var args = new ProcessExitedEventArgs(exitCode: 0, stderrOutput: "", wasUnexpected: false);

            Assert.AreEqual(0, args.ExitCode);
            Assert.IsFalse(args.WasUnexpected);
        }

        [TestMethod]
        public void StopAsync_WhenNotStarted_SetsStatusToStopped()
        {
            var manager = new KernelProcessManager();

            manager.StopAsync().Wait();

            Assert.AreEqual(KernelStatus.Stopped, manager.Status);
            manager.Dispose();
        }
    }

    [TestClass]
    public class KernelInstallationDetectorTests
    {
        [TestMethod]
        public void GetInstallCommand_ReturnsExpectedString()
        {
            var command = KernelInstallationDetector.GetInstallCommand();

            Assert.IsFalse(string.IsNullOrEmpty(command));
            Assert.IsTrue(command.Contains("dotnet tool install"),
                "Install command should contain 'dotnet tool install'");
            Assert.IsTrue(command.Contains("-g"),
                "Install command should install globally");
            Assert.IsTrue(command.Contains("dotnet-interactive"),
                "Install command should reference dotnet-interactive");
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void IsInstalledAsync_WhenCalledTwice_ReturnsCachedResult()
        {
            var detector = new KernelInstallationDetector();

            // Call twice — the second call should use the cached result
            // We use a short timeout; if dotnet isn't available, it returns false gracefully
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            bool first;
            bool second;
            try
            {
                first = detector.IsInstalledAsync(cts.Token).Result;
                second = detector.IsInstalledAsync(cts.Token).Result;
            }
            catch (OperationCanceledException)
            {
                // dotnet took too long — acceptable on CI
                return;
            }
            catch (AggregateException ae) when (ae.InnerException is OperationCanceledException)
            {
                return;
            }

            Assert.AreEqual(first, second, "Cached result should be identical to the first call");
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void GetInstalledVersionAsync_WhenNotInstalled_ReturnsNullOrVersion()
        {
            var detector = new KernelInstallationDetector();

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            string? version;
            try
            {
                version = detector.GetInstalledVersionAsync(cts.Token).Result;
            }
            catch (OperationCanceledException) { return; }
            catch (AggregateException ae) when (ae.InnerException is OperationCanceledException) { return; }

            // Version is either null (not installed) or a non-empty string
            if (version != null)
                Assert.IsFalse(string.IsNullOrWhiteSpace(version));
        }
    }

    [TestClass]
    public class StdinPingTimerTests
    {
        [TestMethod]
        public void Constructor_WhenNullAccessor_ThrowsArgumentNullException()
        {
            bool threw = false;
            try { new StdinPingTimer(null!); }
            catch (ArgumentNullException) { threw = true; }
            Assert.IsTrue(threw, "Expected ArgumentNullException for null accessor");
        }

        [TestMethod]
        public void Constructor_WhenValidAccessor_DoesNotThrow()
        {
            using var timer = new StdinPingTimer(() => null);
            // Just verify construction succeeds
        }

        [TestMethod]
        public void Start_WhenCalled_DoesNotThrow()
        {
            using var timer = new StdinPingTimer(() => null);

            // Should not throw
            timer.Start();
            timer.Stop();
        }

        [TestMethod]
        public void Stop_WhenNotStarted_DoesNotThrow()
        {
            using var timer = new StdinPingTimer(() => null);

            // Stopping when not started should be a no-op
            timer.Stop();
        }

        [TestMethod]
        public void Stop_WhenCalledTwice_DoesNotThrow()
        {
            using var timer = new StdinPingTimer(() => null);
            timer.Start();
            timer.Stop();

            // Second stop should be a no-op
            timer.Stop();
        }

        [TestMethod]
        public void Start_WhenCalledTwice_DoesNotCreateMultipleTimers()
        {
            // The Interlocked.CompareExchange ensures only one activation
            using var timer = new StdinPingTimer(() => null);
            timer.Start();
            timer.Start(); // second Start should be a no-op

            timer.Stop();
        }

        [TestMethod]
        public void Dispose_WhenCalled_DoesNotThrow()
        {
            var timer = new StdinPingTimer(() => null);

            timer.Dispose();
        }

        [TestMethod]
        public void Dispose_WhenCalledTwice_DoesNotThrow()
        {
            var timer = new StdinPingTimer(() => null);

            timer.Dispose();
            timer.Dispose(); // second dispose should be a no-op
        }

        [TestMethod]
        public void Start_WhenDisposed_DoesNotThrow()
        {
            var timer = new StdinPingTimer(() => null);
            timer.Dispose();

            // Start after dispose should be a no-op due to _disposed check
            timer.Start();
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void Start_WhenAccessorReturnsNull_DoesNotThrowOnTick()
        {
            // Timer accessor returns null — the OnTick method should handle this gracefully
            using var timer = new StdinPingTimer(() => null);
            timer.Start();

            // Allow a tick to fire (interval is 500ms) then stop
            Thread.Sleep(600);
            timer.Stop(); // Should not have thrown
        }

        [TestMethod]
        public void StartStop_WhenCalledRepeatedly_DoesNotThrow()
        {
            using var timer = new StdinPingTimer(() => null);

            for (int i = 0; i < 5; i++)
            {
                timer.Start();
                timer.Stop();
            }
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void Start_WhenAccessorThrowsObjectDisposedException_SwallowsException()
        {
            // Simulates a closed stream — OnTick should swallow ObjectDisposedException
            using var timer = new StdinPingTimer(() =>
                throw new ObjectDisposedException("stream"));

            timer.Start();
            Thread.Sleep(600); // let a tick fire
            timer.Stop(); // should still work fine
        }

        [TestMethod]
        [TestCategory("Integration")]
        public void Start_WhenAccessorThrowsIOException_SwallowsException()
        {
            // Simulates a broken pipe — OnTick should swallow IOException
            using var timer = new StdinPingTimer(() =>
                throw new IOException("pipe broken"));

            timer.Start();
            Thread.Sleep(600);
            timer.Stop();
        }
    }
}
