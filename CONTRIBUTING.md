# Contributing to Merlin

Thanks for your interest in contributing! Here is how to get started.

## Development Setup

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download)
2. Clone the repository
3. Run the app locally:

```bash
dotnet run --project src/Merlin.Web
```

On Linux, Merlin reads real system metrics from `/proc` and `/sys`. On other platforms, metrics degrade gracefully to zero values but the UI still loads.

## Running Tests

```bash
dotnet test
```

Tests use fixture data to simulate `/proc` and `/sys` files, so they run on any platform.

## Testing in a Container

Pull the latest published image or build locally:

```bash
# Using the published image
docker compose -f examples/docker/compose.yaml up -d

# Or build from source
docker compose up -d --build
```

Check **http://localhost:5050** to verify.

## Making Changes

1. Fork the repository
2. Create a feature branch from `main`
3. Make your changes
4. Run `dotnet build` (warnings are treated as errors)
5. Run `dotnet test` and ensure all tests pass
6. Commit with [conventional commit](https://www.conventionalcommits.org/) messages:
   - `feat:` for new features
   - `fix:` for bug fixes
   - `docs:` for documentation
   - `refactor:` for code changes that don't add features or fix bugs
   - `test:` for adding or updating tests
   - `chore:` for maintenance tasks
7. Open a pull request against `main`

## Code Style

- Use `record` types for immutable models
- Pass `CancellationToken` through async methods
- Handle errors gracefully -- Merlin should never crash
- Follow existing patterns in the codebase
- Use `dotnet format` to ensure consistent formatting

## Project Structure

```
src/Merlin.Web/
├── Program.cs                              # Entry point, DI, all endpoints
├── Hubs/                                   # SignalR hub
├── Models/                                 # Immutable record types
├── Services/
│   ├── Alerts/                             # Discord webhook alerting
│   ├── Containers/                         # Container engine API client
│   ├── Homepage/                           # Service catalog (labels + config)
│   ├── Metrics/                            # /proc + /sys parsing, ring buffer
│   └── Persistence/                        # SQLite storage
└── wwwroot/                                # Static frontend (vanilla JS, no build step)

tests/Merlin.Web.Tests/                     # xUnit tests with fixture data

examples/
├── docker/                                 # Docker socket compose
├── homepage/                               # Homepage with labels + static config
└── podman/                                 # Rootful Podman compose
```

## Areas for Contribution

### Good First Issues

- Add more temperature sensor paths for different hardware
- Add keyboard shortcuts for container actions
- Improve error messages in the UI for edge cases
- Add more unit test coverage for edge cases

### Larger Features

- **Additional alert channels** -- email, Slack, Telegram, Gotify, ntfy
- **GPU monitoring** -- NVIDIA (nvidia-smi) and AMD (amdgpu) support
- **Custom dashboard layouts** -- drag-and-drop or configurable grid
- **Light theme** -- intentional light mode as an alternative to the dark default
- **Prometheus export** -- `/metrics` endpoint for scraping
- **Multi-node support** -- aggregate metrics from multiple Merlin instances
- **Authentication** -- optional basic auth or OIDC for exposed deployments
- **Container log export** -- download or search historical logs
- **Configurable refresh rates** -- user-adjustable polling intervals

## Questions?

Open an issue -- happy to help.
