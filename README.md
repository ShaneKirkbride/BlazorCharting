# BlazorCharting
Blazor charting Example

## Rendering the Live Chart

The `ChartIsland` component renders its LiveCharts surface automatically when it detects a browser/WebAssembly runtime.
If you are prerendering or running in an environment where `OperatingSystem.IsBrowser()` temporarily returns `false`,
set the `ForceEnableChartRendering` parameter to `true` to bypass the informational placeholder:

```razor
<ChartIsland Points="points"
             Title="title"
             ForceEnableChartRendering="true" />
```

This ensures the "Chart rendering is available in a WebAssembly-enabled environment only." notice stays hidden once the
client is ready.
