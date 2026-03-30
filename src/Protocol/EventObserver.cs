using System;
using System.Collections.Generic;

namespace PolyglotNotebooks.Protocol
{
    /// <summary>
    /// A minimal IObservable/IObserver subject that dispatches events to all subscribers.
    /// Used to avoid a dependency on System.Reactive for a lightweight stdio event stream.
    /// </summary>
    internal sealed class Subject<T> : IObservable<T>, IDisposable
    {
        private readonly List<IObserver<T>> _observers = new List<IObserver<T>>();
        private readonly object _lock = new object();
        private bool _completed;

        public IDisposable Subscribe(IObserver<T> observer)
        {
            lock (_lock)
            {
                if (_completed)
                {
                    observer.OnCompleted();
                    return EmptyDisposable.Instance;
                }
                _observers.Add(observer);
            }
            return new Unsubscriber(_observers, observer, _lock);
        }

        public void OnNext(T value)
        {
            IObserver<T>[] snapshot;
            lock (_lock) snapshot = _observers.ToArray();
            foreach (var observer in snapshot)
                observer.OnNext(value);
        }

        public void OnError(Exception error)
        {
            IObserver<T>[] snapshot;
            lock (_lock) snapshot = _observers.ToArray();
            foreach (var observer in snapshot)
                observer.OnError(error);
        }

        public void OnCompleted()
        {
            IObserver<T>[] snapshot;
            lock (_lock)
            {
                if (_completed) return;
                _completed = true;
                snapshot = _observers.ToArray();
                _observers.Clear();
            }
            foreach (var observer in snapshot)
                observer.OnCompleted();
        }

        public void Dispose() => OnCompleted();

        private sealed class Unsubscriber : IDisposable
        {
            private readonly List<IObserver<T>> _list;
            private readonly IObserver<T> _observer;
            private readonly object _lock;

            public Unsubscriber(List<IObserver<T>> list, IObserver<T> observer, object lockObj)
            {
                _list = list;
                _observer = observer;
                _lock = lockObj;
            }

            public void Dispose()
            {
                lock (_lock) _list.Remove(_observer);
            }
        }

        private sealed class EmptyDisposable : IDisposable
        {
            public static readonly EmptyDisposable Instance = new EmptyDisposable();
            public void Dispose() { }
        }
    }

    /// <summary>
    /// Adapts an Action delegate to IObserver, reducing boilerplate at subscription sites.
    /// </summary>
    internal sealed class ActionObserver<T> : IObserver<T>
    {
        private readonly Action<T> _onNext;
        private readonly Action<Exception>? _onError;
        private readonly Action? _onCompleted;

        public ActionObserver(Action<T> onNext, Action<Exception>? onError = null, Action? onCompleted = null)
        {
            _onNext = onNext;
            _onError = onError;
            _onCompleted = onCompleted;
        }

        public void OnNext(T value) => _onNext(value);
        public void OnError(Exception error) => _onError?.Invoke(error);
        public void OnCompleted() => _onCompleted?.Invoke();
    }

    /// <summary>
    /// Subscribes to the kernel event stream for a specific command token, enabling
    /// callers to await individual event types or the terminal (Succeeded/Failed) outcome.
    /// If the event stream terminates (process exits or errors), all pending tasks are
    /// faulted so callers do not hang indefinitely.
    /// </summary>
    public sealed class EventObserver : IDisposable
    {
        private readonly string _token;
        private readonly IDisposable _subscription;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, TaskCompletionSource<KernelEventEnvelope>> _pending
            = new System.Collections.Concurrent.ConcurrentDictionary<string, TaskCompletionSource<KernelEventEnvelope>>(StringComparer.Ordinal);
        private readonly TaskCompletionSource<KernelEventEnvelope> _terminalTcs
            = new TaskCompletionSource<KernelEventEnvelope>();

        public EventObserver(IObservable<KernelEventEnvelope> events, string token)
        {
            _token = token;
            _subscription = events.Subscribe(new ActionObserver<KernelEventEnvelope>(
                onNext: OnNext,
                onError: OnStreamError,
                onCompleted: OnStreamCompleted));
        }

        /// <summary>
        /// Returns a task that completes when an event of the given type is received for this token.
        /// Multiple calls for the same eventType share the same TCS.
        /// </summary>
        public async System.Threading.Tasks.Task<KernelEventEnvelope> WaitForEventTypeAsync(
            string eventType,
            System.Threading.CancellationToken ct = default)
        {
            var tcs = _pending.GetOrAdd(eventType,
                _ => new TaskCompletionSource<KernelEventEnvelope>());
            var registration = ct.Register(() => tcs.TrySetCanceled());
            try { return await tcs.Task.ConfigureAwait(false); }
            finally { registration.Dispose(); }
        }

        /// <summary>
        /// Returns a task that completes when CommandSucceeded or CommandFailed is received for this token.
        /// Times out after 5 minutes to prevent VS from freezing if the kernel hangs.
        /// NuGet package installs (e.g. #r "nuget:...") can take well over 60 seconds
        /// for large dependency trees; kernel crashes are handled separately via OnStreamCompleted.
        /// </summary>
        public async System.Threading.Tasks.Task<KernelEventEnvelope> WaitForTerminalEventAsync(
            System.Threading.CancellationToken ct = default)
        {
            var registration = ct.Register(() => _terminalTcs.TrySetCanceled());
            try
            {
                var completed = await System.Threading.Tasks.Task.WhenAny(
                    _terminalTcs.Task,
                    System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(300), ct)).ConfigureAwait(false);
                if (completed != _terminalTcs.Task)
                {
                    // Distinguish user/caller cancellation from a genuine 5-minute timeout.
                    // Task.Delay(300, ct) and _terminalTcs.TrySetCanceled() race when ct fires;
                    // if Delay wins WhenAny we must still propagate as OperationCanceledException.
                    ct.ThrowIfCancellationRequested();
                    throw new TimeoutException("Kernel did not respond within 5 minutes.");
                }
                return await _terminalTcs.Task.ConfigureAwait(false);
            }
            finally { registration.Dispose(); }
        }

        private void OnNext(KernelEventEnvelope envelope)
        {
            if (envelope.Command?.Token != _token)
                return;

            if (_pending.TryGetValue(envelope.EventType, out var tcs))
                tcs.TrySetResult(envelope);

            if (envelope.EventType == KernelEventTypes.CommandSucceeded ||
                envelope.EventType == KernelEventTypes.CommandFailed)
            {
                _terminalTcs.TrySetResult(envelope);
            }
        }

        /// <summary>
        /// Called when the kernel process terminates unexpectedly.
        /// Faults all pending waits so callers receive a meaningful exception.
        /// </summary>
        private void OnStreamCompleted()
        {
            var ex = new InvalidOperationException(
                "The kernel process terminated before the command completed.");
            FaultAllPending(ex);
        }

        private void OnStreamError(Exception error)
        {
            FaultAllPending(error);
        }

        private void FaultAllPending(Exception ex)
        {
            foreach (var tcs in _pending.Values)
                tcs.TrySetException(ex);
            _terminalTcs.TrySetException(ex);
        }

        public void Dispose() => _subscription.Dispose();
    }
}
