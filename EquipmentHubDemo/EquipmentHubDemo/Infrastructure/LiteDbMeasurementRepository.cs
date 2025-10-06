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

            // Normalize: allow directory-like inputs and append a filename
            var full = Path.GetFullPath(path);
            if (IsDirectoryLike(full)) full = Path.Combine(full, "measurements.db");
            _path = full;

            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // First open + ensure; on corruption, hard reset and retry once
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
                // DB got corrupted later (e.g., hot reload mid-write) — archive & recreate
                HardResetDatabase();
                OpenDb();
                EnsureIndexes();
                return 0;
            }
        }

        public void Dispose() => _db?.Dispose();

        // ---------------- Internals ----------------

        private void OpenDb()
        {
            // Single-process/host access; auto-upgrade; store as UTC
            var cs = $"Filename={_path};Connection=direct;Upgrade=true;UtcDate=true";
            _db = new LiteDatabase(cs);
            _history = _db.GetCollection<FilteredMeasurement>("history");
            _latest = _db.GetCollection<LatestDoc>("latest");
        }

        private void EnsureIndexes()
        {
            // History indexes for queries
            _history.EnsureIndex(x => x.Key.InstrumentId);
            _history.EnsureIndex(x => x.Key.Metric);
            _history.EnsureIndex(x => x.TimestampUtc);

            // Latest (Id is unique natural key; add query helpers)
            _latest.EnsureIndex(x => x.InstrumentId);
            _latest.EnsureIndex(x => x.Metric);
        }

        private void HardResetDatabase()
        {
            try { _db?.Dispose(); } catch { /* ignore */ }

            // Archive corrupted db if present
            try
            {
                if (File.Exists(_path))
                {
                    var bak = _path + ".corrupt." + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".bak";
                    File.Move(_path, bak);
                }
            }
            catch
            {
                try { if (File.Exists(_path)) File.Delete(_path); } catch { /* ignore */ }
            }

            // Clean sidecar files that can hold bad WAL/index state
            TryDeleteSidecar(_path + "-log");
            TryDeleteSidecar(_path + "-lock");
            TryDeleteSidecar(_path + "-journal");
        }

        private static void TryDeleteSidecar(string p)
        {
            try { if (File.Exists(p)) File.Delete(p); } catch { /* ignore */ }
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
            if (fullPath.EndsWith(Path.DirectorySeparatorChar) ||
                fullPath.EndsWith(Path.AltDirectorySeparatorChar))
                return true;

            // treat as directory when there's no extension and it doesn't exist as a file
            return !Path.HasExtension(fullPath) && !File.Exists(fullPath);
        }
    }
}
