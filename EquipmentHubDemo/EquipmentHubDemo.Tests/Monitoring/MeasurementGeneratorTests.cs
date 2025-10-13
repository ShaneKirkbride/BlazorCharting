using Agent;
using EquipmentHubDemo.Domain.Monitoring;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EquipmentHubDemo.Tests.Monitoring;

public sealed class MeasurementGeneratorTests
{
    [Fact]
    public void CreateMeasurements_ExecutesAllMonitorCommands()
    {
        var agentOptions = Options.Create(new AgentOptions
        {
            Instrument = new InstrumentOptions
            {
                InstrumentId = "IN-01",
                Metrics = { "Power (240VAC)" }
            }
        });
        agentOptions.Value.Normalize();

        var monitorOptions = Options.Create(new InstrumentMonitorOptions
        {
            Instruments = new[] { "IN-01" },
            HeartbeatInterval = TimeSpan.FromSeconds(10),
            SelfCheckInterval = TimeSpan.FromSeconds(10),
            TemperatureInterval = TimeSpan.FromSeconds(10),
            HumidityInterval = TimeSpan.FromSeconds(10),
            HeartbeatCommand = "*IDN?",
            SelfCheckCommand = "SELF:CHECK?",
            TemperatureCommand = "MEAS:TEMP?",
            HumidityCommand = "MEAS:HUM?"
        });

        var scpi = new TestScpiCommandClient();
        scpi.Register("*IDN?", "EquipmentHub,Model,IN-01,1.0");
        scpi.Register("SELF:CHECK?", "0,OK");
        scpi.Register("MEAS:TEMP?", "24.5");
        scpi.Register("MEAS:HUM?", "41.7");

        var timestamp = new DateTime(2024, 11, 05, 08, 30, 00, DateTimeKind.Utc);
        var generator = new MeasurementGenerator(
            agentOptions,
            monitorOptions,
            scpi,
            new TestTimeProvider(timestamp),
            new Random(0),
            NullLogger<MeasurementGenerator>.Instance);

        var results = generator.CreateMeasurements(timestamp).ToList();

        Assert.Equal(new[]
        {
            ("IN-01", "*IDN?"),
            ("IN-01", "SELF:CHECK?"),
            ("IN-01", "MEAS:TEMP?"),
            ("IN-01", "MEAS:HUM?")
        }, scpi.Commands);

        Assert.Contains(results, m => m.Key.Metric == "Heartbeat" && Math.Abs(m.Value - 1d) < 1e-6);
        Assert.Contains(results, m => m.Key.Metric == "SelfCheck" && Math.Abs(m.Value - 1d) < 1e-6);
        Assert.Contains(results, m => m.Key.Metric == "Temperature" && Math.Abs(m.Value - 24.5) < 1e-6);
        Assert.Contains(results, m => m.Key.Metric == "Humidity" && Math.Abs(m.Value - 41.7) < 1e-6);
    }

    [Fact]
    public void CreateMeasurements_InvalidNumericResponse_ThrowsMonitorFailureException()
    {
        var agentOptions = Options.Create(new AgentOptions
        {
            Instrument = new InstrumentOptions
            {
                InstrumentId = "IN-02",
                Metrics = { "Power (240VAC)" }
            }
        });
        agentOptions.Value.Normalize();

        var monitorOptions = Options.Create(new InstrumentMonitorOptions
        {
            Instruments = new[] { "IN-02" },
            HeartbeatInterval = TimeSpan.FromSeconds(10),
            SelfCheckInterval = TimeSpan.FromSeconds(10),
            TemperatureInterval = TimeSpan.FromSeconds(10),
            HumidityInterval = TimeSpan.FromSeconds(10)
        });

        var scpi = new TestScpiCommandClient();
        scpi.Register("*IDN?", "EquipmentHub,Model,IN-02,1.0");
        scpi.Register("SELF:CHECK?", "0,OK");
        scpi.Register("MEAS:TEMP?", "not-a-number");
        scpi.Register("MEAS:HUM?", "40.0");

        var timestamp = new DateTime(2024, 11, 05, 08, 45, 00, DateTimeKind.Utc);
        var generator = new MeasurementGenerator(
            agentOptions,
            monitorOptions,
            scpi,
            new TestTimeProvider(timestamp),
            new Random(0),
            NullLogger<MeasurementGenerator>.Instance);

        Assert.Throws<MonitorFailureException>(() => generator.CreateMeasurements(timestamp).ToList());
    }
}
