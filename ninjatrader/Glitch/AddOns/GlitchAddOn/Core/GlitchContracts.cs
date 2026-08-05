using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Glitch.Core
{
    public enum GlitchExecutionOrigin
    {
        External,
        ExternalProtection,
        HermesMaster,
        HermesMasterProtection,
        GlitchReplication,
        GlitchSynchronization,
        GlitchProtection,
        GlitchFlatten
    }

    public enum GlitchCommandPurpose
    {
        Replication,
        GroupSynchronization,
        HermesMasterEntry,
        HermesMasterExit,
        Protection,
        UserFlatten,
        Observation
    }

    public enum GlitchNativeOperation
    {
        Add,
        Update,
        Remove,
        Unknown
    }

    public enum GlitchOperationPhase
    {
        Accepted,
        WaitingForProtectionCancellation,
        Ready,
        NativeRequestStarted,
        NativePending,
        WaitingForProtection,
        Completed,
        Failed,
        Unknown,
        Superseded
    }

    public abstract class GlitchInput
    {
    }

    public interface IGlitchHermesIntent
    {
        string IntentId { get; }
        string ContentFingerprint { get; }
        string ReceiptStatus { get; }
        string ReceiptCode { get; }
        string ReceiptMessage { get; }
    }

    public static class GlitchHermesIntentContent
    {
        public static string Resolve(string suppliedFingerprint, params string[] values)
        {
            if (!string.IsNullOrWhiteSpace(suppliedFingerprint))
                return suppliedFingerprint.Trim();
            return Hash(string.Join("\u001f", values ?? new string[0]));
        }

        public static string Hash(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                var result = new StringBuilder(digest.Length * 2);
                foreach (byte item in digest)
                    result.Append(item.ToString("X2", CultureInfo.InvariantCulture));
                return result.ToString();
            }
        }
    }

    public sealed class GlitchAccountInstrumentScope
    {
        public GlitchAccountInstrumentScope(string accountName, string instrumentName)
        {
            AccountName = accountName ?? string.Empty;
            InstrumentName = instrumentName ?? string.Empty;
        }

        public string AccountName { get; }
        public string InstrumentName { get; }
    }

    /// <summary>
    /// Exact lifecycle evidence from NinjaTrader. This input updates evidence
    /// only; it never authorizes a native mutation. A separate ExecutionObserved
    /// is emitted only for a representable Add operation.
    /// </summary>
    public sealed class ExecutionLifecycleObserved : GlitchInput
    {
        public ExecutionLifecycleObserved(
            GlitchNativeOperation operation,
            string executionId,
            string accountName,
            string instrumentName,
            string nativeOrderKey,
            int signedQuantity,
            decimal price,
            bool representable,
            string evidenceGap,
            string correlationId = null)
        {
            Operation = operation;
            ExecutionId = executionId ?? string.Empty;
            AccountName = accountName ?? string.Empty;
            InstrumentName = instrumentName ?? string.Empty;
            NativeOrderKey = nativeOrderKey ?? string.Empty;
            SignedQuantity = signedQuantity;
            Price = price;
            Representable = representable;
            EvidenceGap = evidenceGap ?? string.Empty;
            CorrelationId = correlationId ?? string.Empty;
        }

        public GlitchNativeOperation Operation { get; }
        public string ExecutionId { get; }
        public string AccountName { get; }
        public string InstrumentName { get; }
        public string NativeOrderKey { get; }
        public int SignedQuantity { get; }
        public decimal Price { get; }
        public bool Representable { get; }
        public string EvidenceGap { get; }
        public string CorrelationId { get; }
    }

    /// <summary>
    /// Exact native order-state evidence. Order state is deliberately retained
    /// as the NinjaTrader value instead of being collapsed into a boolean.
    /// </summary>
    public sealed class NativeOrderObserved : GlitchInput
    {
        public NativeOrderObserved(
            string accountName,
            string instrumentName,
            string nativeOrderId,
            string signalName,
            string orderState,
            string error,
            string comment,
            string oco,
            string correlationId)
            : this(
                accountName,
                instrumentName,
                !string.IsNullOrWhiteSpace(signalName) ? signalName : nativeOrderId,
                nativeOrderId,
                signalName,
                orderState,
                0,
                0,
                null,
                null,
                error,
                comment,
                oco,
                correlationId,
                string.Empty,
                string.Empty)
        {
        }

        public NativeOrderObserved(
            string accountName,
            string instrumentName,
            string nativeOrderKey,
            string nativeOrderId,
            string signalName,
            string orderState,
            int quantity,
            int filled,
            decimal? stopPrice,
            decimal? limitPrice,
            string error,
            string comment,
            string oco,
            string correlationId,
            string childRole,
            string legId = null)
        {
            AccountName = accountName ?? string.Empty;
            InstrumentName = instrumentName ?? string.Empty;
            NativeOrderKey = nativeOrderKey ?? string.Empty;
            NativeOrderId = nativeOrderId ?? string.Empty;
            SignalName = signalName ?? string.Empty;
            OrderState = orderState ?? "Unknown";
            Quantity = quantity;
            Filled = filled;
            StopPrice = stopPrice;
            LimitPrice = limitPrice;
            Error = error ?? string.Empty;
            Comment = comment ?? string.Empty;
            Oco = oco ?? string.Empty;
            CorrelationId = correlationId ?? string.Empty;
            ChildRole = childRole ?? string.Empty;
            LegId = legId ?? string.Empty;
        }

        public string AccountName { get; }
        public string InstrumentName { get; }
        public string NativeOrderKey { get; }
        public string NativeOrderId { get; }
        public string SignalName { get; }
        public string OrderState { get; }
        public int Quantity { get; }
        public int Filled { get; }
        public decimal? StopPrice { get; }
        public decimal? LimitPrice { get; }
        public string Error { get; }
        public string Comment { get; }
        public string Oco { get; }
        public string CorrelationId { get; }
        public string ChildRole { get; }
        public string LegId { get; }
    }

    public sealed class NativeRequestFailedObserved : GlitchInput
    {
        public NativeRequestFailedObserved(string commandId, string error)
        {
            if (string.IsNullOrWhiteSpace(commandId))
                throw new ArgumentException("Command identity is required.", nameof(commandId));
            CommandId = commandId.Trim();
            Error = error ?? string.Empty;
        }

        public string CommandId { get; }
        public string Error { get; }
    }

    /// <summary>
    /// A native mutator boundary was crossed, but current native evidence cannot
    /// prove whether the request was accepted. Unknown never authorizes a retry.
    /// </summary>
    public sealed class NativeRequestUnknownObserved : GlitchInput
    {
        public NativeRequestUnknownObserved(string commandId, string evidenceGap)
        {
            if (string.IsNullOrWhiteSpace(commandId))
                throw new ArgumentException("Command identity is required.", nameof(commandId));
            CommandId = commandId.Trim();
            EvidenceGap = evidenceGap ?? string.Empty;
        }

        public string CommandId { get; }
        public string EvidenceGap { get; }
    }

    public sealed class NativePlanStaleObserved : GlitchInput
    {
        public NativePlanStaleObserved(
            string commandId,
            string accountName,
            string instrumentName,
            int signedPosition)
        {
            if (string.IsNullOrWhiteSpace(commandId))
                throw new ArgumentException("Command identity is required.", nameof(commandId));
            CommandId = commandId.Trim();
            AccountName = accountName ?? string.Empty;
            InstrumentName = instrumentName ?? string.Empty;
            SignedPosition = signedPosition;
        }

        public string CommandId { get; }
        public string AccountName { get; }
        public string InstrumentName { get; }
        public int SignedPosition { get; }
    }

    public sealed class AccountStatusObserved : GlitchInput
    {
        public AccountStatusObserved(string accountName, string previousStatus, string status)
        {
            AccountName = accountName ?? string.Empty;
            PreviousStatus = previousStatus ?? "Unknown";
            Status = status ?? "Unknown";
        }

        public string AccountName { get; }
        public string PreviousStatus { get; }
        public string Status { get; }
    }

    public sealed class PositionObserved : GlitchInput
    {
        public PositionObserved(
            string accountName,
            string instrumentName,
            int signedQuantity,
            long revision = 0)
        {
            AccountName = Require(accountName, nameof(accountName));
            InstrumentName = Require(instrumentName, nameof(instrumentName));
            SignedQuantity = signedQuantity;
            Revision = revision;
        }

        public string AccountName { get; }
        public string InstrumentName { get; }
        public int SignedQuantity { get; }
        public long Revision { get; }

        private static string Require(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A value is required.", name);
            return value.Trim();
        }
    }

    public sealed class ExecutionObserved : GlitchInput
    {
        public ExecutionObserved(
            string executionId,
            string accountName,
            string instrumentName,
            int signedQuantity,
            decimal price,
            GlitchExecutionOrigin origin,
            string correlationId,
            string protectionCorrelationId = null,
            bool opensExposure = false)
            : this(
                executionId,
                accountName,
                instrumentName,
                signedQuantity,
                price,
                origin,
                correlationId,
                protectionCorrelationId,
                opensExposure ? Math.Abs(signedQuantity) : 0,
                int.MinValue,
                string.Empty,
                false)
        {
        }

        public ExecutionObserved(
            string executionId,
            string accountName,
            string instrumentName,
            int signedQuantity,
            decimal price,
            GlitchExecutionOrigin origin,
            string correlationId,
            string protectionCorrelationId,
            int openingQuantity,
            int postPosition,
            string nativeOrderKey,
            bool isBaseline)
        {
            if (string.IsNullOrWhiteSpace(executionId))
                throw new ArgumentException("Execution identity is required.", nameof(executionId));
            if (string.IsNullOrWhiteSpace(accountName))
                throw new ArgumentException("Account name is required.", nameof(accountName));
            if (string.IsNullOrWhiteSpace(instrumentName))
                throw new ArgumentException("Instrument name is required.", nameof(instrumentName));
            if (signedQuantity == 0)
                throw new ArgumentOutOfRangeException(nameof(signedQuantity));
            if (price <= 0)
                throw new ArgumentOutOfRangeException(nameof(price));

            ExecutionId = executionId.Trim();
            AccountName = accountName.Trim();
            InstrumentName = instrumentName.Trim();
            SignedQuantity = signedQuantity;
            Price = price;
            Origin = origin;
            CorrelationId = correlationId;
            ProtectionCorrelationId = protectionCorrelationId;
            OpeningQuantity = Math.Max(0, Math.Min(Math.Abs(signedQuantity), openingQuantity));
            PostPosition = postPosition;
            NativeOrderKey = nativeOrderKey ?? string.Empty;
            IsBaseline = isBaseline;
        }

        public string ExecutionId { get; }
        public string AccountName { get; }
        public string InstrumentName { get; }
        public int SignedQuantity { get; }
        public decimal Price { get; }
        public GlitchExecutionOrigin Origin { get; }
        public string CorrelationId { get; }
        public string ProtectionCorrelationId { get; }
        public int OpeningQuantity { get; }
        public bool OpensExposure => OpeningQuantity > 0;
        public int PostPosition { get; }
        public string NativeOrderKey { get; }
        public bool IsBaseline { get; }
    }

    public sealed class MasterProtectionLeg
    {
        public MasterProtectionLeg(
            string legId,
            int quantity,
            decimal? stopPrice,
            decimal? targetPrice)
        {
            if (string.IsNullOrWhiteSpace(legId))
                throw new ArgumentException("Leg identity is required.", nameof(legId));
            if (quantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(quantity));
            if (!stopPrice.HasValue && !targetPrice.HasValue)
                throw new ArgumentException("A stop or target price is required.");
            if ((stopPrice.HasValue && stopPrice.Value <= 0)
                || (targetPrice.HasValue && targetPrice.Value <= 0))
                throw new ArgumentOutOfRangeException("Protection prices must be positive.");
            LegId = legId.Trim();
            Quantity = quantity;
            StopPrice = stopPrice;
            TargetPrice = targetPrice;
        }

        public string LegId { get; }
        public int Quantity { get; }
        public decimal? StopPrice { get; }
        public decimal? TargetPrice { get; }
    }

    /// <summary>
    /// Immutable native observation of a complete external master bracket. An
    /// empty leg set means no attributable complete bracket is currently working.
    /// </summary>
    public sealed class MasterProtectionObserved : GlitchInput
    {
        public MasterProtectionObserved(
            string accountName,
            string instrumentName,
            int signedPosition,
            decimal referencePrice,
            string revisionId,
            IEnumerable<MasterProtectionLeg> legs)
        {
            if (string.IsNullOrWhiteSpace(accountName))
                throw new ArgumentException("Account name is required.", nameof(accountName));
            if (string.IsNullOrWhiteSpace(instrumentName))
                throw new ArgumentException("Instrument name is required.", nameof(instrumentName));
            MasterProtectionLeg[] legArray = (legs ?? Enumerable.Empty<MasterProtectionLeg>()).ToArray();
            if (legArray.Length > 0 && (signedPosition == 0 || referencePrice <= 0))
                throw new ArgumentException("A working bracket requires a native position reference.");
            AccountName = accountName.Trim();
            InstrumentName = instrumentName.Trim();
            SignedPosition = signedPosition;
            ReferencePrice = referencePrice;
            RevisionId = revisionId ?? string.Empty;
            Legs = legArray;
        }

        public string AccountName { get; }
        public string InstrumentName { get; }
        public int SignedPosition { get; }
        public decimal ReferencePrice { get; }
        public string RevisionId { get; }
        public IReadOnlyList<MasterProtectionLeg> Legs { get; }
    }

    public sealed class ProtectionCancellationCompletedObserved : GlitchInput
    {
        public ProtectionCancellationCompletedObserved(string commandId)
        {
            if (string.IsNullOrWhiteSpace(commandId))
                throw new ArgumentException("Command identity is required.", nameof(commandId));
            CommandId = commandId.Trim();
        }

        public string CommandId { get; }
    }

    public sealed class FlattenCompletedObserved : GlitchInput
    {
        public FlattenCompletedObserved(string commandId, string accountName)
        {
            if (string.IsNullOrWhiteSpace(commandId))
                throw new ArgumentException("Command identity is required.", nameof(commandId));
            CommandId = commandId.Trim();
            AccountName = accountName ?? string.Empty;
        }

        public string CommandId { get; }
        public string AccountName { get; }
    }

    /// <summary>Serialized barrier posted after the complete startup snapshot.</summary>
    public sealed class RecoveryCompletedObserved : GlitchInput
    {
    }

    public sealed class RouteConfigurationItem
    {
        public RouteConfigurationItem(
            string routeId,
            string masterAccount,
            string followerAccount,
            decimal ratio,
            bool enabled)
        {
            if (string.IsNullOrWhiteSpace(routeId))
                throw new ArgumentException("Route identity is required.", nameof(routeId));
            if (string.IsNullOrWhiteSpace(masterAccount))
                throw new ArgumentException("Master account is required.", nameof(masterAccount));
            if (string.IsNullOrWhiteSpace(followerAccount))
                throw new ArgumentException("Follower account is required.", nameof(followerAccount));
            if (string.Equals(masterAccount.Trim(), followerAccount.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Master and follower must be different accounts.");
            if (ratio < 0 || ratio > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(ratio));

            RouteId = routeId.Trim();
            MasterAccount = masterAccount.Trim();
            FollowerAccount = followerAccount.Trim();
            Ratio = ratio;
            Enabled = enabled;
        }

        public string RouteId { get; }
        public string MasterAccount { get; }
        public string FollowerAccount { get; }
        public decimal Ratio { get; }
        public bool Enabled { get; }
    }

    /// <summary>
    /// One complete route snapshot. Configuration replacement and any requested
    /// synchronization are reduced atomically, after the complete new topology
    /// has been validated.
    /// </summary>
    public sealed class RouteConfigurationChanged : GlitchInput
    {
        public RouteConfigurationChanged(
            bool replicationEnabled,
            IEnumerable<RouteConfigurationItem> routes,
            IEnumerable<string> synchronizeRouteIds = null)
        {
            RouteConfigurationItem[] routeArray =
                (routes ?? Enumerable.Empty<RouteConfigurationItem>()).ToArray();
            if (routeArray.Any(value => value == null))
                throw new ArgumentException("Route configuration cannot contain null entries.", nameof(routes));
            if (routeArray.GroupBy(value => value.RouteId, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1))
                throw new ArgumentException("Route identities must be unique.", nameof(routes));

            RouteConfigurationItem[] effective = routeArray
                .Where(value => value.Enabled)
                .ToArray();
            if (effective.GroupBy(value => value.FollowerAccount, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Select(value => value.MasterAccount)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
                throw new ArgumentException("A follower can have only one enabled master.", nameof(routes));
            var masters = new HashSet<string>(
                effective.Select(value => value.MasterAccount), StringComparer.OrdinalIgnoreCase);
            if (effective.Any(value => masters.Contains(value.FollowerAccount)))
                throw new ArgumentException("Enabled replication topology must have one level.", nameof(routes));

            string[] sync = (synchronizeRouteIds ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var routeIds = new HashSet<string>(
                routeArray.Select(value => value.RouteId), StringComparer.OrdinalIgnoreCase);
            if (sync.Any(value => !routeIds.Contains(value)))
                throw new ArgumentException("Synchronization must target a configured route.", nameof(synchronizeRouteIds));

            ReplicationEnabled = replicationEnabled;
            Routes = routeArray;
            SynchronizeRouteIds = sync;
        }

        public bool ReplicationEnabled { get; }
        public IReadOnlyList<RouteConfigurationItem> Routes { get; }
        public IReadOnlyList<string> SynchronizeRouteIds { get; }
    }

    public sealed class RouteSynchronizationRequested : GlitchInput
    {
        public RouteSynchronizationRequested(string routeId)
        {
            if (string.IsNullOrWhiteSpace(routeId))
                throw new ArgumentException("Route identity is required.", nameof(routeId));
            RouteId = routeId.Trim();
        }

        public string RouteId { get; }
    }

    public sealed class ReplicationQuantityLimitChanged : GlitchInput
    {
        public ReplicationQuantityLimitChanged(string accountName, int? maxOrderQuantity)
        {
            if (string.IsNullOrWhiteSpace(accountName))
                throw new ArgumentException("Account name is required.", nameof(accountName));
            if (maxOrderQuantity.HasValue && maxOrderQuantity.Value <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxOrderQuantity));
            AccountName = accountName.Trim();
            MaxOrderQuantity = maxOrderQuantity;
        }

        public string AccountName { get; }
        public int? MaxOrderQuantity { get; }
    }

    public sealed class FlattenAccountRequested : GlitchInput
    {
        public FlattenAccountRequested(string requestId, string accountName, string reason)
        {
            if (string.IsNullOrWhiteSpace(requestId))
                throw new ArgumentException("Request identity is required.", nameof(requestId));
            if (string.IsNullOrWhiteSpace(accountName))
                throw new ArgumentException("Account name is required.", nameof(accountName));
            RequestId = requestId.Trim();
            AccountName = accountName.Trim();
            Reason = reason ?? string.Empty;
        }

        public string RequestId { get; }
        public string AccountName { get; }
        public string Reason { get; }
    }

    public sealed class HermesTarget
    {
        public HermesTarget(int quantity, decimal price)
            : this(quantity, 0, price)
        {
        }

        public HermesTarget(int quantity, decimal stopPrice, decimal price)
        {
            if (quantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(quantity));
            if (price <= 0)
                throw new ArgumentOutOfRangeException(nameof(price));
            Quantity = quantity;
            StopPrice = stopPrice;
            Price = price;
        }

        public int Quantity { get; }
        public decimal StopPrice { get; }
        public decimal Price { get; }
    }

    public sealed class HermesEntryRequested : GlitchInput, IGlitchHermesIntent
    {
        public HermesEntryRequested(
            string intentId,
            string accountName,
            string instrumentName,
            int signedQuantity,
            decimal decisionReferencePrice,
            decimal stopPrice,
            IEnumerable<HermesTarget> targets,
            string contentFingerprint = null,
            string receiptStatus = "pending",
            string receiptCode = "intent_dispatched",
            string receiptMessage = null)
        {
            if (string.IsNullOrWhiteSpace(intentId))
                throw new ArgumentException("Intent identity is required.", nameof(intentId));
            if (string.IsNullOrWhiteSpace(accountName))
                throw new ArgumentException("Account name is required.", nameof(accountName));
            if (string.IsNullOrWhiteSpace(instrumentName))
                throw new ArgumentException("Instrument name is required.", nameof(instrumentName));
            if (signedQuantity == 0)
                throw new ArgumentOutOfRangeException(nameof(signedQuantity));
            if (decisionReferencePrice <= 0)
                throw new ArgumentOutOfRangeException(nameof(decisionReferencePrice));
            if (stopPrice <= 0)
                throw new ArgumentOutOfRangeException(nameof(stopPrice));

            HermesTarget[] targetArray = (targets ?? throw new ArgumentNullException(nameof(targets))).ToArray();
            if (targetArray.Length == 0 || targetArray.Length > 3)
                throw new ArgumentException("Hermes entries require one to three target legs.", nameof(targets));
            if (targetArray.Sum(value => value.Quantity) != Math.Abs(signedQuantity))
                throw new ArgumentException("Hermes target quantities must equal entry quantity.", nameof(targets));

            IntentId = intentId.Trim();
            AccountName = accountName.Trim();
            InstrumentName = instrumentName.Trim();
            SignedQuantity = signedQuantity;
            DecisionReferencePrice = decisionReferencePrice;
            StopPrice = stopPrice;
            Targets = targetArray;
            ContentFingerprint = GlitchHermesIntentContent.Resolve(
                contentFingerprint,
                IntentId,
                AccountName,
                InstrumentName,
                SignedQuantity.ToString(CultureInfo.InvariantCulture),
                DecisionReferencePrice.ToString(CultureInfo.InvariantCulture),
                StopPrice.ToString(CultureInfo.InvariantCulture),
                string.Join("|", Targets.Select(value =>
                    value.Quantity.ToString(CultureInfo.InvariantCulture) + ":"
                    + value.StopPrice.ToString(CultureInfo.InvariantCulture) + ":"
                    + value.Price.ToString(CultureInfo.InvariantCulture))));
            ReceiptStatus = string.IsNullOrWhiteSpace(receiptStatus) ? "pending" : receiptStatus.Trim();
            ReceiptCode = string.IsNullOrWhiteSpace(receiptCode) ? "intent_dispatched" : receiptCode.Trim();
            ReceiptMessage = string.IsNullOrWhiteSpace(receiptMessage)
                ? (SignedQuantity > 0 ? "ENTER_LONG" : "ENTER_SHORT")
                : receiptMessage.Trim();
        }

        public string IntentId { get; }
        public string AccountName { get; }
        public string InstrumentName { get; }
        public int SignedQuantity { get; }
        public decimal DecisionReferencePrice { get; }
        public decimal StopPrice { get; }
        public IReadOnlyList<HermesTarget> Targets { get; }
        public string ContentFingerprint { get; }
        public string ReceiptStatus { get; }
        public string ReceiptCode { get; }
        public string ReceiptMessage { get; }
    }

    public sealed class HermesExitRequested : GlitchInput, IGlitchHermesIntent
    {
        public HermesExitRequested(
            string intentId,
            string accountName,
            string instrumentName,
            string contentFingerprint = null,
            string receiptStatus = "pending",
            string receiptCode = "intent_dispatched",
            string receiptMessage = "EXIT")
        {
            IntentId = Require(intentId, nameof(intentId));
            AccountName = Require(accountName, nameof(accountName));
            InstrumentName = Require(instrumentName, nameof(instrumentName));
            ContentFingerprint = GlitchHermesIntentContent.Resolve(
                contentFingerprint, IntentId, AccountName, InstrumentName, "EXIT");
            ReceiptStatus = string.IsNullOrWhiteSpace(receiptStatus) ? "pending" : receiptStatus.Trim();
            ReceiptCode = string.IsNullOrWhiteSpace(receiptCode) ? "intent_dispatched" : receiptCode.Trim();
            ReceiptMessage = string.IsNullOrWhiteSpace(receiptMessage) ? "EXIT" : receiptMessage.Trim();
        }

        public string IntentId { get; }
        public string AccountName { get; }
        public string InstrumentName { get; }
        public string ContentFingerprint { get; }
        public string ReceiptStatus { get; }
        public string ReceiptCode { get; }
        public string ReceiptMessage { get; }

        private static string Require(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A value is required.", name);
            return value.Trim();
        }
    }

    public sealed class HermesProtectionUpdate
    {
        public HermesProtectionUpdate(string legId, decimal? stopPrice, decimal? targetPrice)
        {
            if (string.IsNullOrWhiteSpace(legId))
                throw new ArgumentException("Leg identity is required.", nameof(legId));
            if (!stopPrice.HasValue && !targetPrice.HasValue)
                throw new ArgumentException("A stop or target price is required.");
            if ((stopPrice.HasValue && stopPrice.Value <= 0)
                || (targetPrice.HasValue && targetPrice.Value <= 0))
                throw new ArgumentOutOfRangeException("Protection prices must be positive.");
            LegId = legId.Trim();
            StopPrice = stopPrice;
            TargetPrice = targetPrice;
        }

        public string LegId { get; }
        public decimal? StopPrice { get; }
        public decimal? TargetPrice { get; }
    }

    public sealed class HermesProtectionChangeRequested : GlitchInput, IGlitchHermesIntent
    {
        public HermesProtectionChangeRequested(
            string intentId,
            string accountName,
            string instrumentName,
            IEnumerable<HermesProtectionUpdate> updates,
            string contentFingerprint = null,
            string receiptStatus = "pending",
            string receiptCode = "intent_dispatched",
            string receiptMessage = "PROTECTION_CHANGE")
        {
            if (string.IsNullOrWhiteSpace(intentId))
                throw new ArgumentException("Intent identity is required.", nameof(intentId));
            if (string.IsNullOrWhiteSpace(accountName))
                throw new ArgumentException("Account name is required.", nameof(accountName));
            if (string.IsNullOrWhiteSpace(instrumentName))
                throw new ArgumentException("Instrument name is required.", nameof(instrumentName));
            HermesProtectionUpdate[] updateArray =
                (updates ?? throw new ArgumentNullException(nameof(updates))).ToArray();
            if (updateArray.Length == 0)
                throw new ArgumentException("At least one protection update is required.", nameof(updates));
            IntentId = intentId.Trim();
            AccountName = accountName.Trim();
            InstrumentName = instrumentName.Trim();
            Updates = updateArray;
            ContentFingerprint = GlitchHermesIntentContent.Resolve(
                contentFingerprint,
                IntentId,
                AccountName,
                InstrumentName,
                string.Join("|", Updates.Select(value =>
                    value.LegId + ":"
                    + (value.StopPrice.HasValue
                        ? value.StopPrice.Value.ToString(CultureInfo.InvariantCulture) : string.Empty)
                    + ":"
                    + (value.TargetPrice.HasValue
                        ? value.TargetPrice.Value.ToString(CultureInfo.InvariantCulture) : string.Empty))));
            ReceiptStatus = string.IsNullOrWhiteSpace(receiptStatus) ? "pending" : receiptStatus.Trim();
            ReceiptCode = string.IsNullOrWhiteSpace(receiptCode) ? "intent_dispatched" : receiptCode.Trim();
            ReceiptMessage = string.IsNullOrWhiteSpace(receiptMessage)
                ? "PROTECTION_CHANGE" : receiptMessage.Trim();
        }

        public string IntentId { get; }
        public string AccountName { get; }
        public string InstrumentName { get; }
        public IReadOnlyList<HermesProtectionUpdate> Updates { get; }
        public string ContentFingerprint { get; }
        public string ReceiptStatus { get; }
        public string ReceiptCode { get; }
        public string ReceiptMessage { get; }
    }

    public sealed class HermesNoActionRequested : GlitchInput, IGlitchHermesIntent
    {
        public HermesNoActionRequested(
            string intentId,
            string accountName,
            string instrumentName,
            string action,
            string contentFingerprint,
            string receiptStatus,
            string receiptCode,
            string receiptMessage)
        {
            if (string.IsNullOrWhiteSpace(intentId))
                throw new ArgumentException("Intent identity is required.", nameof(intentId));
            IntentId = intentId.Trim();
            AccountName = (accountName ?? string.Empty).Trim();
            InstrumentName = (instrumentName ?? string.Empty).Trim();
            Action = (action ?? string.Empty).Trim();
            ContentFingerprint = GlitchHermesIntentContent.Resolve(
                contentFingerprint,
                IntentId,
                AccountName,
                InstrumentName,
                Action,
                receiptStatus,
                receiptCode,
                receiptMessage);
            ReceiptStatus = string.IsNullOrWhiteSpace(receiptStatus) ? "failed" : receiptStatus.Trim();
            ReceiptCode = string.IsNullOrWhiteSpace(receiptCode)
                ? "intent_not_representable" : receiptCode.Trim();
            ReceiptMessage = string.IsNullOrWhiteSpace(receiptMessage)
                ? ReceiptCode : receiptMessage.Trim();
        }

        public string IntentId { get; }
        public string AccountName { get; }
        public string InstrumentName { get; }
        public string Action { get; }
        public string ContentFingerprint { get; }
        public string ReceiptStatus { get; }
        public string ReceiptCode { get; }
        public string ReceiptMessage { get; }
    }

    public sealed class ProtectionLegTemplate
    {
        public ProtectionLegTemplate(int quantity, decimal targetOffset)
            : this(null, quantity, null, targetOffset)
        {
        }

        public ProtectionLegTemplate(int quantity, decimal stopOffset, decimal targetOffset)
            : this(null, quantity, stopOffset, targetOffset)
        {
        }

        public ProtectionLegTemplate(
            string legId,
            int quantity,
            decimal? stopOffset,
            decimal? targetOffset)
        {
            if (quantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(quantity));
            if (!stopOffset.HasValue && !targetOffset.HasValue)
                throw new ArgumentException("A stop or target offset is required.");
            LegId = legId;
            Quantity = quantity;
            StopOffset = stopOffset;
            TargetOffset = targetOffset;
        }

        public string LegId { get; }
        public int Quantity { get; }
        public decimal? StopOffset { get; }
        public decimal? TargetOffset { get; }
    }

    public sealed class ProtectionTemplate
    {
        public ProtectionTemplate(decimal stopOffset, IEnumerable<ProtectionLegTemplate> targets)
            : this((decimal?)stopOffset, targets)
        {
        }

        public ProtectionTemplate(decimal? stopOffset, IEnumerable<ProtectionLegTemplate> targets)
        {
            ProtectionLegTemplate[] targetArray =
                (targets ?? throw new ArgumentNullException(nameof(targets))).ToArray();
            if (targetArray.Length == 0)
                throw new ArgumentException("At least one target is required.", nameof(targets));
            StopOffset = stopOffset;
            Targets = targetArray.Select(target => !target.StopOffset.HasValue && stopOffset.HasValue
                ? new ProtectionLegTemplate(target.LegId, target.Quantity, stopOffset, target.TargetOffset)
                : target).ToArray();
        }

        public decimal? StopOffset { get; }
        public IReadOnlyList<ProtectionLegTemplate> Targets { get; }
        public int Quantity => Targets.Sum(value => value.Quantity);
    }

    public abstract class GlitchCommand
    {
        protected GlitchCommand(string commandId, GlitchCommandPurpose purpose)
        {
            CommandId = commandId;
            Purpose = purpose;
        }

        public string CommandId { get; }
        public GlitchCommandPurpose Purpose { get; }
    }

    public sealed class RefreshPositionCommand : GlitchCommand
    {
        public RefreshPositionCommand(
            string commandId,
            string accountName,
            string instrumentName)
            : base(commandId, GlitchCommandPurpose.Observation)
        {
            AccountName = accountName ?? string.Empty;
            InstrumentName = instrumentName ?? string.Empty;
        }

        public string AccountName { get; }
        public string InstrumentName { get; }
    }

    public sealed class SubmitMarketCommand : GlitchCommand
    {
        public SubmitMarketCommand(
            string commandId,
            GlitchCommandPurpose purpose,
            string accountName,
            string instrumentName,
            int signedQuantity,
            string parentCorrelationId,
            ProtectionTemplate protection = null,
            string routeId = null)
            : this(
                commandId,
                purpose,
                accountName,
                instrumentName,
                signedQuantity,
                parentCorrelationId,
                protection,
                routeId,
                int.MinValue)
        {
        }

        public SubmitMarketCommand(
            string commandId,
            GlitchCommandPurpose purpose,
            string accountName,
            string instrumentName,
            int signedQuantity,
            string parentCorrelationId,
            ProtectionTemplate protection,
            string routeId,
            int expectedSignedPosition)
            : base(commandId, purpose)
        {
            AccountName = accountName;
            InstrumentName = instrumentName;
            SignedQuantity = signedQuantity;
            ParentCorrelationId = parentCorrelationId;
            Protection = protection;
            RouteId = routeId;
            ExpectedSignedPosition = expectedSignedPosition;
        }

        public string AccountName { get; }
        public string InstrumentName { get; }
        public int SignedQuantity { get; }
        public string ParentCorrelationId { get; }
        public ProtectionTemplate Protection { get; }
        public string RouteId { get; }
        public int ExpectedSignedPosition { get; }
    }

    public sealed class ProtectionTarget
    {
        public ProtectionTarget(int quantity, decimal stopPrice, decimal price)
            : this(null, quantity, stopPrice, price)
        {
        }

        public ProtectionTarget(string legId, int quantity, decimal stopPrice, decimal price)
            : this(legId, quantity, (decimal?)stopPrice, (decimal?)price)
        {
        }

        public ProtectionTarget(
            string legId,
            int quantity,
            decimal? stopPrice,
            decimal? price)
        {
            if (quantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(quantity));
            if (!stopPrice.HasValue && !price.HasValue)
                throw new ArgumentException("A stop or target price is required.");
            LegId = legId;
            Quantity = quantity;
            StopPrice = stopPrice;
            Price = price;
        }

        public string LegId { get; }
        public int Quantity { get; }
        public decimal? StopPrice { get; }
        public decimal? Price { get; }
    }

    public sealed class SubmitProtectionCommand : GlitchCommand
    {
        public SubmitProtectionCommand(
            string commandId,
            string accountName,
            string instrumentName,
            int signedEntryQuantity,
            decimal? stopPrice,
            IEnumerable<ProtectionTarget> targets,
            string parentCorrelationId,
            bool propagatesAsMasterExecution = false,
            decimal entryPrice = 0,
            string routeId = null,
            string exposureId = null)
            : base(commandId, GlitchCommandPurpose.Protection)
        {
            AccountName = accountName;
            InstrumentName = instrumentName;
            SignedEntryQuantity = signedEntryQuantity;
            Targets = (targets ?? throw new ArgumentNullException(nameof(targets))).ToArray();
            StopPrice = Targets.Count > 0 ? Targets[0].StopPrice : stopPrice;
            ParentCorrelationId = parentCorrelationId;
            PropagatesAsMasterExecution = propagatesAsMasterExecution;
            EntryPrice = entryPrice;
            RouteId = routeId;
            ExposureId = exposureId;
        }

        public string AccountName { get; }
        public string InstrumentName { get; }
        public int SignedEntryQuantity { get; }
        public decimal? StopPrice { get; }
        public IReadOnlyList<ProtectionTarget> Targets { get; }
        public string ParentCorrelationId { get; }
        public bool PropagatesAsMasterExecution { get; }
        public decimal EntryPrice { get; }
        public string RouteId { get; }
        public string ExposureId { get; }
    }

    public sealed class ChangeProtectionCommand : GlitchCommand
    {
        public ChangeProtectionCommand(
            string commandId,
            string accountName,
            string instrumentName,
            IEnumerable<HermesProtectionUpdate> updates,
            IEnumerable<string> targetCommandIds = null)
            : base(commandId, GlitchCommandPurpose.Protection)
        {
            AccountName = accountName;
            InstrumentName = instrumentName;
            Updates = (updates ?? throw new ArgumentNullException(nameof(updates))).ToArray();
            TargetCommandIds = (targetCommandIds ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public string AccountName { get; }
        public string InstrumentName { get; }
        public IReadOnlyList<HermesProtectionUpdate> Updates { get; }
        public IReadOnlyList<string> TargetCommandIds { get; }
    }

    public sealed class CancelProtectionCommand : GlitchCommand
    {
        public CancelProtectionCommand(
            string commandId,
            string accountName,
            string instrumentName,
            bool includeExternalProtection,
            IEnumerable<string> legIds = null)
            : this(
                commandId,
                accountName,
                instrumentName,
                includeExternalProtection,
                legIds,
                null)
        {
        }

        public CancelProtectionCommand(
            string commandId,
            string accountName,
            string instrumentName,
            bool includeExternalProtection,
            IEnumerable<string> legIds,
            IEnumerable<string> targetCommandIds)
            : base(commandId, GlitchCommandPurpose.Protection)
        {
            AccountName = accountName;
            InstrumentName = instrumentName;
            IncludeExternalProtection = includeExternalProtection;
            LegIds = (legIds ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            TargetCommandIds = (targetCommandIds ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public string AccountName { get; }
        public string InstrumentName { get; }
        public bool IncludeExternalProtection { get; }
        public IReadOnlyList<string> LegIds { get; }
        public IReadOnlyList<string> TargetCommandIds { get; }
    }

    public sealed class FlattenAccountCommand : GlitchCommand
    {
        public FlattenAccountCommand(
            string commandId,
            string accountName,
            IEnumerable<string> instrumentNames,
            string reason)
            : base(commandId, GlitchCommandPurpose.UserFlatten)
        {
            if (string.IsNullOrWhiteSpace(accountName))
                throw new ArgumentException("Account name is required.", nameof(accountName));
            AccountName = accountName.Trim();
            InstrumentNames = (instrumentNames ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            Reason = reason ?? string.Empty;
        }

        public string AccountName { get; }
        public IReadOnlyList<string> InstrumentNames { get; }
        public string Reason { get; }
    }
}
