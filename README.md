# Merlin

A real-time system monitoring dashboard for single-node container hosts. Built with ASP.NET Core and vanilla JavaScript, deployed as a single container.

Merlin reads host metrics from `/proc` and `/sys`, talks to Podman via its REST API, and streams everything to an animated web dashboard over SignalR.

![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![License](https://img.shields.io/badge/license-MIT-blue)

## Features

- **System metrics** — CPU (per-core), memory, disk, network, temperature
- **Container management** — list, stats, start/stop/restart, live log streaming
- **Real-time updates** — SignalR pushes metrics every 1–2 seconds
- **Animated UI** — GSAP-driven gauge rings, Canvas sparklines, smooth number transitions
- **Dark luxury design** — oklch color system, glowing accents, intentional visual hierarchy
- **Zero config** — auto-discovers disks, network interfaces, temperature sensors, and containers
- **Reduced motion** — respects `prefers-reduced-motion` for accessibility
- **Single container** — one image, three volume mounts, done

## Quick Start

```bash
git clone https://github.com/joachimgoris/merlin.git
cd merlin
```

### Rootful Podman

```bash
sudo podman-compose up -d
```

### Rootless Podman

Edit `compose.yaml` and replace the socket mount:

```yaml
- ${XDG_RUNTIME_DIR}/podman/podman.sock:/var/run/podman/podman.sock
```

Then:

```bash
podman-compose up -d
```

Open **http://your-server:5050** in a browser.

## compose.yaml

```yaml
services:
  merlin:
    build: .
    container_name: merlin
    ports:
      - "5050:5050"
    volumes:
      - /proc:/host/proc:ro
      - /sys:/host/sys:ro
      - /:/host/root:ro
      - /run/podman/podman.sock:/var/run/podman/podman.sock
    environment:
      - HOST_ROOT_PATH=/host/root
    restart: unless-stopped
    security_opt:
      - label=disable
```

### Volume mounts explained

| Mount | Purpose |
|-------|---------|
| `/proc:/host/proc:ro` | Host CPU, memory, network, disk stats |
| `/sys:/host/sys:ro` | Temperature sensors |
| `/:/host/root:ro` | Disk usage (stat host mount points) |
| `podman.sock` | Container list, stats, actions, logs |

All host mounts are **read-only** except the Podman socket (needed for container actions).

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `HOST_PROC_PATH` | `/host/proc` | Path to host's `/proc` |
| `HOST_SYS_PATH` | `/host/sys` | Path to host's `/sys` |
| `HOST_ROOT_PATH` | _(unset)_ | Path to host's `/` (for disk stats) |
| `PODMAN_SOCKET_PATH` | `/var/run/podman/podman.sock` | Podman API socket |
| `ASPNETCORE_URLS` | `http://+:5050` | Listen address |

## Architecture

```
/host/proc, /host/sys
    │
    ▼
LinuxMetricsCollector (1s) ──► MetricsHistory (24h ring buffer)
    │                              │
    ▼                              ▼
MetricsBackgroundService ──► SignalR Hub ──► Browser
                                  ▲
ContainerStatsBackgroundService ──┘
    │
    ▼
PodmanContainerService ──► Podman REST API (Unix socket)
```

### Backend

- **LinuxMetricsCollector** — parses `/proc/stat`, `/proc/meminfo`, `/proc/net/dev`, `/proc/diskstats`, `/proc/1/mounts`, `/sys/class/thermal`, `/sys/class/hwmon`
- **PodmanContainerService** — HTTP client over Unix domain socket, compatible with the Docker/Podman API
- **MetricsHistory** — thread-safe ring buffer storing 86,400 snapshots (24 hours at 1/s)
- **SignalR hub** — broadcasts `SystemMetrics` (1s) and `ContainerList`/`ContainerStats` (2s)
- **REST API** — `/api/metrics/current`, `/api/metrics/history?minutes=N`, `/api/containers`, container actions

### Frontend

- Vanilla JavaScript (ES modules, no build step)
- GSAP for orchestrated animations
- Custom Canvas 2D charts (sparklines, area chart)
- CSS custom properties with oklch colors
- SignalR client for real-time data

## Development

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Linux (for `/proc` and `/sys` access) or macOS (graceful degradation to zero metrics)

### Run locally

```bash
cd src/Merlin.Web
dotnet run
```

On Linux, Merlin reads from real `/proc` and `/sys`. On macOS, metrics degrade to zero values but the app still starts and serves the UI.

### Run tests

```bash
dotnet test
```

### Project structure

```
src/Merlin.Web/
├── Program.cs                          # Entry point, DI, endpoints
├── Hubs/MetricsHub.cs                  # SignalR hub
├── Models/                             # Immutable record types
├── Services/
│   ├── Metrics/                        # /proc + /sys parsing, ring buffer
│   └── Containers/                     # Podman REST API client
└── wwwroot/                            # Static frontend
    ├── index.html
    ├── css/                            # tokens, layout, components, animations
    └── js/                             # app, charts, containers, signalr, animations

tests/Merlin.Web.Tests/
├── MetricsHistoryTests.cs              # Ring buffer correctness
└── LinuxMetricsCollectorTests.cs       # /proc parser tests with fixtures
```

## Graceful Degradation

Merlin is designed for resilience — it's the thing you look at when everything else is broken.

- **No Podman socket** — container section shows "Podman not available", system metrics still work
- **No temperature sensors** — temperature section is hidden
- **Missing `/proc` files** — affected metrics return zero, logged once as warning
- **Network interface changes** — handled dynamically
- **Disk mount changes** — handled dynamically
- **SignalR disconnection** — browser shows reconnecting indicator, auto-reconnects

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for development guidelines.

## License

[MIT](LICENSE)
