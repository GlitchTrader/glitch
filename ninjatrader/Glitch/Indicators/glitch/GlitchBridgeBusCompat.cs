#region Using declarations
using System;
using System.Globalization;
using System.Reflection;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    internal static class GlitchBridgeBusCompat
    {
        private static readonly object SyncRoot = new object();
        private static readonly TimeSpan ResolveRetryInterval = TimeSpan.FromSeconds(1);
        private const string BusTypeFullName = "Glitch.UI.GlitchAnalyticsFeedBus";
        private const string ReadingTypeFullName = "Glitch.UI.GlitchIndicatorReading";
        private const string TelemetryServerTypeFullName = "Glitch.Services.Ai.GlitchExternalTelemetryServer";

        private static DateTime _lastResolveUtc = DateTime.MinValue;
        private static Type _busType;
        private static Type _readingType;
        private static MethodInfo _publishMethod;
        private static MethodInfo _registerBridgeMethod;
        private static MethodInfo _registerTradeInstrumentMethod;
        private static MethodInfo _registerTradeInstrumentInstanceMethod;
        private static MethodInfo _touchBridgeMethod;
        private static MethodInfo _unregisterBridgeMethod;
        private static MethodInfo _registerBootstrapPublisherMethod;
        private static MethodInfo _unregisterBootstrapPublisherMethod;

        public sealed class BridgeReading
        {
            public string InstrumentRoot { get; set; }
            public string InstrumentFullName { get; set; }
            public int Minutes { get; set; }
            public DateTime UtcTime { get; set; }
            public double? Open { get; set; }
            public double? High { get; set; }
            public double? Low { get; set; }
            public double? Volume { get; set; }
            public double? CurrentPrice { get; set; }
            public double? AveragePrice { get; set; }
            public double? Atr { get; set; }
            public double? Adx { get; set; }
            public double Score { get; set; }
            public double? RawScore { get; set; }
            public double? DirectionalScore { get; set; }
            public double? TradeabilityScore { get; set; }
            public string SignalLabel { get; set; }
            public string VolatilityHint { get; set; }
            public string TrendHint { get; set; }
            public string RegimeLabel { get; set; }
            public string NoTradeReasons { get; set; }
            public double? Rsi { get; set; }
            public double? StochK { get; set; }
            public double? ZScore { get; set; }
            public double? DiPlus { get; set; }
            public double? DiMinus { get; set; }
            public double? Cci { get; set; }
            public double? MacdHistogram { get; set; }
            public double? EmaAlignment { get; set; }
            public double? RegimeWeight { get; set; }
            public double? OscillatorCompositeScore { get; set; }
            public double? MaCompositeScore { get; set; }
            public double? OrderFlowScore { get; set; }
            public double? OrderFlowConfidence { get; set; }
            public double? OrderFlowReliability { get; set; }
            public double? OrderFlowCumulativeDelta { get; set; }
            public double? OrderFlowDeltaChange { get; set; }
            public double? OrderFlowVwap { get; set; }
            public double? OrderFlowVwapDeviation { get; set; }
            public double? OrderFlowAggressionBalance { get; set; }
            public double? OrderFlowDepthImbalance { get; set; }
            public string OrderFlowHint { get; set; }
            public string SessionName { get; set; }
            public double? SessionHigh { get; set; }
            public double? SessionLow { get; set; }
            public double? PreviousSessionHigh { get; set; }
            public double? PreviousSessionLow { get; set; }
        }

        public static bool IsAvailable()
        {
            return EnsureResolved();
        }

        public static void RegisterBridge(string instrumentRoot, bool publishToGlitchUi)
        {
            MethodInfo method = GetMethod("RegisterBridge");
            if (method == null)
                return;

            TryInvoke(method, new object[] { instrumentRoot, publishToGlitchUi });
        }

        public static void RegisterTradeInstrument(string instrumentFullName)
        {
            if (!EnsureResolved() || _registerTradeInstrumentMethod == null)
                return;

            TryInvoke(_registerTradeInstrumentMethod, new object[] { instrumentFullName });
        }

        public static void RegisterTradeInstrumentInstance(object instrument)
        {
            if (!EnsureResolved() || _registerTradeInstrumentInstanceMethod == null)
                return;

            TryInvoke(_registerTradeInstrumentInstanceMethod, new object[] { instrument });
        }

        public static void TouchBridge(string instrumentRoot, bool publishToGlitchUi, bool isTrackedPrimaryTimeframe)
        {
            MethodInfo method = GetMethod("TouchBridge");
            if (method == null)
                return;

            TryInvoke(method, new object[] { instrumentRoot, publishToGlitchUi, isTrackedPrimaryTimeframe });
        }

        public static void UnregisterBridge(string instrumentRoot)
        {
            MethodInfo method = GetMethod("UnregisterBridge");
            if (method == null)
                return;

            TryInvoke(method, new object[] { instrumentRoot });
        }

        public static void RegisterBridgeBootstrapPublisher(string instrumentRoot, Action publisher)
        {
            MethodInfo method = GetMethod("RegisterBridgeBootstrapPublisher");
            if (method == null)
                return;

            TryInvoke(method, new object[] { instrumentRoot, publisher });
        }

        public static void UnregisterBridgeBootstrapPublisher(string instrumentRoot, Action publisher)
        {
            MethodInfo method = GetMethod("UnregisterBridgeBootstrapPublisher");
            if (method == null)
                return;

            TryInvoke(method, new object[] { instrumentRoot, publisher });
        }

        public static bool Publish(BridgeReading reading)
        {
            if (reading == null)
                return false;

            if (!EnsureResolved())
                return false;

            object message = CreateReadingMessage(reading);
            if (message == null)
                return false;

            MethodInfo method = _publishMethod;
            if (method == null)
                return false;

            return TryInvoke(method, new object[] { message });
        }

        private static MethodInfo GetMethod(string name)
        {
            if (!EnsureResolved())
                return null;

            switch (name)
            {
                case "Publish":
                    return _publishMethod;
                case "RegisterBridge":
                    return _registerBridgeMethod;
                case "TouchBridge":
                    return _touchBridgeMethod;
                case "UnregisterBridge":
                    return _unregisterBridgeMethod;
                case "RegisterBridgeBootstrapPublisher":
                    return _registerBootstrapPublisherMethod;
                case "UnregisterBridgeBootstrapPublisher":
                    return _unregisterBootstrapPublisherMethod;
                default:
                    return null;
            }
        }

        private static bool EnsureResolved()
        {
            lock (SyncRoot)
            {
                DateTime nowUtc = DateTime.UtcNow;
                bool hasResolvedBus = _busType != null && _readingType != null && _publishMethod != null;
                if (_lastResolveUtc != DateTime.MinValue && (nowUtc - _lastResolveUtc) < ResolveRetryInterval)
                    return hasResolvedBus;

                _lastResolveUtc = nowUtc;

                Type busType = ResolvePreferredBusType();
                Type readingType = ResolveReadingTypeForBus(busType);
                if (busType == null || readingType == null)
                {
                    ClearResolvedTypes();
                    return false;
                }

                MethodInfo publish = busType.GetMethod("Publish", BindingFlags.Public | BindingFlags.Static);
                MethodInfo registerBridge = busType.GetMethod("RegisterBridge", BindingFlags.Public | BindingFlags.Static);
                MethodInfo registerTradeInstrument = busType.GetMethod("RegisterTradeInstrument", BindingFlags.Public | BindingFlags.Static);
                MethodInfo registerTradeInstrumentInstance = busType.GetMethod("RegisterTradeInstrumentInstance", BindingFlags.Public | BindingFlags.Static);
                MethodInfo touchBridge = busType.GetMethod("TouchBridge", BindingFlags.Public | BindingFlags.Static);
                MethodInfo unregisterBridge = busType.GetMethod("UnregisterBridge", BindingFlags.Public | BindingFlags.Static);
                MethodInfo registerPublisher = busType.GetMethod("RegisterBridgeBootstrapPublisher", BindingFlags.Public | BindingFlags.Static);
                MethodInfo unregisterPublisher = busType.GetMethod("UnregisterBridgeBootstrapPublisher", BindingFlags.Public | BindingFlags.Static);

                if (publish == null)
                {
                    ClearResolvedTypes();
                    return false;
                }

                _busType = busType;
                _readingType = readingType;
                _publishMethod = publish;
                _registerBridgeMethod = registerBridge;
                _registerTradeInstrumentMethod = registerTradeInstrument;
                _registerTradeInstrumentInstanceMethod = registerTradeInstrumentInstance;
                _touchBridgeMethod = touchBridge;
                _unregisterBridgeMethod = unregisterBridge;
                _registerBootstrapPublisherMethod = registerPublisher;
                _unregisterBootstrapPublisherMethod = unregisterPublisher;
                return true;
            }
        }

        private static void ClearResolvedTypes()
        {
            _busType = null;
            _readingType = null;
            _publishMethod = null;
            _registerBridgeMethod = null;
            _registerTradeInstrumentMethod = null;
            _registerTradeInstrumentInstanceMethod = null;
            _touchBridgeMethod = null;
            _unregisterBridgeMethod = null;
            _registerBootstrapPublisherMethod = null;
            _unregisterBootstrapPublisherMethod = null;
        }

        private static Type ResolvePreferredBusType()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = assemblies.Length - 1; i >= 0; i--)
            {
                Assembly assembly = assemblies[i];
                if (!IsActiveAddOnAssembly(assembly))
                    continue;

                Type activeBus = assembly.GetType(BusTypeFullName, false);
                if (activeBus != null)
                    return activeBus;
            }

            try
            {
                Assembly currentAssembly = typeof(GlitchBridgeBusCompat).Assembly;
                Type inCurrentAssembly = currentAssembly == null
                    ? null
                    : currentAssembly.GetType(BusTypeFullName, false);
                if (inCurrentAssembly != null)
                    return inCurrentAssembly;
            }
            catch
            {
            }

            for (int i = assemblies.Length - 1; i >= 0; i--)
            {
                Assembly assembly = assemblies[i];
                if (assembly == null)
                    continue;

                Type candidate = assembly.GetType(BusTypeFullName, false);
                if (candidate != null)
                    return candidate;
            }

            return null;
        }

        private static bool IsActiveAddOnAssembly(Assembly assembly)
        {
            if (assembly == null)
                return false;

            try
            {
                Type telemetryType = assembly.GetType(TelemetryServerTypeFullName, false);
                if (telemetryType == null)
                    return false;

                PropertyInfo isRunning = telemetryType.GetProperty(
                    "IsRunning",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (isRunning == null || isRunning.PropertyType != typeof(bool))
                    return false;

                return (bool)isRunning.GetValue(null, null);
            }
            catch
            {
                return false;
            }
        }

        private static Type ResolveReadingTypeForBus(Type busType)
        {
            if (busType == null)
                return null;

            try
            {
                Type inBusAssembly = busType.Assembly == null
                    ? null
                    : busType.Assembly.GetType(ReadingTypeFullName, false);
                if (inBusAssembly != null)
                    return inBusAssembly;
            }
            catch
            {
            }

            return ResolveType(ReadingTypeFullName);
        }

        private static Type ResolveType(string fullName)
        {
            Type resolved = Type.GetType(fullName, false);
            if (resolved != null)
                return resolved;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                if (assembly == null)
                    continue;

                resolved = assembly.GetType(fullName, false);
                if (resolved != null)
                    return resolved;
            }

            return null;
        }

        private static bool TryInvoke(MethodInfo method, object[] args)
        {
            if (method == null)
                return false;

            try
            {
                method.Invoke(null, args);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static object CreateReadingMessage(BridgeReading reading)
        {
            Type readingType = _readingType;
            if (readingType == null)
                return null;

            object message;
            try
            {
                message = Activator.CreateInstance(readingType, true);
            }
            catch
            {
                return null;
            }

            SetProperty(message, "InstrumentRoot", reading.InstrumentRoot);
            SetProperty(message, "InstrumentFullName", reading.InstrumentFullName);
            SetProperty(message, "Minutes", reading.Minutes);
            SetProperty(message, "UtcTime", reading.UtcTime);
            SetProperty(message, "Open", reading.Open);
            SetProperty(message, "High", reading.High);
            SetProperty(message, "Low", reading.Low);
            SetProperty(message, "Volume", reading.Volume);
            SetProperty(message, "CurrentPrice", reading.CurrentPrice);
            SetProperty(message, "AveragePrice", reading.AveragePrice);
            SetProperty(message, "Atr", reading.Atr);
            SetProperty(message, "Adx", reading.Adx);
            SetProperty(message, "Score", reading.Score);
            SetProperty(message, "RawScore", reading.RawScore);
            SetProperty(message, "DirectionalScore", reading.DirectionalScore);
            SetProperty(message, "TradeabilityScore", reading.TradeabilityScore);
            SetProperty(message, "SignalLabel", reading.SignalLabel);
            SetProperty(message, "VolatilityHint", reading.VolatilityHint);
            SetProperty(message, "TrendHint", reading.TrendHint);
            SetProperty(message, "RegimeLabel", reading.RegimeLabel);
            SetProperty(message, "NoTradeReasons", reading.NoTradeReasons);
            SetProperty(message, "Rsi", reading.Rsi);
            SetProperty(message, "StochK", reading.StochK);
            SetProperty(message, "ZScore", reading.ZScore);
            SetProperty(message, "DiPlus", reading.DiPlus);
            SetProperty(message, "DiMinus", reading.DiMinus);
            SetProperty(message, "Cci", reading.Cci);
            SetProperty(message, "MacdHistogram", reading.MacdHistogram);
            SetProperty(message, "EmaAlignment", reading.EmaAlignment);
            SetProperty(message, "RegimeWeight", reading.RegimeWeight);
            SetProperty(message, "OscillatorCompositeScore", reading.OscillatorCompositeScore);
            SetProperty(message, "MaCompositeScore", reading.MaCompositeScore);
            SetProperty(message, "OrderFlowScore", reading.OrderFlowScore);
            SetProperty(message, "OrderFlowConfidence", reading.OrderFlowConfidence);
            SetProperty(message, "OrderFlowReliability", reading.OrderFlowReliability);
            SetProperty(message, "OrderFlowCumulativeDelta", reading.OrderFlowCumulativeDelta);
            SetProperty(message, "OrderFlowDeltaChange", reading.OrderFlowDeltaChange);
            SetProperty(message, "OrderFlowVwap", reading.OrderFlowVwap);
            SetProperty(message, "OrderFlowVwapDeviation", reading.OrderFlowVwapDeviation);
            SetProperty(message, "OrderFlowAggressionBalance", reading.OrderFlowAggressionBalance);
            SetProperty(message, "OrderFlowDepthImbalance", reading.OrderFlowDepthImbalance);
            SetProperty(message, "OrderFlowHint", reading.OrderFlowHint);
            SetProperty(message, "SessionName", reading.SessionName);
            SetProperty(message, "SessionHigh", reading.SessionHigh);
            SetProperty(message, "SessionLow", reading.SessionLow);
            SetProperty(message, "PreviousSessionHigh", reading.PreviousSessionHigh);
            SetProperty(message, "PreviousSessionLow", reading.PreviousSessionLow);

            return message;
        }

        private static void SetProperty(object target, string propertyName, object value)
        {
            if (target == null || string.IsNullOrWhiteSpace(propertyName))
                return;

            PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property == null || !property.CanWrite)
                return;

            try
            {
                object converted = ConvertValue(value, property.PropertyType);
                property.SetValue(target, converted, null);
            }
            catch
            {
            }
        }

        private static object ConvertValue(object value, Type destinationType)
        {
            Type nonNullableType = Nullable.GetUnderlyingType(destinationType) ?? destinationType;

            if (value == null)
            {
                if (Nullable.GetUnderlyingType(destinationType) != null || !destinationType.IsValueType)
                    return null;

                return Activator.CreateInstance(destinationType);
            }

            if (nonNullableType.IsInstanceOfType(value))
                return value;

            if (nonNullableType == typeof(string))
                return value.ToString();

            if (nonNullableType == typeof(DateTime))
            {
                if (value is DateTime)
                    return (DateTime)value;

                DateTime parsedDateTime;
                if (DateTime.TryParse(value.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out parsedDateTime))
                    return parsedDateTime;

                return DateTime.MinValue;
            }

            if (nonNullableType.IsEnum)
                return Enum.Parse(nonNullableType, value.ToString(), true);

            return Convert.ChangeType(value, nonNullableType, CultureInfo.InvariantCulture);
        }
    }
}
