using System.Reflection;
using Bunit;
using EquipmentHubDemo.Components.Pages;
using Microsoft.AspNetCore.Components.Routing;
using Xunit;

namespace EquipmentHubDemo.Components.Tests;

public sealed class RouterConfigurationTests : TestContext
{
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
}
