# Merlin

Real-time system monitoring and homelab dashboard.

![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![License](https://img.shields.io/badge/license-MIT-blue)
![Image](https://img.shields.io/badge/ghcr.io-joachimgoris%2Fmerlin-purple)

Merlin reads host metrics from `/proc` and `/sys`, talks to Docker or Podman via the container API, and streams everything to an animated web dashboard over SignalR. One container, zero config, instant visibility into your server.

## Features

### System Monitoring

- **CPU** -- per-core utilization bars, total usage gauge, load averages (1/5/15 min), clock frequency
- **Memory** -- used/total with gauge ring, swap usage
- **Disk** -- per-mount usage bars with read/write I/O rates
- **Network** -- bidirectional area chart with live TX/RX rates
- **Temperature** -- multi-sensor readings from hwmon and thermal zones
- **Processes** -- sortable top-N process table with CPU%, memory, PID, and state

### Container Management

- Live stats per container: CPU, memory, network TX/RX
- Start, stop, and restart containers from the UI
- Log streaming with keyword search and highlighting
- Per-container CPU and memory sparkline history (300 samples)
- Docker Compose project grouping with collapsible sections
- Health check status indicators and detail view
- Image update detection (compares local digest against remote registry)

### Homepage

- Service catalog with auto-discovery from container labels
- Static JSON config file with hot-reload on change
- Periodic health checks with status indicators
- Grouped tile grid, deduplicated across label and config sources

### Alerts

- Discord webhook notifications for threshold breaches
- CPU sustained above threshold (60-sample rolling window)
- Memory usage above threshold
- Disk usage above threshold (per mount point)
- Container state changes (stopped, recovered, unhealthy)
- Per-alert cooldown to prevent notification storms

### Persistence

- SQLite-backed metrics history (7-day default retention)
- Automatic flush from in-memory ring buffer to disk
- Survives container restarts -- loads persisted data on startup
- Merged query across in-memory and persisted data for the full retention window

### UI

- Dark luxury design with oklch color system and glowing accents
- Animated SVG gauge rings with GSAP
- Canvas sparklines and bidirectional network area chart
- System info bar (hostname, OS, kernel, CPU model, RAM)
- Notification toasts for container actions and connection state
- Responsive layout
- Respects `prefers-reduced-motion` for accessibility

## Quick Start

```bash
mkdir merlin && cd merlin
curl -O https://raw.githubusercontent.com/joachimgoris/merlin/main/examples/docker/compose.yaml
docker compose up -d
```

Open **http://localhost:5050** in a browser.

The command above uses the Docker socket. For Podman, use the Podman example instead:

```bash
curl -O https://raw.githubusercontent.com/joachimgoris/merlin/main/examples/podman/compose.yaml
sudo podman-compose up -d
```

For rootless Podman, edit the socket mount in the compose file:

```yaml
- ${XDG_RUNTIME_DIR}/podman/podman.sock:/var/run/podman/podman.sock
```

### Volume Mounts

| Mount | Purpose |
|-------|---------|
| `/proc:/host/proc:ro` | Host CPU, memory, network, disk, process stats |
| `/sys:/host/sys:ro` | Temperature sensors (hwmon, thermal zones) |
| `/:/host/root:ro` | Disk usage (stat host mount points) |
| Container socket | Container list, stats, actions, logs |
| `merlin-data:/app/data` | SQLite database for metrics persistence |

All host mounts are **read-only**. The container socket is the only read-write mount (needed for start/stop/restart actions).

## Configuration

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `HOST_PROC_PATH` | `/host/proc` (container), `/proc` (host) | Path to the host `/proc` filesystem |
| `HOST_SYS_PATH` | `/host/sys` (container), `/sys` (host) | Path to the host `/sys` filesystem |
| `HOST_ROOT_PATH` | _(unset)_ | Path to the host root `/` for disk usage stats |
| `PODMAN_SOCKET_PATH` | `/var/run/podman/podman.sock` | Container engine API socket path |
| `ASPNETCORE_URLS` | `http://+:5050` | Listen address and port |
| `MERLIN_DB_PATH` | `./data/merlin.db` | SQLite database file path |
| `MERLIN_RETENTION_DAYS` | `7` | Number of days to retain persisted metrics |
| `MERLIN_HOMEPAGE_CONFIG` | `./data/homepage.json` | Path to the static homepage config file |
| `MERLIN_DISCORD_WEBHOOK_URL` | _(unset)_ | Discord webhook URL (enables alert system when set) |
| `MERLIN_ALERT_CPU_THRESHOLD` | `90` | CPU % sustained over 60 samples to trigger alert |
| `MERLIN_ALERT_MEM_THRESHOLD` | `90` | Memory % to trigger alert |
| `MERLIN_ALERT_DISK_THRESHOLD` | `95` | Disk % per mount to trigger alert |
| `MERLIN_ALERT_COOLDOWN_MINUTES` | `15` | Minutes between repeated alerts of the same type |

### Homepage -- Container Labels

Add labels to any container to make it appear on the homepage:

| Label | Required | Description |
|-------|----------|-------------|
| `merlin.homepage.name` | Yes | Display name shown on the tile |
| `merlin.homepage.url` | Yes | URL opened on click |
| `merlin.homepage.healthUrl` | No | URL used for health checks (defaults to `url`) |
| `merlin.homepage.icon` | No | Emoji or image URL for the tile |
| `merlin.homepage.group` | No | Category group heading (default: `Services`) |
| `merlin.homepage.description` | No | Short description shown below the name |

Example in a compose file:

```yaml
services:
  pihole:
    image: pihole/pihole:latest
    labels:
      merlin.homepage.name: "Pi-hole"
      merlin.homepage.url: "http://192.168.1.2/admin"
      merlin.homepage.healthUrl: "http://192.168.1.2/admin/api.php"
      merlin.homepage.icon: "🛡️"
      merlin.homepage.group: "Network"
      merlin.homepage.description: "DNS ad blocker"
```

### Homepage -- Static Config

Create a `homepage.json` file and mount it into the container (or place it at the `MERLIN_HOMEPAGE_CONFIG` path):

```json
{
  "services": [
    {
      "name": "Router",
      "url": "http://192.168.1.1",
      "icon": "🌐",
      "group": "Network",
      "description": "Network router admin panel"
    },
    {
      "name": "Pi-hole",
      "url": "http://192.168.1.2/admin",
      "healthUrl": "http://192.168.1.2/admin/api.php",
      "icon": "🛡️",
      "group": "Network",
      "description": "DNS ad blocker"
    }
  ]
}
```

Mount the file in your compose:

```yaml
volumes:
  - ./homepage.json:/app/data/homepage.json:ro
```

The file is hot-reloaded when modified -- no restart required. Container-discovered services take precedence when the same name+URL appears in both sources.

### Discord Alerts

1. In your Discord server, go to a channel's settings and create a webhook under **Integrations > Webhooks**.
2. Copy the webhook URL.
3. Set the environment variable:

```yaml
environment:
  - MERLIN_DISCORD_WEBHOOK_URL=https://discord.com/api/webhooks/...
```

Merlin will send notifications for:

| Alert | Severity | Condition |
|-------|----------|-----------|
| CPU High | Critical | CPU above threshold for 60 consecutive samples |
| Memory High | Critical | Memory usage above threshold |
| Disk High | Warning | Disk usage above threshold (per mount) |
| Container Stopped | Warning | Running container transitions to non-running state |
| Container Recovered | Info | Non-running container transitions back to running |
| Container Unhealthy | Warning | Container health check reports unhealthy |

Each alert type has a per-subject cooldown (default 15 minutes) to prevent repeated notifications.

## Examples

The [`examples/`](examples/) directory contains ready-to-use compose files:

| Directory | Description |
|-----------|-------------|
| [`examples/docker/`](examples/docker/) | Docker socket setup (most common) |
| [`examples/podman/`](examples/podman/) | Rootful Podman with SELinux label disable |
| [`examples/homepage/`](examples/homepage/) | Homepage with container labels and static config |

## Architecture

```
/host/proc, /host/sys
    |
    v
LinuxMetricsCollector (1s) --> MetricsHistory (24h ring buffer)
    |                               |
    v                               v
MetricsBackgroundService ----> SignalR Hub ------> Browser
                                    ^
ContainerStatsBackgroundService ----+
    |                               ^
    v                               |
PodmanContainerService         ServiceStatusBackgroundService
    |                               |
    v                               v
Container API (Unix socket)    HomepageConfigLoader + ServiceDiscovery
                                    |
MetricsFlushService                 v
    |                          homepage.json + container labels
    v
MetricsRepository (SQLite)
    |
AlertBackgroundService --> AlertEvaluator --> DiscordWebhookClient
```

### Backend

- **LinuxMetricsCollector** -- parses `/proc/stat`, `/proc/meminfo`, `/proc/net/dev`, `/proc/diskstats`, `/proc/1/mounts`, `/sys/class/thermal`, `/sys/class/hwmon`
- **SystemInfoCollector** -- reads hostname, OS, kernel version, CPU model, total RAM (cached after first read)
- **ProcessCollector** -- reads `/proc/[pid]/stat` and `/proc/[pid]/status` for top-N processes by CPU
- **PodmanContainerService** -- HTTP client over Unix domain socket, compatible with the Docker and Podman API
- **MetricsHistory** -- thread-safe ring buffer storing 86,400 snapshots (24 hours at 1/s)
- **ContainerMetricsHistory** -- per-container ring buffer (300 samples) for sparkline data
- **MetricsRepository** -- SQLite persistence with WAL mode, batch inserts, and automatic pruning
- **ImageUpdateChecker** -- compares local image digests against remote registry manifests
- **ServiceDiscovery** -- merges container labels and static config into the homepage service catalog
- **AlertEvaluator** -- evaluates CPU, memory, disk, and container state against thresholds
- **SignalR hub** -- broadcasts `SystemMetrics` (1s) and `ContainerList`/`ContainerStats` (2s)

### Frontend

- Vanilla JavaScript (ES modules, no build step)
- GSAP for orchestrated animations
- Custom Canvas 2D charts (sparklines, area chart)
- CSS custom properties with oklch colors
- SignalR client for real-time data

## API Reference

| Method | Path | Description |
|--------|------|-------------|
| GET | `/health` | Health check endpoint |
| GET | `/api/system-info` | Host info (hostname, OS, kernel, CPU, RAM) |
| GET | `/api/metrics/current` | Latest metrics snapshot |
| GET | `/api/metrics/history?minutes=N` | Historical metrics (1 to 10,080 minutes / 7 days) |
| GET | `/api/processes?top=N` | Top N processes by CPU (default 25) |
| GET | `/api/containers` | Container list with stats |
| GET | `/api/containers/sparklines` | Per-container CPU/memory sparkline data |
| GET | `/api/containers/image-updates` | Cached image update check results |
| POST | `/api/containers/image-updates/check` | Force image update check |
| GET | `/api/containers/{id}/health` | Container health check detail |
| POST | `/api/containers/{id}/start` | Start a container |
| POST | `/api/containers/{id}/stop` | Stop a container |
| POST | `/api/containers/{id}/restart` | Restart a container |
| GET | `/api/homepage/services` | Homepage service catalog with status |
| WS | `/hub/metrics` | SignalR hub for real-time metrics streaming |

## Development

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Linux (for real `/proc` and `/sys` access) or macOS (graceful degradation to zero metrics)

### Run locally

```bash
dotnet run --project src/Merlin.Web
```

Open **http://localhost:5050**. On Linux, Merlin reads real system metrics. On macOS, metrics degrade to zero values but the UI still loads.

### Run tests

```bash
dotnet test
```

Tests use fixture data to simulate `/proc` and `/sys` files, so they run on any platform.

### Build container image

```bash
docker build -t merlin .
```

### Project structure

```
src/Merlin.Web/
├── Program.cs                              # Entry point, DI, endpoints
├── Hubs/MetricsHub.cs                      # SignalR hub
├── Models/                                 # Immutable record types
│   ├── Alert.cs                            #   Alert types and severities
│   ├── ContainerInfo.cs                    #   Container metadata
│   ├── ContainerStats.cs                   #   Container resource stats
│   ├── HomepageConfig.cs                   #   Static config file schema
│   ├── HomepageService.cs                  #   Resolved homepage service
│   ├── ImageUpdateStatus.cs                #   Image digest comparison result
│   ├── ProcessInfo.cs                      #   Process table entry
│   ├── SystemInfo.cs                       #   Host info (hostname, OS, CPU)
│   ├── SystemMetrics.cs                    #   Top-level metrics snapshot
│   └── ...                                 #   CPU, Memory, Disk, Network, Temperature
├── Services/
│   ├── Alerts/                             # Discord webhook alerting
│   │   ├── AlertBackgroundService.cs
│   │   ├── AlertEvaluator.cs
│   │   ├── AlertOptions.cs
│   │   └── DiscordWebhookClient.cs
│   ├── Containers/                         # Container engine API client
│   │   ├── ContainerMetricsHistory.cs
│   │   ├── ContainerStatsBackgroundService.cs
│   │   ├── IContainerService.cs
│   │   ├── ImageUpdateBackgroundService.cs
│   │   ├── ImageUpdateChecker.cs
│   │   └── PodmanContainerService.cs
│   ├── Homepage/                           # Service catalog
│   │   ├── HomepageConfigLoader.cs
│   │   ├── ServiceDiscovery.cs
│   │   └── ServiceStatusBackgroundService.cs
│   ├── Metrics/                            # /proc + /sys parsing, ring buffer
│   │   ├── LinuxMetricsCollector.cs
│   │   ├── MetricsBackgroundService.cs
│   │   ├── MetricsHistory.cs
│   │   ├── ProcessCollector.cs
│   │   └── SystemInfoCollector.cs
│   └── Persistence/                        # SQLite storage
│       ├── MetricsFlushService.cs
│       └── MetricsRepository.cs
└── wwwroot/                                # Static frontend
    ├── index.html
    ├── css/                                # tokens, layout, components, animations, toasts, homepage
    └── js/                                 # app, charts, containers, homepage, processes, signalr, animations, toasts

tests/Merlin.Web.Tests/
├── ContainerMetricsHistoryTests.cs
├── HealthParsingTests.cs
├── HomepageConfigLoaderTests.cs
├── ImageUpdateCheckerTests.cs
├── LinuxMetricsCollectorTests.cs
├── MetricsHistoryGetLatestTests.cs
├── MetricsHistoryTests.cs
├── MetricsRepositoryTests.cs
├── ServiceDiscoveryTests.cs
└── SystemInfoCollectorTests.cs

examples/
├── docker/compose.yaml                     # Docker socket setup
├── homepage/                               # Homepage with labels + static config
│   ├── compose.yaml
│   └── homepage.json
└── podman/compose.yaml                     # Rootful Podman setup
```

## Graceful Degradation

Merlin is designed for resilience -- it is the thing you look at when everything else is broken.

- **No container socket** -- container section shows "not available", system metrics still work
- **No Discord webhook** -- alert system is not registered, everything else works
- **No homepage config** -- homepage shows empty state with setup instructions
- **No temperature sensors** -- temperature section is hidden
- **Missing `/proc` files** -- affected metrics return zero, logged once as warning
- **Network interface changes** -- handled dynamically
- **Disk mount changes** -- handled dynamically
- **SignalR disconnection** -- browser shows reconnecting indicator, auto-reconnects
- **SQLite failure** -- falls back to in-memory data only

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for development guidelines.

## License

[MIT](LICENSE)
