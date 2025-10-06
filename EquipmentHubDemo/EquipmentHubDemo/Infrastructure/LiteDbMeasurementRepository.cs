using System;
using System.IO;
using EquipmentHubDemo.Domain;
using LiteDB;

namespace EquipmentHubDemo.Infrastructure
{
    public sealed class LiteDbMeasurementRepository : IMeasurementRepository, IDisposable
    {
        private readonly string _path;
        private LiteDatabase _db = default!;
        private ILiteCollection<FilteredMeasurement> _history = default!;
        private ILiteCollection<LatestDoc> _latest = default!;

        public LiteDbMeasurementRepository(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Database path must be provided.", nameof(path));

            // --- Path normalization: allow callers to pass a directory ---
            var full = Path.GetFullPath(path);
            if (Directory.Exists(full) || IsDirectoryLike(full))
            {
                full = Path.Combine(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                                    "measurements.db");
            }
            _path = full;

            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // First open/ensure; if corruption shows up, hard reset once and retry.
            try
            {
                OpenDb();
                EnsureIndexes();
            }
            catch (LiteException ex) when (IsCorruption(ex))
            {
                HardResetDatabase();
                OpenDb();
                EnsureIndexes();
            }
        }

        // ---------- Public API ----------
        public void AppendHistory(FilteredMeasurement f)
        {
            if (f is null) throw new ArgumentNullException(nameof(f));
            _history.Insert(f);
        }

        public void UpsertLatest(FilteredMeasurement f)
        {
            if (f is null) throw new ArgumentNullException(nameof(f));

            var doc = new LatestDoc
            {
                Id = $"{f.Key.InstrumentId}:{f.Key.Metric}",
                InstrumentId = f.Key.InstrumentId,
                Metric = f.Key.Metric,
                Value = f.Value,
                TimestampUtc = f.TimestampUtc
            };
            _latest.Upsert(doc);
        }

        public int DeleteHistoryOlderThan(DateTime cutoffUtc)
        {
            try
            {
                return _history.DeleteMany(h => h.TimestampUtc < cutoffUtc);
            }
            catch (LiteException ex) when (IsCorruption(ex))
            {
                HardResetDatabase();
                return 0;
            }
        }

        public void Dispose() => _db?.Dispose();

        // ---------- Internals ----------
        private void OpenDb()
        {
            var cs = $"Filename={_path};Connection=direct;Upgrade=true;UtcDate=true";
            _db = new LiteDatabase(cs);
            _history = _db.GetCollection<FilteredMeasurement>("history");
            _latest = _db.GetCollection<LatestDoc>("latest");
        }

        private void EnsureIndexes()
        {
            _history.EnsureIndex(x => x.Key.InstrumentId);
            _history.EnsureIndex(x => x.Key.Metric);
            _history.EnsureIndex(x => x.TimestampUtc);

            _latest.EnsureIndex(x => x.InstrumentId);
            _latest.EnsureIndex(x => x.Metric);
        }

        private void HardResetDatabase()
        {
            try { _db?.Dispose(); } catch { /* ignore */ }

            var bak = _path + ".corrupt." + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".bak";
            try
            {
                if (File.Exists(_path)) File.Move(_path, bak);
                // also clear LiteDB -log file if present
                var log = _path + "-log";
                if (File.Exists(log)) File.Delete(log);
            }
            catch
            {
                try { if (File.Exists(_path)) File.Delete(_path); } catch { /* ignore */ }
                try { var log = _path + "-log"; if (File.Exists(log)) File.Delete(log); } catch { }
            }
        }

        private static bool IsCorruption(LiteException ex)
        {
            var msg = ex.Message ?? string.Empty;
            return msg.IndexOf("Page type must be data page", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("page type must be collection page", StringComparison.OrdinalIgnoreCase) >= 0
                || msg.IndexOf("Detected loop in FindAll", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsDirectoryLike(string fullPath)
        {
            // If ends with a separator, or has no extension and doesn't exist as a file, treat as directory-like input.
            var looksLikeDir = fullPath.EndsWith(Path.DirectorySeparatorChar) || fullPath.EndsWith(Path.AltDirectorySeparatorChar);
            var hasExt = Path.HasExtension(fullPath);
            var fileExists = File.Exists(fullPath);
            return looksLikeDir || (!hasExt && !fileExists);
        }
    }
}