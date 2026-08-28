using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Glitch.Core
{
    /// <summary>
    /// One serialized reducer. Every order-producing cause becomes an immutable
    /// operation in the FIFO queue for its account and instrument. Native facts,
    /// rather than callback order, advance the queue.
    /// </summary>
    public sealed class GlitchEngine
    {
        private sealed class Route
        {
            public string Id;
            public string Master;
            public string Follower;
            public decimal Ratio;
            public bool Enabled;
        }

        private sealed class AllocationEpoch
        {
            public int MasterSignedTotal;
            public int FollowerTarget;
            public int PositiveSettlementCredit;
            public int NegativeSettlementCredit;
        }

        private sealed class OrderFact
        {
            public NativeOrderObserved Value;
        }

        private sealed class ProtectionSlice
        {
            public string LegId;
            public int RemainingQuantity;
            public decimal? StopOffset;
            public decimal? TargetOffset;
        }

        private sealed class ProtectionBundle
        {
            public string Id;
            public string Account;
            public string Instrument;
            public string RouteId;
            public string SourceMaster;
            public string SourceRevision;
            public string PendingSourceRevision;
            public int Direction;
            public decimal EntryPrice;
            public bool MirrorsManualMaster;
            public bool CancelRequested;
            public bool Superseded;
            public bool SafetyFlattenPending;
            public long CreatedSequence;
            public string CurrentRequestId;
            public readonly List<ProtectionSlice> Slices = new List<ProtectionSlice>();

            public int RemainingQuantity => Slices.Sum(value => value.RemainingQuantity);
        }

        private sealed class ProtectionRequest
        {
            public string CommandId;
            public ProtectionBundle Bundle;
            public TradeOperation Owner;
            public int ExpectedChildren;
            public bool RequestFailed;
            public bool RequestUnknown;
        }

        private abstract class BookOperation
        {
            public string Id;
            public string Account;
            public string Instrument;
            public GlitchOperationPhase Phase;
            public string Failure;
        }

        private sealed class TradeOperation : BookOperation
        {
            public string CauseId;
            public string HermesIntentId;
            public GlitchCommandPurpose Purpose;
            public string RouteId;
            public int RequestedSignedQuantity;
            public int RemainingSignedQuantity;
            public int? TargetSignedPosition;
            public int MaxOpeningStepQuantity;
            public decimal? EntryRangeLow;
            public decimal? EntryRangeHigh;
            public bool CancelExternalProtection;
            public bool MirrorsManualMasterProtection;
            public bool ProtectionCleanupOnly;
            public bool CloseToFlat;
            public bool PositionRefreshRequested;
            public int NextStep;
            public string ActiveCommandId;
            public int ActiveRequestedSignedQuantity;
            public int ActiveFilledSignedQuantity;
            public int ActiveExpectedSignedPosition;
            public long ActivePositionRevision;
            public string CancelCommandId;
            public bool ExternalCancellationCompleted;
            public ProtectionBundle ManualRevisionBundle;
            public MasterProtectionObserved ManualRevision;
            public bool RemoveManualProtection;
            public ProtectionBundle DirectProtectionBundle;
            public readonly List<string> CancelledProtectionRequests = new List<string>();
            public readonly List<ProtectionLegTemplate> RemainingProtection =
                new List<ProtectionLegTemplate>();
            public readonly HashSet<string> PendingProtectionRequests =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class ProtectionChangeOperation : BookOperation
        {
            public string CommandId;
            public string HermesIntentId;
            public int Step;
            public readonly List<HermesProtectionUpdate> Updates =
                new List<HermesProtectionUpdate>();
            public readonly List<string> TargetCommandIds = new List<string>();
            public readonly Dictionary<string, decimal> ExpectedStops =
                new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, decimal> ExpectedTargets =
                new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            public readonly List<ProtectionChangeOperation> Followers =
                new List<ProtectionChangeOperation>();
            public readonly List<ProtectionGeometryRevision> GeometryRevisions =
                new List<ProtectionGeometryRevision>();
        }

        private sealed class ProtectionGeometryRevision
        {
            public ProtectionBundle Bundle;
            public string SourceRevision;
            public readonly List<HermesProtectionUpdate> Updates =
                new List<HermesProtectionUpdate>();
        }

        private sealed class Book
        {
            public string Account;
            public string Instrument;
            public bool PositionKnown;
            public int SignedPosition;
            public long PositionRevision;
            public readonly Queue<BookOperation> Operations = new Queue<BookOperation>();
            public readonly List<ProtectionBundle> Bundles = new List<ProtectionBundle>();
        }

        private sealed class PendingSynchronization
        {
            public string Id;
            public string RouteId;
            public string Instrument;
        }

        private sealed class FlattenOperation
        {
            public string Id;
            public string CommandId;
            public string Account;
            public GlitchOperationPhase Phase;
            public string Failure;
        }

        private sealed class ExecutionEvidence
        {
            public string NativeOrderKey;
            public int Quantity;
        }

        private readonly Dictionary<string, Route> _routes =
            new Dictionary<string, Route>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AllocationEpoch> _allocations =
            new Dictionary<string, AllocationEpoch>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Book> _books =
            new Dictionary<string, Book>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, OrderFact> _orders =
            new Dictionary<string, OrderFact>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _executedByOrder =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ExecutionEvidence> _executionEvidence =
            new Dictionary<string, ExecutionEvidence>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TradeOperation> _tradeByCommand =
            new Dictionary<string, TradeOperation>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TradeOperation> _cancelByCommand =
            new Dictionary<string, TradeOperation>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TradeOperation> _refreshByCommand =
            new Dictionary<string, TradeOperation>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PendingSynchronization> _syncRefreshByCommand =
            new Dictionary<string, PendingSynchronization>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ProtectionRequest> _protectionRequests =
            new Dictionary<string, ProtectionRequest>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ProtectionBundle> _bundleByProtectionCommand =
            new Dictionary<string, ProtectionBundle>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ProtectionChangeOperation> _changeByCommand =
            new Dictionary<string, ProtectionChangeOperation>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FlattenOperation> _flattenByCommand =
            new Dictionary<string, FlattenOperation>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FlattenOperation> _flattenByAccount =
            new Dictionary<string, FlattenOperation>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FlattenOperation> _flattenOperations =
            new Dictionary<string, FlattenOperation>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, BookOperation> _operations =
            new Dictionary<string, BookOperation>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _replicationOrderLimits =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _seenExecutions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _seenRequests =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PendingSynchronization> _pendingSynchronizations =
            new Dictionary<string, PendingSynchronization>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, MasterProtectionObserved> _masterProtectionSnapshots =
            new Dictionary<string, MasterProtectionObserved>(StringComparer.OrdinalIgnoreCase);
        private long _positionRevision;
        private long _bundleSequence;
        private long _localRequestSequence;

        public IReadOnlyList<GlitchCommand> Handle(GlitchInput input)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            var commands = new List<GlitchCommand>();

            var position = input as PositionObserved;
            if (position != null)
            {
                ObservePosition(position);
                CompletePendingSynchronizations(commands);
                PumpAll(commands);
                return commands;
            }

            var order = input as NativeOrderObserved;
            if (order != null)
            {
                ObserveOrder(order);
                PumpAll(commands);
                return commands;
            }

            var failed = input as NativeRequestFailedObserved;
            if (failed != null)
            {
                ObserveRequestFailure(failed, commands);
                PumpAll(commands);
                return commands;
            }

            var unknown = input as NativeRequestUnknownObserved;
            if (unknown != null)
            {
                ObserveRequestUnknown(unknown, commands);
                PumpAll(commands);
                return commands;
            }

            var stale = input as NativePlanStaleObserved;
            if (stale != null)
            {
                ObserveStalePlan(stale);
                PumpAll(commands);
                return commands;
            }

            var cancellation = input as ProtectionCancellationCompletedObserved;
            if (cancellation != null)
            {
                TradeOperation operation;
                if (_cancelByCommand.TryGetValue(cancellation.CommandId, out operation))
                    operation.ExternalCancellationCompleted = true;
                PumpAll(commands);
                return commands;
            }

            var flattenCompleted = input as FlattenCompletedObserved;
            if (flattenCompleted != null)
            {
                CompleteFlatten(flattenCompleted);
                PumpAll(commands);
                return commands;
            }

            var execution = input as ExecutionObserved;
            if (execution != null)
            {
                ObserveExecution(execution, commands);
                PumpAll(commands);
                return commands;
            }

            var executionLifecycle = input as ExecutionLifecycleObserved;
            if (executionLifecycle != null)
            {
                ObserveExecutionLifecycle(executionLifecycle);
                return commands;
            }

            if (input is HermesNoActionRequested)
                return commands;

            if (input is AccountStatusObserved
                || input is RecoveryCompletedObserved)
                return commands;

            var routeConfiguration = input as RouteConfigurationChanged;
            if (routeConfiguration != null)
            {
                ReplaceRoutes(routeConfiguration, commands);
                PumpAll(commands);
                return commands;
            }

            var synchronize = input as RouteSynchronizationRequested;
            if (synchronize != null)
            {
                Synchronize(synchronize.RouteId, commands);
                PumpAll(commands);
                return commands;
            }

            var limit = input as ReplicationQuantityLimitChanged;
            if (limit != null)
            {
                if (limit.MaxOrderQuantity.HasValue)
                    _replicationOrderLimits[limit.AccountName] = limit.MaxOrderQuantity.Value;
                else
                    _replicationOrderLimits.Remove(limit.AccountName);
                return commands;
            }

            var masterProtection = input as MasterProtectionObserved;
            if (masterProtection != null)
            {
                _masterProtectionSnapshots[
                    masterProtection.AccountName + "|" + masterProtection.InstrumentName] =
                    masterProtection;
                ObserveManualMasterProtection(masterProtection, commands);
                PumpAll(commands);
                return commands;
            }

            var hermesEntry = input as HermesEntryRequested;
            if (hermesEntry != null)
            {
                RequestHermesEntry(hermesEntry);
                PumpAll(commands);
                return commands;
            }

            var hermesExit = input as HermesExitRequested;
            if (hermesExit != null)
            {
                RequestHermesExit(hermesExit);
                PumpAll(commands);
                return commands;
            }

            var protectionChange = input as HermesProtectionChangeRequested;
            if (protectionChange != null)
            {
                RequestProtectionChange(protectionChange, commands);
                return commands;
            }

            var flatten = input as FlattenAccountRequested;
            if (flatten != null)
            {
                RequestFlatten(flatten, commands);
                return commands;
            }

            throw new NotSupportedException("Unsupported Glitch input " + input.GetType().FullName + ".");
        }

        public GlitchOperationPhase? GetOperationPhase(string operationId)
        {
            BookOperation operation;
            if (_operations.TryGetValue(operationId ?? string.Empty, out operation))
                return operation.Phase;
            FlattenOperation flatten;
            return _flattenOperations.TryGetValue(operationId ?? string.Empty, out flatten)
                ? (GlitchOperationPhase?)flatten.Phase : null;
        }

        public IReadOnlyList<GlitchAccountInstrumentScope> GetKnownScopes()
        {
            return _books.Values
                .OrderBy(value => value.Account, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value.Instrument, StringComparer.OrdinalIgnoreCase)
                .Select(value => new GlitchAccountInstrumentScope(
                    value.Account, value.Instrument))
                .ToArray();
        }

        public bool IsCommandPending(string commandId)
        {
            TradeOperation trade;
            if (_tradeByCommand.TryGetValue(commandId ?? string.Empty, out trade)
                || _cancelByCommand.TryGetValue(commandId ?? string.Empty, out trade)
                || _refreshByCommand.TryGetValue(commandId ?? string.Empty, out trade))
            {
                return trade.Phase != GlitchOperationPhase.Completed
                    && trade.Phase != GlitchOperationPhase.Failed
                    && trade.Phase != GlitchOperationPhase.Unknown
                    && trade.Phase != GlitchOperationPhase.Superseded;
            }
            if (_syncRefreshByCommand.ContainsKey(commandId ?? string.Empty))
                return true;
            ProtectionRequest protection;
            if (_protectionRequests.TryGetValue(commandId ?? string.Empty, out protection))
                return protection.Bundle != null
                    && !protection.Bundle.Superseded
                    && protection.Bundle.RemainingQuantity > 0
                    && !protection.RequestFailed
                    && !protection.RequestUnknown
                    && !ProtectionEstablished(commandId);
            ProtectionChangeOperation change;
            if (_changeByCommand.TryGetValue(commandId ?? string.Empty, out change))
                return change.Phase == GlitchOperationPhase.NativePending;
            FlattenOperation flatten;
            return _flattenByCommand.TryGetValue(commandId ?? string.Empty, out flatten)
                && flatten.Phase == GlitchOperationPhase.NativePending;
        }

        private void ObservePosition(PositionObserved observed)
        {
            Book book = GetBook(observed.AccountName, observed.InstrumentName);
            book.PositionKnown = true;
            book.SignedPosition = observed.SignedQuantity;
            book.PositionRevision = observed.Revision > 0
                ? observed.Revision
                : ++_positionRevision;
            if (observed.SignedQuantity == 0)
                _masterProtectionSnapshots.Remove(
                    observed.AccountName + "|" + observed.InstrumentName);
            foreach (TradeOperation operation in book.Operations
                .OfType<TradeOperation>()
                .Where(value => value.PositionRefreshRequested)
                .ToArray())
            {
                operation.PositionRefreshRequested = false;
                foreach (string commandId in _refreshByCommand
                    .Where(value => ReferenceEquals(value.Value, operation))
                    .Select(value => value.Key)
                    .ToArray())
                    _refreshByCommand.Remove(commandId);
            }
        }

        private void ObserveOrder(NativeOrderObserved observed)
        {
            string key = OrderKey(observed.AccountName, observed.NativeOrderKey);
            _orders[key] = new OrderFact { Value = observed };

            ProtectionRequest request;
            if (_protectionRequests.TryGetValue(observed.CorrelationId, out request)
                && string.Equals(observed.OrderState, "Rejected", StringComparison.OrdinalIgnoreCase))
                request.RequestFailed = true;
        }

        private void ObserveRequestFailure(
            NativeRequestFailedObserved failed,
            ICollection<GlitchCommand> commands)
        {
            TradeOperation trade;
            if (_tradeByCommand.TryGetValue(failed.CommandId, out trade)
                || _cancelByCommand.TryGetValue(failed.CommandId, out trade)
                || _refreshByCommand.TryGetValue(failed.CommandId, out trade))
            {
                trade.Phase = GlitchOperationPhase.Failed;
                trade.Failure = failed.Error;
            }

            ProtectionRequest protection;
            if (_protectionRequests.TryGetValue(failed.CommandId, out protection))
            {
                protection.RequestFailed = true;
                if (protection.Owner != null)
                {
                    protection.Owner.Phase = GlitchOperationPhase.Failed;
                    protection.Owner.Failure = failed.Error;
                    RequestFlatten(new FlattenAccountRequested(
                        "protection-failure|" + failed.CommandId,
                        protection.Owner.Account,
                        "native_protection_failed|" + failed.CommandId), commands);
                }
            }

            PendingSynchronization synchronization;
            if (_syncRefreshByCommand.TryGetValue(failed.CommandId, out synchronization))
            {
                _pendingSynchronizations.Remove(
                    synchronization.RouteId + "|" + synchronization.Instrument);
                RemoveSynchronizationRefreshes(synchronization);
            }

            ProtectionChangeOperation change;
            if (_changeByCommand.TryGetValue(failed.CommandId, out change))
            {
                change.Phase = GlitchOperationPhase.Failed;
                change.Failure = failed.Error;
                ClearPendingGeometryRevisions(change);
            }

            FlattenOperation flatten;
            if (_flattenByCommand.TryGetValue(failed.CommandId, out flatten))
            {
                flatten.Phase = GlitchOperationPhase.Failed;
                flatten.Failure = failed.Error;
                _flattenByAccount.Remove(flatten.Account);
            }
        }

        private void ObserveRequestUnknown(
            NativeRequestUnknownObserved unknown,
            ICollection<GlitchCommand> commands)
        {
            TradeOperation trade;
            if (_tradeByCommand.TryGetValue(unknown.CommandId, out trade)
                || _cancelByCommand.TryGetValue(unknown.CommandId, out trade)
                || _refreshByCommand.TryGetValue(unknown.CommandId, out trade))
            {
                trade.Phase = GlitchOperationPhase.Unknown;
                trade.Failure = unknown.EvidenceGap;
            }

            ProtectionRequest protection;
            if (_protectionRequests.TryGetValue(unknown.CommandId, out protection))
            {
                protection.RequestUnknown = true;
                if (protection.Owner != null)
                {
                    protection.Owner.Phase = GlitchOperationPhase.Unknown;
                    protection.Owner.Failure = unknown.EvidenceGap;
                    RequestFlatten(new FlattenAccountRequested(
                        "protection-unknown|" + unknown.CommandId,
                        protection.Owner.Account,
                        "native_protection_unknown|" + unknown.CommandId), commands);
                }
            }

            PendingSynchronization synchronization;
            if (_syncRefreshByCommand.TryGetValue(unknown.CommandId, out synchronization))
            {
                _pendingSynchronizations.Remove(
                    synchronization.RouteId + "|" + synchronization.Instrument);
                RemoveSynchronizationRefreshes(synchronization);
            }

            ProtectionChangeOperation change;
            if (_changeByCommand.TryGetValue(unknown.CommandId, out change))
            {
                change.Phase = GlitchOperationPhase.Unknown;
                change.Failure = unknown.EvidenceGap;
            }

            FlattenOperation flatten;
            if (_flattenByCommand.TryGetValue(unknown.CommandId, out flatten))
            {
                flatten.Phase = GlitchOperationPhase.Unknown;
                flatten.Failure = unknown.EvidenceGap;
                _flattenByAccount.Remove(flatten.Account);
            }
        }

        private void ObserveStalePlan(NativePlanStaleObserved stale)
        {
            TradeOperation operation;
            if (!_tradeByCommand.TryGetValue(stale.CommandId, out operation))
                return;
            Book book = GetBook(stale.AccountName, stale.InstrumentName);
            book.PositionKnown = true;
            book.SignedPosition = stale.SignedPosition;
            book.PositionRevision = ++_positionRevision;
            operation.ActiveCommandId = null;
            operation.ActiveRequestedSignedQuantity = 0;
            operation.ActiveFilledSignedQuantity = 0;
            operation.ActiveExpectedSignedPosition = 0;
            operation.ActivePositionRevision = 0;
            operation.Phase = GlitchOperationPhase.Ready;
        }

        private void ObserveExecution(
            ExecutionObserved execution,
            ICollection<GlitchCommand> commands)
        {
            string executionKey = execution.AccountName + "|" + execution.ExecutionId;
            if (!_seenExecutions.Add(executionKey))
                return;

            Book book = GetBook(execution.AccountName, execution.InstrumentName);

            ProtectionTemplate replicatedProtection = null;
            bool suppressReplication = false;
            TradeOperation operation = null;
            bool ownedExecution = !string.IsNullOrWhiteSpace(execution.CorrelationId)
                && _tradeByCommand.TryGetValue(execution.CorrelationId, out operation);
            bool positionIncludesExecution = ownedExecution
                && operation.Phase != GlitchOperationPhase.Superseded
                && book.PositionKnown
                && book.PositionRevision > operation.ActivePositionRevision
                && book.SignedPosition == checked(
                    operation.ActiveExpectedSignedPosition
                    + operation.ActiveFilledSignedQuantity
                    + execution.SignedQuantity);
            book.PositionKnown = positionIncludesExecution;
            if (ownedExecution)
            {
                if (operation.Phase == GlitchOperationPhase.Superseded)
                {
                    suppressReplication = true;
                }
                else
                {
                if (Math.Sign(operation.ActiveRequestedSignedQuantity)
                        != Math.Sign(execution.SignedQuantity)
                    || Math.Abs(operation.ActiveFilledSignedQuantity + execution.SignedQuantity)
                        > Math.Abs(operation.ActiveRequestedSignedQuantity))
                {
                    operation.Phase = GlitchOperationPhase.Unknown;
                    operation.Failure = "native_execution_exceeded_active_trade_step";
                }
                else
                {
                    int priorSignedPosition = checked(
                        operation.ActiveExpectedSignedPosition
                        + operation.ActiveFilledSignedQuantity);
                    int openingQuantity = OpeningQuantityFromPrior(
                        priorSignedPosition,
                        execution.SignedQuantity);
                    operation.ActiveFilledSignedQuantity += execution.SignedQuantity;
                    operation.RemainingSignedQuantity -= execution.SignedQuantity;
                    int appliedSignedQuantity = checked(
                        operation.RequestedSignedQuantity
                        - operation.RemainingSignedQuantity);
                    if (!operation.TargetSignedPosition.HasValue
                        && (Math.Sign(appliedSignedQuantity)
                            != Math.Sign(operation.RequestedSignedQuantity)
                        || Math.Abs(appliedSignedQuantity)
                            > Math.Abs(operation.RequestedSignedQuantity)))
                    {
                        operation.Phase = GlitchOperationPhase.Unknown;
                        operation.Failure = "native_execution_exceeded_immutable_trade_delta";
                        suppressReplication = true;
                        return;
                    }

                    int closingQuantity = Math.Abs(execution.SignedQuantity)
                        - openingQuantity;
                    if (closingQuantity > 0)
                        SettleOwnedExposure(book, -Math.Sign(execution.SignedQuantity), closingQuantity);

                    if (openingQuantity > 0)
                        replicatedProtection = ProtectOpeningFill(
                            book,
                            operation,
                            execution,
                            openingQuantity,
                            commands);
                }
                }
            }

            if (!string.IsNullOrWhiteSpace(execution.ProtectionCorrelationId))
                ObserveProtectiveFill(execution);

            if (execution.Origin == GlitchExecutionOrigin.GlitchFlatten)
                ObserveSafetyFlattenFill(execution);

            if (execution.Origin == GlitchExecutionOrigin.External)
                EnqueueManualMasterProtectionCleanup(execution, book);

            if (!suppressReplication
                && !execution.IsBaseline
                && execution.Origin != GlitchExecutionOrigin.GlitchSynchronization
                && execution.Origin != GlitchExecutionOrigin.GlitchFlatten)
                ReplicateMasterExecution(execution, replicatedProtection, commands);
        }

        private void ObserveExecutionLifecycle(ExecutionLifecycleObserved execution)
        {
            if (string.IsNullOrWhiteSpace(execution.ExecutionId)
                || string.IsNullOrWhiteSpace(execution.AccountName))
                return;

            string executionKey = execution.AccountName + "|" + execution.ExecutionId;
            ExecutionEvidence prior;
            if (_executionEvidence.TryGetValue(executionKey, out prior))
                AdjustExecutedByOrder(
                    execution.AccountName, prior.NativeOrderKey, -prior.Quantity);

            if (execution.Operation == GlitchNativeOperation.Remove)
            {
                _executionEvidence.Remove(executionKey);
                return;
            }

            if ((execution.Operation != GlitchNativeOperation.Add
                    && execution.Operation != GlitchNativeOperation.Update)
                || !execution.Representable
                || string.IsNullOrWhiteSpace(execution.NativeOrderKey))
            {
                _executionEvidence.Remove(executionKey);
                return;
            }

            var current = new ExecutionEvidence
            {
                NativeOrderKey = execution.NativeOrderKey,
                Quantity = Math.Abs(execution.SignedQuantity)
            };
            _executionEvidence[executionKey] = current;
            AdjustExecutedByOrder(
                execution.AccountName, current.NativeOrderKey, current.Quantity);
        }

        private void AdjustExecutedByOrder(
            string accountName,
            string nativeOrderKey,
            int delta)
        {
            if (string.IsNullOrWhiteSpace(nativeOrderKey) || delta == 0)
                return;
            string key = OrderKey(accountName, nativeOrderKey);
            int prior;
            _executedByOrder.TryGetValue(key, out prior);
            int current = Math.Max(0, prior + delta);
            if (current == 0)
                _executedByOrder.Remove(key);
            else
                _executedByOrder[key] = current;
        }

        private void ReplaceRoutes(
            RouteConfigurationChanged configuration,
            ICollection<GlitchCommand> commands)
        {
            var nextIds = new HashSet<string>(
                configuration.Routes.Select(value => value.RouteId),
                StringComparer.OrdinalIgnoreCase);
            foreach (string removed in _routes.Keys.Where(value => !nextIds.Contains(value)).ToArray())
                RemoveRoute(removed);

            foreach (RouteConfigurationItem item in configuration.Routes)
            {
                bool enabled = configuration.ReplicationEnabled && item.Enabled;
                Route prior;
                bool changed = !_routes.TryGetValue(item.RouteId, out prior)
                    || !string.Equals(prior.Master, item.MasterAccount, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(prior.Follower, item.FollowerAccount, StringComparison.OrdinalIgnoreCase)
                    || prior.Ratio != item.Ratio
                    || prior.Enabled != enabled;
                _routes[item.RouteId] = new Route
                {
                    Id = item.RouteId,
                    Master = item.MasterAccount,
                    Follower = item.FollowerAccount,
                    Ratio = item.Ratio,
                    Enabled = enabled
                };
                if (changed)
                    ResetAllocation(item.RouteId);
            }

            foreach (string routeId in configuration.SynchronizeRouteIds)
                Synchronize(routeId, commands);
        }

        private void RemoveRoute(string routeId)
        {
            _routes.Remove(routeId);
            ResetAllocation(routeId);
            foreach (string key in _pendingSynchronizations.Where(value =>
                    string.Equals(value.Value.RouteId, routeId, StringComparison.OrdinalIgnoreCase))
                .Select(value => value.Key).ToArray())
                _pendingSynchronizations.Remove(key);
            foreach (string key in _syncRefreshByCommand.Where(value =>
                    string.Equals(value.Value.RouteId, routeId, StringComparison.OrdinalIgnoreCase))
                .Select(value => value.Key).ToArray())
                _syncRefreshByCommand.Remove(key);
        }

        private void Synchronize(string routeId, ICollection<GlitchCommand> commands)
        {
            Route route;
            if (!_routes.TryGetValue(routeId, out route) || !route.Enabled)
                return;

            string requestId = "SYNC|" + route.Id + "|" + NextLocalRequest();
            foreach (string instrument in KnownInstruments(route).ToArray())
            {
                var pending = new PendingSynchronization
                {
                    Id = requestId,
                    RouteId = route.Id,
                    Instrument = instrument
                };
                _pendingSynchronizations[route.Id + "|" + instrument] = pending;
                TryCompleteSynchronization(pending, commands);
            }
            ResetAllocation(route.Id);
        }

        private void CompletePendingSynchronizations(ICollection<GlitchCommand> commands)
        {
            foreach (PendingSynchronization pending in _pendingSynchronizations.Values.ToArray())
                TryCompleteSynchronization(pending, commands);
        }

        private void TryCompleteSynchronization(
            PendingSynchronization pending,
            ICollection<GlitchCommand> commands)
        {
            Route route;
            if (!_routes.TryGetValue(pending.RouteId, out route) || !route.Enabled)
            {
                _pendingSynchronizations.Remove(pending.RouteId + "|" + pending.Instrument);
                RemoveSynchronizationRefreshes(pending);
                return;
            }
            Book master = GetBook(route.Master, pending.Instrument);
            Book follower = GetBook(route.Follower, pending.Instrument);
            if (!master.PositionKnown)
            {
                string commandId = CommandId(
                    pending.Id + "|" + pending.Instrument + "|MASTER", 1, "POSITION");
                _syncRefreshByCommand[commandId] = pending;
                commands.Add(new RefreshPositionCommand(
                    commandId,
                    route.Master,
                    pending.Instrument));
            }
            if (!follower.PositionKnown)
            {
                string commandId = CommandId(
                    pending.Id + "|" + pending.Instrument + "|FOLLOWER", 1, "POSITION");
                _syncRefreshByCommand[commandId] = pending;
                commands.Add(new RefreshPositionCommand(
                    commandId,
                    route.Follower,
                    pending.Instrument));
            }
            if (!master.PositionKnown || !follower.PositionKnown)
                return;

            int desired = RoundQuantity(checked(master.SignedPosition * route.Ratio));
            int delta = checked(desired - follower.SignedPosition);
            if (delta != 0)
            {
                MasterProtectionObserved masterProtection;
                bool mirrorsManualMasterProtection = _masterProtectionSnapshots.TryGetValue(
                        route.Master + "|" + pending.Instrument, out masterProtection)
                    && masterProtection.TickSize > 0;
                int maxOpeningStepQuantity;
                _replicationOrderLimits.TryGetValue(route.Follower, out maxOpeningStepQuantity);
                EnqueueTrade(
                    pending.Id + "|" + pending.Instrument,
                    "route_sync",
                    GlitchCommandPurpose.GroupSynchronization,
                    route.Follower,
                    pending.Instrument,
                    delta,
                    route.Id,
                    null,
                    false,
                    mirrorsManualMasterProtection,
                    targetSignedPosition: desired,
                    maxOpeningStepQuantity: maxOpeningStepQuantity);
            }
            _pendingSynchronizations.Remove(pending.RouteId + "|" + pending.Instrument);
            RemoveSynchronizationRefreshes(pending);
        }

        private void RemoveSynchronizationRefreshes(PendingSynchronization pending)
        {
            foreach (string commandId in _syncRefreshByCommand.Where(value =>
                ReferenceEquals(value.Value, pending)).Select(value => value.Key).ToArray())
                _syncRefreshByCommand.Remove(commandId);
        }

        private void RequestHermesEntry(HermesEntryRequested request)
        {
            if (!_seenRequests.Add("H|" + request.IntentId))
                return;
            var protection = new ProtectionTemplate(
                request.StopPrice - request.DecisionReferencePrice,
                request.Targets.Select((target, index) => new ProtectionLegTemplate(
                    CompactLegId(request.IntentId, index),
                    target.Quantity,
                    (target.StopPrice > 0 ? target.StopPrice : request.StopPrice)
                        - request.DecisionReferencePrice,
                    target.Price - request.DecisionReferencePrice)));
            EnqueueTrade(
                "HERMES|" + request.IntentId,
                request.IntentId,
                GlitchCommandPurpose.HermesMasterEntry,
                request.AccountName,
                request.InstrumentName,
                request.SignedQuantity,
                null,
                protection,
                false,
                false,
                hermesIntentId: request.IntentId,
                entryRangeLow: request.EntryRangeLow,
                entryRangeHigh: request.EntryRangeHigh);
        }

        private void RequestHermesExit(HermesExitRequested request)
        {
            if (!_seenRequests.Add("H|" + request.IntentId))
                return;
            Book book = GetBook(request.AccountName, request.InstrumentName);
            if (!book.PositionKnown || book.SignedPosition == 0)
                return;
            EnqueueTrade(
                "HERMES|" + request.IntentId,
                request.IntentId,
                GlitchCommandPurpose.HermesMasterExit,
                request.AccountName,
                request.InstrumentName,
                -book.SignedPosition,
                null,
                null,
                true,
                false,
                true,
                hermesIntentId: request.IntentId);
        }

        private void RequestProtectionChange(
            HermesProtectionChangeRequested request,
            ICollection<GlitchCommand> commands)
        {
            if (!_seenRequests.Add("H|" + request.IntentId))
                return;

            Book master = GetBook(request.AccountName, request.InstrumentName);
            List<HermesProtectionUpdate> masterUpdates = ResolveAbsoluteProtectionUpdates(
                master, request.Updates, null);
            if (masterUpdates.Count == 0)
                return;

            var masterOperation = BuildProtectionChangeOperation(
                "HERMES|" + request.IntentId + "|CHANGE|MASTER",
                request.AccountName,
                request.InstrumentName,
                masterUpdates,
                ProtectionCommandsForUpdates(master, masterUpdates),
                master,
                hermesIntentId: request.IntentId);

            foreach (Route route in _routes.Values.Where(value => value.Enabled
                && string.Equals(value.Master, request.AccountName, StringComparison.OrdinalIgnoreCase)))
            {
                Book follower = GetBook(route.Follower, request.InstrumentName);
                List<HermesProtectionUpdate> followerUpdates = ResolveAbsoluteProtectionUpdates(
                    follower, request.Updates, master);
                if (followerUpdates.Count > 0)
                {
                    masterOperation.Followers.Add(BuildProtectionChangeOperation(
                        "HERMES|" + request.IntentId + "|CHANGE|" + route.Id,
                        route.Follower,
                        request.InstrumentName,
                        followerUpdates,
                        ProtectionCommandsForUpdates(follower, followerUpdates),
                        follower,
                        hermesIntentId: request.IntentId));
                }
            }
            _operations[masterOperation.Id] = masterOperation;
            master.Operations.Enqueue(masterOperation);
            Pump(master, commands);
        }

        private static ProtectionChangeOperation BuildProtectionChangeOperation(
            string id,
            string account,
            string instrument,
            IEnumerable<HermesProtectionUpdate> updates,
            IEnumerable<string> targetCommandIds,
            Book book,
            string sourceRevision = null,
            string hermesIntentId = null)
        {
            var operation = new ProtectionChangeOperation
            {
                Id = id,
                Account = account,
                Instrument = instrument,
                Phase = GlitchOperationPhase.Accepted,
                HermesIntentId = hermesIntentId ?? string.Empty
            };
            operation.Updates.AddRange(updates);
            operation.TargetCommandIds.AddRange(targetCommandIds);
            var targetSet = new HashSet<string>(
                operation.TargetCommandIds, StringComparer.OrdinalIgnoreCase);
            foreach (ProtectionBundle bundle in book.Bundles.Where(value =>
                !value.Superseded && targetSet.Contains(value.CurrentRequestId)))
            {
                var revision = new ProtectionGeometryRevision
                {
                    Bundle = bundle,
                    SourceRevision = sourceRevision
                };
                revision.Updates.AddRange(operation.Updates.Where(update =>
                    bundle.Slices.Any(slice => string.Equals(
                        slice.LegId, update.LegId, StringComparison.OrdinalIgnoreCase))));
                if (revision.Updates.Count > 0)
                    operation.GeometryRevisions.Add(revision);
            }
            return operation;
        }

        private static IEnumerable<string> ProtectionCommandsForUpdates(
            Book book,
            IEnumerable<HermesProtectionUpdate> updates)
        {
            var legIds = new HashSet<string>(
                updates.Select(value => value.LegId), StringComparer.OrdinalIgnoreCase);
            return book.Bundles.Where(bundle => !bundle.Superseded
                    && bundle.RemainingQuantity > 0
                    && !string.IsNullOrWhiteSpace(bundle.CurrentRequestId)
                    && bundle.Slices.Any(slice => legIds.Contains(slice.LegId)))
                .Select(bundle => bundle.CurrentRequestId)
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static List<HermesProtectionUpdate> ResolveAbsoluteProtectionUpdates(
            Book target,
            IEnumerable<HermesProtectionUpdate> requested,
            Book source)
        {
            var result = new List<HermesProtectionUpdate>();
            foreach (HermesProtectionUpdate update in requested)
            {
                ProtectionBundle targetBundle = target.Bundles.FirstOrDefault(bundle =>
                    bundle.RemainingQuantity > 0
                    && bundle.Slices.Any(slice => string.Equals(
                        slice.LegId, update.LegId, StringComparison.OrdinalIgnoreCase)));
                if (targetBundle == null)
                    continue;

                decimal? stop = update.StopPrice;
                decimal? targetPrice = update.TargetPrice;
                if (source != null)
                {
                    ProtectionBundle sourceBundle = source.Bundles.FirstOrDefault(bundle =>
                        bundle.RemainingQuantity > 0
                        && bundle.Slices.Any(slice => string.Equals(
                            slice.LegId, update.LegId, StringComparison.OrdinalIgnoreCase)));
                    if (sourceBundle == null)
                        continue;
                    stop = update.StopPrice.HasValue
                        ? targetBundle.EntryPrice + (update.StopPrice.Value - sourceBundle.EntryPrice)
                        : (decimal?)null;
                    targetPrice = update.TargetPrice.HasValue
                        ? targetBundle.EntryPrice + (update.TargetPrice.Value - sourceBundle.EntryPrice)
                        : (decimal?)null;
                }
                result.Add(new HermesProtectionUpdate(update.LegId, stop, targetPrice));
            }
            return result;
        }

        private void RequestFlatten(
            FlattenAccountRequested request,
            ICollection<GlitchCommand> commands)
        {
            if (!_seenRequests.Add("F|" + request.RequestId))
                return;
            FlattenOperation existing;
            if (_flattenByAccount.TryGetValue(request.AccountName, out existing)
                && existing.Phase == GlitchOperationPhase.NativePending)
                return;

            bool protectionSafetyFlatten = request.Reason.StartsWith(
                    "native_protection_failed|", StringComparison.OrdinalIgnoreCase)
                || request.Reason.StartsWith(
                    "native_protection_unknown|", StringComparison.OrdinalIgnoreCase);
            RetireBundlesForFlatten(request.AccountName, protectionSafetyFlatten);
            if (string.Equals(
                    request.Reason, "user_flatten_all", StringComparison.OrdinalIgnoreCase))
                ResetAllocationsForAccount(request.AccountName);

            foreach (Book book in _books.Values.Where(value => string.Equals(
                value.Account, request.AccountName, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (BookOperation operation in book.Operations)
                {
                    if (operation.Phase != GlitchOperationPhase.Completed
                        && operation.Phase != GlitchOperationPhase.Failed)
                        operation.Phase = GlitchOperationPhase.Superseded;
                }
            }
            string[] instruments = _books.Values
                .Where(book => string.Equals(book.Account, request.AccountName, StringComparison.OrdinalIgnoreCase)
                    && (book.SignedPosition != 0 || book.Bundles.Any(bundle => bundle.RemainingQuantity > 0)))
                .Select(book => book.Instrument)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var flatten = new FlattenOperation
            {
                Id = "FLATTEN|" + request.RequestId,
                CommandId = CommandId("FLATTEN|" + request.RequestId, 1, "A"),
                Account = request.AccountName,
                Phase = GlitchOperationPhase.NativePending
            };
            _flattenOperations[flatten.Id] = flatten;
            _flattenByCommand[flatten.CommandId] = flatten;
            _flattenByAccount[flatten.Account] = flatten;
            commands.Add(new FlattenAccountCommand(
                flatten.CommandId,
                request.AccountName,
                instruments,
                request.Reason));
        }

        private void CompleteFlatten(FlattenCompletedObserved completed)
        {
            FlattenOperation flatten;
            if (!_flattenByCommand.TryGetValue(completed.CommandId, out flatten))
                return;
            flatten.Phase = GlitchOperationPhase.Completed;
            FlattenOperation current;
            if (_flattenByAccount.TryGetValue(flatten.Account, out current)
                && ReferenceEquals(current, flatten))
                _flattenByAccount.Remove(flatten.Account);
            foreach (Book book in _books.Values.Where(value => string.Equals(
                value.Account, flatten.Account, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (ProtectionBundle bundle in book.Bundles.Where(value => value.Superseded))
                    bundle.SafetyFlattenPending = false;
            }
        }

        private void RetireBundlesForFlatten(string accountName, bool safetySettlement)
        {
            foreach (Book book in _books.Values.Where(value => string.Equals(
                value.Account, accountName, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (ProtectionBundle bundle in book.Bundles.Where(value =>
                    !value.Superseded && value.RemainingQuantity > 0))
                {
                    bundle.Superseded = true;
                    bundle.PendingSourceRevision = null;
                    bundle.SafetyFlattenPending = safetySettlement
                        && !string.IsNullOrWhiteSpace(bundle.RouteId);
                }
            }
        }

        private void ResetAllocationsForAccount(string accountName)
        {
            foreach (Route route in _routes.Values.Where(value =>
                string.Equals(value.Master, accountName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(value.Follower, accountName, StringComparison.OrdinalIgnoreCase)))
                ResetAllocation(route.Id);
        }

        private void ReplicateMasterExecution(
            ExecutionObserved execution,
            ProtectionTemplate fillProtection,
            ICollection<GlitchCommand> commands)
        {
            FlattenOperation masterFlatten;
            if (_flattenByAccount.TryGetValue(execution.AccountName, out masterFlatten)
                && masterFlatten.Phase == GlitchOperationPhase.NativePending)
                return;
            foreach (Route route in _routes.Values
                .Where(value => value.Enabled
                    && string.Equals(value.Master, execution.AccountName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(value => value.Id, StringComparer.OrdinalIgnoreCase))
            {
                FlattenOperation followerFlatten;
                if (_flattenByAccount.TryGetValue(route.Follower, out followerFlatten)
                    && followerFlatten.Phase == GlitchOperationPhase.NativePending)
                    continue;
                string allocationKey = route.Id + "|" + execution.InstrumentName;
                AllocationEpoch allocation;
                if (!_allocations.TryGetValue(allocationKey, out allocation))
                {
                    allocation = new AllocationEpoch();
                    _allocations[allocationKey] = allocation;
                }
                int nextMasterSignedTotal = checked(
                    allocation.MasterSignedTotal + execution.SignedQuantity);
                int nextTarget = RoundQuantity(
                    checked(nextMasterSignedTotal * route.Ratio));
                int delta = checked(nextTarget - allocation.FollowerTarget);
                allocation.MasterSignedTotal = nextMasterSignedTotal;
                allocation.FollowerTarget = nextTarget;
                if (delta == 0)
                    continue;

                ProtectionTemplate scaled = ScaleProtection(fillProtection, Math.Abs(delta));
                EnqueueSplitTrade(
                    "REPL|" + route.Id + "|" + execution.AccountName + "|" + execution.ExecutionId,
                    execution.ExecutionId,
                    GlitchCommandPurpose.Replication,
                    route.Follower,
                    execution.InstrumentName,
                    delta,
                    route.Id,
                    scaled,
                    fillProtection == null && execution.Origin == GlitchExecutionOrigin.External,
                    HermesIntentIdForExecution(execution));
            }
        }

        private string HermesIntentIdForExecution(ExecutionObserved execution)
        {
            TradeOperation source;
            if (execution != null
                && _tradeByCommand.TryGetValue(execution.CorrelationId ?? string.Empty, out source)
                && source.Purpose == GlitchCommandPurpose.HermesMasterEntry)
                return source.HermesIntentId ?? string.Empty;
            return string.Empty;
        }

        private void EnqueueSplitTrade(
            string operationRoot,
            string causeId,
            GlitchCommandPurpose purpose,
            string account,
            string instrument,
            int signedQuantity,
            string routeId,
            ProtectionTemplate protection,
            bool mirrorsManualMasterProtection,
            string hermesIntentId = null)
        {
            bool opening = IsOpeningIncrease(GetBook(account, instrument), signedQuantity);
            int max;
            if (!opening
                || !_replicationOrderLimits.TryGetValue(account, out max)
                || Math.Abs(signedQuantity) <= max)
            {
                EnqueueTrade(operationRoot, causeId, purpose, account, instrument,
                    signedQuantity, routeId, protection, false,
                    mirrorsManualMasterProtection, hermesIntentId: hermesIntentId);
                return;
            }

            int remaining = Math.Abs(signedQuantity);
            int offset = 0;
            int part = 0;
            while (remaining > 0)
            {
                int quantity = Math.Min(max, remaining);
                ProtectionTemplate slice = SliceProtection(protection, offset, quantity);
                EnqueueTrade(
                    operationRoot + "|PART|" + (++part).ToString(CultureInfo.InvariantCulture),
                    causeId,
                    purpose,
                    account,
                    instrument,
                    Math.Sign(signedQuantity) * quantity,
                    routeId,
                    slice,
                    false,
                    mirrorsManualMasterProtection,
                    hermesIntentId: hermesIntentId);
                remaining -= quantity;
                offset += quantity;
            }
        }

        private void EnqueueTrade(
            string operationId,
            string causeId,
            GlitchCommandPurpose purpose,
            string account,
            string instrument,
            int signedQuantity,
            string routeId,
            ProtectionTemplate protection,
            bool cancelExternalProtection,
            bool mirrorsManualMasterProtection,
            bool closeToFlat = false,
            string hermesIntentId = null,
            int? targetSignedPosition = null,
            int maxOpeningStepQuantity = 0,
            decimal? entryRangeLow = null,
            decimal? entryRangeHigh = null)
        {
            if (signedQuantity == 0 || _operations.ContainsKey(operationId))
                return;
            AllocationEpoch allocation = routeId == null || targetSignedPosition.HasValue
                ? null
                : GetAllocation(routeId, instrument);
            if (allocation != null)
            {
                signedQuantity = ConsumeSettlementCredit(allocation, signedQuantity);
                if (signedQuantity == 0)
                    return;
            }

            var operation = new TradeOperation
            {
                Id = operationId,
                CauseId = causeId,
                HermesIntentId = hermesIntentId ?? string.Empty,
                Purpose = purpose,
                Account = account,
                Instrument = instrument,
                RouteId = routeId,
                RequestedSignedQuantity = signedQuantity,
                RemainingSignedQuantity = signedQuantity,
                TargetSignedPosition = targetSignedPosition,
                MaxOpeningStepQuantity = maxOpeningStepQuantity,
                EntryRangeLow = entryRangeLow,
                EntryRangeHigh = entryRangeHigh,
                CancelExternalProtection = cancelExternalProtection,
                MirrorsManualMasterProtection = mirrorsManualMasterProtection,
                CloseToFlat = closeToFlat,
                Phase = GlitchOperationPhase.Accepted
            };
            if (protection != null)
            {
                operation.RemainingProtection.AddRange(protection.Targets.Select(value =>
                    new ProtectionLegTemplate(
                        value.LegId,
                        value.Quantity,
                        value.StopOffset,
                        value.TargetOffset)));
            }
            _operations[operation.Id] = operation;
            GetBook(account, instrument).Operations.Enqueue(operation);
        }

        private void PumpAll(ICollection<GlitchCommand> commands)
        {
            foreach (Book book in _books.Values
                .OrderBy(value => value.Account, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value.Instrument, StringComparer.OrdinalIgnoreCase)
                .ToArray())
                Pump(book, commands);
        }

        private void Pump(Book book, ICollection<GlitchCommand> commands)
        {
            FlattenOperation flatten;
            if (_flattenByAccount.TryGetValue(book.Account, out flatten)
                && flatten.Phase == GlitchOperationPhase.NativePending)
                return;
            while (book.Operations.Count > 0)
            {
                BookOperation queued = book.Operations.Peek();
                if (queued.Phase == GlitchOperationPhase.Unknown)
                    return;
                if (queued.Phase == GlitchOperationPhase.Completed
                    || queued.Phase == GlitchOperationPhase.Failed
                    || queued.Phase == GlitchOperationPhase.Superseded)
                {
                    book.Operations.Dequeue();
                    continue;
                }

                var change = queued as ProtectionChangeOperation;
                if (change != null)
                {
                    PumpProtectionChange(book, change, commands);
                    if (change.Phase == GlitchOperationPhase.Completed
                        || change.Phase == GlitchOperationPhase.Failed
                        || change.Phase == GlitchOperationPhase.Superseded)
                        continue;
                    return;
                }

                TradeOperation operation = (TradeOperation)queued;
                if (operation.Phase == GlitchOperationPhase.Completed
                    || operation.Phase == GlitchOperationPhase.Failed
                    || operation.Phase == GlitchOperationPhase.Superseded)
                {
                    book.Operations.Dequeue();
                    continue;
                }

                if (operation.Phase == GlitchOperationPhase.Accepted)
                    operation.Phase = GlitchOperationPhase.Ready;

                if (operation.Phase == GlitchOperationPhase.WaitingForProtectionCancellation)
                {
                    if (!CancellationComplete(operation))
                        return;
                    foreach (string requestId in operation.CancelledProtectionRequests)
                    {
                        ProtectionRequest request;
                        if (_protectionRequests.TryGetValue(requestId, out request))
                            request.Bundle.CancelRequested = true;
                    }
                    operation.Phase = GlitchOperationPhase.Ready;
                }

                if (operation.Phase == GlitchOperationPhase.NativePending)
                {
                    bool stepComplete = TradeStepComplete(book, operation);
                    if (operation.Phase == GlitchOperationPhase.Failed
                        || operation.Phase == GlitchOperationPhase.Unknown)
                        continue;
                    if (!stepComplete)
                        return;
                    operation.ActiveCommandId = null;
                    operation.ActiveRequestedSignedQuantity = 0;
                    operation.ActiveFilledSignedQuantity = 0;
                    operation.ActiveExpectedSignedPosition = 0;
                    operation.ActivePositionRevision = 0;
                    if (operation.TargetSignedPosition.HasValue)
                        operation.RemainingSignedQuantity = checked(
                            operation.TargetSignedPosition.Value - book.SignedPosition);
                    else if (operation.CloseToFlat)
                        operation.RemainingSignedQuantity = -book.SignedPosition;
                    if (operation.RemainingSignedQuantity != 0)
                    {
                        operation.Phase = GlitchOperationPhase.Ready;
                        continue;
                    }
                    ReprotectResidualBundles(book, operation, commands);
                    operation.Phase = operation.PendingProtectionRequests.Count == 0
                        ? GlitchOperationPhase.Completed
                        : GlitchOperationPhase.WaitingForProtection;
                    if (operation.Phase == GlitchOperationPhase.Completed)
                        continue;
                    continue;
                }

                if (operation.Phase == GlitchOperationPhase.WaitingForProtection)
                {
                    bool failed = operation.PendingProtectionRequests.Any(ProtectionFailed);
                    if (failed)
                    {
                        operation.Phase = GlitchOperationPhase.Failed;
                        operation.Failure = "native_protection_failed";
                        continue;
                    }
                    if (!operation.PendingProtectionRequests.All(ProtectionEstablished))
                        return;
                    operation.Phase = GlitchOperationPhase.Completed;
                    continue;
                }

                if (operation.Phase != GlitchOperationPhase.Ready)
                    return;

                if ((operation.ManualRevisionBundle != null || operation.ProtectionCleanupOnly)
                    && operation.CancelledProtectionRequests.Count > 0
                    && string.IsNullOrWhiteSpace(operation.CancelCommandId))
                {
                    operation.CancelCommandId = CommandId(
                        operation.Id, ++operation.NextStep, "CANCEL");
                    _cancelByCommand[operation.CancelCommandId] = operation;
                    operation.Phase = GlitchOperationPhase.WaitingForProtectionCancellation;
                    commands.Add(new CancelProtectionCommand(
                        operation.CancelCommandId,
                        operation.Account,
                        operation.Instrument,
                        false,
                        null,
                        operation.CancelledProtectionRequests));
                    return;
                }

                if (operation.ManualRevisionBundle != null)
                    ApplyManualProtectionRevision(operation);

                if (operation.CloseToFlat)
                {
                    if (!book.PositionKnown)
                    {
                        if (!operation.PositionRefreshRequested)
                        {
                            operation.PositionRefreshRequested = true;
                            string refreshCommandId = CommandId(
                                operation.Id, ++operation.NextStep, "POSITION");
                            _refreshByCommand[refreshCommandId] = operation;
                            commands.Add(new RefreshPositionCommand(
                                refreshCommandId,
                                operation.Account,
                                operation.Instrument));
                        }
                        return;
                    }
                    operation.RemainingSignedQuantity = -book.SignedPosition;
                }

                if (operation.TargetSignedPosition.HasValue && book.PositionKnown)
                    operation.RemainingSignedQuantity = checked(
                        operation.TargetSignedPosition.Value - book.SignedPosition);

                if (operation.RemainingSignedQuantity == 0)
                {
                    if (operation.DirectProtectionBundle != null
                        && operation.DirectProtectionBundle.RemainingQuantity > 0)
                    {
                        SubmitBundleProtection(
                            operation.DirectProtectionBundle, operation, commands);
                        operation.DirectProtectionBundle = null;
                    }
                    ReprotectResidualBundles(book, operation, commands);
                    operation.Phase = operation.PendingProtectionRequests.Count == 0
                        ? GlitchOperationPhase.Completed
                        : GlitchOperationPhase.WaitingForProtection;
                    continue;
                }

                List<ProtectionRequest> conflicts = ConflictingProtection(book, operation);
                bool needsExternalCancellation = operation.CancelExternalProtection
                    && string.IsNullOrWhiteSpace(operation.CancelCommandId);
                if ((conflicts.Count > 0 || needsExternalCancellation)
                    && string.IsNullOrWhiteSpace(operation.CancelCommandId))
                {
                    operation.CancelledProtectionRequests.AddRange(
                        conflicts.Select(value => value.CommandId));
                    operation.CancelCommandId = CommandId(
                        operation.Id, ++operation.NextStep, "CANCEL");
                    _cancelByCommand[operation.CancelCommandId] = operation;
                    operation.Phase = GlitchOperationPhase.WaitingForProtectionCancellation;
                    commands.Add(new CancelProtectionCommand(
                        operation.CancelCommandId,
                        operation.Account,
                        operation.Instrument,
                        operation.CancelExternalProtection,
                        null,
                        operation.CancelledProtectionRequests));
                    return;
                }

                if (!book.PositionKnown)
                {
                    if (!operation.PositionRefreshRequested)
                    {
                        operation.PositionRefreshRequested = true;
                        string refreshCommandId = CommandId(
                            operation.Id, ++operation.NextStep, "POSITION");
                        _refreshByCommand[refreshCommandId] = operation;
                        commands.Add(new RefreshPositionCommand(
                            refreshCommandId,
                            operation.Account,
                            operation.Instrument));
                    }
                    return;
                }
                int step = PlanTradeStep(operation, book.SignedPosition);
                operation.ActiveCommandId = CommandId(
                    operation.Id, ++operation.NextStep, "TRADE");
                operation.ActiveRequestedSignedQuantity = step;
                operation.ActiveFilledSignedQuantity = 0;
                operation.ActiveExpectedSignedPosition = book.SignedPosition;
                operation.ActivePositionRevision = book.PositionRevision;
                operation.Phase = GlitchOperationPhase.NativePending;
                _tradeByCommand[operation.ActiveCommandId] = operation;
                commands.Add(new SubmitMarketCommand(
                    operation.ActiveCommandId,
                    operation.Purpose,
                    operation.Account,
                    operation.Instrument,
                    step,
                    operation.CauseId,
                    null,
                    operation.RouteId,
                    book.SignedPosition,
                    operation.EntryRangeLow,
                    operation.EntryRangeHigh));
                return;
            }
        }

        private void PumpProtectionChange(
            Book book,
            ProtectionChangeOperation operation,
            ICollection<GlitchCommand> commands)
        {
            if (operation.Phase == GlitchOperationPhase.Accepted)
                operation.Phase = GlitchOperationPhase.Ready;
            if (operation.Phase == GlitchOperationPhase.Ready)
            {
                CaptureProtectionChangeTargets(operation);
                if (operation.ExpectedStops.Count == 0
                    && operation.ExpectedTargets.Count == 0)
                {
                    operation.Phase = GlitchOperationPhase.Failed;
                    operation.Failure = "no_working_protection_matched";
                    return;
                }
                operation.CommandId = CommandId(operation.Id, ++operation.Step, "CHANGE");
                _changeByCommand[operation.CommandId] = operation;
                operation.Phase = GlitchOperationPhase.NativePending;
                commands.Add(new ChangeProtectionCommand(
                    operation.CommandId,
                    operation.Account,
                    operation.Instrument,
                    operation.Updates,
                    operation.TargetCommandIds,
                    operation.HermesIntentId));
                return;
            }
            if (operation.Phase != GlitchOperationPhase.NativePending)
                return;
            if (!ProtectionChangeComplete(operation))
            {
                if (operation.Phase == GlitchOperationPhase.Failed)
                    ClearPendingGeometryRevisions(operation);
                return;
            }
            ApplyProtectionGeometryRevisions(operation);
            operation.Phase = GlitchOperationPhase.Completed;
            foreach (ProtectionChangeOperation follower in operation.Followers)
            {
                _operations[follower.Id] = follower;
                Book followerBook = GetBook(follower.Account, follower.Instrument);
                followerBook.Operations.Enqueue(follower);
                Pump(followerBook, commands);
            }
        }

        private static void ApplyProtectionGeometryRevisions(
            ProtectionChangeOperation operation)
        {
            foreach (ProtectionGeometryRevision revision in operation.GeometryRevisions)
            {
                foreach (HermesProtectionUpdate update in revision.Updates)
                {
                    foreach (ProtectionSlice slice in revision.Bundle.Slices.Where(value =>
                        string.Equals(value.LegId, update.LegId, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (update.StopPrice.HasValue)
                            slice.StopOffset = update.StopPrice.Value - revision.Bundle.EntryPrice;
                        if (update.TargetPrice.HasValue)
                            slice.TargetOffset = update.TargetPrice.Value - revision.Bundle.EntryPrice;
                    }
                }
                if (revision.SourceRevision != null)
                {
                    revision.Bundle.SourceRevision = revision.SourceRevision;
                    revision.Bundle.PendingSourceRevision = null;
                }
            }
        }

        private static void ClearPendingGeometryRevisions(
            ProtectionChangeOperation operation)
        {
            foreach (ProtectionGeometryRevision revision in operation.GeometryRevisions)
            {
                if (revision.SourceRevision != null)
                    revision.Bundle.PendingSourceRevision = null;
            }
        }

        private void CaptureProtectionChangeTargets(ProtectionChangeOperation operation)
        {
            var targetCommands = new HashSet<string>(
                operation.TargetCommandIds, StringComparer.OrdinalIgnoreCase);
            foreach (HermesProtectionUpdate update in operation.Updates)
            {
                foreach (NativeOrderObserved order in _orders.Values.Select(value => value.Value)
                    .Where(value => string.Equals(value.AccountName, operation.Account, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(value.InstrumentName, operation.Instrument, StringComparison.OrdinalIgnoreCase)
                        && targetCommands.Contains(value.CorrelationId)
                        && string.Equals(value.LegId, update.LegId, StringComparison.OrdinalIgnoreCase)
                        && !IsTerminal(value.OrderState)))
                {
                    string key = OrderKey(order.AccountName, order.NativeOrderKey);
                    if (update.StopPrice.HasValue && order.StopPrice.HasValue)
                        operation.ExpectedStops[key] = update.StopPrice.Value;
                    if (update.TargetPrice.HasValue && order.LimitPrice.HasValue)
                        operation.ExpectedTargets[key] = update.TargetPrice.Value;
                }
            }
        }

        private bool ProtectionChangeComplete(ProtectionChangeOperation operation)
        {
            foreach (KeyValuePair<string, decimal> expected in operation.ExpectedStops)
            {
                OrderFact fact;
                if (!_orders.TryGetValue(expected.Key, out fact))
                    return false;
                if (HasNativeError(fact.Value))
                {
                    operation.Phase = GlitchOperationPhase.Failed;
                    operation.Failure = "native_protection_change_rejected";
                    return false;
                }
                if (!IsTerminal(fact.Value.OrderState)
                    && fact.Value.StopPrice != expected.Value)
                    return false;
            }
            foreach (KeyValuePair<string, decimal> expected in operation.ExpectedTargets)
            {
                OrderFact fact;
                if (!_orders.TryGetValue(expected.Key, out fact))
                    return false;
                if (HasNativeError(fact.Value))
                {
                    operation.Phase = GlitchOperationPhase.Failed;
                    operation.Failure = "native_protection_change_rejected";
                    return false;
                }
                if (!IsTerminal(fact.Value.OrderState)
                    && fact.Value.LimitPrice != expected.Value)
                    return false;
            }
            return true;
        }

        private static bool HasNativeError(NativeOrderObserved order)
        {
            return !string.IsNullOrWhiteSpace(order.Error)
                && !string.Equals(order.Error, "NoError", StringComparison.OrdinalIgnoreCase);
        }

        private ProtectionTemplate ProtectOpeningFill(
            Book book,
            TradeOperation operation,
            ExecutionObserved execution,
            int openingQuantity,
            ICollection<GlitchCommand> commands)
        {
            ProtectionTemplate allocated = AllocateProtection(
                operation.RemainingProtection, openingQuantity);
            var bundle = new ProtectionBundle
            {
                Id = operation.Id + "|FILL|" + execution.ExecutionId,
                Account = operation.Account,
                Instrument = operation.Instrument,
                RouteId = operation.RouteId,
                SourceMaster = operation.MirrorsManualMasterProtection
                    ? ResolveRouteMaster(operation.RouteId) : null,
                Direction = Math.Sign(execution.SignedQuantity),
                EntryPrice = execution.Price,
                MirrorsManualMaster = operation.MirrorsManualMasterProtection,
                CreatedSequence = ++_bundleSequence
            };
            if (allocated == null)
            {
                MasterProtectionObserved observed;
                if (bundle.MirrorsManualMaster
                    && !string.IsNullOrWhiteSpace(bundle.SourceMaster)
                    && _masterProtectionSnapshots.TryGetValue(
                        bundle.SourceMaster + "|" + bundle.Instrument, out observed)
                    && observed.Legs.Count > 0
                    && Math.Sign(observed.SignedPosition) == bundle.Direction)
                {
                    ConfigureManualBundle(bundle, observed, openingQuantity);
                    book.Bundles.Add(bundle);
                    SubmitBundleProtection(bundle, operation, commands);
                    return null;
                }
                bundle.Slices.Add(new ProtectionSlice
                {
                    LegId = CompactLegId(bundle.Id, 0),
                    RemainingQuantity = openingQuantity
                });
                book.Bundles.Add(bundle);
                return null;
            }

            foreach (ProtectionLegTemplate target in allocated.Targets)
            {
                bundle.Slices.Add(new ProtectionSlice
                {
                    LegId = target.LegId,
                    RemainingQuantity = target.Quantity,
                    StopOffset = target.StopOffset,
                    TargetOffset = target.TargetOffset
                });
            }
            book.Bundles.Add(bundle);
            SubmitBundleProtection(bundle, operation, commands);
            return allocated;
        }

        private string ResolveRouteMaster(string routeId)
        {
            Route route;
            return !string.IsNullOrWhiteSpace(routeId)
                && _routes.TryGetValue(routeId, out route)
                ? route.Master : null;
        }

        private static void ConfigureManualBundle(
            ProtectionBundle bundle,
            MasterProtectionObserved observed,
            int quantity)
        {
            List<ProtectionLegTemplate> scaled = ScaleManualProtection(
                quantity, bundle.EntryPrice, observed);
            bundle.Slices.Clear();
            bundle.Slices.AddRange(scaled.Select(value => new ProtectionSlice
            {
                LegId = value.LegId,
                RemainingQuantity = value.Quantity,
                StopOffset = value.StopOffset,
                TargetOffset = value.TargetOffset
            }));
            bundle.SourceRevision = ManualProtectionRevision(observed);
            bundle.PendingSourceRevision = null;
        }

        private void SubmitBundleProtection(
            ProtectionBundle bundle,
            TradeOperation owner,
            ICollection<GlitchCommand> commands)
        {
            ProtectionSlice[] active = bundle.Slices
                .Where(value => value.RemainingQuantity > 0
                    && (value.StopOffset.HasValue || value.TargetOffset.HasValue))
                .ToArray();
            if (active.Length == 0)
                return;
            string commandId = CommandId(
                owner.Id, ++owner.NextStep, "PROTECT" + bundle.CreatedSequence);
            var targets = active.Select(value => new ProtectionTarget(
                value.LegId,
                value.RemainingQuantity,
                value.StopOffset.HasValue
                    ? bundle.EntryPrice + value.StopOffset.Value : (decimal?)null,
                value.TargetOffset.HasValue
                    ? bundle.EntryPrice + value.TargetOffset.Value : (decimal?)null)).ToArray();
            int expected = targets.Sum(value =>
                (value.StopPrice.HasValue ? 1 : 0) + (value.Price.HasValue ? 1 : 0));
            var request = new ProtectionRequest
            {
                CommandId = commandId,
                Bundle = bundle,
                Owner = owner,
                ExpectedChildren = expected
            };
            _protectionRequests[commandId] = request;
            _bundleByProtectionCommand[commandId] = bundle;
            bundle.CurrentRequestId = commandId;
            bundle.CancelRequested = false;
            owner.PendingProtectionRequests.Add(commandId);
            commands.Add(new SubmitProtectionCommand(
                commandId,
                bundle.Account,
                bundle.Instrument,
                bundle.Direction * targets.Sum(value => value.Quantity),
                targets[0].StopPrice,
                targets,
                owner.ActiveCommandId ?? owner.Id,
                owner.Purpose == GlitchCommandPurpose.HermesMasterEntry,
                bundle.EntryPrice,
                bundle.RouteId,
                bundle.Id,
                owner.HermesIntentId));
        }

        private void ReprotectResidualBundles(
            Book book,
            TradeOperation operation,
            ICollection<GlitchCommand> commands)
        {
            var cancelled = new HashSet<string>(
                operation.CancelledProtectionRequests, StringComparer.OrdinalIgnoreCase);
            int positiveBudget = book.PositionKnown && book.SignedPosition > 0
                ? book.SignedPosition : 0;
            int negativeBudget = book.PositionKnown && book.SignedPosition < 0
                ? -book.SignedPosition : 0;
            foreach (ProtectionBundle active in book.Bundles.Where(value =>
                !value.Superseded
                && value.RemainingQuantity > 0
                && !cancelled.Contains(value.CurrentRequestId)))
            {
                if (active.Direction > 0)
                    positiveBudget = Math.Max(0, positiveBudget - active.RemainingQuantity);
                else
                    negativeBudget = Math.Max(0, negativeBudget - active.RemainingQuantity);
            }
            foreach (string requestId in operation.CancelledProtectionRequests.ToArray())
            {
                ProtectionRequest prior;
                if (!_protectionRequests.TryGetValue(requestId, out prior))
                    continue;
                ProtectionBundle bundle = prior.Bundle;
                if (bundle.Superseded || bundle.RemainingQuantity == 0)
                    continue;
                bundle.Superseded = true;
                int budget = bundle.Direction > 0 ? positiveBudget : negativeBudget;
                int replacementQuantity = Math.Min(bundle.RemainingQuantity, budget);
                if (replacementQuantity <= 0)
                    continue;
                var replacement = new ProtectionBundle
                {
                    Id = bundle.Id,
                    Account = bundle.Account,
                    Instrument = bundle.Instrument,
                    RouteId = bundle.RouteId,
                    SourceMaster = bundle.SourceMaster,
                    SourceRevision = bundle.SourceRevision,
                    Direction = bundle.Direction,
                    EntryPrice = bundle.EntryPrice,
                    MirrorsManualMaster = bundle.MirrorsManualMaster,
                    CreatedSequence = ++_bundleSequence
                };
                int remaining = replacementQuantity;
                foreach (ProtectionSlice value in bundle.Slices.Where(value =>
                    value.RemainingQuantity > 0))
                {
                    int quantity = Math.Min(value.RemainingQuantity, remaining);
                    replacement.Slices.Add(new ProtectionSlice
                    {
                        LegId = value.LegId,
                        RemainingQuantity = quantity,
                        StopOffset = value.StopOffset,
                        TargetOffset = value.TargetOffset
                    });
                    remaining -= quantity;
                    if (remaining == 0)
                        break;
                }
                book.Bundles.Add(replacement);
                SubmitBundleProtection(replacement, operation, commands);
                if (bundle.Direction > 0)
                    positiveBudget -= replacementQuantity;
                else
                    negativeBudget -= replacementQuantity;
            }
            operation.CancelledProtectionRequests.Clear();
        }

        private void ObserveProtectiveFill(ExecutionObserved execution)
        {
            ProtectionBundle bundle;
            if (!_bundleByProtectionCommand.TryGetValue(
                    execution.ProtectionCorrelationId, out bundle))
                return;
            int remaining = Math.Abs(execution.SignedQuantity);
            foreach (ProtectionSlice slice in bundle.Slices.Where(value => value.RemainingQuantity > 0))
            {
                int consumed = Math.Min(slice.RemainingQuantity, remaining);
                slice.RemainingQuantity -= consumed;
                remaining -= consumed;
                if (remaining == 0)
                    break;
            }
            if (!string.IsNullOrWhiteSpace(bundle.RouteId))
            {
                AllocationEpoch allocation = GetAllocation(bundle.RouteId, bundle.Instrument);
                int remainingCredit = ApplyCreditToWaitingOperations(
                    bundle.RouteId, bundle.Account, bundle.Instrument, execution.SignedQuantity);
                if (remainingCredit != 0)
                    AddSettlementCredit(allocation, remainingCredit);
            }
        }

        private void ObserveSafetyFlattenFill(ExecutionObserved execution)
        {
            int remaining = Math.Abs(execution.SignedQuantity);
            int exposureDirection = -Math.Sign(execution.SignedQuantity);
            Book book = GetBook(execution.AccountName, execution.InstrumentName);
            foreach (ProtectionBundle bundle in book.Bundles
                .Where(value => value.SafetyFlattenPending
                    && value.Direction == exposureDirection
                    && value.RemainingQuantity > 0)
                .OrderBy(value => value.CreatedSequence))
            {
                int bundleBudget = Math.Min(bundle.RemainingQuantity, remaining);
                int consumed = 0;
                foreach (ProtectionSlice slice in bundle.Slices.Where(value =>
                    value.RemainingQuantity > 0))
                {
                    int quantity = Math.Min(slice.RemainingQuantity, bundleBudget - consumed);
                    slice.RemainingQuantity -= quantity;
                    consumed += quantity;
                    if (consumed == bundleBudget)
                        break;
                }
                if (consumed > 0)
                {
                    int signedCredit = Math.Sign(execution.SignedQuantity) * consumed;
                    AllocationEpoch allocation = GetAllocation(bundle.RouteId, bundle.Instrument);
                    int remainingCredit = ApplyCreditToWaitingOperations(
                        bundle.RouteId, bundle.Account, bundle.Instrument, signedCredit);
                    if (remainingCredit != 0)
                        AddSettlementCredit(allocation, remainingCredit);
                    remaining -= consumed;
                }
                if (bundle.RemainingQuantity == 0)
                    bundle.SafetyFlattenPending = false;
                if (remaining == 0)
                    return;
            }
        }

        private static void SettleOwnedExposure(Book book, int exposureDirection, int quantity)
        {
            int remaining = quantity;
            foreach (ProtectionBundle bundle in book.Bundles
                .Where(value => !value.Superseded
                    && value.Direction == exposureDirection
                    && value.RemainingQuantity > 0)
                .OrderBy(value => value.CreatedSequence))
            {
                foreach (ProtectionSlice slice in bundle.Slices.Where(value => value.RemainingQuantity > 0))
                {
                    int consumed = Math.Min(slice.RemainingQuantity, remaining);
                    slice.RemainingQuantity -= consumed;
                    remaining -= consumed;
                    if (remaining == 0)
                        return;
                }
            }
        }

        private void EnqueueManualMasterProtectionCleanup(
            ExecutionObserved execution,
            Book book)
        {
            int exposureDirection = -Math.Sign(execution.SignedQuantity);
            int protectedQuantity = book.Bundles
                .Where(value => !value.Superseded
                    && string.IsNullOrWhiteSpace(value.RouteId)
                    && value.Direction == exposureDirection)
                .Sum(value => value.RemainingQuantity);
            int closingQuantity = Math.Min(
                Math.Abs(execution.SignedQuantity),
                protectedQuantity);
            if (closingQuantity <= 0)
                return;

            ProtectionBundle[] directMasterBundles = book.Bundles
                .Where(value => !value.Superseded
                    && string.IsNullOrWhiteSpace(value.RouteId)
                    && value.Direction == exposureDirection
                    && value.RemainingQuantity > 0)
                .OrderBy(value => value.CreatedSequence)
                .ToArray();
            if (directMasterBundles.Length == 0)
                return;

            string[] liveRequests = directMasterBundles
                .Select(value => value.CurrentRequestId)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Where(value =>
                {
                    ProtectionRequest request;
                    return _protectionRequests.TryGetValue(value, out request)
                        && ProtectionHasLiveOrders(request);
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            SettleOwnedExposure(book, exposureDirection, closingQuantity);
            if (liveRequests.Length == 0)
                return;

            string operationId = "MASTER-CLEANUP|" + execution.AccountName
                + "|" + execution.ExecutionId;
            if (_operations.ContainsKey(operationId))
                return;
            var operation = new TradeOperation
            {
                Id = operationId,
                CauseId = execution.ExecutionId,
                Purpose = GlitchCommandPurpose.Protection,
                Account = execution.AccountName,
                Instrument = execution.InstrumentName,
                ProtectionCleanupOnly = true,
                Phase = GlitchOperationPhase.Accepted
            };
            operation.CancelledProtectionRequests.AddRange(liveRequests);
            _operations[operation.Id] = operation;
            book.Operations.Enqueue(operation);
        }

        private List<ProtectionRequest> ConflictingProtection(
            Book book,
            TradeOperation operation)
        {
            int requestedDirection = Math.Sign(operation.RemainingSignedQuantity);
            return book.Bundles
                .Where(bundle => !bundle.Superseded
                    && bundle.RemainingQuantity > 0
                    && (operation.CloseToFlat
                        || bundle.Direction == -requestedDirection
                        || (book.PositionKnown
                            && (book.SignedPosition == 0
                                || Math.Sign(book.SignedPosition) != bundle.Direction)))
                    && !string.IsNullOrWhiteSpace(bundle.CurrentRequestId))
                .Select(bundle =>
                {
                    ProtectionRequest request;
                    return _protectionRequests.TryGetValue(bundle.CurrentRequestId, out request)
                        ? request : null;
                })
                .Where(request => request != null && ProtectionHasLiveOrders(request))
                .Distinct()
                .ToList();
        }

        private bool CancellationComplete(TradeOperation operation)
        {
            if (operation.CancelExternalProtection && !operation.ExternalCancellationCompleted)
                return false;
            foreach (string requestId in operation.CancelledProtectionRequests)
            {
                ProtectionRequest request;
                if (!_protectionRequests.TryGetValue(requestId, out request)
                    || !ProtectionCancellationComplete(request))
                    return false;
            }
            return true;
        }

        private bool ProtectionCancellationComplete(ProtectionRequest request)
        {
            NativeOrderObserved[] facts = OrdersForCorrelation(request.CommandId).ToArray();
            if (facts.Length < request.ExpectedChildren)
                return false;
            foreach (NativeOrderObserved fact in facts)
            {
                if (!IsTerminal(fact.OrderState))
                    return false;
                if (string.Equals(fact.OrderState, "Filled", StringComparison.OrdinalIgnoreCase))
                {
                    int executed;
                    _executedByOrder.TryGetValue(
                        OrderKey(fact.AccountName, fact.NativeOrderKey), out executed);
                    if (executed < Math.Max(1, fact.Filled))
                        return false;
                }
            }
            return true;
        }

        private bool TradeStepComplete(Book book, TradeOperation operation)
        {
            NativeOrderObserved[] facts = OrdersForCorrelation(operation.ActiveCommandId).ToArray();
            if (facts.Any(value => string.Equals(
                value.OrderState, "Rejected", StringComparison.OrdinalIgnoreCase)))
            {
                operation.Phase = GlitchOperationPhase.Failed;
                operation.Failure = "native_trade_rejected";
                return false;
            }
            NativeOrderObserved terminal = facts.FirstOrDefault(value => IsTerminal(value.OrderState));
            if (terminal == null)
                return false;
            int nativeFilled = Math.Abs(terminal.Filled);
            if (Math.Abs(operation.ActiveFilledSignedQuantity) < nativeFilled)
                return false;
            if (operation.ActiveFilledSignedQuantity != operation.ActiveRequestedSignedQuantity)
            {
                operation.Phase = GlitchOperationPhase.Failed;
                operation.Failure = "native_trade_terminal_before_requested_fill";
                return false;
            }
            if (book == null
                || !book.PositionKnown
                || book.PositionRevision <= operation.ActivePositionRevision)
                return false;
            return true;
        }

        private static int OpeningQuantityFromPrior(
            int priorSignedPosition,
            int signedExecution)
        {
            if (signedExecution == 0)
                return 0;
            if (priorSignedPosition == 0
                || Math.Sign(priorSignedPosition) == Math.Sign(signedExecution))
                return Math.Abs(signedExecution);
            return Math.Max(
                0,
                Math.Abs(signedExecution) - Math.Abs(priorSignedPosition));
        }

        private bool ProtectionEstablished(string commandId)
        {
            ProtectionRequest request;
            if (!_protectionRequests.TryGetValue(commandId, out request)
                || request.RequestFailed
                || request.RequestUnknown)
                return false;
            NativeOrderObserved[] facts = OrdersForCorrelation(commandId).ToArray();
            if (facts.Length < request.ExpectedChildren)
                return false;
            return facts.All(value => IsProtectionEstablishedState(value.OrderState));
        }

        private bool ProtectionFailed(string commandId)
        {
            ProtectionRequest request;
            if (!_protectionRequests.TryGetValue(commandId, out request))
                return true;
            if (request.RequestFailed)
                return true;
            return OrdersForCorrelation(commandId).Any(value =>
                string.Equals(value.OrderState, "Rejected", StringComparison.OrdinalIgnoreCase));
        }

        private bool ProtectionHasLiveOrders(ProtectionRequest request)
        {
            return OrdersForCorrelation(request.CommandId).Any(value => !IsTerminal(value.OrderState));
        }

        private IEnumerable<NativeOrderObserved> OrdersForCorrelation(string commandId)
        {
            return _orders.Values.Select(value => value.Value).Where(value =>
                string.Equals(value.CorrelationId, commandId, StringComparison.OrdinalIgnoreCase));
        }

        private void ObserveManualMasterProtection(
            MasterProtectionObserved observed,
            ICollection<GlitchCommand> commands)
        {
            foreach (Route route in _routes.Values.Where(value => value.Enabled
                && string.Equals(value.Master, observed.AccountName, StringComparison.OrdinalIgnoreCase)))
            {
                Book follower = GetBook(route.Follower, observed.InstrumentName);
                foreach (ProtectionBundle bundle in follower.Bundles
                    .Where(value => value.MirrorsManualMaster
                        && !value.Superseded
                        && value.RemainingQuantity > 0
                        && string.Equals(
                            value.SourceMaster, observed.AccountName, StringComparison.OrdinalIgnoreCase)))
                {
                    string revision = ManualProtectionRevision(observed);
                    if (string.Equals(bundle.SourceRevision, revision, StringComparison.Ordinal)
                        || string.Equals(bundle.PendingSourceRevision, revision, StringComparison.Ordinal))
                        continue;
                    bool remove = observed.Legs.Count == 0
                        || Math.Sign(observed.SignedPosition) != bundle.Direction;
                    ProtectionRequest current = null;
                    bool currentLive = !string.IsNullOrWhiteSpace(bundle.CurrentRequestId)
                        && _protectionRequests.TryGetValue(bundle.CurrentRequestId, out current)
                        && ProtectionHasLiveOrders(current);
                    if (!remove && currentLive)
                    {
                        List<ProtectionLegTemplate> desired = ScaleManualProtection(
                            bundle.RemainingQuantity, bundle.EntryPrice, observed);
                        bool sameStructure = desired.Count == bundle.Slices.Count
                            && desired.All(value => bundle.Slices.Any(slice =>
                                string.Equals(slice.LegId, value.LegId, StringComparison.OrdinalIgnoreCase)
                                && slice.RemainingQuantity == value.Quantity
                                && slice.StopOffset.HasValue == value.StopOffset.HasValue
                                && slice.TargetOffset.HasValue == value.TargetOffset.HasValue));
                        if (sameStructure)
                        {
                            var updates = desired.Select(value => new HermesProtectionUpdate(
                                value.LegId,
                                value.StopOffset.HasValue
                                    ? bundle.EntryPrice + value.StopOffset.Value : (decimal?)null,
                                value.TargetOffset.HasValue
                                    ? bundle.EntryPrice + value.TargetOffset.Value : (decimal?)null))
                                .ToArray();
                            ProtectionChangeOperation change = BuildProtectionChangeOperation(
                                bundle.Id + "|MANUAL-CHANGE|" + revision,
                                bundle.Account,
                                bundle.Instrument,
                                updates,
                                new[] { current.CommandId },
                                follower,
                                revision);
                            bundle.PendingSourceRevision = revision;
                            _operations[change.Id] = change;
                            follower.Operations.Enqueue(change);
                            continue;
                        }
                    }
                    var owner = new TradeOperation
                    {
                        Id = bundle.Id + "|MANUAL|" + revision,
                        Account = bundle.Account,
                        Instrument = bundle.Instrument,
                        Purpose = GlitchCommandPurpose.Protection,
                        Phase = GlitchOperationPhase.Accepted,
                        ManualRevisionBundle = bundle,
                        ManualRevision = observed,
                        RemoveManualProtection = remove
                    };
                    bundle.PendingSourceRevision = revision;
                    if (currentLive)
                        owner.CancelledProtectionRequests.Add(current.CommandId);
                    _operations[owner.Id] = owner;
                    follower.Operations.Enqueue(owner);
                }
            }
        }

        private static void ApplyManualProtectionRevision(TradeOperation operation)
        {
            ProtectionBundle bundle = operation.ManualRevisionBundle;
            MasterProtectionObserved observed = operation.ManualRevision;
            string revision = ManualProtectionRevision(observed);
            if (operation.RemoveManualProtection || bundle.RemainingQuantity == 0)
            {
                bundle.Superseded = true;
                bundle.SourceRevision = revision;
                bundle.PendingSourceRevision = null;
                operation.CancelledProtectionRequests.Clear();
            }
            else
            {
                ConfigureManualBundle(bundle, observed, bundle.RemainingQuantity);
                bundle.SourceRevision = revision;
                if (operation.CancelledProtectionRequests.Count == 0)
                    operation.DirectProtectionBundle = bundle;
            }
            operation.ManualRevisionBundle = null;
            operation.ManualRevision = null;
        }

        private static List<ProtectionLegTemplate> ScaleManualProtection(
            int followerQuantity,
            decimal followerEntryPrice,
            MasterProtectionObserved observed)
        {
            int sourceTotal = observed.Legs.Sum(value => value.Quantity);
            int sourceCumulative = 0;
            int emitted = 0;
            var result = new List<ProtectionLegTemplate>();
            foreach (MasterProtectionLeg source in observed.Legs)
            {
                sourceCumulative += source.Quantity;
                int target = RoundQuantity(((decimal)sourceCumulative / sourceTotal) * followerQuantity);
                int quantity = target - emitted;
                emitted = target;
                if (quantity <= 0)
                    continue;
                result.Add(new ProtectionLegTemplate(
                    source.LegId,
                    quantity,
                    source.StopPrice.HasValue
                        ? RoundToTick(
                            followerEntryPrice
                                + source.StopPrice.Value - observed.ReferencePrice,
                            observed.TickSize) - followerEntryPrice
                        : (decimal?)null,
                    source.TargetPrice.HasValue
                        ? RoundToTick(
                            followerEntryPrice
                                + source.TargetPrice.Value - observed.ReferencePrice,
                            observed.TickSize) - followerEntryPrice
                        : (decimal?)null));
            }
            return result;
        }

        private static string ManualProtectionRevision(MasterProtectionObserved observed)
        {
            string revision = observed.Legs.Count == 0
                ? "NONE|" + observed.RevisionId
                : observed.RevisionId;
            if (observed.TickSize <= 0)
                return revision;
            return revision
                + "|REF=" + observed.ReferencePrice.ToString(CultureInfo.InvariantCulture)
                + "|TICK=" + observed.TickSize.ToString(CultureInfo.InvariantCulture);
        }

        private static decimal RoundToTick(decimal price, decimal tickSize)
        {
            if (tickSize <= 0)
                return price;
            return decimal.Round(
                price / tickSize, 0, MidpointRounding.AwayFromZero) * tickSize;
        }

        private int ApplyCreditToWaitingOperations(
            string routeId,
            string account,
            string instrument,
            int signedCredit)
        {
            int remaining = Math.Abs(signedCredit);
            int sign = Math.Sign(signedCredit);
            Book book = GetBook(account, instrument);
            foreach (TradeOperation operation in book.Operations.OfType<TradeOperation>().Where(value =>
                string.Equals(value.RouteId, routeId, StringComparison.OrdinalIgnoreCase)
                && !value.TargetSignedPosition.HasValue
                && value.RemainingSignedQuantity != 0
                && Math.Sign(value.RemainingSignedQuantity) == sign
                && (value.Phase == GlitchOperationPhase.Accepted
                    || value.Phase == GlitchOperationPhase.Ready
                    || value.Phase == GlitchOperationPhase.WaitingForProtectionCancellation)))
            {
                int consumed = Math.Min(Math.Abs(operation.RemainingSignedQuantity), remaining);
                operation.RemainingSignedQuantity -= sign * consumed;
                remaining -= consumed;
                if (remaining == 0)
                    return 0;
            }
            return sign * remaining;
        }

        private static int PlanTradeStep(TradeOperation operation, int signedPosition)
        {
            int remainingSignedQuantity = operation.TargetSignedPosition.HasValue
                ? checked(operation.TargetSignedPosition.Value - signedPosition)
                : operation.RemainingSignedQuantity;
            if (operation.Purpose == GlitchCommandPurpose.GroupSynchronization)
            {
                if (operation.MaxOpeningStepQuantity > 0
                    && IsOpeningIncrease(signedPosition, remainingSignedQuantity)
                    && Math.Abs(remainingSignedQuantity) > operation.MaxOpeningStepQuantity)
                {
                    return Math.Sign(remainingSignedQuantity)
                        * operation.MaxOpeningStepQuantity;
                }
                return remainingSignedQuantity;
            }
            if (operation.Purpose == GlitchCommandPurpose.Replication)
                return remainingSignedQuantity;
            if (signedPosition == 0
                || Math.Sign(signedPosition) == Math.Sign(remainingSignedQuantity))
                return remainingSignedQuantity;
            return Math.Sign(remainingSignedQuantity) * Math.Min(
                Math.Abs(signedPosition), Math.Abs(remainingSignedQuantity));
        }

        private static bool IsOpeningIncrease(Book book, int signedQuantity)
        {
            return !book.PositionKnown || IsOpeningIncrease(book.SignedPosition, signedQuantity);
        }

        private static bool IsOpeningIncrease(int signedPosition, int signedQuantity)
        {
            return signedPosition == 0
                || Math.Sign(signedPosition) == Math.Sign(signedQuantity);
        }

        private static ProtectionTemplate AllocateProtection(
            IList<ProtectionLegTemplate> remaining,
            int quantity)
        {
            if (quantity <= 0 || remaining.Count == 0)
                return null;
            int needed = quantity;
            var allocated = new List<ProtectionLegTemplate>();
            while (needed > 0 && remaining.Count > 0)
            {
                ProtectionLegTemplate first = remaining[0];
                int take = Math.Min(first.Quantity, needed);
                allocated.Add(new ProtectionLegTemplate(
                    first.LegId, take, first.StopOffset, first.TargetOffset));
                needed -= take;
                if (take == first.Quantity)
                    remaining.RemoveAt(0);
                else
                    remaining[0] = new ProtectionLegTemplate(
                        first.LegId,
                        first.Quantity - take,
                        first.StopOffset,
                        first.TargetOffset);
            }
            if (needed != 0)
                throw new InvalidOperationException("Opening fills exceeded the declared protection quantity.");
            return new ProtectionTemplate(allocated[0].StopOffset, allocated);
        }

        private static ProtectionTemplate ScaleProtection(ProtectionTemplate source, int quantity)
        {
            if (source == null || quantity <= 0)
                return null;
            int cumulative = 0;
            int emitted = 0;
            var result = new List<ProtectionLegTemplate>();
            foreach (ProtectionLegTemplate leg in source.Targets)
            {
                cumulative += leg.Quantity;
                int target = RoundQuantity(((decimal)cumulative / source.Quantity) * quantity);
                int legQuantity = target - emitted;
                emitted = target;
                if (legQuantity > 0)
                    result.Add(new ProtectionLegTemplate(
                        leg.LegId, legQuantity, leg.StopOffset, leg.TargetOffset));
            }
            return new ProtectionTemplate(result[0].StopOffset, result);
        }

        private static ProtectionTemplate SliceProtection(
            ProtectionTemplate source,
            int offset,
            int quantity)
        {
            if (source == null)
                return null;
            int skip = offset;
            int needed = quantity;
            var result = new List<ProtectionLegTemplate>();
            foreach (ProtectionLegTemplate leg in source.Targets)
            {
                if (skip >= leg.Quantity)
                {
                    skip -= leg.Quantity;
                    continue;
                }
                int take = Math.Min(leg.Quantity - skip, needed);
                result.Add(new ProtectionLegTemplate(
                    leg.LegId, take, leg.StopOffset, leg.TargetOffset));
                needed -= take;
                skip = 0;
                if (needed == 0)
                    break;
            }
            if (needed != 0)
                throw new InvalidOperationException("Protection slice did not equal the requested quantity.");
            return new ProtectionTemplate(result[0].StopOffset, result);
        }

        private static int ConsumeSettlementCredit(AllocationEpoch allocation, int signedQuantity)
        {
            if (signedQuantity > 0 && allocation.PositiveSettlementCredit > 0)
            {
                int consumed = Math.Min(signedQuantity, allocation.PositiveSettlementCredit);
                allocation.PositiveSettlementCredit -= consumed;
                return signedQuantity - consumed;
            }
            if (signedQuantity < 0 && allocation.NegativeSettlementCredit > 0)
            {
                int consumed = Math.Min(-signedQuantity, allocation.NegativeSettlementCredit);
                allocation.NegativeSettlementCredit -= consumed;
                return signedQuantity + consumed;
            }
            return signedQuantity;
        }

        private static void AddSettlementCredit(AllocationEpoch allocation, int signedQuantity)
        {
            if (signedQuantity > 0)
                allocation.PositiveSettlementCredit += signedQuantity;
            else
                allocation.NegativeSettlementCredit += -signedQuantity;
        }

        private AllocationEpoch GetAllocation(string routeId, string instrument)
        {
            string key = routeId + "|" + instrument;
            AllocationEpoch allocation;
            if (!_allocations.TryGetValue(key, out allocation))
            {
                allocation = new AllocationEpoch();
                _allocations[key] = allocation;
            }
            return allocation;
        }

        private void ResetAllocation(string routeId)
        {
            string prefix = routeId + "|";
            foreach (string key in _allocations.Keys.Where(value =>
                value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray())
                _allocations.Remove(key);
        }

        private Book GetBook(string account, string instrument)
        {
            string key = account + "|" + instrument;
            Book book;
            if (!_books.TryGetValue(key, out book))
            {
                book = new Book { Account = account, Instrument = instrument };
                _books[key] = book;
            }
            return book;
        }

        private IEnumerable<string> KnownInstruments(Route route)
        {
            return _books.Values.Where(book =>
                    string.Equals(book.Account, route.Master, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(book.Account, route.Follower, StringComparison.OrdinalIgnoreCase))
                .Select(book => book.Instrument)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
        }

        private string NextLocalRequest()
        {
            return (++_localRequestSequence).ToString("D8", CultureInfo.InvariantCulture);
        }

        private static string CommandId(string operationId, int step, string role)
        {
            return "G" + HashHex(
                operationId + "|" + step.ToString(CultureInfo.InvariantCulture) + "|" + role,
                20);
        }

        private static string CompactLegId(string value, int index)
        {
            return "L" + HashHex(
                value + "|" + (index + 1).ToString(CultureInfo.InvariantCulture) + "|LEG",
                15);
        }

        private static string HashHex(string value, int characters)
        {
            using (SHA256 sha = SHA256.Create())
            {
                string hex = string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty))
                    .Select(item => item.ToString("X2", CultureInfo.InvariantCulture)));
                return hex.Substring(0, characters);
            }
        }

        private static string OrderKey(string account, string nativeOrderKey)
        {
            return (account ?? string.Empty) + "|" + (nativeOrderKey ?? string.Empty);
        }

        private static bool IsTerminal(string state)
        {
            return string.Equals(state, "Cancelled", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state, "Filled", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state, "Rejected", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsProtectionEstablishedState(string state)
        {
            return string.Equals(state, "Submitted", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state, "Accepted", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state, "Working", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state, "PartFilled", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state, "Filled", StringComparison.OrdinalIgnoreCase);
        }

        private static int RoundQuantity(decimal quantity)
        {
            return decimal.ToInt32(decimal.Round(quantity, 0, MidpointRounding.AwayFromZero));
        }
    }
}
