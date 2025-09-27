using System.Linq;
using EquipmentHubDemo.Domain.Control;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EquipmentHubDemo.Infrastructure.Control;

public sealed record KubernetesTrafficOptions
{
    public const string SectionName = "KubernetesTraffic";

    public IReadOnlyList<string> Namespaces { get; init; } = Array.Empty<string>();

    public double TargetUtilization { get; init; } = 0.65;
}

public sealed class KubernetesNetworkTrafficOptimizer : INetworkTrafficOptimizer
{
    private readonly KubernetesTrafficOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<KubernetesNetworkTrafficOptimizer> _logger;
    private readonly Random _random;

    public KubernetesNetworkTrafficOptimizer(
        IOptions<KubernetesTrafficOptions> options,
        TimeProvider timeProvider,
        ILogger<KubernetesNetworkTrafficOptimizer> logger,
        Random random)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value ?? throw new ArgumentException("Options value cannot be null.", nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    public Task<ControlOperationResult> OptimizeAsync(string clusterName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clusterName);

        var timestamp = _timeProvider.GetUtcNow().UtcDateTime;
        var namespaces = _options.Namespaces.Count == 0 ? new[] { "default" } : _options.Namespaces;
        var optimizedNamespaces = namespaces
            .OrderBy(_ => _random.Next())
            .Take(Math.Max(1, namespaces.Count / 2))
            .ToArray();

        var details = $"Optimized {optimizedNamespaces.Length} namespace(s) towards {_options.TargetUtilization:P0} utilization.";
        _logger.LogInformation(
            "Optimized network traffic on cluster {Cluster} for namespaces {Namespaces}.",
            clusterName,
            string.Join(",", optimizedNamespaces));

        return Task.FromResult(new ControlOperationResult(clusterName, "Kubernetes:Optimize", timestamp, details));
    }
}
