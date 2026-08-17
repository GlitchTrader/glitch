using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Glitch.Core;
using Glitch.Services;

namespace Glitch.Infrastructure
{
    public sealed class GlitchRouteDefinition
    {
        public string RouteId { get; set; }
        public string MasterAccount { get; set; }
        public string FollowerAccount { get; set; }
        public decimal Ratio { get; set; }
        public bool Enabled { get; set; }
    }

    public sealed class GlitchRuntimeNotice
    {
        public DateTime CreatedUtc { get; set; }
        public string AccountName { get; set; }
        public string Category { get; set; }
        public string Message { get; set; }
    }

    public enum GlitchHermesSubmissionDisposition
    {
        Accepted,
        Duplicate,
        ContentConflict,
        Unavailable
    }

    public sealed class GlitchHermesSubmissionReceipt
    {
        public GlitchHermesSubmissionDisposition Disposition { get; set; }
        public string IntentId { get; set; }
        public string ContentFingerprint { get; set; }
        public string Status { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// AddOn-lifetime composition root. It owns the reducer, serialized event loop,
    /// and sole native gateway; windows are clients and may come and go independently.
    /// </summary>
    public sealed class GlitchRuntimeHost : IDisposable
    {
        private sealed class RecoveryCommandState
        {
            public GlitchCommand Command;
            public string Fingerprint;
            public string LatestPhase;
        }

        private static readonly object ActiveGate = new object();
        private static GlitchRuntimeHost _active;

        private readonly object _gate = new object();
        private readonly object _configurationGate = new object();
        private readonly object _hermesGate = new object();
        private readonly GlitchEngine _engine = new GlitchEngine();
        private readonly GlitchRuntime _runtime;
        private readonly NinjaTraderGateway _gateway;
        private readonly GlitchOperationJournal _operationJournal;
        private readonly GlitchMutationGate _mutationGate = new GlitchMutationGate();
        private readonly Dictionary<string, GlitchRouteDefinition> _configuredRoutes =
            new Dictionary<string, GlitchRouteDefinition>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _commandFingerprints =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RecoveryCommandState> _recoveryJournalCommands =
            new Dictionary<string, RecoveryCommandState>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, GlitchCommand> _recoveryEmittedCommands =
            new Dictionary<string, GlitchCommand>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _recoveryEmissionOrder = new List<string>();
        private readonly HashSet<string> _recoveryNativeCorrelations =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, GlitchHermesSubmissionReceipt> _hermesReceipts =
            new Dictionary<string, GlitchHermesSubmissionReceipt>(StringComparer.OrdinalIgnoreCase);
        private long _eventSequence;
        private long _generation;
        private bool _started;
        private bool _stopping;
        private bool _replicationEnabled;
        private bool _recovering;
        private bool _mutationsAllowed = true;
        private bool _runtimeFailed;

        public GlitchRuntimeHost()
        {
            _operationJournal = new GlitchOperationJournal();
            _gateway = new NinjaTraderGateway(PublishNotice);
            _runtime = new GlitchRuntime(Consume, OnRuntimeInputFailed);
        }

        public static GlitchRuntimeHost Active
        {
            get
            {
                lock (ActiveGate)
                    return _active;
            }
        }

        public event Action<GlitchRuntimeNotice> Notice;

        public void Start()
        {
            _recovering = true;
            if (!_operationJournal.TryLoad(
                out IReadOnlyList<GlitchRecoveryRecord> recoveryRecords,
                out string recoveryError))
            {
                BlockMutations("journal_unreadable");
                PublishNotice(
                    "System",
                    "Recovery",
                    "recovery_blocked|reason=journal_unreadable|error=" + Clean(recoveryError));
            }
            else
            {
                try
                {
                    ReplayRecovery(recoveryRecords);
                }
                catch (Exception error)
                {
                    BlockMutations("replay_failed");
                    PublishNotice(
                        "System",
                        "Recovery",
                        "recovery_blocked|reason=replay_failed|error=" + Clean(error.Message));
                }
            }
            LoadPersistedRouteConfiguration();
            lock (_gate)
            {
                if (_started)
                    return;
                _generation = _runtime.Start();
                _started = true;
            }
            try
            {
                _gateway.Start(PostNative);
                foreach (GlitchAccountInstrumentScope scope in _engine.GetKnownScopes())
                    _gateway.PublishPosition(scope);
                Post(new RecoveryCompletedObserved(), "recovery_barrier");
            }
            catch
            {
                _gateway.Dispose();
                lock (_gate)
                    _started = false;
                _runtime.Stop();
                throw;
            }
            lock (ActiveGate)
                _active = this;
            StartRail();
            PublishNotice("System", "Runtime", "runtime_started|generation=" + _generation);
        }

        public bool ReplaceRoutes(
            IEnumerable<GlitchRouteDefinition> definitions,
            bool replicationEnabled,
            bool synchronizeChanges = false,
            bool persistDesiredState = true)
        {
            GlitchRouteDefinition[] routes = (definitions ?? Enumerable.Empty<GlitchRouteDefinition>())
                .Where(route => route != null && !string.IsNullOrWhiteSpace(route.RouteId))
                .Select(route => new GlitchRouteDefinition
                {
                    RouteId = route.RouteId.Trim(),
                    MasterAccount = route.MasterAccount?.Trim(),
                    FollowerAccount = route.FollowerAccount?.Trim(),
                    Ratio = route.Ratio,
                    Enabled = route.Enabled
                })
                .ToArray();
            lock (_configurationGate)
            {
                string validationError = ValidateRouteConfiguration(routes);
                if (!string.IsNullOrWhiteSpace(validationError))
                {
                    PublishNotice(
                        "System", "Replication",
                        "route_configuration_rejected|reason=" + validationError);
                    return false;
                }

                Dictionary<string, GlitchRouteDefinition> priorRoutes;
                bool priorReplicationEnabled;
                lock (_gate)
                {
                    priorRoutes = _configuredRoutes.ToDictionary(
                        value => value.Key,
                        value => value.Value,
                        StringComparer.OrdinalIgnoreCase);
                    priorReplicationEnabled = _replicationEnabled;
                }
                bool changed = priorReplicationEnabled != replicationEnabled
                    || priorRoutes.Count != routes.Length
                    || routes.Any(route => !priorRoutes.TryGetValue(route.RouteId, out GlitchRouteDefinition prior)
                        || !SameRoute(prior, route));
                string[] synchronizeRouteIds = synchronizeChanges && replicationEnabled
                    ? routes.Where(route => route.Enabled
                            && (!priorReplicationEnabled
                                || !priorRoutes.TryGetValue(route.RouteId, out GlitchRouteDefinition prior)
                                || !SameRoute(prior, route)))
                        .Select(route => route.RouteId)
                        .ToArray()
                    : Array.Empty<string>();
                if (!changed && synchronizeRouteIds.Length == 0)
                {
                    lock (_gate)
                    {
                        if (_runtimeFailed && replicationEnabled)
                            return false;
                    }
                    return true;
                }

                bool desiredStateChanged = persistDesiredState
                    && priorReplicationEnabled != replicationEnabled;
                if (desiredStateChanged
                    && !TryPersistReplicationEnabled(replicationEnabled))
                {
                    return false;
                }

                var input = new RouteConfigurationChanged(
                    replicationEnabled,
                    routes.Select(route => new RouteConfigurationItem(
                        route.RouteId,
                        route.MasterAccount,
                        route.FollowerAccount,
                        route.Ratio,
                        route.Enabled)),
                    synchronizeRouteIds);
                if (!PostDurably(
                        input,
                        "route_configuration_changed",
                        allowDuringRuntimeFault: !replicationEnabled))
                {
                    if (desiredStateChanged)
                        TryPersistReplicationEnabled(priorReplicationEnabled);
                    return false;
                }

                lock (_gate)
                {
                    _configuredRoutes.Clear();
                    foreach (GlitchRouteDefinition route in routes)
                        _configuredRoutes[route.RouteId] = route;
                    _replicationEnabled = replicationEnabled;
                }
                return true;
            }
        }

        private static bool SameRoute(GlitchRouteDefinition left, GlitchRouteDefinition right)
        {
            return left != null && right != null
                && string.Equals(left.RouteId, right.RouteId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.MasterAccount, right.MasterAccount, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.FollowerAccount, right.FollowerAccount, StringComparison.OrdinalIgnoreCase)
                && left.Ratio == right.Ratio
                && left.Enabled == right.Enabled;
        }

        internal static string ValidateRouteConfiguration(IEnumerable<GlitchRouteDefinition> values)
        {
            GlitchRouteDefinition[] routes = (values ?? Enumerable.Empty<GlitchRouteDefinition>()).ToArray();
            if (routes.Any(route => string.IsNullOrWhiteSpace(route.RouteId)
                    || string.IsNullOrWhiteSpace(route.MasterAccount)
                    || string.IsNullOrWhiteSpace(route.FollowerAccount)))
                return "missing_identity";
            if (routes.Any(route => route.Ratio < 0 || route.Ratio > int.MaxValue))
                return "ratio_out_of_native_range";
            if (routes.Any(route => string.Equals(
                    route.MasterAccount, route.FollowerAccount, StringComparison.OrdinalIgnoreCase)))
                return "master_equals_follower";
            if (routes.GroupBy(route => route.RouteId, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1))
                return "duplicate_route_id";
            if (routes.Where(route => route.Enabled)
                .GroupBy(route => route.FollowerAccount, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Select(route => route.MasterAccount)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
                return "follower_has_multiple_masters";
            var masters = new HashSet<string>(routes.Where(route => route.Enabled)
                .Select(route => route.MasterAccount), StringComparer.OrdinalIgnoreCase);
            return routes.Any(route => route.Enabled && masters.Contains(route.FollowerAccount))
                ? "account_is_both_master_and_follower"
                : string.Empty;
        }

        public bool SetReplicationEnabled(bool enabled)
        {
            GlitchRouteDefinition[] routes;
            lock (_gate)
            {
                routes = _configuredRoutes.Values.ToArray();
            }
            bool accepted = ReplaceRoutes(routes, enabled, enabled);
            if (accepted)
            {
                PublishNotice(
                    "System",
                    "Replication",
                    "replication_enabled_changed|origin=hermes_control|enabled="
                    + (enabled ? "true" : "false"));
            }
            else
            {
                PublishNotice(
                    "System", "Replication",
                    "replication_enabled_change_rejected|origin=hermes_control|requested="
                    + (enabled ? "true" : "false"));
            }
            return accepted;
        }

        public bool ReplicationEnabled
        {
            get { lock (_gate) return _replicationEnabled; }
        }

        public bool ReplicationEffective
        {
            get
            {
                lock (_gate)
                    return _replicationEnabled && _configuredRoutes.Values.Any(route => route.Enabled);
            }
        }

        public GlitchHermesSubmissionReceipt FindHermesSubmission(
            string intentId,
            string contentFingerprint)
        {
            lock (_hermesGate)
            {
                GlitchHermesSubmissionReceipt existing;
                if (!_hermesReceipts.TryGetValue(intentId ?? string.Empty, out existing))
                    return null;
                return CopyHermesReceipt(
                    existing,
                    string.Equals(
                        existing.ContentFingerprint,
                        contentFingerprint,
                        StringComparison.Ordinal)
                        ? GlitchHermesSubmissionDisposition.Duplicate
                        : GlitchHermesSubmissionDisposition.ContentConflict);
            }
        }

        public GlitchHermesSubmissionReceipt SubmitHermes(GlitchInput request)
        {
            IGlitchHermesIntent intent = request as IGlitchHermesIntent;
            if (intent == null)
                throw new ArgumentException("Hermes input identity is required.", nameof(request));

            lock (_hermesGate)
            {
                GlitchHermesSubmissionReceipt existing;
                if (_hermesReceipts.TryGetValue(intent.IntentId, out existing))
                {
                    return CopyHermesReceipt(
                        existing,
                        string.Equals(
                            existing.ContentFingerprint,
                            intent.ContentFingerprint,
                            StringComparison.Ordinal)
                            ? GlitchHermesSubmissionDisposition.Duplicate
                            : GlitchHermesSubmissionDisposition.ContentConflict);
                }

                long generation;
                lock (_gate)
                {
                    if (!_started || _stopping || _runtimeFailed)
                        return BuildUnavailableHermesReceipt(intent);
                    generation = _generation;
                }

                if (!_operationJournal.TryAppendInput(
                        request, "hermes_intent", out string journalError))
                {
                    PublishNotice(
                        "System",
                        "Persistence",
                        "input_not_accepted|type=" + request.GetType().Name
                        + "|error=" + Clean(journalError));
                    BlockMutations("durable_input_unwritten");
                    return BuildUnavailableHermesReceipt(intent);
                }

                GlitchHermesSubmissionReceipt accepted = BuildHermesReceipt(
                    intent, GlitchHermesSubmissionDisposition.Accepted);
                _hermesReceipts[intent.IntentId] = accepted;
                bool posted = _runtime.TryPost(
                    generation,
                    new GlitchRuntimeEvent(
                        Interlocked.Increment(ref _eventSequence),
                        "durable|hermes_intent",
                        request));
                if (!posted)
                    BlockMutations("durable_input_not_queued");

                // Once the journal append succeeds, the intent is accepted. If
                // the live queue cannot receive it, recovery owns the pending work.
                return CopyHermesReceipt(accepted, GlitchHermesSubmissionDisposition.Accepted);
            }
        }

        public bool SynchronizeRoute(string routeId)
        {
            return PostDurably(new RouteSynchronizationRequested(routeId), "route_sync_requested");
        }

        public bool RequestFlatten(string requestId, string accountName, string reason)
        {
            if (string.IsNullOrWhiteSpace(accountName))
                return false;
            IReadOnlyDictionary<string, bool> results = RequestFlattenBatch(
                requestId, new[] { accountName }, reason);
            bool accepted;
            return results.TryGetValue(accountName.Trim(), out accepted) && accepted;
        }

        public IReadOnlyDictionary<string, bool> RequestFlattenBatch(
            string requestRoot,
            IEnumerable<string> accountNames,
            string reason)
        {
            if (string.IsNullOrWhiteSpace(requestRoot))
                throw new ArgumentException("Request identity is required.", nameof(requestRoot));
            string[] accounts = (accountNames ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var results = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            if (accounts.Length == 0)
                return results;

            _mutationGate.Fence(accounts);
            for (int index = 0; index < accounts.Length; index++)
            {
                string account = accounts[index];
                string requestId = accounts.Length == 1
                    ? requestRoot.Trim()
                    : requestRoot.Trim() + "-" + index;
                bool accepted = false;
                try
                {
                    accepted = PostDurably(
                        new FlattenAccountRequested(requestId, account, reason),
                        "flatten_requested",
                        allowDuringRuntimeFault: true);
                    results[account] = accepted;
                }
                catch
                {
                    for (int pending = index; pending < accounts.Length; pending++)
                        _mutationGate.Release(accounts[pending]);
                    throw;
                }
                if (!accepted)
                    _mutationGate.Release(account);
            }
            return results;
        }

        public bool RequestFlattenAllAvailable(string requestRoot, string reason)
        {
            string[] accounts = _gateway.SnapshotFlattenEligibleAccountNames();
            if (accounts.Length == 0)
            {
                PublishNotice(
                    "System",
                    "Order",
                    "flatten_all_not_requested|reason=no_connected_native_accounts");
                return false;
            }
            if (!SetReplicationEnabled(false))
            {
                PublishNotice(
                    "System",
                    "Order",
                    "flatten_all_not_requested|reason=replication_stop_failed");
                return false;
            }
            IReadOnlyDictionary<string, bool> results = RequestFlattenBatch(
                requestRoot,
                accounts,
                reason);
            return accounts.All(account =>
                results.TryGetValue(account, out bool accepted) && accepted);
        }

        public bool SetReplicationOrderLimit(string accountName, int? maxOrderQuantity)
        {
            return PostDurably(
                new ReplicationQuantityLimitChanged(accountName, maxOrderQuantity),
                "replication_quantity_limit_changed");
        }

        public void Dispose()
        {
            lock (ActiveGate)
            {
                if (ReferenceEquals(_active, this))
                    _active = null;
            }

            lock (_gate)
            {
                if (!_started || _stopping)
                    return;
                _stopping = true;
            }
            try
            {
                StopRail();
                // Close native ingress first. A callback that already crossed the
                // gateway lock can still enqueue while _started remains true; the
                // runtime is drained only after that boundary is closed.
                _gateway.Dispose();
                lock (_gate)
                    _started = false;
                _runtime.Dispose();
                PublishNotice("System", "Runtime", "runtime_stopped|generation=" + _generation);
            }
            finally
            {
                lock (_gate)
                {
                    _started = false;
                    _stopping = false;
                }
            }
        }

        private void PostNative(GlitchInput input)
        {
            Post(input, "native");
        }

        private bool Post(GlitchInput input, string kind)
        {
            long generation;
            lock (_gate)
            {
                if (!_started || (_stopping && !string.Equals(kind, "native", StringComparison.Ordinal)))
                    return false;
                generation = _generation;
            }
            return _runtime.TryPost(
                generation,
                new GlitchRuntimeEvent(Interlocked.Increment(ref _eventSequence), kind, input));
        }

        private bool PostDurably(
            GlitchInput input,
            string kind,
            bool allowDuringRuntimeFault = false)
        {
            long generation;
            lock (_gate)
            {
                if (!_started || _stopping
                    || (_runtimeFailed && !allowDuringRuntimeFault))
                    return false;
                generation = _generation;
            }
            if (!_operationJournal.TryAppendInput(
                input, kind, out string journalError))
            {
                PublishNotice(
                    "System",
                    "Persistence",
                    "input_not_accepted|type=" + input.GetType().Name
                    + "|error=" + Clean(journalError));
                BlockMutations("durable_input_unwritten");
                return false;
            }
            bool posted = _runtime.TryPost(
                generation,
                new GlitchRuntimeEvent(
                    Interlocked.Increment(ref _eventSequence),
                    "durable|" + kind,
                    input));
            if (!posted)
            {
                BlockMutations("durable_input_not_queued");
                return true;
            }
            return true;
        }

        private void Consume(GlitchRuntimeEvent runtimeEvent)
        {
            if (runtimeEvent.Input == null)
                return;
            bool recoveryBarrier = runtimeEvent.Input is RecoveryCompletedObserved;
            bool alreadyDurable = runtimeEvent.Kind.StartsWith(
                "durable|", StringComparison.Ordinal);
            if (!recoveryBarrier && !alreadyDurable)
            {
                if (!_operationJournal.TryAppendInput(
                    runtimeEvent.Input, runtimeEvent.Kind, out string inputError))
                {
                    PublishNotice(
                        "System",
                        "Persistence",
                        "input_unjournaled|type=" + runtimeEvent.Input.GetType().Name
                        + "|error=" + Clean(inputError));
                    BlockMutations("native_input_unwritten");
                    return;
                }
                TrackNativeCorrelation(runtimeEvent.Input);
            }
            PublishNativeFactNotice(runtimeEvent.Input);
            if (runtimeEvent.Input is AccountStatusObserved)
                _gateway.RefreshAccountSubscriptions();
            bool allowedDuringRuntimeFault = runtimeEvent.Input is FlattenAccountRequested
                || runtimeEvent.Input is FlattenCompletedObserved;
            var faultRouteConfiguration = runtimeEvent.Input as RouteConfigurationChanged;
            allowedDuringRuntimeFault |= faultRouteConfiguration != null
                && !faultRouteConfiguration.ReplicationEnabled;
            lock (_gate)
            {
                if (_runtimeFailed && !allowedDuringRuntimeFault)
                    return;
            }
            IReadOnlyList<GlitchCommand> commands = _engine.Handle(runtimeEvent.Input);
            if (_recovering)
            {
                foreach (GlitchCommand command in commands)
                    RememberRecoveryEmission(command);
                if (recoveryBarrier)
                    FinishRecovery();
                return;
            }
            foreach (GlitchCommand command in commands)
                DispatchNewCommand(command, runtimeEvent.Kind);
            var flattenCompleted = runtimeEvent.Input as FlattenCompletedObserved;
            if (flattenCompleted != null)
            {
                _mutationGate.Release(flattenCompleted.AccountName);
                PublishNotice(
                    flattenCompleted.AccountName,
                    "Order",
                    "native_mutation_fence_released|reason=flatten_terminal");
            }
        }

        private void BlockMutations(string reason)
        {
            lock (_gate)
            {
                _mutationsAllowed = false;
                _runtimeFailed = true;
                _replicationEnabled = false;
            }
            PublishNotice(
                "System", "Persistence",
                "native_mutations_blocked|reason=" + Clean(reason));
        }

        private void OnRuntimeInputFailed(Exception error)
        {
            BlockMutations("runtime_input_failed");
            PublishNotice(
                "System",
                "Runtime",
                "runtime_input_failed|error=" + Clean(error?.Message));
        }

        private void ReplayRecovery(IEnumerable<GlitchRecoveryRecord> records)
        {
            foreach (GlitchRecoveryRecord record in records ?? Enumerable.Empty<GlitchRecoveryRecord>())
            {
                if (record.Input != null)
                {
                    RememberRecoveredHermesIntent(record.Input);
                    ApplyRecoveredHostConfiguration(record.Input);
                    foreach (GlitchCommand command in _engine.Handle(record.Input))
                        RememberRecoveryEmission(command);
                }
                if (record.Command == null)
                    continue;
                string fingerprint = GlitchOperationJournal.Fingerprint(record.Command);
                string prior;
                if (_commandFingerprints.TryGetValue(record.Command.CommandId, out prior)
                    && !string.Equals(prior, fingerprint, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "journal_command_identity_conflict:" + record.Command.CommandId);
                _commandFingerprints[record.Command.CommandId] = fingerprint;
                GlitchCommand emitted;
                if (_recoveryEmittedCommands.TryGetValue(record.Command.CommandId, out emitted)
                    && !string.Equals(
                        GlitchOperationJournal.FingerprintForReplay(
                            emitted, record.HermesIntentPresent),
                        GlitchOperationJournal.FingerprintForReplay(
                            record.Command, record.HermesIntentPresent),
                        StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "replayed_command_content_conflict:" + record.Command.CommandId);
                _recoveryJournalCommands[record.Command.CommandId] = new RecoveryCommandState
                {
                    Command = record.Command,
                    Fingerprint = fingerprint,
                    LatestPhase = record.Phase ?? string.Empty
                };
            }
        }

        private void RememberRecoveredHermesIntent(GlitchInput input)
        {
            IGlitchHermesIntent intent = input as IGlitchHermesIntent;
            if (intent == null)
                return;
            lock (_hermesGate)
            {
                GlitchHermesSubmissionReceipt existing;
                if (_hermesReceipts.TryGetValue(intent.IntentId, out existing))
                {
                    if (!string.Equals(
                            existing.ContentFingerprint,
                            intent.ContentFingerprint,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Hermes intent identity has conflicting durable content: "
                            + intent.IntentId);
                    }
                    return;
                }
                _hermesReceipts[intent.IntentId] = BuildHermesReceipt(
                    intent, GlitchHermesSubmissionDisposition.Accepted);
            }
        }

        private static GlitchHermesSubmissionReceipt BuildHermesReceipt(
            IGlitchHermesIntent intent,
            GlitchHermesSubmissionDisposition disposition)
        {
            return new GlitchHermesSubmissionReceipt
            {
                Disposition = disposition,
                IntentId = intent.IntentId,
                ContentFingerprint = intent.ContentFingerprint,
                Status = intent.ReceiptStatus,
                Code = intent.ReceiptCode,
                Message = intent.ReceiptMessage
            };
        }

        private static GlitchHermesSubmissionReceipt BuildUnavailableHermesReceipt(
            IGlitchHermesIntent intent)
        {
            return new GlitchHermesSubmissionReceipt
            {
                Disposition = GlitchHermesSubmissionDisposition.Unavailable,
                IntentId = intent.IntentId,
                ContentFingerprint = intent.ContentFingerprint,
                Status = "failed",
                Code = "runtime_not_accepting_intents",
                Message = intent.ReceiptMessage
            };
        }

        private static GlitchHermesSubmissionReceipt CopyHermesReceipt(
            GlitchHermesSubmissionReceipt source,
            GlitchHermesSubmissionDisposition disposition)
        {
            return new GlitchHermesSubmissionReceipt
            {
                Disposition = disposition,
                IntentId = source.IntentId,
                ContentFingerprint = source.ContentFingerprint,
                Status = source.Status,
                Code = source.Code,
                Message = source.Message
            };
        }

        private void ApplyRecoveredHostConfiguration(GlitchInput input)
        {
            var flattenRequested = input as FlattenAccountRequested;
            if (flattenRequested != null)
                _mutationGate.Fence(flattenRequested.AccountName);
            var flattenCompleted = input as FlattenCompletedObserved;
            if (flattenCompleted != null)
                _mutationGate.Release(flattenCompleted.AccountName);

            var configuration = input as RouteConfigurationChanged;
            if (configuration != null)
            {
                _configuredRoutes.Clear();
                foreach (RouteConfigurationItem route in configuration.Routes)
                {
                    _configuredRoutes[route.RouteId] = new GlitchRouteDefinition
                    {
                        RouteId = route.RouteId,
                        MasterAccount = route.MasterAccount,
                        FollowerAccount = route.FollowerAccount,
                        Ratio = route.Ratio,
                        Enabled = route.Enabled
                    };
                }
                _replicationEnabled = configuration.ReplicationEnabled;
            }
        }

        private void LoadPersistedRouteConfiguration()
        {
            try
            {
                lock (_gate)
                {
                    if (!_mutationsAllowed)
                        return;
                }
                GlitchRuntimePolicySettings settings = GlitchRuntimePolicyStore.LoadSettings(
                    GlitchRuntimePolicyStore.GetDefaultSettingsPath());
                bool replicationEnabled = settings?.ReplicationUiEnabled ?? false;
                var routes = new List<GlitchRouteDefinition>();
                foreach (GlitchStateStore.AccountGroupRecord group in GlitchStateStore.LoadAccountGroups(
                    GlitchStateStore.GetDefaultConfigurationPath()))
                {
                    if (group == null || string.IsNullOrWhiteSpace(group.GroupId)
                        || string.IsNullOrWhiteSpace(group.MasterAccount))
                        continue;
                    foreach (GlitchStateStore.AccountGroupMemberRecord member in
                        group.Members ?? new List<GlitchStateStore.AccountGroupMemberRecord>())
                    {
                        if (member == null || string.IsNullOrWhiteSpace(member.FollowerAccount)
                            || double.IsNaN(member.Ratio) || double.IsInfinity(member.Ratio)
                            || member.Ratio < 0)
                            continue;
                        routes.Add(new GlitchRouteDefinition
                        {
                            RouteId = group.GroupId.Trim() + "|" + group.MasterAccount.Trim()
                                + "|" + member.FollowerAccount.Trim(),
                            MasterAccount = group.MasterAccount.Trim(),
                            FollowerAccount = member.FollowerAccount.Trim(),
                            Ratio = (decimal)member.Ratio,
                            Enabled = member.IsEnabled
                        });
                    }
                }
                var enabledMasters = new HashSet<string>(routes.Where(value => value.Enabled)
                    .Select(value => value.MasterAccount), StringComparer.OrdinalIgnoreCase);
                bool invalid = routes.Where(value => value.Enabled)
                        .GroupBy(value => value.FollowerAccount, StringComparer.OrdinalIgnoreCase)
                        .Any(group => group.Select(value => value.MasterAccount)
                            .Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                    || routes.Any(value => value.Enabled
                        && enabledMasters.Contains(value.FollowerAccount));
                if (invalid)
                {
                    BlockMutations("persisted_route_configuration_invalid");
                    PublishNotice(
                        "System", "Replication",
                        "persisted_route_configuration_rejected|reason=invalid_topology");
                    return;
                }

                Dictionary<string, GlitchRouteDefinition> recoveredRoutes;
                bool recoveredReplicationEnabled;
                bool changed;
                lock (_gate)
                {
                    recoveredReplicationEnabled = _replicationEnabled;
                    recoveredRoutes = _configuredRoutes.ToDictionary(
                        value => value.Key,
                        value => value.Value,
                        StringComparer.OrdinalIgnoreCase);
                    changed = recoveredReplicationEnabled != replicationEnabled
                        || recoveredRoutes.Count != routes.Count
                        || routes.Any(route => !recoveredRoutes.TryGetValue(
                                route.RouteId, out GlitchRouteDefinition existing)
                            || !SameRoute(existing, route));
                }
                if (!changed)
                    return;

                string[] synchronizeRouteIds = replicationEnabled
                    ? routes.Where(route => route.Enabled
                            && (!recoveredReplicationEnabled
                                || !recoveredRoutes.TryGetValue(
                                    route.RouteId, out GlitchRouteDefinition existing)
                                || !SameRoute(existing, route)))
                        .Select(route => route.RouteId)
                        .ToArray()
                    : Array.Empty<string>();
                var input = new RouteConfigurationChanged(
                    replicationEnabled,
                    routes.Select(route => new RouteConfigurationItem(
                        route.RouteId,
                        route.MasterAccount,
                        route.FollowerAccount,
                        route.Ratio,
                        route.Enabled)),
                    synchronizeRouteIds);

                if (!_operationJournal.TryAppendInput(
                        input, "startup_route_configuration", out string journalError))
                {
                    PublishNotice(
                        "System", "Persistence",
                        "startup_route_configuration_unwritten|error=" + Clean(journalError));
                    BlockMutations("startup_route_configuration_unwritten");
                    return;
                }

                foreach (GlitchCommand command in _engine.Handle(input))
                    RememberRecoveryEmission(command);
                ApplyRecoveredHostConfiguration(input);
                PublishNotice(
                    "System", "Replication",
                    "startup_route_configuration_applied|routes=" + routes.Count
                    + "|enabled=" + (replicationEnabled ? "true" : "false"));
            }
            catch (Exception error)
            {
                BlockMutations("persisted_route_configuration_unavailable");
                PublishNotice(
                    "System", "Replication",
                    "persisted_route_configuration_unavailable|error=" + Clean(error.Message));
            }
        }

        private bool TryPersistReplicationEnabled(bool enabled)
        {
            try
            {
                string path = GlitchRuntimePolicyStore.GetDefaultSettingsPath();
                GlitchRuntimePolicySettings settings =
                    GlitchRuntimePolicyStore.LoadSettings(path);
                settings.ReplicationUiEnabled = enabled;
                GlitchRuntimePolicyStore.SaveSettings(path, settings);
                return true;
            }
            catch (Exception error)
            {
                PublishNotice(
                    "System", "Persistence",
                    "replication_desired_state_unwritten|enabled="
                    + (enabled ? "true" : "false")
                    + "|error=" + Clean(error.Message));
                BlockMutations("replication_desired_state_unwritten");
                return false;
            }
        }

        private void RememberRecoveryEmission(GlitchCommand command)
        {
            if (command == null)
                return;
            string fingerprint = GlitchOperationJournal.Fingerprint(command);
            GlitchCommand prior;
            if (_recoveryEmittedCommands.TryGetValue(command.CommandId, out prior))
            {
                if (!string.Equals(
                        GlitchOperationJournal.Fingerprint(prior), fingerprint, StringComparison.Ordinal))
                {
                    if (_recoveryJournalCommands.ContainsKey(command.CommandId))
                        return;
                    throw new InvalidOperationException(
                        "replayed_command_identity_conflict:" + command.CommandId);
                }
                return;
            }
            RecoveryCommandState journalState;
            if (_recoveryJournalCommands.TryGetValue(command.CommandId, out journalState)
                && !string.Equals(journalState.Fingerprint, fingerprint, StringComparison.Ordinal))
                return;
            _recoveryEmittedCommands[command.CommandId] = command;
            _recoveryEmissionOrder.Add(command.CommandId);
        }

        private void FinishRecovery()
        {
            _recovering = false;
            int resumed = 0;
            int waiting = 0;
            int unknown = 0;
            foreach (string commandId in _recoveryEmissionOrder.ToArray())
            {
                GlitchCommand command = _recoveryEmittedCommands[commandId];
                if (!_engine.IsCommandPending(commandId))
                    continue;
                RecoveryCommandState state;
                if (!_recoveryJournalCommands.TryGetValue(commandId, out state))
                {
                    DispatchNewCommand(command, "recovery_resume_unaccepted");
                    resumed++;
                    continue;
                }
                string phase = state.LatestPhase ?? string.Empty;
                if (string.Equals(phase, "accepted", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(phase, "planned", StringComparison.OrdinalIgnoreCase))
                {
                    IssueAcceptedCommand(command);
                    resumed++;
                    continue;
                }
                if (string.Equals(
                    phase, "native_request_not_started", StringComparison.OrdinalIgnoreCase))
                {
                    Post(new NativeRequestFailedObserved(
                        commandId, "recovered_native_request_not_started"), "recovery_resolution");
                    waiting++;
                    continue;
                }
                if (command is RefreshPositionCommand)
                {
                    IssueAcceptedCommand(command);
                    resumed++;
                    continue;
                }
                if ((command is SubmitMarketCommand || command is SubmitProtectionCommand)
                    && _recoveryNativeCorrelations.Contains(commandId))
                {
                    waiting++;
                    continue;
                }
                var cancel = command as CancelProtectionCommand;
                if (cancel != null && _gateway.IsProtectionCancellationSatisfied(cancel))
                {
                    Post(new ProtectionCancellationCompletedObserved(commandId), "recovery_resolution");
                    waiting++;
                    continue;
                }
                var flatten = command as FlattenAccountCommand;
                if (flatten != null && _gateway.IsFlattenSatisfied(flatten))
                {
                    Post(new FlattenCompletedObserved(commandId, flatten.AccountName), "recovery_resolution");
                    waiting++;
                    continue;
                }
                Post(new NativeRequestUnknownObserved(
                    commandId,
                    "native_request_started_without_current_correlation"), "recovery_resolution");
                unknown++;
            }
            PublishNotice(
                "System",
                "Recovery",
                "recovery_completed|resumed=" + resumed
                + "|waiting=" + waiting
                + "|unknown=" + unknown
                + "|mutations_allowed=" + (_mutationsAllowed ? "true" : "false"));
        }

        private void DispatchNewCommand(GlitchCommand command, string source)
        {
            string fingerprint = GlitchOperationJournal.Fingerprint(command);
            string prior;
            lock (_gate)
            {
                if (_commandFingerprints.TryGetValue(command.CommandId, out prior))
                {
                    if (!string.Equals(prior, fingerprint, StringComparison.Ordinal))
                    {
                        PublishNotice(
                            "System", "Runtime",
                            "command_identity_conflict|command=" + command.CommandId);
                        Post(new NativeRequestUnknownObserved(
                            command.CommandId, "command_identity_conflict"), "runtime");
                    }
                    return;
                }
                _commandFingerprints[command.CommandId] = fingerprint;
            }
            if (!_operationJournal.TryAppend(command, "accepted", source, out string acceptedError))
            {
                PublishNotice(
                    "System",
                    "Persistence",
                    "native_command_not_issued|command=" + command.CommandId
                    + "|reason=accepted_unwritten|error=" + Clean(acceptedError));
                Post(new NativeRequestFailedObserved(
                    command.CommandId, "accepted_unwritten:" + acceptedError), "persistence");
                return;
            }
            IssueAcceptedCommand(command);
        }

        private void IssueAcceptedCommand(GlitchCommand command)
        {
            if (!_mutationsAllowed
                && !(command is RefreshPositionCommand)
                && !(command is FlattenAccountCommand))
            {
                Post(new NativeRequestFailedObserved(
                    command.CommandId, "runtime_mutation_blocked_by_recovery"), "recovery");
                return;
            }

            bool nativeMutationStarted = false;
            try
            {
                string accountName = CommandAccount(command);
                bool admitted = _mutationGate.TryExecute(
                    accountName,
                    command is FlattenAccountCommand || command is RefreshPositionCommand,
                    () => _gateway.Execute(command, value =>
                    {
                        if (!_operationJournal.TryAppend(
                            value, "native_request_started", string.Empty, out string startError))
                        {
                            BlockMutations("native_request_start_unwritten");
                            throw new InvalidOperationException(
                                "native_request_start_unwritten:" + startError);
                        }
                        nativeMutationStarted = true;
                    }));
                if (!admitted)
                {
                    _operationJournal.TryAppend(
                        command,
                        "native_request_not_started",
                        "account_fenced_by_flatten",
                        out _);
                    PublishNotice(
                        accountName,
                        "Order",
                        "native_command_blocked|command=" + command.CommandId
                        + "|reason=account_fenced_by_flatten");
                    Post(new NativeRequestFailedObserved(
                        command.CommandId, "account_fenced_by_flatten"), "flatten_fence");
                    return;
                }
                if (!_operationJournal.TryAppend(
                    command, "native_request_returned", string.Empty, out string returnedError))
                {
                    PublishNotice(
                        "System",
                        "Persistence",
                        "native_request_receipt_unwritten|command=" + command.CommandId
                        + "|state=" + (nativeMutationStarted ? "native_pending" : "no_mutation")
                        + "|error=" + Clean(returnedError));
                    BlockMutations("native_request_receipt_unwritten");
                }
            }
            catch (Exception error)
            {
                _operationJournal.TryAppend(
                    command,
                    nativeMutationStarted ? "native_request_threw" : "native_request_not_started",
                    error.GetType().Name + ":" + error.Message,
                    out _);
                PublishNotice(
                    "System",
                    "Order",
                    "native_command_" + (nativeMutationStarted ? "unknown" : "failed")
                    + "|command=" + command.CommandId
                    + "|error=" + Clean(error.Message));
                AppendHermesProtectionFailure(command, error, nativeMutationStarted);
                Post(
                    nativeMutationStarted
                        ? (GlitchInput)new NativeRequestUnknownObserved(
                            command.CommandId, error.Message)
                        : new NativeRequestFailedObserved(command.CommandId, error.Message),
                    "native_request");
            }
        }

        private static void AppendHermesProtectionFailure(
            GlitchCommand command,
            Exception error,
            bool nativeMutationStarted)
        {
            var change = command as ChangeProtectionCommand;
            if (nativeMutationStarted
                || change == null
                || string.IsNullOrWhiteSpace(change.HermesIntentId))
                return;
            GlitchExecutionEvidenceWriter.TryAppend(
                change.HermesIntentId,
                "failed",
                "native_protection_change_rejected",
                "account=" + Clean(change.AccountName)
                + "|instrument=" + Clean(change.InstrumentName)
                + "|command=" + Clean(change.CommandId)
                + "|error=" + Clean(error.GetType().Name + ":" + error.Message),
                DateTime.UtcNow);
        }

        private static string CommandAccount(GlitchCommand command)
        {
            var market = command as SubmitMarketCommand;
            if (market != null) return market.AccountName;
            var protection = command as SubmitProtectionCommand;
            if (protection != null) return protection.AccountName;
            var change = command as ChangeProtectionCommand;
            if (change != null) return change.AccountName;
            var cancel = command as CancelProtectionCommand;
            if (cancel != null) return cancel.AccountName;
            var flatten = command as FlattenAccountCommand;
            if (flatten != null) return flatten.AccountName;
            var refresh = command as RefreshPositionCommand;
            return refresh?.AccountName;
        }

        private void TrackNativeCorrelation(GlitchInput input)
        {
            if (!_recovering)
                return;
            var order = input as NativeOrderObserved;
            if (order != null && !string.IsNullOrWhiteSpace(order.CorrelationId))
                _recoveryNativeCorrelations.Add(order.CorrelationId);
            var execution = input as ExecutionObserved;
            if (execution != null)
            {
                if (!string.IsNullOrWhiteSpace(execution.CorrelationId))
                    _recoveryNativeCorrelations.Add(execution.CorrelationId);
                if (!string.IsNullOrWhiteSpace(execution.ProtectionCorrelationId))
                    _recoveryNativeCorrelations.Add(execution.ProtectionCorrelationId);
            }
        }

        private void PublishNativeFactNotice(GlitchInput input)
        {
            var execution = input as ExecutionLifecycleObserved;
            if (execution != null)
            {
                PublishNotice(
                    execution.AccountName,
                    "Execution",
                    "native_execution|" + GlitchOperationJournal.DescribeFact(execution));
                return;
            }
            var order = input as NativeOrderObserved;
            if (order != null)
            {
                PublishNotice(
                    order.AccountName,
                    "Order",
                    "native_order|" + GlitchOperationJournal.DescribeFact(order));
                return;
            }
            var status = input as AccountStatusObserved;
            if (status != null)
            {
                PublishNotice(
                    status.AccountName,
                    "Connection",
                    "account_status|" + GlitchOperationJournal.DescribeFact(status));
            }
        }

        private void PublishNotice(string account, string category, string message)
        {
            Action<GlitchRuntimeNotice> handler = Notice;
            handler?.Invoke(new GlitchRuntimeNotice
            {
                CreatedUtc = DateTime.UtcNow,
                AccountName = account,
                Category = category,
                Message = message
            });
        }

        private void StartRail()
        {
            GlitchAiRailPolicyStore.EnsureDefaultExists();
            GlitchAiIntentServer.IntentAccepted += OnIntentAccepted;
            GlitchAiIntentServer.IntentRejected += OnIntentRejected;
            GlitchHermesControlServer.SetReplication = SetReplicationEnabled;
            GlitchHermesControlServer.GetReplication = () => ReplicationEnabled;
            GlitchHermesControlServer.GetReplicationEffective = () => ReplicationEffective;
            GlitchHermesControlServer.FlattenAllAsync = () => Task.FromResult(FlattenConfiguredAccounts());
            GlitchHermesControlServer.GetFlattenEvidence = BuildConfiguredFlattenEvidence;
            GlitchHermesControlServer.TradingModeChanged = paused =>
                PublishNotice("System", "Glitch AI", paused ? "trading_paused" : "trading_resumed");
            GlitchHermesControlServer.CommandFailed = (commandId, message) =>
                PublishNotice(
                    "System", "Glitch AI", "control_command_failed|id=" + Clean(commandId)
                    + "|reason=" + Clean(message));

            if (!GlitchExternalTelemetryServer.IsRunning && !GlitchExternalTelemetryServer.TryStart())
                PublishNotice("System", "Telemetry", "telemetry_start_failed|bind=127.0.0.1:8787");
            if (!GlitchAiIntentServer.IsRunning && !GlitchAiIntentServer.TryStart())
                PublishNotice("System", "Intent", "intent_server_start_failed|bind=127.0.0.1:8788");
            if (!GlitchHermesControlServer.IsRunning && !GlitchHermesControlServer.TryStart())
                PublishNotice("System", "Glitch AI", "control_server_start_failed|bind=127.0.0.1:8789");
        }

        private void StopRail()
        {
            GlitchExternalTelemetryServer.TryStop();
            GlitchAiIntentServer.TryStop();
            GlitchHermesControlServer.TryStop();
            GlitchAiIntentServer.IntentAccepted -= OnIntentAccepted;
            GlitchAiIntentServer.IntentRejected -= OnIntentRejected;
            GlitchHermesControlServer.SetReplication = null;
            GlitchHermesControlServer.GetReplication = null;
            GlitchHermesControlServer.GetReplicationEffective = null;
            GlitchHermesControlServer.FlattenAllAsync = null;
            GlitchHermesControlServer.GetFlattenEvidence = null;
            GlitchHermesControlServer.TradingModeChanged = null;
            GlitchHermesControlServer.CommandFailed = null;
        }

        private bool FlattenConfiguredAccounts()
        {
            string[] accounts;
            lock (_gate)
            {
                accounts = _configuredRoutes.Values
                    .SelectMany(route => new[] { route.MasterAccount, route.FollowerAccount })
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            if (accounts.Length == 0)
                return false;
            if (!SetReplicationEnabled(false))
                return false;
            string requestRoot = "hermes-flatten-" + Guid.NewGuid().ToString("N");
            IReadOnlyDictionary<string, bool> results = RequestFlattenBatch(
                requestRoot, accounts, "hermes_flatten_all");
            return accounts.All(account => results.TryGetValue(account, out bool accepted) && accepted);
        }

        private string BuildConfiguredFlattenEvidence()
        {
            string[] accounts;
            lock (_gate)
            {
                accounts = _configuredRoutes.Values
                    .SelectMany(route => new[] { route.MasterAccount, route.FollowerAccount })
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            return _gateway.BuildFlattenEvidence(accounts);
        }

        private void OnIntentAccepted(string intentId, string instrument, string action)
        {
            PublishNotice(
                "System", "Intent", "intent_accepted|id=" + Clean(intentId)
                + "|instrument=" + Clean(instrument) + "|action=" + Clean(action));
        }

        private void OnIntentRejected(
            string intentId,
            string instrument,
            string action,
            int failedCheck,
            string failedCode)
        {
            PublishNotice(
                "System", "Intent", "intent_rejected|id=" + Clean(intentId)
                + "|instrument=" + Clean(instrument) + "|action=" + Clean(action)
                + "|check=" + failedCheck + "|code=" + Clean(failedCode));
        }

        private static string Clean(string value)
        {
            return (value ?? string.Empty).Replace("|", "/").Replace("\r", " ").Replace("\n", " ");
        }
    }
}
