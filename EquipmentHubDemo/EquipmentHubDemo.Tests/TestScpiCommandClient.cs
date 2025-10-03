using EquipmentHubDemo.Domain.Monitoring;

namespace EquipmentHubDemo.Tests;

internal sealed class TestScpiCommandClient : IScpiCommandClient
{
    private readonly Dictionary<string, Func<string, string, string>> _handlers = new(StringComparer.OrdinalIgnoreCase);
    private Func<string, string, string>? _defaultHandler;
    private readonly List<(string InstrumentId, string Command)> _commands = new();

    public IReadOnlyList<(string InstrumentId, string Command)> Commands => _commands;

    public void Register(string command, string response)
        => _handlers[command] = (_, _) => response;

    public void Register(string command, Func<string, string, string> handler)
        => _handlers[command] = handler ?? throw new ArgumentNullException(nameof(handler));

    public void RegisterException(string command, Exception exception)
    {
        if (exception is null)
        {
            throw new ArgumentNullException(nameof(exception));
        }

        _handlers[command] = (_, _) => throw exception;
    }

    public void RegisterDefault(Func<string, string, string> handler)
        => _defaultHandler = handler ?? throw new ArgumentNullException(nameof(handler));

    public Task<string> SendAsync(string instrumentId, string command, CancellationToken cancellationToken = default)
    {
        _commands.Add((instrumentId, command));
        if (_handlers.TryGetValue(command, out var handler))
        {
            return Task.FromResult(handler(instrumentId, command));
        }

        if (_defaultHandler is not null)
        {
            return Task.FromResult(_defaultHandler(instrumentId, command));
        }

        throw new InvalidOperationException($"No handler registered for command '{command}'.");
    }
}
