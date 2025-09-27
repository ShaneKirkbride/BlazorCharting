using EquipmentHubDemo.Domain.Monitoring;
using EquipmentHubDemo.Instrumentation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Agent;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Services
            .AddOptions<AgentOptions>()
            .Bind(builder.Configuration.GetSection("Agent"))
            .PostConfigure(options => options.Normalize());

        builder.Services
            .AddOptions<InstrumentMonitorOptions>()
            .Bind(builder.Configuration.GetSection(InstrumentMonitorOptions.SectionName));

        builder.Services.Configure<SimulatedScpiOptions>(builder.Configuration.GetSection(SimulatedScpiOptions.SectionName));

        builder.Services.AddSingleton(Random.Shared);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IScpiCommandClient, SimulatedScpiCommandClient>();
        builder.Services.AddSingleton<IMeasurementGenerator, MeasurementGenerator>();
        builder.Services.AddHostedService<AgentPublisher>();

        builder.Logging.SetMinimumLevel(LogLevel.Information);

        using var host = builder.Build();
        await host.RunAsync().ConfigureAwait(false);
    }
}
