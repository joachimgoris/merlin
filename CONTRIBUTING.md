# Contributing to Merlin

Thanks for your interest in contributing! Here's how to get started.

## Development Setup

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download)
2. Clone the repository
3. Run the app locally:

```bash
cd src/Merlin.Web
dotnet run
```

On Linux, Merlin reads real system metrics. On other platforms, metrics degrade gracefully to zero.

## Running Tests

```bash
dotnet test
```

Tests use fixture data to simulate `/proc` and `/sys` files, so they run on any platform.

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
- Handle errors gracefully — Merlin should never crash
- Follow existing patterns in the codebase

## Areas for Contribution

### Good first issues

- Add more temperature sensor paths for different hardware
- Improve changelog filename detection
- Add keyboard shortcuts for container actions

### Larger features

- Webhook/email notifications for high resource usage
- Historical data persistence (optional SQLite)
- Docker socket support (in addition to Podman)
- Custom dashboard layouts
- Additional themes
- GPU monitoring (NVIDIA/AMD)

## Testing in a Container

```bash
podman-compose up -d --build
```

This builds the image and starts Merlin with host mounts. Check `http://localhost:5050`.

## Questions?

Open an issue — happy to help.
