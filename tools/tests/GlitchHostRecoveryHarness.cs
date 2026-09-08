using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Glitch.Core;
using Glitch.Infrastructure;
using NinjaTrader.Cbi;

internal static class GlitchHostRecoveryHarness
{
    private static int _checks;
    private static void Assert(bool condition, string message)
    { _checks++; if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture : IDisposable
    {
        public readonly Instrument Instrument;
        public readonly Account Account;
        public readonly GlitchOperationJournal Journal;
        public readonly GlitchEngine Seed = new GlitchEngine();
        public GlitchRuntimeHost Host;
        private readonly string _root = Path.Combine(Path.GetTempPath(), "GlitchHostRecovery-" + Guid.NewGuid().ToString("N"));
        private readonly List<GlitchRuntimeNotice> _notices = new List<GlitchRuntimeNotice>();

        public Fixture()
        {
            NinjaTrader.Core.Globals.UserDataDir = _root;
            Journal = new GlitchOperationJournal();
            Instrument = new Instrument { FullName = "M2K 09-26" };
            Instrument.MarketData.Bid.Price = 2972.1;
            Instrument.MarketData.Ask.Price = 2972.2;
            Instrument.Registry[Instrument.FullName] = Instrument;
            Account = new Account { Name = "Master" };
            Account.All.Clear(); Account.All.Add(Account);
            Append(new PositionObserved(Account.Name, Instrument.FullName, -1));
        }
        public IReadOnlyList<GlitchCommand> Append(GlitchInput input)
        {
            Assert(Journal.TryAppendInput(input, "test", out string error), error);
            return Seed.Handle(input);
        }
        public FlattenAccountCommand Flatten(string id, string terminal = "unknown")
        {
            var command = Append(new FlattenAccountRequested(id, Account.Name, "user_flatten_all"))
                .OfType<FlattenAccountCommand>().Single();
            foreach (string phase in new[] { "accepted", "native_request_started", "native_request_returned" })
                Assert(Journal.TryAppend(command, phase, "test", out string error), error);
            if (terminal == "unknown") Append(new NativeRequestUnknownObserved(command.CommandId, "native_flatten_timeout"));
            if (terminal == "failed") Append(new NativeRequestFailedObserved(command.CommandId, "native_failed"));
            if (terminal == "complete") Append(new FlattenCompletedObserved(command.CommandId, command.AccountName));
            return command;
        }
        public bool HasNotice(string value)
        { lock (_notices) return _notices.Any(n => n.Message.Contains(value)); }
        public void Start()
        {
            lock (_notices) _notices.Clear();
            Host = new GlitchRuntimeHost();
            Host.Notice += n => { lock (_notices) _notices.Add(n); };
            Host.Start();
            Assert(SpinWait.SpinUntil(() => HasNotice("recovery_completed|"), 3000), "host did not finish recovery");
            lock (_notices)
                Assert(!HasNotice("recovery_blocked|") && !HasNotice("runtime_input_failed|"),
                    "host recovery faulted: " + string.Join(";", _notices.Select(n => n.Message)));
        }
        public bool IsFenced => ((GlitchMutationGate)typeof(GlitchRuntimeHost)
            .GetField("_mutationGate", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(Host)).IsFenced(Account.Name);
        public FlattenCompletedObserved[] Completions()
        {
            Assert(Journal.TryLoad(out var records, out string error), error);
            return records.Select(r => r.Input).OfType<FlattenCompletedObserved>().ToArray();
        }
        public void Enter(string id)
        {
            var receipt = Host.SubmitHermes(new HermesEntryRequested(id, Account.Name, Instrument.FullName,
                1, 2972.1m, 2970.6m, new[] { new HermesTarget(1, 2970.6m, 2975.1m) }));
            Assert(receipt.Disposition == GlitchHermesSubmissionDisposition.Accepted,
                "new intent was not accepted: " + receipt.Disposition + "|" + receipt.Code);
        }
        public void Dispose()
        {
            Host?.Dispose();
            Account.All.Clear();
            // Only this fixture's newly created, explicitly named temporary tree.
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }

    private static void ClearedUnknownDoesNotStrandFence()
    {
        foreach (string terminal in new[] { "unknown", "failed" })
        using (var f = new Fixture())
        {
            var flatten = f.Flatten("old-" + terminal, terminal);
            // Model an earlier intent blocked by this same fence: never replay it.
            f.Append(new PositionObserved(f.Account.Name, f.Instrument.FullName, 0));
            var entry = f.Append(new HermesEntryRequested("blocked-old", f.Account.Name, f.Instrument.FullName,
                1, 2972.1m, 2970.6m, new[] { new HermesTarget(1, 2970.6m, 2975.1m) }))
                .OfType<SubmitMarketCommand>().Single();
            Assert(f.Journal.TryAppend(entry, "native_request_not_started", "account_fenced_by_flatten", out string error), error);
            f.Append(new NativeRequestFailedObserved(entry.CommandId, "account_fenced_by_flatten"));
            f.Start();
            Assert(SpinWait.SpinUntil(() => !f.IsFenced, 2000), "native flat/clear account remained fenced after " + terminal + " recovery");
            Assert(f.Completions().Single().CommandId == flatten.CommandId, "resolved flatten was not journaled");
            Assert(f.Account.Flattens == 0 && f.Account.Submits == 0 && f.Account.Creates == 0,
                "recovery resubmitted old flatten or entry instead of observing native state");
            // Replay the newly persisted completion a second time (idempotent reload).
            f.Host.Dispose(); f.Start();
            Assert(!f.IsFenced && f.Completions().Length == 1, "second recovery recreated the fence or completion");
            f.Enter("fresh-entry");
            Assert(SpinWait.SpinUntil(() => f.Account.Submits == 1, 2000), "fresh entry still blocked after native clear proof");
            Assert(f.Account.Creates == 1 && f.Account.Flattens == 0, "entry path made duplicate mutations");
        }
    }

    private static void UnresolvedNativeStateStaysFenced()
    {
        foreach (string state in new[] { "position", "missing", "Initialized", "CancelPending", "Working" })
        using (var f = new Fixture())
        {
            f.Flatten("old");
            var otherInstrument = new Instrument { FullName = "MNQ 09-26" };
            Instrument.Registry[otherInstrument.FullName] = otherInstrument;
            if (state == "position") f.Account.SetPosition(otherInstrument, 2);
            else if (state == "missing") Account.All.Clear();
            else f.Account.Orders.Add(new Order { Account = f.Account, Instrument = otherInstrument,
                Quantity = 1, OrderState = (OrderState)Enum.Parse(typeof(OrderState), state), OrderId = "existing" });
            f.Start();
            Assert(f.IsFenced && f.Completions().Length == 0, "recovery unlocked unresolved native " + state);
            f.Enter("blocked-fresh");
            Assert(SpinWait.SpinUntil(() => f.HasNotice("reason=account_fenced_by_flatten"), 2000), "fenced entry was not rejected");
            Assert(f.Account.Creates == 0 && f.Account.Submits == 0 && f.Account.Flattens == 0,
                "unknown recovery retried or opened native exposure");
        }
    }

    private static void OnlyLatestFlattenMayResolve()
    {
        foreach (string state in new[] { "unknown", "pending", "complete" })
        using (var f = new Fixture())
        {
            f.Flatten("older");
            var latest = f.Flatten("newer", state);
            f.Start();
            Assert(SpinWait.SpinUntil(() => !f.IsFenced, 2000), "latest native-clear flatten stayed fenced");
            Assert(f.Completions().Single().CommandId == latest.CommandId, "older flatten displaced the newer request");
            Assert(f.Account.Flattens == 0 && f.Account.Creates == 0, "native-clear recovery replayed a mutation");
        }
        using (var f = new Fixture())
        {
            f.Flatten("older"); f.Flatten("newer", "pending");
            f.Account.SetPosition(f.Instrument, -1);
            f.Start();
            Assert(f.IsFenced && f.Completions().Length == 0, "older unknown cleared newer unresolved request");
            Assert(f.Account.Flattens == 0, "already-started newer flatten was replayed");
        }
    }

    public static void Run()
    {
        ClearedUnknownDoesNotStrandFence();
        UnresolvedNativeStateStaysFenced();
        OnlyLatestFlattenMayResolve();
        Console.WriteLine("Glitch host recovery harness passed: " + _checks + " checks.");
    }
}
