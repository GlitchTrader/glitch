using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace Glitch.Core
{
    /// <summary>
    /// Immutable input copied from a native callback before it crosses into the runtime.
    /// This lifecycle tracer intentionally carries no NinjaTrader objects and exposes no
    /// order capability.
    /// </summary>
    public sealed class GlitchRuntimeEvent
    {
        public GlitchRuntimeEvent(long sequence, string kind)
            : this(sequence, kind, null)
        {
        }

        public GlitchRuntimeEvent(long sequence, string kind, GlitchInput input)
        {
            if (string.IsNullOrWhiteSpace(kind))
                throw new ArgumentException("Event kind is required.", nameof(kind));

            Sequence = sequence;
            Kind = kind;
            Input = input;
        }

        public long Sequence { get; }
        public string Kind { get; }
        public GlitchInput Input { get; }
    }

    /// <summary>
    /// Owns exactly one serialized event consumer generation at a time. Native callback
    /// subscriptions retain the generation returned by Start and must present it to
    /// TryPost; callbacks from a retired AddOn generation are therefore ignored.
    /// </summary>
    public sealed class GlitchRuntime : IDisposable
    {
        private sealed class Session
        {
            public Session(long generation)
            {
                Generation = generation;
                Queue = new BlockingCollection<GlitchRuntimeEvent>();
            }

            public long Generation { get; }
            public BlockingCollection<GlitchRuntimeEvent> Queue { get; }
            public Thread Worker { get; set; }
        }

        private readonly object _gate = new object();
        private readonly Action<GlitchRuntimeEvent> _consume;
        private readonly Action<Exception> _reportError;
        private readonly TimeSpan _stopTimeout;
        private Session _session;
        private long _lastGeneration;
        private bool _stopping;
        private bool _disposed;

        public GlitchRuntime(Action<GlitchRuntimeEvent> consume)
            : this(consume, error => { }, TimeSpan.FromSeconds(5))
        {
        }

        public GlitchRuntime(Action<GlitchRuntimeEvent> consume, Action<Exception> reportError)
            : this(consume, reportError, TimeSpan.FromSeconds(5))
        {
        }

        internal GlitchRuntime(
            Action<GlitchRuntimeEvent> consume,
            Action<Exception> reportError,
            TimeSpan stopTimeout)
        {
            _consume = consume ?? throw new ArgumentNullException(nameof(consume));
            _reportError = reportError ?? throw new ArgumentNullException(nameof(reportError));
            if (stopTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(stopTimeout));
            _stopTimeout = stopTimeout;
        }

        public bool IsRunning
        {
            get
            {
                lock (_gate)
                    return _session != null;
            }
        }

        public long Start()
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                if (_stopping)
                    throw new InvalidOperationException("The previous runtime generation is still stopping.");
                if (_session != null)
                    return _session.Generation;

                var session = new Session(++_lastGeneration);
                session.Worker = new Thread(() => Consume(session))
                {
                    IsBackground = true,
                    Name = "Glitch Runtime " + session.Generation
                };
                _session = session;
                session.Worker.Start();
                return session.Generation;
            }
        }

        public bool TryPost(long generation, GlitchRuntimeEvent runtimeEvent)
        {
            if (runtimeEvent == null)
                throw new ArgumentNullException(nameof(runtimeEvent));

            lock (_gate)
            {
                Session session = _session;
                if (_disposed || _stopping || session == null || session.Generation != generation)
                    return false;

                return session.Queue.TryAdd(runtimeEvent);
            }
        }

        public void Stop()
        {
            Session session;
            lock (_gate)
            {
                session = _session;
                if (session == null)
                    return;
                if (ReferenceEquals(Thread.CurrentThread, session.Worker))
                    throw new InvalidOperationException("The runtime cannot stop itself from its consumer callback.");

                _session = null;
                _stopping = true;
                session.Queue.CompleteAdding();
            }

            if (!session.Worker.Join(_stopTimeout))
                throw new TimeoutException("The Glitch runtime did not stop within " + _stopTimeout + ".");

            session.Queue.Dispose();
            lock (_gate)
                _stopping = false;
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                    return;
            }

            Stop();
            lock (_gate)
                _disposed = true;
        }

        private void Consume(Session session)
        {
            foreach (GlitchRuntimeEvent runtimeEvent in session.Queue.GetConsumingEnumerable())
            {
                try
                {
                    _consume(runtimeEvent);
                }
                catch (Exception error)
                {
                    try
                    {
                        _reportError(error);
                    }
                    catch (Exception reportFailure)
                    {
                        Trace.TraceError(
                            "Glitch runtime error reporter failed: " + reportFailure);
                    }
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(GlitchRuntime));
        }
    }
}
