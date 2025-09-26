using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using EquipmentHubDemo.Components.Pages;
using EquipmentHubDemo.Domain;
using EquipmentHubDemo.Domain.Live;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EquipmentHubDemo.Components.Tests;

public sealed class RouterConfigurationTests : TestContext
{
    public RouterConfigurationTests()
    {
        Services.AddSingleton<ILiveMeasurementClient>(new NoopLiveMeasurementClient());
    }

    [Fact]
    public void AppRouter_IncludesSharedComponentsAssembly()
    {
        JSInterop.Setup<string>("Blazor._internal.PageTitle.getAndRemoveExistingTitle").SetResult(string.Empty);

        // Act
        var cut = RenderComponent<EquipmentHubDemo.Components.App>();

        // Assert
        var router = cut.FindComponent<Router>();
        var additionalAssemblies = router.Instance.AdditionalAssemblies ?? Array.Empty<Assembly>();

        Assert.Contains(typeof(Home).Assembly, additionalAssemblies);
    }

    [Fact]
    public void ClientRouter_IncludesSharedComponentsAssembly()
    {
        JSInterop.Setup<string>("Blazor._internal.PageTitle.getAndRemoveExistingTitle").SetResult(string.Empty);

        // Act
        var cut = RenderComponent<EquipmentHubDemo.Client.Routes>();

        // Assert
        var router = cut.FindComponent<Router>();
        var additionalAssemblies = router.Instance.AdditionalAssemblies ?? Array.Empty<Assembly>();

        Assert.Contains(typeof(Home).Assembly, additionalAssemblies);
    }

    private sealed class NoopLiveMeasurementClient : ILiveMeasurementClient
    {
        public Task<IReadOnlyList<string>> GetAvailableKeysAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public Task<IReadOnlyList<PointDto>> GetMeasurementsAsync(string key, long sinceTicks, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PointDto>>(Array.Empty<PointDto>());
    }
}
