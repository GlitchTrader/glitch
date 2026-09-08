using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Glitch.Core;
using Glitch.Infrastructure;
using NinjaTrader.Cbi;

internal static class GlitchNativeSafetyHarness
{
    private static int _checks;
    private static void Assert(bool condition, string message)
    { _checks++; if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture : IDisposable
    {
        public readonly Instrument Instrument;
        public readonly Account Account;
        public readonly NinjaTraderGateway Gateway;
        private readonly List<GlitchInput> _facts = new List<GlitchInput>();
        public readonly ManualResetEvent TimeoutObserved = new ManualResetEvent(false);
        public Fixture(double tick = 0.1, bool throwOnTimeoutNotice = false)
        {
            Instrument = new Instrument { FullName = "M2K 09-26" };
            Instrument.MasterInstrument.TickSize = tick;
            Instrument.MarketData.Bid.Price = 2972.1;
            Instrument.MarketData.Ask.Price = 2972.2;
            Instrument.Registry[Instrument.FullName] = Instrument;
            Account = new Account { Name = "Master" };
            Account.All.Clear(); Account.All.Add(Account);
            Action<string, string, string> notice = (a, c, m) => {
                if (throwOnTimeoutNotice && m.StartsWith("native_flatten_timeout"))
                    throw new InvalidOperationException("retiring UI subscriber");
            };
#if BASELINE
            Gateway = new NinjaTraderGateway(notice);
#else
            Gateway = new NinjaTraderGateway(notice, TimeSpan.FromMilliseconds(80));
#endif
            Gateway.Start(input => {
                lock (_facts) _facts.Add(input);
                if (input is NativeRequestUnknownObserved) TimeoutObserved.Set();
            });
        }
        public T[] Facts<T>() where T : GlitchInput
        { lock (_facts) return _facts.OfType<T>().ToArray(); }
        public void Dispose() { Gateway.Dispose(); TimeoutObserved.Dispose(); }
    }

    private static void Reject(Action action, string marker)
    {
        try { action(); }
        catch (InvalidOperationException error)
        { Assert(error.Message.Contains(marker), "wrong rejection: " + error.Message); return; }
        throw new InvalidOperationException("expected rejection before any native order: " + marker);
    }

    private static void TestIncidentEntry()
    {
        using (var f = new Fixture())
        {
            var engine = new GlitchEngine();
            engine.Handle(new PositionObserved("Master", f.Instrument.FullName, 0));
            SubmitMarketCommand entry = engine.Handle(new HermesEntryRequested(
                "incident", "Master", f.Instrument.FullName, -1, 2972.1m, 2973.6m,
                new[] { new HermesTarget(1, 2973.6m, 2969.45m) }))
                .OfType<SubmitMarketCommand>().Single();
            Reject(() => f.Gateway.Execute(entry), "entry_target_not_representable");
            Assert(f.Account.Creates == 0 && f.Account.Submits == 0 && f.Account.Positions.Count == 0,
                "invalid incident bracket opened exposure or stranded an Initialized order");
            Assert(GlitchExecutionEvidenceWriter.Codes.Contains("failed:entry_protection_not_representable"),
                "native rejection did not produce a terminal Hermes execution receipt");
        }
    }

    private static void TestAllLegsBeforeCreate()
    {
        using (var f = new Fixture())
        {
            var bracket = new SubmitProtectionCommand("invalidtarget", "Master", f.Instrument.FullName,
                -2, 2973.6m, new[] {
                    new ProtectionTarget("L1", 1, 2973.6m, 2969.4m),
                    new ProtectionTarget("L2", 1, 2973.6m, 2969.45m)
                }, "parent", false, 2972.1m);
            Reject(() => f.Gateway.Execute(bracket), "not aligned");
            Assert(f.Account.Creates == 0 && f.Account.Submits == 0,
                "invalid later protection leg stranded earlier children");
        }
    }

    private static void TestValidGeometryAndManualCloses()
    {
        foreach (double tick in new[] { 0.1, 0.25 })
        foreach (int direction in new[] { -1, 1 })
        using (var f = new Fixture(tick))
        {
            var template = new ProtectionTemplate(-direction * 1.5m,
                new[] { new ProtectionLegTemplate("L1", 1, -direction * 1.5m, direction * 3m) });
            f.Gateway.Execute(new SubmitMarketCommand("valid", GlitchCommandPurpose.HermesMasterEntry,
                "Master", f.Instrument.FullName, direction, "intent", template));
            Assert(f.Account.Creates == 1 && f.Account.Submits == 1, "valid entry changed");
        }
        using (var f = new Fixture())
        {
            f.Gateway.Execute(new SubmitMarketCommand("manualcopy", GlitchCommandPurpose.Replication,
                "Master", f.Instrument.FullName, 1, "manual"));
            f.Account.SetPosition(f.Instrument, -2);
            f.Gateway.Execute(new SubmitMarketCommand("close", GlitchCommandPurpose.HermesMasterExit,
                "Master", f.Instrument.FullName, 2, "close"));
            Assert(f.Account.Submits == 2 && f.Account.Orders.Last().OrderAction == OrderAction.BuyToCover,
                "manual replication or risk-reducing close was blocked");
        }
        using (var f = new Fixture())
        {
            Reject(() => f.Gateway.Execute(new SubmitMarketCommand("missing",
                GlitchCommandPurpose.HermesMasterEntry, "Master", f.Instrument.FullName, 1, "intent")),
                "entry_protection_missing");
            Assert(f.Account.Creates == 0, "missing bracket opened exposure");
            f.Gateway.Execute(new SubmitProtectionCommand("validbracket", "Master", f.Instrument.FullName,
                -1, 2973.6m, new[] { new ProtectionTarget("L1", 1, 2973.6m, 2969.4m) }, "parent", false, 2972.1m));
            Assert(f.Account.Creates == 2 && f.Account.Submits == 1,
                "valid protection no longer submits one OCO batch");
            Assert(f.Account.Orders.All(o => o.OrderState == OrderState.Working)
                && f.Account.Orders.Select(o => o.Oco).Distinct().Count() == 1, "valid OCO changed");
        }
    }

    private static void TestFlattenTimeoutAndRetry()
    {
        using (var f = new Fixture())
        {
            var engine = new GlitchEngine();
            engine.Handle(new PositionObserved("Master", f.Instrument.FullName, -2));
            f.Account.SetPosition(f.Instrument, -2);
            FlattenAccountCommand first = engine.Handle(new FlattenAccountRequested(
                "first", "Master", "native_protection_failed|incident"))
                .OfType<FlattenAccountCommand>().Single();
            f.Gateway.Execute(first);
            Assert(f.TimeoutObserved.WaitOne(2000), "flatten timeout stayed silently pending");
            var timeout = f.Facts<NativeRequestUnknownObserved>().Single();
            Assert(timeout.CommandId == first.CommandId && f.Facts<FlattenCompletedObserved>().Length == 0,
                "timeout was incorrectly reported as flat");
            engine.Handle(timeout);
            FlattenAccountCommand retry = engine.Handle(new FlattenAccountRequested(
                "retry", "Master", "user_flatten_all"))
                .OfType<FlattenAccountCommand>().Single();
            f.Gateway.Execute(retry);
            engine.Handle(new NativeRequestUnknownObserved(first.CommandId, "late timeout"));
            engine.Handle(new NativeRequestFailedObserved(first.CommandId, "late failure"));
            Assert(!engine.Handle(new FlattenAccountRequested("duplicate", "Master", "user_flatten_all"))
                .OfType<FlattenAccountCommand>().Any(), "old failure removed the newer pending flatten barrier");
            Assert(f.Account.Flattens == 2 && f.Account.Submits == 0,
                "explicit retry was ignored or blindly submitted a market order");
            Assert(f.Account.LastFlatten.Single() == f.Instrument, "flatten changed instrument scope");
            f.Account.CompleteFlat(f.Instrument);
            Assert(f.Facts<FlattenCompletedObserved>().Single().CommandId == retry.CommandId,
                "old flatten completion displaced the retry");
        }
    }

    private static void TestSynchronousFlatAndDisposal()
    {
        using (var f = new Fixture())
        {
            f.Account.SetPosition(f.Instrument, -1);
            f.Account.OnFlatten = (account, instruments) => account.CompleteFlat(instruments.Single());
            f.Gateway.Execute(new FlattenAccountCommand("sync", "Master", new[] { f.Instrument.FullName }, "user_flatten_all"));
            Assert(f.Facts<FlattenCompletedObserved>().Length == 1, "synchronous close not recognized");
            Assert(!f.TimeoutObserved.WaitOne(150), "completed flatten leaked its timeout");
        }
        using (var f = new Fixture())
        {
            f.Account.SetPosition(f.Instrument, -1);
            f.Gateway.Execute(new FlattenAccountCommand("dispose", "Master", new[] { f.Instrument.FullName }, "user_flatten_all"));
            f.Gateway.Dispose();
            Assert(!f.TimeoutObserved.WaitOne(150), "retired gateway emitted a timeout");
        }
    }

    private static void TestFlatPositionWithStuckOrder()
    {
        using (var f = new Fixture(throwOnTimeoutNotice: true))
        {
            Order ghost = f.Account.CreateOrder(f.Instrument, OrderAction.BuyToCover,
                OrderType.StopMarket, OrderEntry.Automated, TimeInForce.Gtc, 1, 0, 2973.6,
                "incident", "incidentstop", DateTime.MaxValue, null);
            f.Account.Cancel(new[] { ghost });
            f.Gateway.Execute(new FlattenAccountCommand("pendingcancel", "Master",
                new[] { f.Instrument.FullName }, "user_flatten_all"));
            Assert(f.TimeoutObserved.WaitOne(2000), "flat position hid the nonterminal order");
            Assert(f.Facts<FlattenCompletedObserved>().Length == 0,
                "CancelPending order was incorrectly considered cleared");
            ghost.OrderState = OrderState.Cancelled;
            f.Account.EmitOrder(ghost);
            Assert(f.Facts<FlattenCompletedObserved>().Single().CommandId == "pendingcancel",
                "late native cancellation could not complete the timed-out flatten");
        }
    }

    private static Instrument RegisterInstrument(string name)
    {
        var instrument = new Instrument { FullName = name };
        Instrument.Registry[name] = instrument;
        return instrument;
    }

    private static void TestFlattenDiscoversNativeExposure()
    {
        using (var f = new Fixture())
        {
            Instrument known = RegisterInstrument("MES 09-26");
            Instrument orderOnly = RegisterInstrument("MNQ 09-26");
            f.Account.SetPosition(f.Instrument, -2);
            Order pending = f.Account.CreateOrder(orderOnly, OrderAction.Buy, OrderType.Limit,
                OrderEntry.Automated, TimeInForce.Gtc, 1, 29000, 0, "", "manual", DateTime.MaxValue, null);
            f.Account.Submit(new[] { pending });
            var unrelated = new Account { Name = "Unrelated" };
            Account.All.Add(unrelated);
            unrelated.SetPosition(f.Instrument, 3);
            var command = new FlattenAccountCommand("nativeunion", "Master",
                new[] { known.FullName, known.FullName }, "user_flatten_all");
            Assert(!f.Gateway.IsFlattenSatisfied(command),
                "recovery declared a partial instrument list account-flat");
            f.Account.OnFlatten = (account, instruments) => {
                foreach (Instrument instrument in instruments) account.CompleteFlat(instrument);
            };
            f.Gateway.Execute(command);
            Assert(f.Account.LastFlatten.Length == 3 && f.Account.LastFlatten.Contains(known)
                && f.Account.LastFlatten.Contains(f.Instrument) && f.Account.LastFlatten.Contains(orderOnly),
                "account flatten omitted native position/order instruments or duplicated a scope");
            Assert(f.Gateway.IsFlattenSatisfied(command)
                && f.Facts<FlattenCompletedObserved>().Single().CommandId == command.CommandId,
                "complete native account cleanup was not acknowledged");
            Assert(unrelated.Flattens == 0 && unrelated.Positions.Single().Quantity == 3,
                "account flatten affected another account");
        }
    }

    private static void TestOmittedNonterminalOrders()
    {
        foreach (OrderState state in new[] { OrderState.Initialized, OrderState.CancelPending,
            OrderState.ChangePending, OrderState.Submitted, OrderState.PartFilled })
        using (var f = new Fixture())
        {
            Instrument known = RegisterInstrument("MES 09-26");
            Order orphan = f.Account.CreateOrder(f.Instrument, OrderAction.BuyToCover, OrderType.StopMarket,
                OrderEntry.Automated, TimeInForce.Gtc, 1, 0, 2973.6, "", "orphan", DateTime.MaxValue, null);
            orphan.OrderState = state;
            var command = new FlattenAccountCommand("omitted-" + state, "Master",
                new[] { known.FullName }, "user_flatten_all");
            Assert(!f.Gateway.IsFlattenSatisfied(command), "recovery ignored omitted " + state + " order");
            f.Gateway.Execute(command);
            Assert(f.Account.LastFlatten.Contains(f.Instrument), "native flatten omitted " + state + " order");
            Assert(f.TimeoutObserved.WaitOne(2000) && f.Facts<FlattenCompletedObserved>().Length == 0,
                "nonterminal omitted order was reported cleared: " + state);
            orphan.OrderState = OrderState.Cancelled;
            f.Account.EmitOrder(orphan);
            Assert(f.Facts<FlattenCompletedObserved>().Single().CommandId == command.CommandId
                && f.Gateway.IsFlattenSatisfied(command), "late cancellation did not settle " + state);
        }
    }

    private static void TestLateInstrumentBlocksFalseCompletion()
    {
        using (var f = new Fixture())
        {
            Instrument lateInstrument = RegisterInstrument("MES 09-26");
            Order late = null;
            f.Account.SetPosition(f.Instrument, -1);
            f.Account.OnFlatten = (account, instruments) => {
                late = account.CreateOrder(lateInstrument, OrderAction.Buy, OrderType.Limit,
                    OrderEntry.Automated, TimeInForce.Gtc, 1, 7700, 0, "", "late", DateTime.MaxValue, null);
                account.Submit(new[] { late });
                account.CompleteFlat(f.Instrument);
            };
            var command = new FlattenAccountCommand("lateinstrument", "Master",
                new[] { f.Instrument.FullName }, "user_flatten_all");
            f.Gateway.Execute(command);
            Assert(f.TimeoutObserved.WaitOne(2000) && f.Facts<FlattenCompletedObserved>().Length == 0,
                "flatten completed while another native instrument still had an order");
            Assert(!f.Gateway.IsFlattenSatisfied(command), "recovery ignored an order arriving during flatten");
            f.Account.Cancel(new[] { late });
            Assert(f.Facts<FlattenCompletedObserved>().Single().CommandId == command.CommandId,
                "late order cancellation did not complete the account request");
        }
    }

    public static int Main()
    {
        try
        {
            TestFlattenDiscoversNativeExposure();
            TestOmittedNonterminalOrders();
            TestLateInstrumentBlocksFalseCompletion();
            TestIncidentEntry();
            TestAllLegsBeforeCreate();
            TestValidGeometryAndManualCloses();
            TestFlattenTimeoutAndRetry();
            TestSynchronousFlatAndDisposal();
            TestFlatPositionWithStuckOrder();
            Console.WriteLine("Glitch native safety harness passed: " + _checks + " checks.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
