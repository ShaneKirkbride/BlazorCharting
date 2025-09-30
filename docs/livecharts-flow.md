# LiveCharts Rendering Flow

This document describes how live measurement data flows through the Equipment Hub demo and how the Blazor components integrate with LiveCharts to render streaming telemetry.

## High-level pipeline

1. **Data acquisition** – `HttpLiveMeasurementClient` implements `ILiveMeasurementClient`, probing multiple API base URIs until it successfully fetches JSON payloads for `/api/keys` and `/api/live` requests. The client normalizes responses, validates inputs, and caches the last healthy endpoint for subsequent calls.【F:EquipmentHubDemo/EquipmentHubDemo.Client/Services/HttpLiveMeasurementClient.cs†L13-L120】【F:EquipmentHubDemo/EquipmentHubDemo.Domain/Live/ILiveMeasurementClient.cs†L20-L27】
2. **UI orchestration** – The `Home` page schedules two asynchronous loops: a periodic poller (`PollLoopAsync`) that pulls batched `PointDto` updates for each selected key, and a background key-refresh loop that keeps the checkbox list in sync with the server. Both loops rely on `CancellationTokenSource` instances so they can be paused, resumed, or disposed cooperatively. `ChartStream` objects buffer per-key point histories, enforce a 2,000-point cap, and expose `SinceTicks` so the next poll only requests incremental data.【F:EquipmentHubDemo/EquipmentHubDemo.Components/Pages/Home.razor†L18-L330】【F:EquipmentHubDemo/EquipmentHubDemo.Components/Pages/Home.razor†L332-L640】
3. **Chart rendering** – Each active `ChartStream` is rendered with a `ChartIsland` child component. The component wraps LiveCharts primitives, translating the mutable `PointDto` list into LiveCharts `ObservablePoint` instances on the WASM side. When prerendering or running server-side, it displays an informational placeholder instead of initializing LiveCharts.【F:EquipmentHubDemo/EquipmentHubDemo.Components/Pages/Home.razor†L44-L67】【F:EquipmentHubDemo/EquipmentHubDemo.Components/Pages/ChartIsland.razor†L13-L116】

The combination of these layers yields a responsive UI that keeps chart surfaces synchronized with live backend data while respecting Blazor’s rendering model and LiveCharts’ configuration requirements.

## `ChartIsland` lifecycle

The `ChartIsland` component is responsible for bridging the Blazor render tree with LiveCharts’ SkiaSharp rendering surface.

### Initialization

* `OnInitialized` configures LiveCharts exactly once per WASM session by interlocking on a static flag and registering the SkiaSharp view. It also constructs a single `LineSeries<ObservablePoint>` that reuses a component-level `ObservableCollection` to minimize allocations during updates.【F:EquipmentHubDemo/EquipmentHubDemo.Components/Pages/ChartIsland.razor†L68-L109】
* The `ShouldRenderChart` guard ensures the LiveCharts canvas is only created when running in a browser or when `ForceEnableChartRendering` overrides the check. This supports SSR/prerendering scenarios by substituting a message placeholder outside of WebAssembly.【F:EquipmentHubDemo/EquipmentHubDemo.Components/Pages/ChartIsland.razor†L57-L87】

### Incremental updates

`OnParametersSet` validates the `Points` input, decides whether a full resynchronization is required, and otherwise appends only new points to `_values`. The logic tracks the previous title, point count, and first timestamp to detect when a stream has reset (for example, when the backend truncates history). When a full sync is required it clears the observable collection before repopulating it. After each update it enforces the 2,000-point retention cap to align with the backend buffers.【F:EquipmentHubDemo/EquipmentHubDemo.Components/Pages/ChartIsland.razor†L111-L178】

### Axis metadata strategy

Axis labels and label formatters are selected dynamically based on the measurement key. The component parses keys with `MeasureKey.TryParse`, looks up friendly axis metadata for known metrics (temperature, humidity, power), and falls back to sensible defaults for unrecognized metrics. Time labels use an OLE Automation date conversion and a fixed UTC clock format so they remain consistent regardless of browser locale.【F:EquipmentHubDemo/EquipmentHubDemo.Components/Pages/ChartIsland.razor†L21-L56】【F:EquipmentHubDemo/EquipmentHubDemo.Components/Pages/ChartIsland.razor†L180-L214】

## Supporting patterns and practices

* **Streaming buffers** – `ChartStream.Apply` mutates a rolling `List<PointDto>` in place, only trimming when the buffer exceeds the configured cap. Because each stream instance is long-lived (per selected key), LiveCharts receives stable object references, which avoids redraw glitches due to wholesale collection replacements.【F:EquipmentHubDemo/EquipmentHubDemo.Components/Pages/Home.razor†L518-L640】
* **Input validation and defensive programming** – Both the client and the chart components validate parameters (null checks, `sinceTicks` bounds, metric parsing). This upholds the SOLID Single Responsibility principle by constraining errors to the layer responsible for them, while tests assert these guards explicitly.【F:EquipmentHubDemo/EquipmentHubDemo.Client/Services/HttpLiveMeasurementClient.cs†L33-L53】【F:EquipmentHubDemo/EquipmentHubDemo.Components/Pages/ChartIsland.razor†L116-L131】【F:EquipmentHubDemo/EquipmentHubDemo.Components.Tests/ChartIslandTests.cs†L16-L239】
* **Asynchronous coordination** – The `Home` page exemplifies the Command–Query Separation pattern: user actions toggle polling (`ToggleAsync`), while background loops query data. It relies on `PeriodicTimer` for cadence control and `CancellationTokenSource` for graceful teardown, keeping UI state consistent with chart rendering. This approach avoids race conditions and ensures `DisposeAsync` can stop both loops deterministically.【F:EquipmentHubDemo/EquipmentHubDemo.Components/Pages/Home.razor†L88-L330】
* **Single configuration entry point** – LiveCharts global configuration is guarded by an interlocked flag so multiple component instances don’t re-register SkiaSharp. This follows the Singleton pattern for library bootstrapping while still being unit testable (tests reset the static flag via reflection).【F:EquipmentHubDemo/EquipmentHubDemo.Components/Pages/ChartIsland.razor†L68-L90】【F:EquipmentHubDemo/EquipmentHubDemo.Components.Tests/ChartIslandTests.cs†L16-L239】

Together these patterns deliver a maintainable LiveCharts integration that adheres to SOLID design principles: clear separation of concerns between data access, UI orchestration, and rendering; resilience to malformed inputs; and predictable lifecycle management for shared resources.
