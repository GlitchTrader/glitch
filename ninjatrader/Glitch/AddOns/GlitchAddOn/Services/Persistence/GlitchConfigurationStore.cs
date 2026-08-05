using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Glitch.Services
{
    /// <summary>
    /// Single durable User configuration authority. Native state and receipts
    /// never enter this document. Legacy TSV files are migration inputs only.
    /// </summary>
    public static class GlitchConfigurationStore
    {
        public const string FileName = "Configuration.v1.tsv";
        private const string VersionRow = "V\tglitch.configuration.v1";
        private static readonly object Gate = new object();

        public static bool IsCanonicalPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path)
                && string.Equals(
                    Path.GetFileName(path), FileName, StringComparison.OrdinalIgnoreCase);
        }

        public static IReadOnlyList<string> LoadPolicyRows(
            string path,
            out bool recoveredFromBackup)
        {
            return LoadSection(path, "P", true, out recoveredFromBackup);
        }

        public static IReadOnlyList<string> LoadAccountOverrideRows(
            string path,
            out bool recoveredFromBackup)
        {
            return LoadSection(path, "A", true, out recoveredFromBackup);
        }

        public static IReadOnlyList<string> LoadAccountGroupRows(
            string path,
            out bool recoveredFromBackup)
        {
            bool recovered;
            List<string> document = LoadDocument(path, out recovered);
            recoveredFromBackup = recovered;
            return document.Where(value => IsRow(value, "G") || IsRow(value, "M"))
                .ToArray();
        }

        public static void SavePolicyRows(string path, IEnumerable<string> rows)
        {
            SaveSection(path, new[] { "P" }, PrefixRows("P", rows));
        }

        public static void SaveAccountOverrideRows(string path, IEnumerable<string> rows)
        {
            SaveSection(path, new[] { "A" }, PrefixRows("A", rows));
        }

        public static void SaveAccountGroupRows(string path, IEnumerable<string> rows)
        {
            SaveSection(
                path,
                new[] { "G", "M" },
                DataRows(rows).Where(value => IsRow(value, "G") || IsRow(value, "M")));
        }

        private static IReadOnlyList<string> LoadSection(
            string path,
            string rowType,
            bool removePrefix,
            out bool recoveredFromBackup)
        {
            List<string> document = LoadDocument(path, out recoveredFromBackup);
            IEnumerable<string> rows = document.Where(value => IsRow(value, rowType));
            if (removePrefix)
                rows = rows.Select(value => value.Substring(2));
            return rows.ToArray();
        }

        private static List<string> LoadDocument(string path, out bool recoveredFromBackup)
        {
            lock (Gate)
            {
                EnsureMigrated(path);
                return ReadValidatedWithBackup(path, out recoveredFromBackup);
            }
        }

        private static void SaveSection(
            string path,
            IEnumerable<string> replacedTypes,
            IEnumerable<string> replacementRows)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A configuration path is required.", nameof(path));
            lock (Gate)
            {
                EnsureMigrated(path);
                List<string> document = ReadValidatedWithBackup(
                    path, out bool recoveredFromBackup);
                var replaced = new HashSet<string>(
                    replacedTypes ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                var next = new List<string> { VersionRow };
                next.AddRange(document.Skip(1).Where(value =>
                    !replaced.Contains(RowType(value))));
                next.AddRange(replacementRows ?? Array.Empty<string>());
                Validate(next);
                Write(path, next, recoveredFromBackup);
            }
        }

        private static IEnumerable<string> PrefixRows(
            string rowType,
            IEnumerable<string> rows)
        {
            return DataRows(rows).Select(value => rowType + "\t" + value);
        }

        private static IEnumerable<string> DataRows(IEnumerable<string> rows)
        {
            return (rows ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Where(value => !value.StartsWith("#", StringComparison.Ordinal));
        }

        private static void EnsureMigrated(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A configuration path is required.", nameof(path));
            if (File.Exists(path) || File.Exists(path + ".bak"))
                return;

            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            var document = new List<string> { VersionRow };
            document.AddRange(ReadLegacy(directory, "RuntimePolicy.tsv")
                .Select(value => "P\t" + value));
            document.AddRange(ReadLegacy(directory, "AccountOverrides.tsv")
                .Select(value => "A\t" + value));
            document.AddRange(ReadLegacy(directory, "AccountGroups.tsv")
                .Where(value => IsRow(value, "G") || IsRow(value, "M")));
            Validate(document);
            Write(path, document);
        }

        private static IEnumerable<string> ReadLegacy(string directory, string fileName)
        {
            if (string.IsNullOrWhiteSpace(directory))
                return Array.Empty<string>();
            string path = Path.Combine(directory, fileName);
            if (!File.Exists(path))
                return Array.Empty<string>();
            return File.ReadAllLines(path, Encoding.UTF8)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Where(value => !value.StartsWith("#", StringComparison.Ordinal))
                .ToArray();
        }

        private static List<string> ReadValidatedWithBackup(
            string path,
            out bool recoveredFromBackup)
        {
            recoveredFromBackup = false;
            try
            {
                List<string> primary = Read(path);
                Validate(primary);
                return primary;
            }
            catch
            {
                string backupPath = path + ".bak";
                if (!File.Exists(backupPath))
                    throw;
                List<string> backup = Read(backupPath);
                Validate(backup);
                recoveredFromBackup = true;
                return backup;
            }
        }

        private static List<string> Read(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Configuration document is missing.", path);
            return File.ReadAllLines(path, Encoding.UTF8)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Where(value => !value.StartsWith("#", StringComparison.Ordinal))
                .ToList();
        }

        private static void Validate(IReadOnlyList<string> rows)
        {
            if (rows == null || rows.Count == 0
                || !string.Equals(rows[0], VersionRow, StringComparison.Ordinal))
                throw new InvalidDataException("Configuration schema is missing or unsupported.");
            if (rows.Skip(1).Any(value => IsRow(value, "V")))
                throw new InvalidDataException("Configuration contains more than one version row.");

            foreach (string row in rows.Skip(1))
            {
                string type = RowType(row);
                int fields = row.Split('\t').Length;
                bool valid = (type == "P" && fields >= 3)
                    || (type == "A" && fields >= 4)
                    || (type == "G" && fields >= 4)
                    || (type == "M" && fields >= 7);
                if (!valid)
                    throw new InvalidDataException(
                        "Configuration contains an invalid " + type + " row.");
            }
        }

        private static void Write(
            string path,
            IEnumerable<string> rows,
            bool preserveExistingBackup = false)
        {
            string[] document = GlitchStateStore.WithTsvBanner(new[]
                {
                    "# schema\tglitch.configuration.v1",
                    "# P=policy A=manual-account G=group M=group-member"
                }.Concat(rows ?? Array.Empty<string>()));
            if (preserveExistingBackup)
            {
                GlitchStateStore.WriteAllLinesAtomicPreservingBackup(
                    path, document, new UTF8Encoding(false));
                return;
            }

            GlitchStateStore.WriteAllLinesAtomic(
                path, document, new UTF8Encoding(false));
        }

        private static bool IsRow(string value, string type)
        {
            return string.Equals(RowType(value), type, StringComparison.OrdinalIgnoreCase);
        }

        private static string RowType(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            int separator = value.IndexOf('\t');
            return (separator < 0 ? value : value.Substring(0, separator)).Trim();
        }
    }
}
