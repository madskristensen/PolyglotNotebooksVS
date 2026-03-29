using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PolyglotNotebooks.Protocol;

#pragma warning disable MSTEST0037
#pragma warning disable VSTHRD002

namespace PolyglotNotebooks.Test
{
    /// <summary>
    /// Tests for <see cref="Subject{T}"/>, <see cref="ActionObserver{T}"/>,
    /// and <see cref="EventObserver"/> in Protocol/EventObserver.cs.
    /// </summary>
    [TestClass]
    public class EventObserverTests
    {
        // ════════════════════════════════════════════════════════════════════════
        // Helpers
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Simple IObserver that records all callbacks for assertion.
        /// </summary>
        private sealed class RecordingObserver<T> : IObserver<T>
        {
            public List<T> Values { get; } = new List<T>();
            public List<Exception> Errors { get; } = new List<Exception>();
            public int CompletedCount { get; private set; }

            public void OnNext(T value) => Values.Add(value);
            public void OnError(Exception error) => Errors.Add(error);
            public void OnCompleted() => CompletedCount++;
        }

        private static KernelEventEnvelope MakeEnvelope(string eventType, string token)
        {
            return new KernelEventEnvelope
            {
                EventType = eventType,
                Command = new KernelCommandEnvelope { Token = token }
            };
        }

        // ════════════════════════════════════════════════════════════════════════
        // Subject<T> tests
        // ════════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void Subject_OnNext_DispatchesToAllSubscribers()
        {
            var subject = new Subject<int>();
            var obs1 = new RecordingObserver<int>();
            var obs2 = new RecordingObserver<int>();

            subject.Subscribe(obs1);
            subject.Subscribe(obs2);

            subject.OnNext(42);

            Assert.AreEqual(1, obs1.Values.Count);
            Assert.AreEqual(42, obs1.Values[0]);
            Assert.AreEqual(1, obs2.Values.Count);
            Assert.AreEqual(42, obs2.Values[0]);
        }

        [TestMethod]
        public void Subject_Unsubscribe_StopsReceivingEvents()
        {
            var subject = new Subject<int>();
            var obs = new RecordingObserver<int>();

            var subscription = subject.Subscribe(obs);
            subject.OnNext(1);
            subscription.Dispose();
            subject.OnNext(2);

            Assert.AreEqual(1, obs.Values.Count, "Should only have received the first event");
            Assert.AreEqual(1, obs.Values[0]);
        }

        [TestMethod]
        public void Subject_OnCompleted_ClearsObserversAndCallsOnCompleted()
        {
            var subject = new Subject<int>();
            var obs = new RecordingObserver<int>();

            subject.Subscribe(obs);
            subject.OnCompleted();

            Assert.AreEqual(1, obs.CompletedCount);

            // After completion, new OnNext should not reach the observer
            subject.OnNext(99);
            Assert.AreEqual(0, obs.Values.Count, "No events after completion");
        }

        [TestMethod]
        public void Subject_SubscribeAfterCompleted_ImmediatelyCallsOnCompleted()
        {
            var subject = new Subject<int>();
            subject.OnCompleted();

            var obs = new RecordingObserver<int>();
            subject.Subscribe(obs);

            Assert.AreEqual(1, obs.CompletedCount, "Late subscriber should get OnCompleted immediately");
        }

        [TestMethod]
        public void Subject_OnError_DispatchesToAllSubscribers()
        {
            var subject = new Subject<int>();
            var obs1 = new RecordingObserver<int>();
            var obs2 = new RecordingObserver<int>();

            subject.Subscribe(obs1);
            subject.Subscribe(obs2);

            var ex = new InvalidOperationException("test error");
            subject.OnError(ex);

            Assert.AreEqual(1, obs1.Errors.Count);
            Assert.AreSame(ex, obs1.Errors[0]);
            Assert.AreEqual(1, obs2.Errors.Count);
            Assert.AreSame(ex, obs2.Errors[0]);
        }

        [TestMethod]
        public void Subject_OnCompleted_IsIdempotent()
        {
            var subject = new Subject<int>();
            var obs = new RecordingObserver<int>();

            subject.Subscribe(obs);
            subject.OnCompleted();
            subject.OnCompleted(); // second call should be a no-op

            Assert.AreEqual(1, obs.CompletedCount, "OnCompleted must only call observers once");
        }

        [TestMethod]
        public void Subject_OnNext_AfterCompleted_DoesNotDispatch()
        {
            var subject = new Subject<int>();
            var obs = new RecordingObserver<int>();

            subject.Subscribe(obs);
            subject.OnCompleted();
            subject.OnNext(42);

            Assert.AreEqual(0, obs.Values.Count);
        }

