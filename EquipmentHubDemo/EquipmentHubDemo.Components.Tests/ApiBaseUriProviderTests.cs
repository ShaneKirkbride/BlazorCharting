using System.Collections.Generic;
using EquipmentHubDemo.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using Xunit;

namespace EquipmentHubDemo.Components.Tests;

public sealed class ApiBaseUriProviderTests
{
    [Fact]
    public void GetBaseUris_WhenConfigured_ReturnsUniqueAddresses()
    {
        var options = Options.Create(new ApiClientOptions
        {
            BaseAddresses = new List<string>
            {
                "https://api.example.com",
                "https://api.example.com/",
                "https://backup.example.com"
            }
        });

        var navigation = new TestNavigationManager("https://app.example.com/");
        var provider = new ApiBaseUriProvider(options, navigation);

        var uris = provider.GetBaseUris();

        Assert.Equal(3, uris.Count);
        Assert.Equal("https://api.example.com/", uris[0].AbsoluteUri);
        Assert.Equal("https://backup.example.com/", uris[1].AbsoluteUri);
        Assert.Equal("https://app.example.com/", uris[2].AbsoluteUri);
    }

    [Fact]
    public void GetBaseUris_WhenLoopback_AddsFallbacks()
    {
        var options = Options.Create(new ApiClientOptions
        {
            BaseAddresses = new List<string>()
        });

        var navigation = new TestNavigationManager("http://127.0.0.1:6000/");
        var provider = new ApiBaseUriProvider(options, navigation);

        var uris = provider.GetBaseUris();

        Assert.Equal(3, uris.Count);
        Assert.Equal("http://127.0.0.1:6000/", uris[0].AbsoluteUri);
        Assert.Contains(uris, u => u.AbsoluteUri == "https://localhost:7118/");
        Assert.Contains(uris, u => u.AbsoluteUri == "http://localhost:5026/");
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager(string baseUri)
        {
            Initialize(baseUri, baseUri);
        }

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            // Navigation is not required for tests.
        }
    }
}
