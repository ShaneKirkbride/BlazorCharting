using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EquipmentHubDemo.Components.Streaming;
using EquipmentHubDemo.Domain;
using EquipmentHubDemo.Domain.Live;
using EquipmentHubDemo.Domain.Monitoring;
using EquipmentHubDemo.Domain.Predict;
using EquipmentHubDemo.Domain.Status;
using Microsoft.AspNetCore.Components;

namespace EquipmentHubDemo.Components.Pages;

public sealed partial class Home : ComponentBase, IAsyncDisposable
{
    // Define grid layout behavior (n x n charts)
    private const int DefaultGridCols = 3; // change this for 2x2, 4x4, etc.
    private int GridCols => DefaultGridCols;
    private int MaxChartsDisplayed => GridCols * GridCols;

    private readonly ChartStreamManager _streamManager = new();
    private readonly List<string> _availableKeys = new();
    private readonly List<string> _pinnedKeys = new();

    private MeasurementCatalog _catalog = MeasurementCatalog.Empty;
    private string _instrumentSearch = string.Empty;
    private string? _activeInstrumentId;

    private IReadOnlyList<string> _selectedKeys = Array.Empty<string>();
    private IReadOnlyList<PredictiveStatus> _predictiveStatuses = Array.Empty<PredictiveStatus>();
    private IReadOnlyList<MonitoringStatus> _monitoringStatuses = Array.Empty<MonitoringStatus>();
    private string? _selectionFeedback;
    private bool _selectionFeedbackIsWarning;

    private CancellationTokenSource? _cts;
    private Task? _pollingTask;
    private CancellationTokenSource? _keyRefreshCts;
    private Task? _keyRefreshTask;
    private CancellationTokenSource? _statusRefreshCts;
    private Task? _statusRefreshTask;

    private string? error;
    private string? statusError;

    [Inject]
    public required ISystemStatusClient StatusClient { get; set; }

    [Parameter]
    public bool ForceEnableLiveCharts { get; set; }

    private bool SupportsLiveCharts => ForceEnableLiveCharts || OperatingSystem.IsBrowser();

    private string InstrumentSearch
    {
        get => _instrumentSearch;
        set
        {
            _instrumentSearch = value ?? string.Empty;
            EnsureActiveInstrument();
            ClearSelectionFeedback();
        }
    }

    protected override async Task OnInitializedAsync()
    {
        EnsureStatusRefreshLoop();

        if (!SupportsLiveCharts)
        {
            error = "Live charts are available only when running the WebAssembly client.";
            return;
        }

        try
        {
            await TryLoadCatalogAsync(initialLoad: true);
        }
        catch (Exception ex)
        {
            error = "Init failed: " + ex.Message;
        }

        EnsureKeyRefreshLoop();
    }

    private async Task StartPollingAsync()
    {
        await StopPollingAsync();

        if (_selectedKeys.Count == 0)
        {
            return;
        }

        _streamManager.ResetForSelection(_selectedKeys);

        _cts = new CancellationTokenSource();
        _pollingTask = PollLoopAsync(_cts.Token);
        await InvokeAsync(StateHasChanged);
    }

