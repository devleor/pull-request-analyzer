# Makefile Guide

This guide explains all available `make` commands for the Pull Request Analyzer project.

## Quick Start

```bash
make dev
```

This single command starts everything:

1. Starts Docker (Redis + Redis Commander)
2. Waits for Redis to be healthy
3. Builds the project in Release mode
4. Starts the API and background worker

## Services Started by `make dev`

| Service | URL | Description |
|---|---|---|
| API + Swagger | http://localhost:5000/swagger | Main API |
| Health Check | http://localhost:5000/health | Redis connectivity |
| Redis | localhost:6379 | Cache + Queue + Lock |
| Redis Commander | http://localhost:8081 | Redis UI |

## All Commands

### Development

| Command | Description |
|---|---|
| `make dev` | Start everything (Docker + build + API) |
| `make start` | Alias for `make dev` |
| `make run` | Run API only (Docker must already be up) |
| `make watch` | Hot-reload mode (Docker must already be up) |
| `make stop` | Stop API and Docker |
| `make restart` | Stop and start everything again |

### Build

| Command | Description |
|---|---|
| `make build` | Build in Release mode |
| `make rebuild` | Clean then build |
| `make clean` | Remove build artifacts |
| `make publish` | Publish to `./publish` |
| `make restore` | Restore NuGet dependencies |

### Docker

| Command | Description |
|---|---|
| `make docker-up` | Start Docker services (Redis) |
| `make docker-down` | Stop Docker services |
| `make docker-logs` | Follow Docker logs |
| `make docker-ps` | List running containers |
| `make docker-rebuild` | Restart Docker services |
| `make docker-clean` | Remove containers and volumes |

### Quality

| Command | Description |
|---|---|
| `make test` | Run tests |
| `make test-watch` | Run tests in watch mode |
| `make lint` | Static analysis |
| `make format` | Format code |

### Utilities

| Command | Description |
|---|---|
| `make help` | List all commands |
| `make info` | Show project info |
| `make health` | Check health of all services |
| `make status` | Show Docker container status |
| `make setup` | First-time setup |
| `make distclean` | Deep clean (artifacts + Docker volumes) |

## Common Workflows

### Daily Development

```bash
make dev       # Start everything
make restart   # After config changes
make stop      # End of day
```

### Code Quality

```bash
make lint      # Check code quality
make format    # Auto-format code
make test      # Run tests
```

### Deployment

```bash
make clean && make build && make publish
# Application is now in ./publish/
```

## Troubleshooting

**Redis not connecting:**

```bash
make docker-ps       # Check if Redis container is running
make docker-logs     # Check Redis logs
make docker-rebuild  # Restart Redis
```

**`make dev` hangs waiting for Redis:**

```bash
make docker-logs     # Check what Redis is doing
make docker-clean && make dev  # Force restart
```

**Build errors:**

```bash
make clean && make build   # Clean build
make restore               # Restore NuGet packages
```

**Port 5000 already in use:**

```bash
lsof -i :5000   # Find the process
kill -9 <PID>   # Kill it
make run        # Restart
```