        [TestMethod]
        public void Subject_MultipleSubscribers_AllReceive()
        {
            var subject = new Subject<string>();
            var obs1 = new RecordingObserver<string>();
            var obs2 = new RecordingObserver<string>();
            var obs3 = new RecordingObserver<string>();

            subject.Subscribe(obs1);
            subject.Subscribe(obs2);
            subject.Subscribe(obs3);

            subject.OnNext("a");
            subject.OnNext("b");

            Assert.AreEqual(2, obs1.Values.Count);
            Assert.AreEqual(2, obs2.Values.Count);
            Assert.AreEqual(2, obs3.Values.Count);
            Assert.AreEqual("a", obs1.Values[0]);
            Assert.AreEqual("b", obs3.Values[1]);
        }

        [TestMethod]
        public void Subject_Dispose_CallsOnCompleted()
        {
            var subject = new Subject<int>();
            var obs = new RecordingObserver<int>();

            subject.Subscribe(obs);
            subject.Dispose();

            Assert.AreEqual(1, obs.CompletedCount, "Dispose should trigger OnCompleted");
        }

        // ════════════════════════════════════════════════════════════════════════
        // ActionObserver<T> tests
        // ════════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void ActionObserver_OnNext_InvokesCallback()
        {
            int received = 0;
            var obs = new ActionObserver<int>(v => received = v);

            obs.OnNext(42);

            Assert.AreEqual(42, received);
        }

        [TestMethod]
        public void ActionObserver_OnCompleted_InvokesCallback()
        {
            bool called = false;
            var obs = new ActionObserver<int>(_ => { }, onCompleted: () => called = true);

            obs.OnCompleted();

            Assert.IsTrue(called);
        }

        [TestMethod]
        public void ActionObserver_OnError_InvokesCallback()
        {
            Exception? received = null;
            var ex = new InvalidOperationException("boom");
            var obs = new ActionObserver<int>(_ => { }, onError: e => received = e);

            obs.OnError(ex);

            Assert.AreSame(ex, received);
        }

        [TestMethod]
        public void ActionObserver_NullOnErrorCallback_DoesNotThrow()
        {
            var obs = new ActionObserver<int>(_ => { });

            // OnError with null callback should not throw
            bool threw = false;
            try { obs.OnError(new Exception("test")); }
            catch { threw = true; }
            Assert.IsFalse(threw, "OnError with null callback must not throw");
        }

        [TestMethod]
        public void ActionObserver_NullOnCompletedCallback_DoesNotThrow()
        {
            var obs = new ActionObserver<int>(_ => { });

            bool threw = false;
            try { obs.OnCompleted(); }
            catch { threw = true; }
            Assert.IsFalse(threw, "OnCompleted with null callback must not throw");
        }

        // ════════════════════════════════════════════════════════════════════════
        // EventObserver tests
        // ════════════════════════════════════════════════════════════════════════

        [TestMethod]
        public void EventObserver_IgnoresEventsWithWrongToken()
        {
            var subject = new Subject<KernelEventEnvelope>();
            const string myToken = "token-A";

            using var observer = new EventObserver(subject, myToken);

            // Register a waiter for a specific event type
            var task = observer.WaitForEventTypeAsync(KernelEventTypes.ReturnValueProduced);

            // Push an event with a DIFFERENT token — should be ignored
            subject.OnNext(MakeEnvelope(KernelEventTypes.ReturnValueProduced, "token-B"));

            Assert.IsFalse(task.IsCompleted, "Event with wrong token must be ignored");
        }

        [TestMethod]
        public void EventObserver_WaitForEventTypeAsync_ResolvesOnMatchingEvent()
        {
            var subject = new Subject<KernelEventEnvelope>();
            const string token = "test-token";

            using var observer = new EventObserver(subject, token);
            var task = observer.WaitForEventTypeAsync(KernelEventTypes.ReturnValueProduced);

            var envelope = MakeEnvelope(KernelEventTypes.ReturnValueProduced, token);
            subject.OnNext(envelope);

            Assert.IsTrue(task.IsCompleted);
            Assert.AreSame(envelope, task.GetAwaiter().GetResult());
        }

        [TestMethod]
        public void EventObserver_WaitForTerminalEventAsync_ResolvesOnCommandSucceeded()
        {
            var subject = new Subject<KernelEventEnvelope>();
            const string token = "test-token";

            using var observer = new EventObserver(subject, token);
            var task = observer.WaitForTerminalEventAsync();

            var envelope = MakeEnvelope(KernelEventTypes.CommandSucceeded, token);
            subject.OnNext(envelope);

            Assert.IsTrue(task.IsCompleted);
            var result = task.GetAwaiter().GetResult();
            Assert.AreEqual(KernelEventTypes.CommandSucceeded, result.EventType);
        }

