using EquipmentHubDemo.Domain.Monitoring;
using EquipmentHubDemo.Instrumentation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EquipmentHubDemo.Tests.Instrumentation;

public sealed class SimulatedScpiCommandClientTests
{
    private static SimulatedScpiCommandClient CreateClient()
    {
        var options = Options.Create(new SimulatedScpiOptions
        {
            Instruments = new[]
            {
                new SimulatedInstrumentProfile
                {
                    InstrumentId = "IN-700",
                    Manufacturer = "EquipmentHub",
                    Model = "SignalGen",
                    SerialNumber = "1234",
                    BaseTemperatureCelsius = 25,
                    BaseHumidityPercent = 40
                }
            },
            TemperatureNoiseAmplitude = 0.5,
            HumidityNoiseAmplitude = 1.0
        });

        return new SimulatedScpiCommandClient(options, NullLogger<SimulatedScpiCommandClient>.Instance, new Random(0));
    }

    [Fact]
    public async Task SendAsync_IdentityCommand_ReturnsDescriptor()
    {
        var client = CreateClient();

        var response = await client.SendAsync("IN-700", "*IDN?");

        Assert.Equal("EquipmentHub,SignalGen,1234,1.0", response);
    }

    [Fact]
    public async Task SendAsync_TemperatureAndHumidity_ReturnValuesWithinRange()
    {
        var client = CreateClient();

        var temperature = await client.SendAsync("IN-700", "MEAS:TEMP?");
        var humidity = await client.SendAsync("IN-700", "MEAS:HUM?");

        var tempValue = double.Parse(temperature, System.Globalization.CultureInfo.InvariantCulture);
        var humidValue = double.Parse(humidity, System.Globalization.CultureInfo.InvariantCulture);

        Assert.InRange(tempValue, 23.0, 27.0);
        Assert.InRange(humidValue, 35.5, 44.5);
    }

    [Fact]
    public async Task SendAsync_ConfigurationCommand_AppliesSetting()
    {
        var client = CreateClient();

        var response = await client.SendAsync("IN-700", "CONF:MODE AUTO");

        Assert.Equal("MODE=AUTO", response);
    }

    [Fact]
    public async Task SendAsync_VerificationBeforeNormalization_Warns()
    {
        var client = CreateClient();

        var status = await client.SendAsync("IN-700", "RF:VER?");
        Assert.Equal("WARN", status);

        await client.SendAsync("IN-700", "RF:NORM");
        var statusAfterNorm = await client.SendAsync("IN-700", "RF:VER?");
        Assert.Equal("PASS", statusAfterNorm);
    }

    [Fact]
    public async Task SendAsync_UnknownCommand_Throws()
    {
        var client = CreateClient();

        await Assert.ThrowsAsync<MonitorFailureException>(() => client.SendAsync("IN-700", "UNSUPPORTED"));
    }
}
