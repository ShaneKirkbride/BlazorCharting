using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EquipmentHubDemo.Components.Streaming;
using EquipmentHubDemo.Domain.Monitoring;
using EquipmentHubDemo.Domain.Predict;
using EquipmentHubDemo.Domain.Status;
using Microsoft.AspNetCore.Components;

namespace EquipmentHubDemo.Components.Pages;

public sealed partial class Home : ComponentBase, IAsyncDisposable
{
    private const int MaxChartsDisplayed = 9;

    private readonly ChartStreamManager _streamManager = new();
    private readonly List<string> _availableKeys = new();

    private IReadOnlyList<string> _selectedKeys = Array.Empty<string>();
    private IReadOnlyList<PredictiveStatus> _predictiveStatuses = Array.Empty<PredictiveStatus>();
    private IReadOnlyList<MonitoringStatus> _monitoringStatuses = Array.Empty<MonitoringStatus>();

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

    protected override async Task OnInitializedAsync()
    {
        try
        {
            await TryLoadKeysAsync(initialLoad: true);
        }
        catch (Exception ex)
        {
            error = "Init failed: " + ex.Message;
        }

        EnsureKeyRefreshLoop();
        EnsureStatusRefreshLoop();
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

    private async Task<bool> TryLoadKeysAsync(bool initialLoad)
    {
        var latestKeys = await Measurements.GetAvailableKeysAsync();
        var latest = latestKeys is null ? new List<string>() : new List<string>(latestKeys);

        _availableKeys.Clear();
        _availableKeys.AddRange(latest);

        if (_availableKeys.Count == 0)
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

                List<string> latest;
                try
                {
                    var refreshedKeys = await Measurements.GetAvailableKeysAsync(ct);
                    latest = refreshedKeys is null ? new List<string>() : new List<string>(refreshedKeys);
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

                    _availableKeys.Clear();
                    _availableKeys.AddRange(latest);

                    if (_availableKeys.Count == 0)
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

    private bool UpdateSelectionFromAvailableKeys()
    {
        var sanitizedSelection = new List<string>(MaxChartsDisplayed);

        foreach (var key in _selectedKeys)
        {
            if (sanitizedSelection.Count >= MaxChartsDisplayed)
            {
                break;
            }

            if (_availableKeys.Contains(key, StringComparer.Ordinal)
                && !sanitizedSelection.Contains(key, StringComparer.Ordinal))
            {
                sanitizedSelection.Add(key);
            }
        }

        foreach (var key in _availableKeys)
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

    private static string GetHealthCss(MonitoringHealth health) => health switch
    {
        MonitoringHealth.Nominal => "status-chip status-chip--nominal",
        MonitoringHealth.Stale => "status-chip status-chip--stale",
        _ => "status-chip status-chip--missing"
    };

    private static string DescribeHealth(MonitoringHealth health) => health switch
    {
        MonitoringHealth.Nominal => "Nominal",
        MonitoringHealth.Stale => "Stale",
        _ => "No Data"
    };
}
