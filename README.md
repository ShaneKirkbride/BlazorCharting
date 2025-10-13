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

### 1. Clone the repository and restore dependencies

```bash
git clone <repo-url>
cd BlazorCharting
dotnet restore EquipmentHubDemo.sln
```

Restoring the solution downloads NuGet packages for the hub, agent, instrumentation library, and automated tests.

### 2. Build everything once

```bash
dotnet build EquipmentHubDemo.sln
```

The build ensures shared contracts stay in sync before you start any processes. Subsequent `dotnet run` executions can use the faster `--no-build` flag if you do not change code in between runs.

### 3. Launch the Equipment Hub server

```bash
dotnet run --project EquipmentHubDemo/EquipmentHubDemo/EquipmentHubDemo.csproj
```

While the command is running:

- The Razor Components UI and JSON endpoints bind to the development Kestrel ports (`http://localhost:5026`, `https://localhost:7118`).
- The embedded ZeroMQ XSUB/XPUB proxy starts listening on `tcp://*:5556` / `tcp://*:5557` and background workers (broker, filter/store, TTL, and live subscriber services) begin processing.
- LiteDB files (`data/measurements.db` and `data/diagnostics.db`) are created automatically the first time telemetry arrives. Leave this terminal open to keep the server online.

### 4. Launch the agent publisher

Open a **second** terminal so the hub keeps running, then execute:

```bash
dotnet run --project EquipmentHubDemo/Agent/Agent.csproj
```

The agent will:

- Load cadence, retry, and instrument definitions from `EquipmentHubDemo/Agent/appsettings.json` (override settings with environment variables if desired).
- Issue simulated SCPI commands, synthesise temperature/humidity readings, and publish them on topic `measure` over `tcp://127.0.0.1:5556`.
- Log command results and retry attempts to the console. Stop the agent with `Ctrl+C` when you are finished.

### 5. Validate the telemetry flow

1. Watch the agent logs—successful publishes display entries similar to `Published measurement for Instrument-01`.
2. Check the hub terminal for messages from the filter worker confirming that it subscribed and stored diagnostics.
3. Navigate to `https://localhost:7118` and verify the chart begins updating within a few seconds. You can also inspect `https://localhost:7118/api/live` to view the JSON payload the UI consumes.

If the chart stays empty, confirm that both processes are still running and that no firewall rules block `localhost` ZeroMQ traffic.

### 6. Shut everything down cleanly

- Press `Ctrl+C` in the agent terminal first so the hub stops receiving new telemetry.
- Press `Ctrl+C` in the hub terminal. All LiteDB connections flush automatically, making the next startup clean.

## Configuration Reference

- **Agent settings** (`EquipmentHubDemo/Agent/appsettings.json`): configure publish frequency, retry counts, the single instrument published by this agent, and monitoring cadences. Intervals are parsed as standard `hh:mm:ss` strings. Launch additional agent processes with different instrument identifiers when you need more sources.
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
