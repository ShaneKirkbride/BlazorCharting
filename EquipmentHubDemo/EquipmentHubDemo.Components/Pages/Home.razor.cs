using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EquipmentHubDemo.Components.Streaming;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace EquipmentHubDemo.Components.Pages;

public sealed partial class Home : ComponentBase, IAsyncDisposable
{
    private const int MaxChartsDisplayed = 3;

    private readonly ChartStreamManager _streamManager = new();
    private readonly List<string> _availableKeys = new();

    private IReadOnlyList<string> _selectedKeys = Array.Empty<string>();

    private CancellationTokenSource? _cts;
    private Task? _pollingTask;
    private CancellationTokenSource? _keyRefreshCts;
    private Task? _keyRefreshTask;

    private bool running = true;
    private string? error;
    private string? selectionWarning;

    private IReadOnlyList<string> AvailableKeys => _availableKeys;

    private long TotalReceived => _streamManager.TotalReceived;

    private int ActiveBufferCount => _streamManager.CountActivePoints(_selectedKeys);

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
    }

    private async Task ToggleAsync()
    {
        running = !running;
        if (running)
        {
            await StartPollingAsync();
        }
        else
        {
            await StopPollingAsync();
        }
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
                if (!running)
                {
                    continue;
                }

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

        UpdateSelectionAfterKeyRefresh(ensureSelection: initialLoad);

        if (running)
        {
            if (initialLoad)
            {
                await StartPollingAsync();
            }
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

                    var selectionChanged = UpdateSelectionAfterKeyRefresh(ensureSelection: !hadKeysBefore);

                    if (running && (selectionChanged || !hadKeysBefore))
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

    private async Task OnKeyToggledAsync(string key, ChangeEventArgs e)
    {
        var isChecked = e.Value is bool boolean && boolean;

        if (isChecked)
        {
            if (_selectedKeys.Contains(key, StringComparer.Ordinal))
            {
                return;
            }

            if (_selectedKeys.Count >= MaxChartsDisplayed)
            {
                selectionWarning = $"You can display up to {MaxChartsDisplayed} charts at a time.";
                await InvokeAsync(StateHasChanged);
                return;
            }

            selectionWarning = null;
            _selectedKeys = _selectedKeys.Concat(new[] { key }).ToList();
        }
        else
        {
            if (!_selectedKeys.Contains(key, StringComparer.Ordinal))
            {
                return;
            }

            selectionWarning = null;
            _selectedKeys = _selectedKeys.Where(k => !string.Equals(k, key, StringComparison.Ordinal)).ToList();
        }

        _streamManager.EnsureSelection(_selectedKeys);

        if (running)
        {
            await StartPollingAsync();
        }
        else
        {
            StateHasChanged();
        }
    }

    private IEnumerable<ChartStream> GetActiveStreams()
        => _streamManager.GetActiveStreams(_selectedKeys);

    private bool UpdateSelectionAfterKeyRefresh(bool ensureSelection)
    {
        var sanitizedSelection = _selectedKeys
            .Where(key => _availableKeys.Contains(key, StringComparer.Ordinal))
            .ToList();

        var selectionChanged = sanitizedSelection.Count != _selectedKeys.Count
            || !_selectedKeys.SequenceEqual(sanitizedSelection, StringComparer.Ordinal);

        if (ensureSelection && sanitizedSelection.Count == 0 && _availableKeys.Count > 0)
        {
            sanitizedSelection = _availableKeys.Take(MaxChartsDisplayed).ToList();
            selectionChanged = true;
        }

        if (selectionChanged)
        {
            selectionWarning = null;
        }

        _selectedKeys = sanitizedSelection;

        _streamManager.EnsureSelection(_selectedKeys);

        return selectionChanged;
    }

    private static string CreateCheckboxId(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "key-unknown";
        }

        var builder = new StringBuilder("key-");
        foreach (var ch in key)
        {
            builder.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-');
        }

        return builder.ToString();
    }

    private bool IsSelected(string key)
        => _selectedKeys.Contains(key, StringComparer.Ordinal);
}
