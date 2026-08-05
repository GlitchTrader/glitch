using System;

namespace Glitch.Infrastructure
{
    /// <summary>
    /// AppDomain-wide ownership for the AddOn runtime. NinjaScript recompiles
    /// load a new Glitch assembly into the same AppDomain, so assembly-static
    /// fields cannot prevent two generations from overlapping.
    /// </summary>
    internal sealed class GlitchRuntimeOwnershipLease : IDisposable
    {
        private const string GateSlot = "Glitch.RuntimeOwnership.Gate.v1";
        private const string OwnerSlot = "Glitch.RuntimeOwnership.Owner.v1";

        private readonly Action _shutdown;
        private bool _ownsSlot;

        public GlitchRuntimeOwnershipLease(Action shutdown)
        {
            _shutdown = shutdown ?? throw new ArgumentNullException(nameof(shutdown));
        }

        public bool IsOwner
        {
            get
            {
                lock (GetGate())
                    return _ownsSlot && ReferenceEquals(GetOwner(), _shutdown);
            }
        }

        public void Acquire()
        {
            lock (GetGate())
            {
                if (_ownsSlot && ReferenceEquals(GetOwner(), _shutdown))
                    return;

                Action priorOwner = GetOwner();
                if (priorOwner != null && !ReferenceEquals(priorOwner, _shutdown))
                {
                    // Do not clear the prior owner until its synchronous shutdown
                    // succeeds. A failed handoff must never create two runtimes.
                    priorOwner();
                    if (ReferenceEquals(GetOwner(), priorOwner))
                        AppDomain.CurrentDomain.SetData(OwnerSlot, null);
                }

                Action remainingOwner = GetOwner();
                if (remainingOwner != null && !ReferenceEquals(remainingOwner, _shutdown))
                    throw new InvalidOperationException("Glitch runtime ownership changed during handoff.");

                AppDomain.CurrentDomain.SetData(OwnerSlot, _shutdown);
                _ownsSlot = true;
            }
        }

        public void Dispose()
        {
            lock (GetGate())
            {
                if (_ownsSlot && ReferenceEquals(GetOwner(), _shutdown))
                    AppDomain.CurrentDomain.SetData(OwnerSlot, null);
                _ownsSlot = false;
            }
        }

        internal static void ResetForTests()
        {
            lock (GetGate())
                AppDomain.CurrentDomain.SetData(OwnerSlot, null);
        }

        private static Action GetOwner()
        {
            object owner = AppDomain.CurrentDomain.GetData(OwnerSlot);
            if (owner == null)
                return null;

            Action shutdown = owner as Action;
            if (shutdown == null)
                throw new InvalidOperationException("The Glitch runtime ownership slot is invalid.");
            return shutdown;
        }

        private static object GetGate()
        {
            AppDomain domain = AppDomain.CurrentDomain;
            lock (domain)
            {
                object gate = domain.GetData(GateSlot);
                if (gate == null)
                {
                    gate = new object();
                    domain.SetData(GateSlot, gate);
                }
                return gate;
            }
        }
    }
}