        [TestMethod]
        public void EventObserver_WaitForTerminalEventAsync_ResolvesOnCommandFailed()
        {
            var subject = new Subject<KernelEventEnvelope>();
            const string token = "test-token";

            using var observer = new EventObserver(subject, token);
            var task = observer.WaitForTerminalEventAsync();

            var envelope = MakeEnvelope(KernelEventTypes.CommandFailed, token);
            subject.OnNext(envelope);

            Assert.IsTrue(task.IsCompleted);
            var result = task.GetAwaiter().GetResult();
            Assert.AreEqual(KernelEventTypes.CommandFailed, result.EventType);
        }

        [TestMethod]
        public void EventObserver_WaitForEventTypeAsync_CancellationThrowsTaskCanceledException()
        {
            var subject = new Subject<KernelEventEnvelope>();
            using var observer = new EventObserver(subject, "token");
            using var cts = new CancellationTokenSource();

            var task = observer.WaitForEventTypeAsync(KernelEventTypes.ReturnValueProduced, cts.Token);

            cts.Cancel();

            bool threw = false;
            try { task.GetAwaiter().GetResult(); }
            catch (TaskCanceledException) { threw = true; }
            catch (OperationCanceledException) { threw = true; }
            Assert.IsTrue(threw, "Cancelled token should cause TaskCanceledException");
        }

        [TestMethod]
        public void EventObserver_Dispose_Unsubscribes()
        {
            var subject = new Subject<KernelEventEnvelope>();
            const string token = "test-token";

            var observer = new EventObserver(subject, token);
            var task = observer.WaitForEventTypeAsync(KernelEventTypes.ReturnValueProduced);
            observer.Dispose();

            // After dispose, pushing a matching event should NOT complete the task
            // because the observer unsubscribed from the subject.
            subject.OnNext(MakeEnvelope(KernelEventTypes.ReturnValueProduced, token));

            Assert.IsFalse(task.IsCompleted, "After Dispose, events should not be received");
        }

        [TestMethod]
        public void EventObserver_StreamCompleted_FaultsAllPendingWaits()
        {
            var subject = new Subject<KernelEventEnvelope>();
            const string token = "test-token";

            using var observer = new EventObserver(subject, token);
            var eventTask = observer.WaitForEventTypeAsync(KernelEventTypes.ReturnValueProduced);
            var terminalTask = observer.WaitForTerminalEventAsync();

            // Simulate the kernel process dying
            subject.OnCompleted();

            bool eventFaulted = false;
            try { eventTask.GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { eventFaulted = true; }

            bool terminalFaulted = false;
            try { terminalTask.GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { terminalFaulted = true; }

            Assert.IsTrue(eventFaulted, "WaitForEventTypeAsync should fault when stream completes");
            Assert.IsTrue(terminalFaulted, "WaitForTerminalEventAsync should fault when stream completes");
        }

        [TestMethod]
        public void EventObserver_StreamError_FaultsAllPendingWaits()
        {
            var subject = new Subject<KernelEventEnvelope>();
            const string token = "test-token";

            using var observer = new EventObserver(subject, token);
            var eventTask = observer.WaitForEventTypeAsync(KernelEventTypes.ReturnValueProduced);
            var terminalTask = observer.WaitForTerminalEventAsync();

            var error = new InvalidOperationException("stream broke");
            subject.OnError(error);

            bool eventFaulted = false;
            try { eventTask.GetAwaiter().GetResult(); }
            catch (InvalidOperationException ex) when (ex == error) { eventFaulted = true; }

            bool terminalFaulted = false;
            try { terminalTask.GetAwaiter().GetResult(); }
            catch (InvalidOperationException ex) when (ex == error) { terminalFaulted = true; }

            Assert.IsTrue(eventFaulted, "WaitForEventTypeAsync should fault with stream error");
            Assert.IsTrue(terminalFaulted, "WaitForTerminalEventAsync should fault with stream error");
        }

        [TestMethod]
        public void EventObserver_NonTerminalEvent_DoesNotResolveTerminalWait()
        {
            var subject = new Subject<KernelEventEnvelope>();
            const string token = "test-token";

            using var observer = new EventObserver(subject, token);
            var task = observer.WaitForTerminalEventAsync();

            // Push a non-terminal event with the correct token
            subject.OnNext(MakeEnvelope(KernelEventTypes.ReturnValueProduced, token));

            Assert.IsFalse(task.IsCompleted, "Non-terminal events must not resolve the terminal wait");
        }

        [TestMethod]
        public void EventObserver_NullCommandToken_IsIgnored()
        {
            var subject = new Subject<KernelEventEnvelope>();
            using var observer = new EventObserver(subject, "my-token");

            var task = observer.WaitForEventTypeAsync(KernelEventTypes.ReturnValueProduced);

            // Envelope with null Command should be ignored (Command?.Token != _token)
            subject.OnNext(new KernelEventEnvelope
            {
                EventType = KernelEventTypes.ReturnValueProduced,
                Command = null
            });

            Assert.IsFalse(task.IsCompleted, "Event with null Command should be ignored");
        }
    }
}
