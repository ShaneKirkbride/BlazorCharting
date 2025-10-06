using System;
using System.IO;
using Xunit;

namespace EquipmentHubDemo.Tests.StaticAssets;

public sealed class ClientAssetTests
{
    [Fact]
    public void IndexHtml_DoesNotReferenceFavicon()
    {
        var root = RepositoryRootLocator.Find();
        var indexPath = Path.Combine(root, "EquipmentHubDemo", "EquipmentHubDemo.Client", "wwwroot", "index.html");

        Assert.True(File.Exists(indexPath), $"Client index file not found at '{indexPath}'.");

        var html = File.ReadAllText(indexPath);

        Assert.DoesNotContain("rel=\"icon\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("js/webgl-debug-renderer-info-patch.js", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FaviconFiles_AreNotPresent()
    {
        var root = RepositoryRootLocator.Find();
        var clientFaviconPath = Path.Combine(root, "EquipmentHubDemo", "EquipmentHubDemo.Client", "wwwroot", "favicon.ico");
        var serverFaviconPath = Path.Combine(root, "EquipmentHubDemo", "EquipmentHubDemo", "wwwroot", "favicon.png");

        Assert.False(File.Exists(clientFaviconPath), $"Unexpected favicon discovered at '{clientFaviconPath}'.");
        Assert.False(File.Exists(serverFaviconPath), $"Unexpected favicon discovered at '{serverFaviconPath}'.");
    }

    private static class RepositoryRootLocator
    {
        public static string Find()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                var solutionPath = Path.Combine(directory.FullName, "EquipmentHubDemo.sln");
                if (File.Exists(solutionPath))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Unable to locate the repository root starting from the current test directory.");
        }
    }
}