    private async Task StopPollingAsync()
    {
        if (_cts is null)
        {
            return;
        }

        _cts.Cancel();
        var toAwait = _pollingTask;
        _pollingTask = null;

        _cts.Dispose();
        _cts = null;

        if (toAwait is not null)
        {
            try
            {
                await toAwait;
            }
            catch (OperationCanceledException)
            {
                // expected when stopping
            }
        }
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
            while (await timer.WaitForNextTickAsync(ct))
            {
                try
                {
                    var keysSnapshot = _selectedKeys;
                    if (keysSnapshot.Count == 0)
                    {
                        continue;
                    }

                    var hadUpdates = false;

                    foreach (var key in keysSnapshot)
                    {
                        if (!_streamManager.TryGetStream(key, out var stream))
                        {
                            continue;
                        }

                        var batch = await Measurements.GetMeasurementsAsync(key, stream.SinceTicks, ct);

                        if (batch is null || batch.Count == 0)
                        {
                            continue;
                        }

                        if (stream.Apply(batch))
                        {
                            hadUpdates = true;
                        }
                    }

                    if (hadUpdates)
                    {
                        await InvokeAsync(StateHasChanged);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    error ??= "Polling error: " + ex.Message;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // loop cancelled -> exit silently
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopPollingAsync();
        await StopKeyRefreshLoopAsync();
        await StopStatusRefreshLoopAsync();
    }

    private async Task<bool> TryLoadCatalogAsync(bool initialLoad)
    {
        var latestCatalog = await Measurements.GetCatalogAsync();
        var hasKeys = ApplyCatalog(latestCatalog, initialLoad);

        if (!hasKeys)
        {
            await StopPollingAsync();
            error = "Waiting for live data…";
            _selectedKeys = Array.Empty<string>();
            _streamManager.Clear();
            StateHasChanged();
            return false;
        }

        error = null;

        var selectionChanged = UpdateSelectionFromAvailableKeys();

        if (initialLoad || selectionChanged)
        {
            await StartPollingAsync();
        }
        else
        {
            StateHasChanged();
        }

        return true;
    }

    private bool ApplyCatalog(MeasurementCatalog? catalog, bool initialLoad)
    {
        _catalog = NormalizeCatalog(catalog);
        var keys = _catalog.GetAllKeys();

        var hadKeysBefore = _availableKeys.Count > 0;

        _availableKeys.Clear();
        _availableKeys.AddRange(keys);

        SyncPinnedKeys(keys);

        if ((_pinnedKeys.Count == 0 && keys.Count > 0) && (initialLoad || !hadKeysBefore))
        {
            SeedPinnedKeysFromCatalog();
        }
        else if (_pinnedKeys.Count == 0 && keys.Count > 0)
        {
            foreach (var key in keys.Take(MaxChartsDisplayed))
            {
                ForcePinKey(key);
            }
        }

        EnsureActiveInstrument();

        return _availableKeys.Count > 0;
    }

    private static MeasurementCatalog NormalizeCatalog(MeasurementCatalog? catalog)
    {
        if (catalog is null)
        {
            return MeasurementCatalog.Empty;
        }

        var instruments = new List<InstrumentSlice>();

        foreach (var instrument in catalog.Instruments ?? Array.Empty<InstrumentSlice>())
        {
            if (instrument is null)
            {
                continue;
            }

            var metrics = new List<MetricSlice>();
            if (instrument.Metrics is not null)
            {
                foreach (var metric in instrument.Metrics)
                {
                    if (metric is null || string.IsNullOrWhiteSpace(metric.Key))
                    {
                        continue;
                    }

                    var isPreferred = metric.IsPreferred || LiveCatalogPreferences.IsPreferredMetric(metric.Metric);
                    var displayName = string.IsNullOrWhiteSpace(metric.DisplayName)
                        ? (string.IsNullOrWhiteSpace(metric.Metric) ? metric.Key : metric.Metric)
                        : metric.DisplayName;

                    metrics.Add(new MetricSlice
                    {
                        Key = metric.Key,
                        DisplayName = displayName,
                        Metric = metric.Metric ?? string.Empty,
                        IsPreferred = isPreferred
                    });
                }
            }

            if (metrics.Count == 0)
            {
                continue;
            }

            var displayLabel = string.IsNullOrWhiteSpace(instrument.DisplayName)
                ? instrument.Id ?? string.Empty
                : instrument.DisplayName;

            instruments.Add(new InstrumentSlice
            {
                Id = instrument.Id ?? string.Empty,
                DisplayName = displayLabel,
                Metrics = metrics
                    .OrderByDescending(m => m.IsPreferred)
                    .ThenBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            });
        }

        var ordered = instruments
            .OrderBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new MeasurementCatalog
        {
            Instruments = ordered
        };
    }

    private void SyncPinnedKeys(IReadOnlyList<string> availableKeys)
    {
        if (_pinnedKeys.Count == 0)
        {
            return;
        }

        var known = new HashSet<string>(availableKeys, StringComparer.Ordinal);
        for (var i = _pinnedKeys.Count - 1; i >= 0; i--)
        {
            if (!known.Contains(_pinnedKeys[i]))
            {
                _pinnedKeys.RemoveAt(i);
            }
        }
    }

    private void SeedPinnedKeysFromCatalog()
    {
        foreach (var metric in _catalog.Instruments.SelectMany(instrument => instrument.Metrics))
        {
            if (!metric.IsPreferred)
            {
                continue;
            }

            ForcePinKey(metric.Key);

            if (_pinnedKeys.Count >= MaxChartsDisplayed)
            {
                return;
            }
        }

        if (_pinnedKeys.Count > 0)
        {
            return;
        }

        foreach (var key in _availableKeys.Take(MaxChartsDisplayed))
        {
            ForcePinKey(key);
        }
    }

    private void ForcePinKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (_pinnedKeys.Any(existing => string.Equals(existing, key, StringComparison.Ordinal)))
        {
            return;
        }

        _pinnedKeys.Add(key);
    }

    private bool TryPinKey(string key, out string? failureMessage)
    {
        failureMessage = null;

        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (_pinnedKeys.Any(existing => string.Equals(existing, key, StringComparison.Ordinal)))
        {
            return false;
        }

        if (_pinnedKeys.Count >= MaxChartsDisplayed)
        {
            failureMessage = $"A maximum of {MaxChartsDisplayed} charts can be pinned. Unpin a metric to add another.";
            return false;
        }

        _pinnedKeys.Add(key);
        return true;
    }

    private bool RemovePinnedKey(string key)
    {
        for (var i = 0; i < _pinnedKeys.Count; i++)
        {
            if (string.Equals(_pinnedKeys[i], key, StringComparison.Ordinal))
            {
                _pinnedKeys.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    private void ClearSelectionFeedback()
    {
        _selectionFeedback = null;
        _selectionFeedbackIsWarning = false;
    }

    private void SetSelectionFeedback(string message, bool isWarning = false)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            _selectionFeedback = message;
            _selectionFeedbackIsWarning = isWarning;
        }
    }

    private void EnsureKeyRefreshLoop()
    {
        if (_keyRefreshTask is not null)
        {
            return;
        }

        _keyRefreshCts = new CancellationTokenSource();
        _keyRefreshTask = RefreshKeysLoopAsync(_keyRefreshCts.Token);
    }

    private void EnsureStatusRefreshLoop()
    {
        if (_statusRefreshTask is not null)
        {
            return;
        }

        _statusRefreshCts = new CancellationTokenSource();
        _statusRefreshTask = RefreshStatusLoopAsync(_statusRefreshCts.Token);
    }

    private async Task RefreshKeysLoopAsync(CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(2);
        var firstIteration = true;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (!firstIteration)
                {
                    await Task.Delay(delay, ct);
                }
                firstIteration = false;

                MeasurementCatalog? refreshedCatalog;
                try
                {
                    refreshedCatalog = await Measurements.GetCatalogAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    await InvokeAsync(() =>
                    {
                        error = "Key refresh failed: " + ex.Message;
                        StateHasChanged();
                    });
                    continue;
                }

                await InvokeAsync(async () =>
                {
                    var hadKeysBefore = _availableKeys.Count > 0;
                    var hasKeys = ApplyCatalog(refreshedCatalog, initialLoad: false);

                    if (!hasKeys)
                    {
                        if (hadKeysBefore)
                        {
                            await StopPollingAsync();
                        }

                        error = "Waiting for live data…";
                        _selectedKeys = Array.Empty<string>();
                        _streamManager.Clear();
                        StateHasChanged();
                        return;
                    }

                    error = null;

                    var selectionChanged = UpdateSelectionFromAvailableKeys();

                    if (selectionChanged || !hadKeysBefore)
                    {
                        await StartPollingAsync();
                    }
                    else
                    {
                        StateHasChanged();
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
            // loop cancelled -> exit silently
        }
        finally
        {
            await InvokeAsync(() =>
            {
                _keyRefreshTask = null;
                _keyRefreshCts?.Dispose();
                _keyRefreshCts = null;
            });
        }
    }

    private async Task StopKeyRefreshLoopAsync()
    {
        if (_keyRefreshTask is null)
        {
            return;
        }

        try
        {
            _keyRefreshCts?.Cancel();
            await _keyRefreshTask;
        }
        catch (OperationCanceledException)
        {
            // expected on cancellation
        }
        finally
        {
            _keyRefreshCts?.Dispose();
            _keyRefreshCts = null;
            _keyRefreshTask = null;
        }
    }

    private async Task StopStatusRefreshLoopAsync()
    {
        if (_statusRefreshTask is null)
        {
            return;
        }

        try
        {
            _statusRefreshCts?.Cancel();
            await _statusRefreshTask;
        }
        catch (OperationCanceledException)
        {
            // expected on cancellation
        }
        finally
        {
            _statusRefreshCts?.Dispose();
            _statusRefreshCts = null;
            _statusRefreshTask = null;
        }
    }

    private IEnumerable<ChartStream> GetActiveStreams()
        => _streamManager.GetActiveStreams(_selectedKeys);

    private ChartCardSummary BuildStreamSummary(ChartStream stream)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        var key = stream.Key;

        string instrumentLabel;
        string metricLabel;

        if (MeasureKey.TryParse(key, out var parsed))
        {
            instrumentLabel = string.IsNullOrWhiteSpace(parsed.InstrumentId)
                ? "Unassigned instrument"
                : parsed.InstrumentId;
            metricLabel = string.IsNullOrWhiteSpace(parsed.Metric)
                ? "Telemetry stream"
                : parsed.Metric;
        }
        else
        {
            instrumentLabel = string.IsNullOrWhiteSpace(key)
                ? "Live telemetry stream"
                : key;
            metricLabel = "Live telemetry stream";
        }

        var points = stream.Points;
        var pointCount = points.Count;

        var samplesText = pointCount == 0
            ? "No samples"
            : pointCount.ToString("N0", CultureInfo.CurrentCulture);

        string lastObservedText;
        string latestValueText;

        if (pointCount == 0)
        {
            lastObservedText = "Awaiting samples";
            latestValueText = "—";
        }
        else
        {
            var lastPoint = points[^1];
            lastObservedText = lastPoint.X.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
            latestValueText = lastPoint.Y.ToString("F2", CultureInfo.CurrentCulture);
        }

        return new ChartCardSummary(
            Title: key,
            InstrumentLabel: instrumentLabel,
            MetricLabel: metricLabel,
            SamplesText: samplesText,
            LastObservedText: lastObservedText,
            LatestValueText: latestValueText,
            HeaderId: BuildHeaderId(key));
    }

    private static string BuildHeaderId(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "chart-card";
        }

        var builder = new StringBuilder(key.Length + 10);
        builder.Append("chart-");

        foreach (var c in key)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
            else if (c is '-' or '_')
            {
                builder.Append(c);
            }
            else
            {
                builder.Append('-');
            }
        }

        return builder.ToString();
    }

    private sealed record ChartCardSummary(
        string Title,
        string InstrumentLabel,
        string MetricLabel,
        string SamplesText,
        string LastObservedText,
        string LatestValueText,
        string HeaderId);

    private bool UpdateSelectionFromAvailableKeys()
    {
        var orderedKeys = GetOrderedKeys();
        var sanitizedSelection = new List<string>(MaxChartsDisplayed);

        foreach (var key in _pinnedKeys)
        {
            if (sanitizedSelection.Count >= MaxChartsDisplayed)
            {
                break;
            }

            if (orderedKeys.Contains(key, StringComparer.Ordinal) && !sanitizedSelection.Contains(key, StringComparer.Ordinal))
            {
                sanitizedSelection.Add(key);
            }
        }

        foreach (var key in orderedKeys)
        {
            if (sanitizedSelection.Count >= MaxChartsDisplayed)
            {
                break;
            }

            if (!sanitizedSelection.Contains(key, StringComparer.Ordinal))
            {
                sanitizedSelection.Add(key);
            }
        }

        var selectionChanged = sanitizedSelection.Count != _selectedKeys.Count
            || !_selectedKeys.SequenceEqual(sanitizedSelection, StringComparer.Ordinal);

        _selectedKeys = sanitizedSelection;

        _streamManager.EnsureSelection(_selectedKeys);

        return selectionChanged;
    }

    private IReadOnlyList<string> GetOrderedKeys()
    {
        if (_catalog.Instruments.Count == 0)
        {
            return _availableKeys;
        }

        return _catalog.Instruments
            .SelectMany(instrument => instrument.Metrics)
            .Select(metric => metric.Key)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToList();
    }

    private List<InstrumentSlice> GetFilteredInstruments()
    {
        if (_catalog.Instruments.Count == 0)
        {
            return new List<InstrumentSlice>();
        }

        if (string.IsNullOrWhiteSpace(_instrumentSearch))
        {
            return _catalog.Instruments.ToList();
        }

        var term = _instrumentSearch.Trim();
        return _catalog.Instruments
            .Where(instrument => ContainsIgnoreCase(instrument.DisplayName, term) || ContainsIgnoreCase(instrument.Id, term))
            .ToList();
    }

    private static bool ContainsIgnoreCase(string source, string value)
        => source?.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;

    private InstrumentSlice? GetActiveInstrument()
    {
        if (_catalog.Instruments.Count == 0 || string.IsNullOrWhiteSpace(_activeInstrumentId))
        {
            return null;
        }

        return _catalog.Instruments
            .FirstOrDefault(instrument => string.Equals(instrument.Id, _activeInstrumentId, StringComparison.Ordinal));
    }

    private void SelectInstrument(string instrumentId)
    {
        if (string.Equals(_activeInstrumentId, instrumentId, StringComparison.Ordinal))
        {
            return;
        }

        _activeInstrumentId = instrumentId;
        ClearSelectionFeedback();
    }

    private void EnsureActiveInstrument()
    {
        var filtered = GetFilteredInstruments();
        if (filtered.Count == 0)
        {
            _activeInstrumentId = null;
            return;
        }

        if (_activeInstrumentId is null || filtered.All(instrument => !string.Equals(instrument.Id, _activeInstrumentId, StringComparison.Ordinal)))
        {
            _activeInstrumentId = filtered[0].Id;
        }
    }

    private bool IsMetricPinned(string key)
        => !string.IsNullOrWhiteSpace(key) && _pinnedKeys.Any(existing => string.Equals(existing, key, StringComparison.Ordinal));

    private string BuildMetricCheckboxId(string key)
        => BuildHeaderId(key) + "-slicer";

    private async Task ToggleMetricAsync(string key, bool isSelected)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (isSelected)
        {
            if (!TryPinKey(key, out var failureMessage))
            {
                if (!string.IsNullOrWhiteSpace(failureMessage))
                {
                    SetSelectionFeedback(failureMessage, isWarning: true);
                    StateHasChanged();
                }

                return;
            }

            ClearSelectionFeedback();
        }
        else
        {
            if (RemovePinnedKey(key))
            {
                ClearSelectionFeedback();
            }
        }

        var selectionChanged = UpdateSelectionFromAvailableKeys();

        if (selectionChanged)
        {
            await StartPollingAsync();
        }
        else
        {
            StateHasChanged();
        }
    }

    private async Task PinRecommendedMetricsAsync()
    {
        var instrument = GetActiveInstrument();
        if (instrument is null)
        {
            return;
        }

        var hasPreferred = false;
        var addedAny = false;

        foreach (var metric in instrument.Metrics)
        {
            if (!metric.IsPreferred)
            {
                continue;
            }

            hasPreferred = true;

            if (TryPinKey(metric.Key, out var failureMessage))
            {
                addedAny = true;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(failureMessage))
            {
                SetSelectionFeedback(failureMessage, isWarning: true);
                StateHasChanged();
                return;
            }
        }

        if (!hasPreferred)
        {
            SetSelectionFeedback("This instrument does not expose recommended metrics yet.");
            StateHasChanged();
            return;
        }

        if (!addedAny)
        {
            SetSelectionFeedback("All recommended metrics are already pinned for this instrument.");
            StateHasChanged();
            return;
        }

        ClearSelectionFeedback();

        var selectionChanged = UpdateSelectionFromAvailableKeys();
        if (selectionChanged)
        {
            await StartPollingAsync();
        }
        else
        {
            StateHasChanged();
        }
    }

    private async Task ClearInstrumentPinsAsync()
    {
        var instrument = GetActiveInstrument();
        if (instrument is null)
        {
            return;
        }

        var removedAny = false;

        foreach (var metric in instrument.Metrics)
        {
            removedAny |= RemovePinnedKey(metric.Key);
        }

        if (!removedAny)
        {
            SetSelectionFeedback("No pinned metrics for this instrument yet.");
            StateHasChanged();
            return;
        }

        ClearSelectionFeedback();

        var selectionChanged = UpdateSelectionFromAvailableKeys();
        if (selectionChanged)
        {
            await StartPollingAsync();
        }
        else
        {
            StateHasChanged();
        }
    }

    private string GetActiveSampleCountText()
    {
        var total = _streamManager.CountActivePoints(_selectedKeys);
        return total == 0 ? "Awaiting data" : total.ToString("N0", CultureInfo.CurrentCulture);
    }

    private string BuildChipLabel(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "Telemetry";
        }

        if (MeasureKey.TryParse(key, out var parsed))
        {
            if (string.IsNullOrWhiteSpace(parsed.Metric))
            {
                return parsed.InstrumentId;
            }

            return string.IsNullOrWhiteSpace(parsed.InstrumentId)
                ? parsed.Metric
                : $"{parsed.InstrumentId} · {parsed.Metric}";
        }

        return key;
    }

