# BlazorCharting

This repository hosts the Equipment Hub demo: a Blazor-based operations console that visualises live instrument telemetry alongside the agent that streams data into the hub. In addition to the UI, the solution includes monitoring, control, and predictive-maintenance services that coordinate through an in-process ZeroMQ broker.

## Architecture at a Glance

- **Equipment Hub server** (`EquipmentHubDemo/EquipmentHubDemo`): Razor Components application that hosts the UI, exposes minimal JSON APIs, runs background workers, and persists diagnostics to LiteDB files under `data/`.
- **Agent service** (`EquipmentHubDemo/Agent`): .NET worker service that issues SCPI commands against a simulated client, then publishes raw measurements on a configurable interval using NetMQ.
- **ZeroMQ bridge**: the hub hosts an XSUB/XPUB proxy on `tcp://*:5556`/`tcp://*:5557` so external publishers (the agent) and in-process subscribers (filter, live cache, TTL workers) can exchange telemetry.
- **Live processing pipeline**: the filter worker subscribes to the agent stream, applies a simple delay filter, persists history/diagnostics, and republishes filtered data for the live cache and TTL workers to consume.

## Prerequisites

1. Install the [.NET SDK 9.0](https://dotnet.microsoft.com/download) (the solution pins runtime `9.0.9`).
2. Optional (recommended for Blazor hybrid builds): `dotnet workload install wasm-tools`.
3. No native ZeroMQ dependency is required—[NetMQ](https://github.com/NetMQ/NetMQ) ships a pure-managed broker used by both the agent and hub.

## Getting Started

### 1. Clone and restore

```bash
git clone <repo-url>
cd BlazorCharting
dotnet restore EquipmentHubDemo.sln
```

### 2. Build the solution

```bash
dotnet build EquipmentHubDemo.sln
```

This step compiles the server, agent, shared instrumentation, and all tests.

### 3. Start the Equipment Hub server

```bash
dotnet run --project EquipmentHubDemo/EquipmentHubDemo/EquipmentHubDemo.csproj
```

- Hosts the Razor Components UI and JSON endpoints on the default Kestrel ports (`http://localhost:5000`, `https://localhost:5001`).
- Binds the ZeroMQ proxy (`tcp://*:5556` / `tcp://*:5557`) and starts background services (broker, filter/store, TTL, and live subscriber workers).
- Creates `data/measurements.db` and `data/diagnostics.db` on demand under the server’s content root.

Keep this process running so the UI and ZeroMQ proxy remain available.

### 4. Start the agent publisher

In a new terminal:

```bash
dotnet run --project EquipmentHubDemo/Agent/Agent.csproj
```

- Reads `EquipmentHubDemo/Agent/appsettings.json` for publish interval, retry policy, and instrument definitions.
- Issues simulated SCPI commands, synthesises additional metrics, and publishes measurements to the hub on topic `measure` via `tcp://127.0.0.1:5556`.
- Logs command executions and retry attempts; stop the agent with `Ctrl+C` once you are done.

### 5. Explore the UI and APIs

- Navigate to `https://localhost:5001` to load the Blazor dashboard. The `ChartIsland` component automatically switches to the interactive LiveCharts surface when the browser is ready.
- Query live data via the JSON endpoints exposed at `/api/keys` and `/api/live` to integrate the filtered telemetry stream into external clients.

## Configuration Reference

- **Agent settings** (`EquipmentHubDemo/Agent/appsettings.json`): configure publish frequency, retry counts, instruments, and monitoring cadences. Intervals are parsed as standard `hh:mm:ss` strings.
- **Hub settings** (`EquipmentHubDemo/EquipmentHubDemo/appsettings.json`): tune live cache retention, TTL cleanup, predictive diagnostics lookback, and simulated SCPI noise. Add origins under `Cors:AllowedOrigins` to grant browser access from remote hosts.
- **ZeroMQ topology** (`EquipmentHubDemo/EquipmentHubDemo/Messaging/Zmq.cs`): adjust bind/connect addresses if the agent runs on a different host.
- **Predictive diagnostics** automatically recreate the LiteDB file if corruption is detected, so deleting `data/diagnostics.db` is a safe way to reset history.

## Validation

Run the full test suite before committing changes:

```bash
dotnet test EquipmentHubDemo.sln
```

This executes monitoring, control, instrumentation, and predictive analytics unit tests across the solution.

## Troubleshooting

- Ensure both the hub and agent are running; the UI shows stale data when the agent is stopped.
- If you modify ZeroMQ endpoints, update both the agent’s `PublishEndpoint` and the hub’s `Zmq` constants so they align.
- Delete the `data/` folder while both processes are stopped to start with a clean measurement/diagnostics database. New LiteDB files are created automatically on next launch.

## Rendering the Live Chart

The `ChartIsland` component renders its LiveCharts surface automatically when it detects a browser/WebAssembly runtime. If you are prerendering or running in an environment where `OperatingSystem.IsBrowser()` temporarily returns `false`, set the `ForceEnableChartRendering` parameter to `true` to bypass the informational placeholder:

```razor
<ChartIsland Points="points"
             Title="title"
             ForceEnableChartRendering="true" />
```

This ensures the "Chart rendering is available in a WebAssembly-enabled environment only." notice stays hidden once the client is ready.
