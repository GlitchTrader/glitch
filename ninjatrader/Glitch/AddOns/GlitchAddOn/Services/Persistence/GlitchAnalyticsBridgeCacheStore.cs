using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Glitch.UI;

namespace Glitch.Services
{
    internal static class GlitchAnalyticsBridgeCacheStore
    {
        private const string CacheFileName = "AnalyticsBridgeCache.json";

        internal sealed class PersistedInstrumentFeed
        {
            public string InstrumentRoot { get; set; }
            public string InstrumentFullName { get; set; }
            public DateTime LastUpdatedUtc { get; set; }
            public double? CurrentPrice { get; set; }
            public string SessionName { get; set; }
            public double? SessionHigh { get; set; }
            public double? SessionLow { get; set; }
            public double? PreviousSessionHigh { get; set; }
            public double? PreviousSessionLow { get; set; }
            public List<GlitchIndicatorReading> Readings { get; set; }
        }

        internal sealed class PersistedFeedDocument
        {
            public List<PersistedInstrumentFeed> Instruments { get; set; }
        }

        public static string GetCacheFilePath()
        {
            return GlitchStateStore.GetDefaultPath(CacheFileName);
        }

        public static List<PersistedInstrumentFeed> Load()
        {
            string path = GetCacheFilePath();
            if (!File.Exists(path))
                return new List<PersistedInstrumentFeed>();

            try
            {
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                    return new List<PersistedInstrumentFeed>();

                object deserialized = DeserializeJson(json);
                PersistedFeedDocument document = deserialized as PersistedFeedDocument;
                if (document?.Instruments == null || document.Instruments.Count == 0)
                    return new List<PersistedInstrumentFeed>();

                return document.Instruments;
            }
            catch
            {
                return new List<PersistedInstrumentFeed>();
            }
        }

        public static void Save(IEnumerable<PersistedInstrumentFeed> instruments)
        {
            if (instruments == null)
                return;

            var list = new List<PersistedInstrumentFeed>();
            foreach (PersistedInstrumentFeed feed in instruments)
            {
                if (feed == null || string.IsNullOrWhiteSpace(feed.InstrumentRoot))
                    continue;
                if (feed.Readings == null || feed.Readings.Count == 0)
                    continue;

                list.Add(feed);
            }

            if (list.Count == 0)
                return;

            string path = GetCacheFilePath();
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            string tempPath = path + ".tmp";
            string json = SerializeJson(new PersistedFeedDocument { Instruments = list });
            File.WriteAllText(tempPath, json);
            if (File.Exists(path))
                File.Delete(path);
            File.Move(tempPath, path);
        }

        private static string SerializeJson(object value)
        {
            if (value == null)
                return "{}";

            Type serializerType = ResolveJsonSerializerType();
            if (serializerType == null)
                return "{}";

            try
            {
                object serializer = Activator.CreateInstance(serializerType);
                MethodInfo serialize = serializerType.GetMethod("Serialize", new[] { typeof(object) });
                if (serialize == null)
                    return "{}";

                return serialize.Invoke(serializer, new[] { value }) as string ?? "{}";
            }
            catch
            {
                return "{}";
            }
        }

        private static object DeserializeJson(string json)
        {
            Type serializerType = ResolveJsonSerializerType();
            if (serializerType == null)
                return null;

            try
            {
                object serializer = Activator.CreateInstance(serializerType);
                MethodInfo deserialize = serializerType.GetMethod(
                    "Deserialize",
                    new[] { typeof(string), typeof(Type) });
                if (deserialize == null)
                    return null;

                return deserialize.Invoke(serializer, new object[] { json, typeof(PersistedFeedDocument) });
            }
            catch
            {
                return null;
            }
        }

        private static Type ResolveJsonSerializerType()
        {
            string[] candidates =
            {
                "System.Web.Script.Serialization.JavaScriptSerializer, System.Web.Extensions, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35",
                "System.Web.Script.Serialization.JavaScriptSerializer, System.Web.Extensions"
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                try
                {
                    Type serializerType = Type.GetType(candidates[i], false);
                    if (serializerType != null)
                        return serializerType;
                }
                catch
                {
                }
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                try
                {
                    Type serializerType = assemblies[i].GetType(
                        "System.Web.Script.Serialization.JavaScriptSerializer",
                        false,
                        false);
                    if (serializerType != null)
                        return serializerType;
                }
                catch
                {
                }
            }

            return null;
        }
    }
}
