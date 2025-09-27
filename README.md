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

## Step-by-step setup

1. **Restore/build the solution once.** From the repository root run `dotnet restore EquipmentHubDemo.sln` (or `dotnet build`) so the server can pack the WebAssembly client assets the first time you run it.
2. **Start the ASP.NET Core host.** Launch it with `dotnet watch --project EquipmentHubDemo/EquipmentHubDemo` (or `dotnet run`). This single process serves the UI, exposes the REST endpoints, and starts the NetMQ proxy plus worker background services that populate the live cache.
3. **Start the data agent.** In a second terminal, run `dotnet run --project EquipmentHubDemo/Agent` (add `--no-build` after the first build). It will connect to `tcp://127.0.0.1:5556`, generate the configured instrument metrics, and keep publishing them on the `measure` topic.
4. **Ensure the WebAssembly client is served.** When developing locally, run `dotnet watch --project EquipmentHubDemo/EquipmentHubDemo.Client` (or let the server publish the client assets) so that the `ChartIsland` component can initialize LiveCharts instead of showing its “WebAssembly-enabled environment only” placeholder.
5. **Open the site in a browser.** Navigate to the server’s URL (e.g., `https://localhost:5001`). The Home page will display the available measurement keys, begin polling `/api/live` for incremental points, and stream them into the chart while showing total samples received and buffer size. Use the key selector and pause button to control the feed.
6. **Verify data flow (optional).** Watch the console logs: the agent reports publishing attempts, and the server workers print optional “filtered …” lines as they relay measurements into the cache.
