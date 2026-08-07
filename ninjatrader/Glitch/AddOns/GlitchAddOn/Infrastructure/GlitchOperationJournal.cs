using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using Glitch.Core;

namespace Glitch.Infrastructure
{
    internal sealed class GlitchRecoveryRecord
    {
        public string Phase { get; set; }
        public GlitchCommand Command { get; set; }
        public GlitchInput Input { get; set; }
        public string Fingerprint { get; set; }
    }

    /// <summary>
    /// The one append-only operation journal. Each line is a complete versioned
    /// record that can be replayed without treating the file as broker truth.
    /// </summary>
    internal sealed class GlitchOperationJournal
    {
        private const string Schema = "glitch.operation.v5";
        private readonly object _gate = new object();
        private readonly string _path;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer
        {
            MaxJsonLength = 1024 * 1024
        };

        public GlitchOperationJournal()
        {
            _path = Path.Combine(
                NinjaTrader.Core.Globals.UserDataDir,
                "glitch",
                "runtime",
                "operations.v5.jsonl");
        }

        public bool TryAppend(
            GlitchCommand command,
            string phase,
            string detail,
            out string error)
        {
            error = null;
            if (command == null)
            {
                error = "command_missing";
                return false;
            }
            var record = BaseRecord(phase, command.CommandId, command.GetType().Name);
            record["fingerprint"] = Fingerprint(command);
            record["detail"] = detail ?? string.Empty;
            record["command"] = SerializeCommand(command);
            return TryAppendRecord(record, out error);
        }

        public bool TryAppendInput(GlitchInput input, string source, out string error)
        {
            error = null;
            if (input == null)
            {
                error = "input_missing";
                return false;
            }
            var record = BaseRecord("input_accepted", InputCorrelation(input), input.GetType().Name);
            record["source"] = source ?? string.Empty;
            record["input"] = SerializeInput(input);
            return TryAppendRecord(record, out error);
        }

        public bool TryLoad(out IReadOnlyList<GlitchRecoveryRecord> records, out string error)
        {
            var result = new List<GlitchRecoveryRecord>();
            error = null;
            try
            {
                lock (_gate)
                {
                    if (!File.Exists(_path))
                    {
                        records = result;
                        return true;
                    }
                    foreach (string line in File.ReadLines(_path, Encoding.UTF8))
                    {
                        if (string.IsNullOrWhiteSpace(line))
                            continue;
                        var value = _json.DeserializeObject(line) as Dictionary<string, object>;
                        if (value == null || !string.Equals(
                            Text(value, "schema"), Schema, StringComparison.Ordinal))
                            continue;
                        var recovered = new GlitchRecoveryRecord
                        {
                            Phase = Text(value, "phase"),
                            Fingerprint = Text(value, "fingerprint")
                        };
                        Dictionary<string, object> command = Map(value, "command");
                        Dictionary<string, object> input = Map(value, "input");
                        if (command != null)
                        {
                            recovered.Command = DeserializeCommand(command);
                            if (recovered.Command == null)
                                throw new InvalidDataException(
                                    "Unsupported command record " + Text(command, "type") + ".");
                        }
                        if (input != null)
                        {
                            recovered.Input = DeserializeInput(input);
                            if (recovered.Input == null)
                                throw new InvalidDataException(
                                    "Unsupported input record " + Text(input, "type") + ".");
                        }
                        result.Add(recovered);
                    }
                }
                records = result;
                return true;
            }
            catch (Exception ex)
            {
                records = result;
                error = ex.GetType().Name + ":" + ex.Message;
                return false;
            }
        }

