using System;
using System.Collections.ObjectModel;
using System.Reflection;
using Bunit;
using EquipmentHubDemo.Components.Pages;
using EquipmentHubDemo.Domain;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using Xunit;

namespace EquipmentHubDemo.Components.Tests;

public sealed class ChartIslandTests : TestContext
{
    [Fact]
    public void ChartIsland_RendersCartesianChartAndTracksIncomingPoints()
    {
        ResetConfigurationFlag();
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
        Assert.Equal("Time (UTC)", axes[0].Name);

        var yAxesField = typeof(ChartIsland).GetField("yAxes", BindingFlags.Instance | BindingFlags.NonPublic);
        var yAxes = Assert.IsAssignableFrom<Axis[]>(yAxesField?.GetValue(instance));
        Assert.Equal("Value", yAxes[0].Name);

        var configuredFlag = GetConfigurationFlag();
        Assert.Equal(1, configuredFlag);
    }

    [Fact]
    public void ChartIsland_UpdatesAxisMetadataBasedOnMeasureKey()
    {
        ResetConfigurationFlag();

        var baseTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var points = new[] { new PointDto { X = baseTime, Y = 3.5 } };

        var cut = RenderComponent<ChartIsland>(parameters => parameters
            .Add(p => p.Title, "UXG-01:Power")
            .Add(p => p.Points, points));

        var xAxesField = typeof(ChartIsland).GetField("xAxes", BindingFlags.Instance | BindingFlags.NonPublic);
        var xAxes = Assert.IsAssignableFrom<Axis[]>(xAxesField?.GetValue(cut.Instance));
        var yAxesField = typeof(ChartIsland).GetField("yAxes", BindingFlags.Instance | BindingFlags.NonPublic);
        var yAxes = Assert.IsAssignableFrom<Axis[]>(yAxesField?.GetValue(cut.Instance));

        Assert.Equal("UXG-01 Time (UTC)", xAxes[0].Name);
        Assert.Equal("Power (dBm)", yAxes[0].Name);

        cut.SetParametersAndRender(parameters => parameters
            .Add(p => p.Title, "UXG-01:SNR")
            .Add(p => p.Points, points));

        Assert.Equal("UXG-01 Time (UTC)", xAxes[0].Name);
        Assert.Equal("Signal-to-Noise Ratio (dB)", yAxes[0].Name);

        cut.SetParametersAndRender(parameters => parameters
            .Add(p => p.Title, "UXG-02:Voltage")
            .Add(p => p.Points, points));

        Assert.Equal("UXG-02 Time (UTC)", xAxes[0].Name);
        Assert.Equal("Voltage", yAxes[0].Name);
    }

    [Fact]
    public void ChartIsland_ResetsValuesWhenPointsShrink()
    {
        ResetConfigurationFlag();
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

    [Fact]
    public void ChartIsland_UpdatesValuesWhenSlidingWindowMaintainsCount()
    {
        ResetConfigurationFlag();

        var baseTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var initialPoints = new[]
        {
            new PointDto { X = baseTime, Y = 1 },
            new PointDto { X = baseTime.AddSeconds(1), Y = 2 },
            new PointDto { X = baseTime.AddSeconds(2), Y = 3 }
        };

        var cut = RenderComponent<ChartIsland>(parameters => parameters
            .Add(p => p.Title, "Line Sliding")
            .Add(p => p.Points, initialPoints));

        var valuesField = typeof(ChartIsland).GetField("_values", BindingFlags.Instance | BindingFlags.NonPublic);
        var values = Assert.IsAssignableFrom<ObservableCollection<ObservablePoint>>(valuesField?.GetValue(cut.Instance));

        Assert.Equal(initialPoints.Length, values.Count);

        var slidingPoints = new[]
        {
            initialPoints[1],
            initialPoints[2],
            new PointDto { X = baseTime.AddSeconds(3), Y = 4 }
        };

        cut.SetParametersAndRender(parameters => parameters
            .Add(p => p.Title, "Line Sliding")
            .Add(p => p.Points, slidingPoints));

        Assert.Equal(slidingPoints.Length, values.Count);
        Assert.Equal(slidingPoints[0].X.ToOADate(), values[0].X);
        Assert.Equal(slidingPoints[^1].Y, values[^1].Y);
    }

    [Fact]
    public void ChartIsland_ShowsPlaceholderWhenForceDisabledOutsideBrowser()
    {
        ResetConfigurationFlag();

        var cut = RenderComponent<ChartIsland>(parameters => parameters
            .Add(p => p.Title, "Line Z")
            .Add(p => p.ForceEnableChartRendering, false)
            .Add(p => p.Points, Array.Empty<PointDto>()));

        var placeholder = cut.Find("[data-testid='chart-placeholder']");
        Assert.Contains("WebAssembly-enabled", placeholder.TextContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChartIsland_InitializesLineSeriesWithLiveCharts()
    {
        ResetConfigurationFlag();

        var cut = RenderComponent<ChartIsland>(parameters => parameters
            .Add(p => p.Title, "Line B")
            .Add(p => p.Points, Array.Empty<PointDto>()));

        var valuesField = typeof(ChartIsland).GetField("_values", BindingFlags.Instance | BindingFlags.NonPublic);
        var values = Assert.IsAssignableFrom<ObservableCollection<ObservablePoint>>(valuesField?.GetValue(cut.Instance));

        var seriesField = typeof(ChartIsland).GetField("Series", BindingFlags.Instance | BindingFlags.NonPublic);
        var series = Assert.IsAssignableFrom<ISeries[]>(seriesField?.GetValue(cut.Instance));
        var lineSeries = Assert.IsType<LineSeries<ObservablePoint>>(Assert.Single(series));

        Assert.Same(values, lineSeries.Values);
        Assert.Null(lineSeries.GeometryFill);
        Assert.Null(lineSeries.GeometryStroke);
        Assert.Equal(0, lineSeries.LineSmoothness);

        var configuredFlag = GetConfigurationFlag();
        Assert.Equal(1, configuredFlag);
    }

    [Fact]
    public void ChartIsland_ThrowsWhenPointsParameterIsNull()
    {
        ResetConfigurationFlag();

        var exception = Assert.Throws<ArgumentNullException>(() =>
            RenderComponent<ChartIsland>(parameters => parameters
                .Add(p => p.Title, "Line C")
                .Add(p => p.Points, null)));

        Assert.Equal("Points", exception.ParamName);
    }

    [Fact]
    public void ChartIsland_ThrowsWhenPointsContainNullEntries()
    {
        ResetConfigurationFlag();

        PointDto?[] points =
        {
            new PointDto { X = DateTime.UtcNow, Y = 1 },
            null
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            RenderComponent<ChartIsland>(parameters => parameters
                .Add(p => p.Title, "Line D")
                .Add(p => p.Points, points!)));

        Assert.Equal("Points", exception.ParamName);
        Assert.Contains("null entries", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static int GetConfigurationFlag()
    {
        var field = typeof(ChartIsland).GetField("_configuredFlag", BindingFlags.Static | BindingFlags.NonPublic);
        return (int)(field?.GetValue(null) ?? 0);
    }

    private static void ResetConfigurationFlag()
    {
        var field = typeof(ChartIsland).GetField("_configuredFlag", BindingFlags.Static | BindingFlags.NonPublic);
        field?.SetValue(null, 0);
    }
}