    private async Task RefreshStatusLoopAsync(CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(5);
        var firstIteration = true;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (!firstIteration)
                {
                    await Task.Delay(delay, ct);
                }

                firstIteration = false;

                IReadOnlyList<PredictiveStatus> predictive;
                IReadOnlyList<MonitoringStatus> monitoring;

                try
                {
                    var predictiveTask = StatusClient.GetPredictiveStatusesAsync(ct);
                    var monitoringTask = StatusClient.GetMonitoringStatusesAsync(ct);

                    predictive = await predictiveTask.ConfigureAwait(false);
                    monitoring = await monitoringTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    await InvokeAsync(() =>
                    {
                        statusError = "Status refresh failed: " + ex.Message;
                        StateHasChanged();
                    });
                    continue;
                }

                await InvokeAsync(() =>
                {
                    _predictiveStatuses = predictive ?? Array.Empty<PredictiveStatus>();
                    _monitoringStatuses = monitoring ?? Array.Empty<MonitoringStatus>();
                    statusError = null;
                    StateHasChanged();
                });
            }
        }
        catch (OperationCanceledException)
        {
            // loop cancelled -> exit silently
        }
        finally
        {
            await InvokeAsync(() =>
            {
                _statusRefreshTask = null;
                _statusRefreshCts?.Dispose();
                _statusRefreshCts = null;
            });
        }
    }

    private IReadOnlyList<PredictiveStatus> GetPredictiveStatuses() => _predictiveStatuses;

    private IReadOnlyList<MonitoringStatus> GetMonitoringStatuses() => _monitoringStatuses;

    private static string FormatAge(TimeSpan? age)
    {
        if (age is null)
        {
            return "n/a";
        }

        var value = age.Value;
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        if (value.TotalSeconds < 60)
        {
            return $"{Math.Round(value.TotalSeconds)}s ago";
        }

        if (value.TotalMinutes < 60)
        {
            return $"{Math.Round(value.TotalMinutes)}m ago";
        }

        if (value.TotalHours < 24)
        {
            return $"{Math.Round(value.TotalHours)}h ago";
        }

        return $"{Math.Round(value.TotalDays)}d ago";
    }

    private static readonly IReadOnlyList<StatusBadgeViewModel> LegendBadges = new[]
    {
        CreateBadge(StatusSeverity.Critical, "Immediate service required for unsafe conditions."),
        CreateBadge(StatusSeverity.Warning, "Maintenance scheduled soon; monitor closely."),
        CreateBadge(StatusSeverity.Nominal, "Systems operating within expected limits."),
    };

    private static IReadOnlyList<StatusBadgeViewModel> GetLegendBadges() => LegendBadges;

    private static StatusBadgeViewModel BuildMaintenanceBadge(PredictiveStatus status)
    {
        var severity = DetermineMaintenanceSeverity(status);
        var (action, scheduledFor) = GetPrimaryPlan(status);

        var probability = status.Insight.FailureProbability.ToString("P0", CultureInfo.InvariantCulture);
        var scheduleText = DescribePlanSchedule(action, scheduledFor);

        var description = severity switch
        {
            StatusSeverity.Critical => $"High failure risk at {probability}; {scheduleText}.",
            StatusSeverity.Warning => $"Watch conditions ({probability}); {scheduleText}.",
            _ => $"Stable trend ({probability}); {scheduleText}."
        };

        return CreateBadge(
            severity,
            description,
            $"{status.InstrumentId} {status.Metric}: {description}");
    }

    private static StatusBadgeViewModel BuildMonitoringBadge(MonitoringStatus status)
    {
        var severity = status.Health switch
        {
            MonitoringHealth.Missing => StatusSeverity.Critical,
            MonitoringHealth.Stale => StatusSeverity.Warning,
            _ => StatusSeverity.Nominal
        };

        var lastReading = status.Age is null
            ? "No readings received yet."
            : $"Last reading {FormatAge(status.Age)}.";

        var description = severity switch
        {
            StatusSeverity.Critical => $"Telemetry unavailable. {lastReading}",
            StatusSeverity.Warning => $"Telemetry stale. {lastReading}",
            _ => $"Telemetry current. {lastReading}"
        };

        return CreateBadge(
            severity,
            description,
            $"{status.InstrumentId} {status.Metric}: {description}");
    }

    private static StatusSeverity DetermineMaintenanceSeverity(PredictiveStatus status)
    {
        var (action, scheduledFor) = GetPrimaryPlan(status);
        var now = DateTime.UtcNow;
        var untilAction = scheduledFor - now;

        if (untilAction <= TimeSpan.Zero || status.Insight.FailureProbability >= 0.6)
        {
            return StatusSeverity.Critical;
        }

        if (status.Insight.FailureProbability >= 0.3 || untilAction <= TimeSpan.FromDays(1))
        {
            return StatusSeverity.Warning;
        }

        return StatusSeverity.Nominal;
    }

    private static (string Action, DateTime ScheduledUtc) GetPrimaryPlan(PredictiveStatus status)
    {
        if (status.RepairPlan.ScheduledFor <= status.ServicePlan.ScheduledFor)
        {
            return (status.RepairPlan.Action, status.RepairPlan.ScheduledFor);
        }

        return (status.ServicePlan.Action, status.ServicePlan.ScheduledFor);
    }

    private static string DescribePlanSchedule(string action, DateTime scheduledUtc)
    {
        var relative = DescribeRelativeTime(scheduledUtc);
        var local = scheduledUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        var normalizedAction = string.IsNullOrWhiteSpace(action) ? "maintenance" : action.ToLowerInvariant();

        return $"{normalizedAction} {relative} ({local})";
    }

    private static string DescribeRelativeTime(DateTime momentUtc)
    {
        var delta = momentUtc - DateTime.UtcNow;
        var magnitude = DescribeDuration(delta);

        return delta >= TimeSpan.Zero ? $"in {magnitude}" : $"{magnitude} ago";
    }

    private static string DescribeDuration(TimeSpan duration)
    {
        var span = duration.Duration();

        if (span.TotalDays >= 1)
        {
            var days = Math.Max(1, (int)Math.Round(span.TotalDays));
            return $"{days} {(days == 1 ? "day" : "days")}";
        }

        if (span.TotalHours >= 1)
        {
            var hours = Math.Max(1, (int)Math.Round(span.TotalHours));
            return $"{hours} {(hours == 1 ? "hour" : "hours")}";
        }

        if (span.TotalMinutes >= 1)
        {
            var minutes = Math.Max(1, (int)Math.Round(span.TotalMinutes));
            return $"{minutes} {(minutes == 1 ? "minute" : "minutes")}";
        }

        var seconds = Math.Max(1, (int)Math.Round(span.TotalSeconds));
        return $"{seconds} {(seconds == 1 ? "second" : "seconds")}";
    }

    private static StatusBadgeViewModel CreateBadge(StatusSeverity severity, string description, string? ariaDescription = null)
    {
        var palette = GetPalette(severity);
        var ariaText = string.IsNullOrWhiteSpace(ariaDescription)
            ? $"{palette.Label}. {description}"
            : ariaDescription!;

        return new StatusBadgeViewModel(
            $"status-badge {palette.CssModifier}",
            palette.Label,
            description,
            palette.Icon,
            ariaText);
    }

    private static StatusBadgePalette GetPalette(StatusSeverity severity) => severity switch
    {
        StatusSeverity.Critical => new StatusBadgePalette("status-badge--critical", "Critical", "⛔"),
        StatusSeverity.Warning => new StatusBadgePalette("status-badge--warning", "Warning", "△"),
        _ => new StatusBadgePalette("status-badge--nominal", "Nominal", "✓")
    };

    private sealed record StatusBadgeViewModel(string CssClass, string Label, string Description, string Icon, string AriaLabel);

    private sealed record StatusBadgePalette(string CssModifier, string Label, string Icon);

    private enum StatusSeverity
    {
        Nominal,
        Warning,
        Critical
    }
}
