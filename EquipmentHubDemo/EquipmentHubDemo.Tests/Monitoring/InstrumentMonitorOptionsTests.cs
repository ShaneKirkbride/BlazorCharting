using EquipmentHubDemo.Domain.Monitoring;

namespace EquipmentHubDemo.Tests.Monitoring;

public sealed class InstrumentMonitorOptionsTests
{
    [Fact]
    public void Validate_WhenNoInstrumentsConfigured_Throws()
    {
        var options = new InstrumentMonitorOptions();

        var exception = Assert.Throws<InvalidOperationException>(() => options.Validate());

        Assert.Equal("At least one instrument must be configured for monitoring.", exception.Message);
    }

    [Theory]
    [InlineData("HeartbeatInterval")]
    [InlineData("SelfCheckInterval")]
    [InlineData("TemperatureInterval")]
    [InlineData("HumidityInterval")]
    public void Validate_WhenIntervalNotPositive_Throws(string property)
    {
        var options = new InstrumentMonitorOptions
        {
            Instruments = new[] { "IN-01" },
            HeartbeatInterval = TimeSpan.FromSeconds(1),
            SelfCheckInterval = TimeSpan.FromSeconds(1),
            TemperatureInterval = TimeSpan.FromSeconds(1),
            HumidityInterval = TimeSpan.FromSeconds(1)
        };

        switch (property)
        {
            case nameof(InstrumentMonitorOptions.HeartbeatInterval):
                options = options with { HeartbeatInterval = TimeSpan.Zero };
                break;
            case nameof(InstrumentMonitorOptions.SelfCheckInterval):
                options = options with { SelfCheckInterval = TimeSpan.Zero };
                break;
            case nameof(InstrumentMonitorOptions.TemperatureInterval):
                options = options with { TemperatureInterval = TimeSpan.Zero };
                break;
            case nameof(InstrumentMonitorOptions.HumidityInterval):
                options = options with { HumidityInterval = TimeSpan.Zero };
                break;
        }

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Equal(property, ex.ParamName);
    }
}
