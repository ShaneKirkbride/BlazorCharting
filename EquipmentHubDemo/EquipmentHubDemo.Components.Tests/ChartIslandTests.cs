using System.Collections.ObjectModel;
using System.Reflection;
using Bunit;
using EquipmentHubDemo.Components.Pages;
using EquipmentHubDemo.Domain;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using Xunit;

namespace EquipmentHubDemo.Components.Tests;

public sealed class ChartIslandTests : TestContext
{
    [Fact]
    public void ChartIsland_RendersCartesianChartAndTracksIncomingPoints()
    {
        // Arrange
        var baseTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var points = new[]
        {
            new PointDto { X = baseTime, Y = 3.5 },
            new PointDto { X = baseTime.AddSeconds(1), Y = 7.25 }
        };

        // Act
        var cut = RenderComponent<ChartIsland>(parameters => parameters
            .Add(p => p.Title, "Telemetry")
            .Add(p => p.Points, points));

        // Assert
        var status = cut.Find("p").TextContent.Trim();
        Assert.Equal($"island points: {points.Length}", status);

        var placeholder = cut.Find("[data-testid='chart-placeholder']");
        Assert.Contains("WebAssembly-enabled", placeholder.TextContent);

        var instance = cut.Instance;

        var valuesField = typeof(ChartIsland).GetField("_values", BindingFlags.Instance | BindingFlags.NonPublic);
        var values = Assert.IsAssignableFrom<ObservableCollection<ObservablePoint>>(valuesField?.GetValue(instance));
        Assert.Equal(points.Length, values.Count);
        Assert.Equal(points[0].X.ToOADate(), values[0].X);
        Assert.Equal(points[0].Y, values[0].Y);
        Assert.Equal(points[1].X.ToOADate(), values[1].X);
        Assert.Equal(points[1].Y, values[1].Y);

        var axesField = typeof(ChartIsland).GetField("xAxes", BindingFlags.Instance | BindingFlags.NonPublic);
        var axes = Assert.IsAssignableFrom<Axis[]>(axesField?.GetValue(instance));
        Assert.Equal("Telemetry", axes[0].Name);
    }

    [Fact]
    public void ChartIsland_ResetsValuesWhenPointsShrink()
    {
        // Arrange
        var baseTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var initialPoints = new[]
        {
            new PointDto { X = baseTime, Y = 1 },
        };
        var cut = RenderComponent<ChartIsland>(parameters => parameters
            .Add(p => p.Title, "Line A")
            .Add(p => p.Points, initialPoints));

        var valuesField = typeof(ChartIsland).GetField("_values", BindingFlags.Instance | BindingFlags.NonPublic);
        var values = Assert.IsAssignableFrom<ObservableCollection<ObservablePoint>>(valuesField?.GetValue(cut.Instance));

        Assert.Equal(initialPoints.Length, values.Count);

        // Act - provide a shorter snapshot for the same title
        var trimmedPoints = Array.Empty<PointDto>();
        cut.SetParametersAndRender(parameters => parameters
            .Add(p => p.Title, "Line A")
            .Add(p => p.Points, trimmedPoints));

        // Assert
        Assert.Empty(values);
    }
}
