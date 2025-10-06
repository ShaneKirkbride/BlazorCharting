using System;
using System.Threading;
using System.Threading.Tasks;
using EquipmentHubDemo.Domain;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EquipmentHubDemo.Workers
{
    /// <summary>Deletes old history rows on a fixed cadence.</summary>
    public sealed class TtlWorker : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

        private readonly IMeasurementRepository _repo;
        private readonly TimeProvider _time;
        private readonly ILogger<TtlWorker> _log;
        private readonly TimeSpan _historyRetention;
        private readonly Random _rng;

        public TtlWorker(
            IMeasurementRepository repo,
            TimeProvider time,
            IOptions<TtlWorkerOptions> options,
            ILogger<TtlWorker> log)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _time = time ?? throw new ArgumentNullException(nameof(time));
            _log = log ?? throw new ArgumentNullException(nameof(log));

            if (options?.Value is null)
                throw new ArgumentException("Options value cannot be null.", nameof(options));

            if (options.Value.HistoryRetention <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(options), options.Value.HistoryRetention, "HistoryRetention must be positive.");

            _historyRetention = options.Value.HistoryRetention;
            _rng = Random.Shared;
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            // Small startup delay to avoid hot-reload/host churn races
            try { await Task.Delay(StartupDelay, ct); } catch (OperationCanceledException) { return; }

            // Add a small jitter to avoid synchronized deletes across instances
            var jitter = TimeSpan.FromMilliseconds(_rng.Next(0, 750));
            try { await Task.Delay(jitter, ct); } catch (OperationCanceledException) { return; }

            using var timer = new PeriodicTimer(Interval);
            var backoff = TimeSpan.Zero;

            while (true)
            {
                try
                {
                    // If we’re backing off, wait that additional time before the next run
                    if (backoff > TimeSpan.Zero)
                    {
                        _log.LogWarning("TTL worker backing off for {Backoff} after previous error.", backoff);
                        await Task.Delay(backoff, ct);
                    }

                    var nowUtc = _time.GetUtcNow().UtcDateTime;
                    var cutoffUtc = nowUtc - _historyRetention;

                    var deleted = _repo.DeleteHistoryOlderThan(cutoffUtc);
                    if (deleted > 0)
                    {
                        _log.LogInformation("TTL deleted {Count} history docs older than {Cutoff:o}.", deleted, cutoffUtc);
                    }

                    // success: reset backoff
                    backoff = TimeSpan.Zero;
                }
                catch (OperationCanceledException)
                {
                    // normal shutdown
                    break;
                }
                catch (Exception ex)
                {
                    // Don’t crash the host on TTL errors (e.g., LiteDB corruption — repo handles reset)
                    _log.LogError(ex, "TTL pass failed. Will retry with backoff.");
                    backoff = backoff == TimeSpan.Zero
                        ? TimeSpan.FromSeconds(2)
                        : TimeSpan.FromMilliseconds(Math.Min(backoff.TotalMilliseconds * 2, MaxBackoff.TotalMilliseconds));
                }

                // Wait until next tick or cancellation
                try
                {
                    if (!await timer.WaitForNextTickAsync(ct))
                        break; // timer completed (shouldn’t happen)
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