        internal static string Fingerprint(GlitchCommand command)
        {
            if (command == null)
                return string.Empty;
            string canonical = Canonical(SerializeCommand(command));
            using (SHA256 sha = SHA256.Create())
            {
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical))
                    .Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
            }
        }

        internal static string DescribeFact(GlitchInput input)
        {
            var execution = input as ExecutionLifecycleObserved;
            if (execution != null)
                return "operation=" + execution.Operation
                    + "|execution_id=" + execution.ExecutionId
                    + "|account=" + execution.AccountName
                    + "|instrument=" + execution.InstrumentName
                    + "|native_order=" + execution.NativeOrderKey
                    + "|signed_quantity=" + execution.SignedQuantity
                    + "|price=" + execution.Price.ToString(CultureInfo.InvariantCulture)
                    + "|commission=" + execution.Commission.ToString(CultureInfo.InvariantCulture)
                    + "|representable=" + execution.Representable
                    + "|evidence_gap=" + execution.EvidenceGap
                    + "|correlation=" + execution.CorrelationId;
            var applied = input as ExecutionObserved;
            if (applied != null)
                return "execution_id=" + applied.ExecutionId
                    + "|account=" + applied.AccountName
                    + "|instrument=" + applied.InstrumentName
                    + "|signed_quantity=" + applied.SignedQuantity
                    + "|price=" + applied.Price.ToString(CultureInfo.InvariantCulture)
                    + "|origin=" + applied.Origin
                    + "|correlation=" + applied.CorrelationId
                    + "|protection=" + applied.ProtectionCorrelationId
                    + "|native_order=" + applied.NativeOrderKey
                    + "|baseline=" + applied.IsBaseline;
            var order = input as NativeOrderObserved;
            if (order != null)
                return "account=" + order.AccountName
                    + "|instrument=" + order.InstrumentName
                    + "|native_order_key=" + order.NativeOrderKey
                    + "|native_order_id=" + order.NativeOrderId
                    + "|signal=" + order.SignalName
                    + "|state=" + order.OrderState
                    + "|quantity=" + order.Quantity
                    + "|filled=" + order.Filled
                    + "|error=" + order.Error
                    + "|comment=" + order.Comment
                    + "|oco=" + order.Oco
                    + "|correlation=" + order.CorrelationId
                    + "|child_role=" + order.ChildRole
                    + "|leg_id=" + order.LegId;
            var status = input as AccountStatusObserved;
            if (status != null)
                return "account=" + status.AccountName
                    + "|previous=" + status.PreviousStatus
                    + "|status=" + status.Status;
            return string.Empty;
        }

        private bool TryAppendRecord(Dictionary<string, object> record, out string error)
        {
            error = null;
            try
            {
                string directory = Path.GetDirectoryName(_path);
                lock (_gate)
                {
                    if (!Directory.Exists(directory))
                        Directory.CreateDirectory(directory);
                    string line = _json.Serialize(record);
                    using (var stream = new FileStream(
                        _path, FileMode.Append, FileAccess.Write, FileShare.Read))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    {
                        writer.WriteLine(line);
                        writer.Flush();
                        stream.Flush(true);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ":" + ex.Message;
                return false;
            }
        }

        private static Dictionary<string, object> BaseRecord(
            string phase,
            string commandId,
            string type)
        {
            return new Dictionary<string, object>
            {
                { "schema", Schema },
                { "created_utc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) },
                { "phase", phase ?? string.Empty },
                { "command_id", commandId ?? string.Empty },
                { "type", type ?? string.Empty }
            };
        }

        private static Dictionary<string, object> SerializeCommand(GlitchCommand command)
        {
            var result = new Dictionary<string, object>
            {
                { "type", command.GetType().Name },
                { "command_id", command.CommandId },
                { "purpose", command.Purpose.ToString() }
            };
            var refresh = command as RefreshPositionCommand;
            if (refresh != null)
            {
                result["account"] = refresh.AccountName;
                result["instrument"] = refresh.InstrumentName;
                return result;
            }
            var market = command as SubmitMarketCommand;
            if (market != null)
            {
                result["account"] = market.AccountName;
                result["instrument"] = market.InstrumentName;
                result["signed_quantity"] = market.SignedQuantity;
                result["expected_position"] = market.ExpectedSignedPosition;
                result["parent"] = market.ParentCorrelationId ?? string.Empty;
                result["route"] = market.RouteId ?? string.Empty;
                return result;
            }
            var protection = command as SubmitProtectionCommand;
            if (protection != null)
            {
                result["account"] = protection.AccountName;
                result["instrument"] = protection.InstrumentName;
                result["signed_entry"] = protection.SignedEntryQuantity;
                result["entry_price"] = protection.EntryPrice;
                result["parent"] = protection.ParentCorrelationId ?? string.Empty;
                result["route"] = protection.RouteId ?? string.Empty;
                result["exposure"] = protection.ExposureId ?? string.Empty;
                result["hermes_intent"] = protection.HermesIntentId ?? string.Empty;
                result["propagates"] = protection.PropagatesAsMasterExecution;
                result["targets"] = protection.Targets.Select(value =>
                    (object)new Dictionary<string, object>
                    {
                        { "leg_id", value.LegId ?? string.Empty },
                        { "quantity", value.Quantity },
                        { "stop", value.StopPrice.HasValue ? (object)value.StopPrice.Value : null },
                        { "target", value.Price.HasValue ? (object)value.Price.Value : null }
                    }).ToArray();
                return result;
            }
            var change = command as ChangeProtectionCommand;
            if (change != null)
            {
                result["account"] = change.AccountName;
                result["instrument"] = change.InstrumentName;
                result["updates"] = change.Updates.Select(value =>
                    (object)new Dictionary<string, object>
                    {
                        { "leg_id", value.LegId ?? string.Empty },
                        { "stop", value.StopPrice.HasValue ? (object)value.StopPrice.Value : null },
                        { "target", value.TargetPrice.HasValue ? (object)value.TargetPrice.Value : null }
                    }).ToArray();
                result["targets"] = change.TargetCommandIds.ToArray();
                result["hermes_intent"] = change.HermesIntentId ?? string.Empty;
                return result;
            }
            var cancel = command as CancelProtectionCommand;
            if (cancel != null)
            {
                result["account"] = cancel.AccountName;
                result["instrument"] = cancel.InstrumentName;
                result["include_external"] = cancel.IncludeExternalProtection;
                result["legs"] = cancel.LegIds.ToArray();
                result["targets"] = cancel.TargetCommandIds.ToArray();
                return result;
            }
            var flatten = command as FlattenAccountCommand;
            if (flatten != null)
            {
                result["account"] = flatten.AccountName;
                result["instruments"] = flatten.InstrumentNames.ToArray();
                result["reason"] = flatten.Reason;
            }
            return result;
        }

        private static Dictionary<string, object> SerializeInput(GlitchInput input)
        {
            var result = new Dictionary<string, object>
            {
                { "type", input.GetType().Name }
            };
            var execution = input as ExecutionObserved;
            if (execution != null)
            {
                result["execution_id"] = execution.ExecutionId;
                result["account"] = execution.AccountName;
                result["instrument"] = execution.InstrumentName;
                result["signed_quantity"] = execution.SignedQuantity;
                result["price"] = execution.Price;
                result["origin"] = execution.Origin.ToString();
                result["correlation"] = execution.CorrelationId ?? string.Empty;
                result["protection"] = execution.ProtectionCorrelationId ?? string.Empty;
                result["native_order_key"] = execution.NativeOrderKey;
                result["baseline"] = execution.IsBaseline;
                return result;
            }
            var lifecycle = input as ExecutionLifecycleObserved;
            if (lifecycle != null)
            {
                result["operation"] = lifecycle.Operation.ToString();
                result["execution_id"] = lifecycle.ExecutionId;
                result["account"] = lifecycle.AccountName;
                result["instrument"] = lifecycle.InstrumentName;
                result["native_order_key"] = lifecycle.NativeOrderKey;
                result["signed_quantity"] = lifecycle.SignedQuantity;
                result["price"] = lifecycle.Price;
                result["commission"] = lifecycle.Commission;
                result["representable"] = lifecycle.Representable;
                result["evidence_gap"] = lifecycle.EvidenceGap;
                result["correlation"] = lifecycle.CorrelationId;
                return result;
            }
            var order = input as NativeOrderObserved;
            if (order != null)
            {
                result["account"] = order.AccountName;
                result["instrument"] = order.InstrumentName;
                result["native_order_key"] = order.NativeOrderKey;
                result["native_order_id"] = order.NativeOrderId;
                result["signal"] = order.SignalName;
                result["state"] = order.OrderState;
                result["quantity"] = order.Quantity;
                result["filled"] = order.Filled;
                result["stop"] = order.StopPrice.HasValue ? (object)order.StopPrice.Value : null;
                result["limit"] = order.LimitPrice.HasValue ? (object)order.LimitPrice.Value : null;
                result["error"] = order.Error;
                result["comment"] = order.Comment;
                result["oco"] = order.Oco;
                result["correlation"] = order.CorrelationId;
                result["child_role"] = order.ChildRole;
                result["leg_id"] = order.LegId;
                return result;
            }
            var status = input as AccountStatusObserved;
            if (status != null)
            {
                result["account"] = status.AccountName;
                result["previous"] = status.PreviousStatus;
                result["status"] = status.Status;
                return result;
            }
            var position = input as PositionObserved;
            if (position != null)
            {
                result["account"] = position.AccountName;
                result["instrument"] = position.InstrumentName;
                result["signed_quantity"] = position.SignedQuantity;
                result["revision"] = position.Revision;
                return result;
            }
            var failed = input as NativeRequestFailedObserved;
            if (failed != null)
            {
                result["command_id"] = failed.CommandId;
                result["error"] = failed.Error;
                return result;
            }
            var unknown = input as NativeRequestUnknownObserved;
            if (unknown != null)
            {
                result["command_id"] = unknown.CommandId;
                result["evidence_gap"] = unknown.EvidenceGap;
                return result;
            }
            var stale = input as NativePlanStaleObserved;
            if (stale != null)
            {
                result["command_id"] = stale.CommandId;
                result["account"] = stale.AccountName;
                result["instrument"] = stale.InstrumentName;
                result["signed_quantity"] = stale.SignedPosition;
                return result;
            }
            var cancelled = input as ProtectionCancellationCompletedObserved;
            if (cancelled != null)
            {
                result["command_id"] = cancelled.CommandId;
                return result;
            }
            var flattenCompleted = input as FlattenCompletedObserved;
            if (flattenCompleted != null)
            {
                result["command_id"] = flattenCompleted.CommandId;
                result["account"] = flattenCompleted.AccountName;
                return result;
            }
            var routeConfiguration = input as RouteConfigurationChanged;
            if (routeConfiguration != null)
            {
                result["replication_enabled"] = routeConfiguration.ReplicationEnabled;
                result["routes"] = routeConfiguration.Routes.Select(route =>
                    (object)new Dictionary<string, object>
                    {
                        { "route", route.RouteId },
                        { "master", route.MasterAccount },
                        { "follower", route.FollowerAccount },
                        { "ratio", route.Ratio },
                        { "enabled", route.Enabled }
                    }).ToArray();
                result["synchronize_routes"] = routeConfiguration.SynchronizeRouteIds.ToArray();
                return result;
            }
            var synchronization = input as RouteSynchronizationRequested;
            if (synchronization != null)
            {
                result["route"] = synchronization.RouteId;
                return result;
            }
            var limit = input as ReplicationQuantityLimitChanged;
            if (limit != null)
            {
                result["account"] = limit.AccountName;
                result["max_order_quantity"] = limit.MaxOrderQuantity.HasValue
                    ? (object)limit.MaxOrderQuantity.Value : null;
                return result;
            }
            var flatten = input as FlattenAccountRequested;
            if (flatten != null)
            {
                result["request_id"] = flatten.RequestId;
                result["account"] = flatten.AccountName;
                result["reason"] = flatten.Reason;
                return result;
            }
            var entry = input as HermesEntryRequested;
            if (entry != null)
            {
                AddHermesReceipt(result, entry);
                result["account"] = entry.AccountName;
                result["instrument"] = entry.InstrumentName;
                result["signed_quantity"] = entry.SignedQuantity;
                result["reference_price"] = entry.DecisionReferencePrice;
                result["stop"] = entry.StopPrice;
                result["targets"] = entry.Targets.Select(value =>
                    (object)new Dictionary<string, object>
                    {
                        { "quantity", value.Quantity },
                        { "stop", value.StopPrice },
                        { "target", value.Price }
                    }).ToArray();
                return result;
            }
            var exit = input as HermesExitRequested;
            if (exit != null)
            {
                AddHermesReceipt(result, exit);
                result["account"] = exit.AccountName;
                result["instrument"] = exit.InstrumentName;
                return result;
            }
            var change = input as HermesProtectionChangeRequested;
            if (change != null)
            {
                AddHermesReceipt(result, change);
                result["account"] = change.AccountName;
                result["instrument"] = change.InstrumentName;
                result["updates"] = change.Updates.Select(value =>
                    (object)new Dictionary<string, object>
                    {
                        { "leg_id", value.LegId },
                        { "stop", value.StopPrice.HasValue ? (object)value.StopPrice.Value : null },
                        { "target", value.TargetPrice.HasValue ? (object)value.TargetPrice.Value : null }
                    }).ToArray();
                return result;
            }
            var noAction = input as HermesNoActionRequested;
            if (noAction != null)
            {
                AddHermesReceipt(result, noAction);
                result["account"] = noAction.AccountName;
                result["instrument"] = noAction.InstrumentName;
                result["action"] = noAction.Action;
                return result;
            }
            var masterProtection = input as MasterProtectionObserved;
            if (masterProtection != null)
            {
                result["account"] = masterProtection.AccountName;
                result["instrument"] = masterProtection.InstrumentName;
                result["signed_quantity"] = masterProtection.SignedPosition;
                result["reference_price"] = masterProtection.ReferencePrice;
                result["revision_id"] = masterProtection.RevisionId;
                result["legs"] = masterProtection.Legs.Select(value =>
                    (object)new Dictionary<string, object>
                    {
                        { "leg_id", value.LegId },
                        { "quantity", value.Quantity },
                        { "stop", value.StopPrice.HasValue ? (object)value.StopPrice.Value : null },
                        { "target", value.TargetPrice.HasValue ? (object)value.TargetPrice.Value : null }
                    }).ToArray();
                return result;
            }
            throw new NotSupportedException("Unsupported journal input " + input.GetType().FullName + ".");
        }

        private static void AddHermesReceipt(
            IDictionary<string, object> result,
            IGlitchHermesIntent intent)
        {
            result["intent_id"] = intent.IntentId;
            result["content_fingerprint"] = intent.ContentFingerprint;
            result["receipt_status"] = intent.ReceiptStatus;
            result["receipt_code"] = intent.ReceiptCode;
            result["receipt_message"] = intent.ReceiptMessage;
        }

        private static GlitchCommand DeserializeCommand(Dictionary<string, object> value)
        {
            string type = Text(value, "type");
            string id = Text(value, "command_id");
            if (type == nameof(RefreshPositionCommand))
                return new RefreshPositionCommand(id, Text(value, "account"), Text(value, "instrument"));
            if (type == nameof(SubmitMarketCommand))
                return new SubmitMarketCommand(
                    id,
                    EnumValue(value, "purpose", GlitchCommandPurpose.Replication),
                    Text(value, "account"),
                    Text(value, "instrument"),
                    Integer(value, "signed_quantity"),
                    Text(value, "parent"),
                    null,
                    EmptyToNull(Text(value, "route")),
                    Integer(value, "expected_position"));
            if (type == nameof(SubmitProtectionCommand))
            {
                var targets = List(value, "targets").Select(item => new ProtectionTarget(
                    Text(item, "leg_id"),
                    Integer(item, "quantity"),
                    NullableDecimal(item, "stop"),
                    NullableDecimal(item, "target"))).ToArray();
                return new SubmitProtectionCommand(
                    id,
                    Text(value, "account"),
                    Text(value, "instrument"),
                    Integer(value, "signed_entry"),
                    targets.Length == 0 ? null : targets[0].StopPrice,
                    targets,
                    Text(value, "parent"),
                    Boolean(value, "propagates"),
                    DecimalValue(value, "entry_price"),
                    EmptyToNull(Text(value, "route")),
                    EmptyToNull(Text(value, "exposure")),
                    EmptyToNull(Text(value, "hermes_intent")));
            }
            if (type == nameof(ChangeProtectionCommand))
                return new ChangeProtectionCommand(
                    id,
                    Text(value, "account"),
                    Text(value, "instrument"),
                    List(value, "updates").Select(item => new HermesProtectionUpdate(
                        Text(item, "leg_id"),
                        NullableDecimal(item, "stop"),
                        NullableDecimal(item, "target"))),
                    Strings(value, "targets"),
                    EmptyToNull(Text(value, "hermes_intent")));
            if (type == nameof(CancelProtectionCommand))
                return new CancelProtectionCommand(
                    id,
                    Text(value, "account"),
                    Text(value, "instrument"),
                    Boolean(value, "include_external"),
                    Strings(value, "legs"),
                    Strings(value, "targets"));
            if (type == nameof(FlattenAccountCommand))
                return new FlattenAccountCommand(
                    id, Text(value, "account"), Strings(value, "instruments"), Text(value, "reason"));
            return null;
        }

        private static GlitchInput DeserializeInput(Dictionary<string, object> value)
        {
            string type = Text(value, "type");
            if (type == nameof(ExecutionObserved))
                return new ExecutionObserved(
                    Text(value, "execution_id"),
                    Text(value, "account"),
                    Text(value, "instrument"),
                    Integer(value, "signed_quantity"),
                    DecimalValue(value, "price"),
                    EnumValue(value, "origin", GlitchExecutionOrigin.External),
                    EmptyToNull(Text(value, "correlation")),
                    EmptyToNull(Text(value, "protection")),
                    Text(value, "native_order_key"),
                    Boolean(value, "baseline"));
            if (type == nameof(ExecutionLifecycleObserved))
                return new ExecutionLifecycleObserved(
                    EnumValue(value, "operation", GlitchNativeOperation.Unknown),
                    Text(value, "execution_id"),
                    Text(value, "account"),
                    Text(value, "instrument"),
                    Text(value, "native_order_key"),
                    Integer(value, "signed_quantity"),
                    DecimalValue(value, "price"),
                    Boolean(value, "representable"),
                    Text(value, "evidence_gap"),
                    Text(value, "correlation"),
                    DecimalValue(value, "commission"));
            if (type == nameof(NativeOrderObserved))
                return new NativeOrderObserved(
                    Text(value, "account"),
                    Text(value, "instrument"),
                    Text(value, "native_order_key"),
                    Text(value, "native_order_id"),
                    Text(value, "signal"),
                    Text(value, "state"),
                    Integer(value, "quantity"),
                    Integer(value, "filled"),
                    NullableDecimal(value, "stop"),
                    NullableDecimal(value, "limit"),
                    Text(value, "error"),
                    Text(value, "comment"),
                    Text(value, "oco"),
                    Text(value, "correlation"),
                    Text(value, "child_role"),
                    Text(value, "leg_id"));
            if (type == nameof(AccountStatusObserved))
                return new AccountStatusObserved(
                    Text(value, "account"), Text(value, "previous"), Text(value, "status"));
            if (type == nameof(PositionObserved))
                return new PositionObserved(
                    Text(value, "account"),
                    Text(value, "instrument"),
                    Integer(value, "signed_quantity"),
                    LongValue(value, "revision"));
            if (type == nameof(NativeRequestFailedObserved))
                return new NativeRequestFailedObserved(
                    Text(value, "command_id"), Text(value, "error"));
            if (type == nameof(NativeRequestUnknownObserved))
                return new NativeRequestUnknownObserved(
                    Text(value, "command_id"), Text(value, "evidence_gap"));
            if (type == nameof(NativePlanStaleObserved))
                return new NativePlanStaleObserved(
                    Text(value, "command_id"),
                    Text(value, "account"),
                    Text(value, "instrument"),
                    Integer(value, "signed_quantity"));
            if (type == nameof(ProtectionCancellationCompletedObserved))
                return new ProtectionCancellationCompletedObserved(Text(value, "command_id"));
            if (type == nameof(FlattenCompletedObserved))
                return new FlattenCompletedObserved(
                    Text(value, "command_id"), Text(value, "account"));
            if (type == nameof(RouteConfigurationChanged))
                return new RouteConfigurationChanged(
                    Boolean(value, "replication_enabled"),
                    List(value, "routes").Select(route => new RouteConfigurationItem(
                        Text(route, "route"),
                        Text(route, "master"),
                        Text(route, "follower"),
                        DecimalValue(route, "ratio"),
                        Boolean(route, "enabled"))),
                    Strings(value, "synchronize_routes"));
            if (type == nameof(RouteSynchronizationRequested))
                return new RouteSynchronizationRequested(Text(value, "route"));
            if (type == nameof(ReplicationQuantityLimitChanged))
                return new ReplicationQuantityLimitChanged(
                    Text(value, "account"), NullableInteger(value, "max_order_quantity"));
            if (type == nameof(FlattenAccountRequested))
                return new FlattenAccountRequested(
                    Text(value, "request_id"), Text(value, "account"), Text(value, "reason"));
            if (type == nameof(HermesEntryRequested))
                return new HermesEntryRequested(
                    Text(value, "intent_id"),
                    Text(value, "account"),
                    Text(value, "instrument"),
                    Integer(value, "signed_quantity"),
                    DecimalValue(value, "reference_price"),
                    DecimalValue(value, "stop"),
                    List(value, "targets").Select(item => new HermesTarget(
                        Integer(item, "quantity"),
                        DecimalValue(item, "stop"),
                        DecimalValue(item, "target"))),
                    Text(value, "content_fingerprint"),
                    Text(value, "receipt_status"),
                    Text(value, "receipt_code"),
                    Text(value, "receipt_message"));
            if (type == nameof(HermesExitRequested))
                return new HermesExitRequested(
                    Text(value, "intent_id"),
                    Text(value, "account"),
                    Text(value, "instrument"),
                    Text(value, "content_fingerprint"),
                    Text(value, "receipt_status"),
                    Text(value, "receipt_code"),
                    Text(value, "receipt_message"));
            if (type == nameof(HermesProtectionChangeRequested))
                return new HermesProtectionChangeRequested(
                    Text(value, "intent_id"),
                    Text(value, "account"),
                    Text(value, "instrument"),
                    List(value, "updates").Select(item => new HermesProtectionUpdate(
                        Text(item, "leg_id"),
                        NullableDecimal(item, "stop"),
                        NullableDecimal(item, "target"))),
                    Text(value, "content_fingerprint"),
                    Text(value, "receipt_status"),
                    Text(value, "receipt_code"),
                    Text(value, "receipt_message"));
            if (type == nameof(HermesNoActionRequested))
                return new HermesNoActionRequested(
                    Text(value, "intent_id"),
                    Text(value, "account"),
                    Text(value, "instrument"),
                    Text(value, "action"),
                    Text(value, "content_fingerprint"),
                    Text(value, "receipt_status"),
                    Text(value, "receipt_code"),
                    Text(value, "receipt_message"));
            if (type == nameof(MasterProtectionObserved))
                return new MasterProtectionObserved(
                    Text(value, "account"),
                    Text(value, "instrument"),
                    Integer(value, "signed_quantity"),
                    DecimalValue(value, "reference_price"),
                    Text(value, "revision_id"),
                    List(value, "legs").Select(item => new MasterProtectionLeg(
                        Text(item, "leg_id"),
                        Integer(item, "quantity"),
                        NullableDecimal(item, "stop"),
                        NullableDecimal(item, "target"))));
            return null;
        }

        private static string Describe(GlitchCommand command)
        {
            Dictionary<string, object> value = SerializeCommand(command);
            return string.Join("|", value.Where(pair =>
                    pair.Value == null || pair.Value is string || pair.Value.GetType().IsValueType)
                .Select(pair => pair.Key + "=" + Convert.ToString(
                    pair.Value, CultureInfo.InvariantCulture)));
        }

        private static string InputCorrelation(GlitchInput input)
        {
            var order = input as NativeOrderObserved;
            if (order != null)
                return order.CorrelationId;
            var execution = input as ExecutionObserved;
            if (execution != null)
                return execution.CorrelationId;
            var lifecycle = input as ExecutionLifecycleObserved;
            if (lifecycle != null)
                return lifecycle.CorrelationId;
            var failed = input as NativeRequestFailedObserved;
            if (failed != null)
                return failed.CommandId;
            var unknown = input as NativeRequestUnknownObserved;
            if (unknown != null)
                return unknown.CommandId;
            var stale = input as NativePlanStaleObserved;
            if (stale != null)
                return stale.CommandId;
            var cancelled = input as ProtectionCancellationCompletedObserved;
            if (cancelled != null)
                return cancelled.CommandId;
            var flattened = input as FlattenCompletedObserved;
            return flattened?.CommandId ?? string.Empty;
        }

        private static string Canonical(object value)
        {
            if (value == null)
                return "null";
            var map = value as Dictionary<string, object>;
            if (map != null)
                return "{" + string.Join(",", map.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => pair.Key + ":" + Canonical(pair.Value))) + "}";
            var array = value as object[];
            if (array != null)
                return "[" + string.Join(",", array.Select(Canonical)) + "]";
            var strings = value as string[];
            if (strings != null)
                return "[" + string.Join(",", strings.Select(Canonical)) + "]";
            var enumerable = value as IEnumerable;
            if (!(value is string) && enumerable != null)
                return "[" + string.Join(",", enumerable.Cast<object>().Select(Canonical)) + "]";
            if (value is bool)
                return (bool)value ? "true" : "false";
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static Dictionary<string, object> Map(
            Dictionary<string, object> value,
            string key)
        {
            object raw;
            return value != null && value.TryGetValue(key, out raw)
                ? raw as Dictionary<string, object>
                : null;
        }

        private static IEnumerable<Dictionary<string, object>> List(
            Dictionary<string, object> value,
            string key)
        {
            object raw;
            if (value == null || !value.TryGetValue(key, out raw) || raw == null)
                return Enumerable.Empty<Dictionary<string, object>>();
            var array = raw as object[];
            if (array != null)
                return array.OfType<Dictionary<string, object>>();
            var list = raw as ArrayList;
            return list == null
                ? Enumerable.Empty<Dictionary<string, object>>()
                : list.OfType<Dictionary<string, object>>();
        }

        private static IEnumerable<string> Strings(
            Dictionary<string, object> value,
            string key)
        {
            object raw;
            if (value == null || !value.TryGetValue(key, out raw) || raw == null)
                return Enumerable.Empty<string>();
            var array = raw as object[];
            if (array != null)
                return array.Select(Convert.ToString);
            var list = raw as ArrayList;
            return list == null ? Enumerable.Empty<string>() : list.Cast<object>().Select(Convert.ToString);
        }

        private static string Text(Dictionary<string, object> value, string key)
        {
            object raw;
            return value != null && value.TryGetValue(key, out raw) && raw != null
                ? Convert.ToString(raw, CultureInfo.InvariantCulture)
                : string.Empty;
        }

        private static int Integer(Dictionary<string, object> value, string key)
        {
            object raw;
            return value != null && value.TryGetValue(key, out raw) && raw != null
                ? Convert.ToInt32(raw, CultureInfo.InvariantCulture)
                : 0;
        }

        private static int? NullableInteger(Dictionary<string, object> value, string key)
        {
            object raw;
            return value != null && value.TryGetValue(key, out raw) && raw != null
                ? (int?)Convert.ToInt32(raw, CultureInfo.InvariantCulture)
                : null;
        }

        private static long LongValue(Dictionary<string, object> value, string key)
        {
            object raw;
            return value != null && value.TryGetValue(key, out raw) && raw != null
                ? Convert.ToInt64(raw, CultureInfo.InvariantCulture)
                : 0L;
        }

        private static decimal DecimalValue(Dictionary<string, object> value, string key)
        {
            object raw;
            return value != null && value.TryGetValue(key, out raw) && raw != null
                ? Convert.ToDecimal(raw, CultureInfo.InvariantCulture)
                : 0;
        }

        private static decimal? NullableDecimal(Dictionary<string, object> value, string key)
        {
            object raw;
            return value != null && value.TryGetValue(key, out raw) && raw != null
                ? (decimal?)Convert.ToDecimal(raw, CultureInfo.InvariantCulture)
                : null;
        }

        private static bool Boolean(Dictionary<string, object> value, string key)
        {
            object raw;
            return value != null && value.TryGetValue(key, out raw) && raw != null
                && Convert.ToBoolean(raw, CultureInfo.InvariantCulture);
        }

        private static T EnumValue<T>(Dictionary<string, object> value, string key, T fallback)
            where T : struct
        {
            T parsed;
            return Enum.TryParse(Text(value, key), true, out parsed) ? parsed : fallback;
        }

        private static string EmptyToNull(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }
}
