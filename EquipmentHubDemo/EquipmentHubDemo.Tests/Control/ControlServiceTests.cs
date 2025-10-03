using EquipmentHubDemo.Domain.Control;
using EquipmentHubDemo.Domain.Monitoring;
using EquipmentHubDemo.Infrastructure.Control;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EquipmentHubDemo.Tests.Control;

public sealed class ControlServiceTests
{
    [Fact]
    public async Task ScenarioConfigurationService_ConfiguresAllParameters()
    {
        var scpi = new TestScpiCommandClient();
        scpi.RegisterDefault((_, command) => command);
        var now = new DateTimeOffset(2024, 11, 05, 9, 0, 0, TimeSpan.Zero);
        var timeProvider = new TestTimeProvider(now);
        var service = new ScenarioConfigurationService(scpi, timeProvider, NullLogger<ScenarioConfigurationService>.Instance);

        var scenario = new InstrumentScenario(
            "Warmup",
            "IN-100",
            new Dictionary<string, string>
            {
                ["MODE"] = "CW",
                ["POWER"] = "10DBM"
            });

        var result = await service.ConfigureAsync(scenario, CancellationToken.None);

        Assert.Equal(new[]
        {
            ("IN-100", "CONF:MODE CW"),
            ("IN-100", "CONF:POWER 10DBM")
        }, scpi.Commands);
        Assert.Equal("IN-100", result.InstrumentId);
        Assert.Equal("Scenario:Warmup", result.Operation);
        Assert.Equal(now.UtcDateTime, result.TimestampUtc);
        Assert.Contains("Configured 2 parameter", result.Details);
    }

    [Fact]
    public async Task InstrumentCalibrationService_PerformsCalibrationSequence()
    {
        var scpi = new TestScpiCommandClient();
        scpi.RegisterDefault((_, _) => "OK");
        var now = new DateTimeOffset(2024, 11, 05, 10, 0, 0, TimeSpan.Zero);
        var timeProvider = new TestTimeProvider(now);
        var service = new InstrumentCalibrationService(scpi, timeProvider, NullLogger<InstrumentCalibrationService>.Instance);

        var result = await service.ScheduleYearlyCalibrationAsync("IN-200", new DateOnly(2024, 12, 1), CancellationToken.None);

        Assert.Equal(new[]
        {
            ("IN-200", "CAL:INIT"),
            ("IN-200", "CAL:STORE")
        }, scpi.Commands);
        Assert.Equal("Calibration", result.Operation);
        Assert.Equal(now.UtcDateTime, result.TimestampUtc);
        Assert.Contains("2024-12-01", result.Details);
    }

    [Fact]
    public async Task RfPathService_NormalizeAndVerify_Succeeds()
    {
        var scpi = new TestScpiCommandClient();
        scpi.Register("RF:NORM", "NORM:2024-11-05T10:30:00Z");
        scpi.Register("RF:VER?", "PASS");
        var now = new DateTimeOffset(2024, 11, 05, 10, 30, 0, TimeSpan.Zero);
        var timeProvider = new TestTimeProvider(now);
        var service = new RfPathService(scpi, timeProvider, NullLogger<RfPathService>.Instance);

        var normalize = await service.NormalizeAsync("IN-300", CancellationToken.None);
        var verify = await service.VerifyAsync("IN-300", CancellationToken.None);

        Assert.Equal(new[]
        {
            ("IN-300", "RF:NORM"),
            ("IN-300", "RF:VER?")
        }, scpi.Commands);
        Assert.Equal("RF:Normalize", normalize.Operation);
        Assert.Equal("RF:Verify", verify.Operation);
        Assert.Equal(now.UtcDateTime, normalize.TimestampUtc);
        Assert.Equal(now.UtcDateTime, verify.TimestampUtc);
    }

    [Fact]
    public async Task RfPathService_Verify_WhenNotPass_Throws()
    {
        var scpi = new TestScpiCommandClient();
        scpi.Register("RF:VER?", "WARN");
        var service = new RfPathService(scpi, new TestTimeProvider(DateTimeOffset.UtcNow), NullLogger<RfPathService>.Instance);

        await Assert.ThrowsAsync<MonitorFailureException>(() => service.VerifyAsync("IN-301", CancellationToken.None));
    }

    [Fact]
    public async Task KubernetesNetworkTrafficOptimizer_ReturnsOptimizationResult()
    {
        var options = Options.Create(new KubernetesTrafficOptions
        {
            Namespaces = new[] { "default", "measure", "control" },
            TargetUtilization = 0.75
        });
        var now = new DateTimeOffset(2024, 11, 05, 11, 0, 0, TimeSpan.Zero);
        var optimizer = new KubernetesNetworkTrafficOptimizer(
            options,
            new TestTimeProvider(now),
            NullLogger<KubernetesNetworkTrafficOptimizer>.Instance,
            new Random(0));

        var result = await optimizer.OptimizeAsync("cluster-alpha", CancellationToken.None);

        Assert.Equal("cluster-alpha", result.InstrumentId);
        Assert.Equal("Kubernetes:Optimize", result.Operation);
        Assert.Equal(now.UtcDateTime, result.TimestampUtc);
        Assert.Contains("75", result.Details);
    }
}
