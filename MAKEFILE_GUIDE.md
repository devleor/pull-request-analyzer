# Makefile Guide - Pull Request Analyzer

This guide explains how to use the Makefile to manage the Pull Request Analyzer project.

## Quick Start

### One Command to Rule Them All

```bash
# This single command does EVERYTHING:
# 1. Starts Docker (RabbitMQ)
# 2. Waits for RabbitMQ to be ready
# 3. Builds the project
# 4. Starts the API
make dev
```

That's it! After running `make dev`, your entire development environment is ready.

### First Time Setup

If you prefer a more manual approach:

```bash
# Setup: restore dependencies and start Docker
make setup

# Then run the API
make run
```

### Daily Development

```bash
# Start everything with one command
make dev

# Or use the alias
make start
```

### Stop Everything

```bash
make stop
```

## What `make dev` Does

When you run `make dev`, it automatically:

1. **Starts Docker Services**: Launches RabbitMQ container
2. **Waits for RabbitMQ**: Polls until RabbitMQ is ready (up to 20 seconds)
3. **Builds the Project**: Compiles the C# project in Release mode
4. **Starts the API**: Runs the ASP.NET Core application

Then you get immediate access to:
- **RabbitMQ Management UI**: `http://localhost:15672` (guest/guest)
- **API Swagger**: `http://localhost:5000/swagger`
- **API Health**: `http://localhost:5000/health`

## Available Commands

### Build Commands

| Command | Description |
|---------|-------------|
| `make restore` | Restore NuGet dependencies |
| `make build` | Build the project in Release mode |
| `make clean` | Clean build artifacts |
| `make rebuild` | Clean and rebuild the project |
| `make publish` | Publish the application to `./publish` folder |

**Examples:**

```bash
# Build the project
make build

# Clean everything and rebuild
make rebuild

# Publish for deployment
make publish
```

### Docker Commands

| Command | Description |
|---------|-------------|
| `make docker-up` | Start RabbitMQ and other services |
| `make docker-down` | Stop Docker services |
| `make docker-logs` | View Docker service logs |
| `make docker-ps` | Show running Docker containers |
| `make docker-clean` | Remove containers and volumes |
| `make docker-rebuild` | Restart Docker services |

**Examples:**

```bash
# Start RabbitMQ manually
make docker-up

# View logs
make docker-logs

# Restart everything
make docker-rebuild

# Clean up (removes data!)
make docker-clean
```

### Run Commands

| Command | Description |
|---------|-------------|
| `make dev` | **Start everything** (Docker + API) ⭐ |
| `make start` | Alias for `dev` |
| `make run` | Run the API server only |
| `make run-release` | Build and run in Release mode |
| `make watch` | Watch for changes and rebuild |

**Examples:**

```bash
# Start everything (recommended)
make dev

# Run API only (if Docker is already running)
make run

# Watch for changes
make watch
```

### Test Commands

| Command | Description |
|---------|-------------|
| `make test` | Run unit tests |
| `make test-watch` | Run tests in watch mode |

**Examples:**

```bash
# Run all tests once
make test

# Run tests continuously
make test-watch
```

### Code Quality Commands

| Command | Description |
|---------|-------------|
| `make lint` | Run code analysis |
| `make format` | Format code with dotnet format |

**Examples:**

```bash
# Check code quality
make lint

# Auto-format code
make format
```

### Utility Commands

| Command | Description |
|---------|-------------|
| `make help` | Display all available commands |
| `make info` | Show project information |
| `make status` | Show Docker service status |
| `make health` | Check system health |
| `make docs` | Display documentation files |

**Examples:**

```bash
# See all commands
make help

# Check if everything is running
make health

# View project info
make info
```

### Combined Commands

| Command | Description |
|---------|-------------|
| `make setup` | Setup project (restore deps + start Docker) |
| `make all` | Full setup (clean, build, start Docker) |
| `make dev` | **Start everything** ⭐ |
| `make start` | Alias for `dev` |
| `make stop` | Stop everything |
| `make restart` | Restart everything |

**Examples:**

```bash
# First time setup
make all

# Start development
make dev

# Stop everything
make stop

# Restart after changes
make restart
```

### Advanced Commands

| Command | Description |
|---------|-------------|
| `make generate-pr-data` | Generate example PR data from GitHub |
| `make distclean` | Deep clean (removes all artifacts and Docker data) |

**Examples:**

```bash
# Generate PR data (requires GITHUB_TOKEN)
export GITHUB_TOKEN='your_token'
make generate-pr-data

# Complete cleanup
make distclean
```

## Common Workflows

### Development Workflow (Recommended)

```bash
# First time
make dev

# After making code changes, the API will auto-reload if you're using dotnet watch
# Or restart with:
make restart
```

### Full Development Workflow

```bash
# Start everything
make dev

# In another terminal, while dev is running:
# Check code quality
make lint
make format

# Run tests
make test

# When done, stop everything
make stop
```

### Deployment Workflow

```bash
# Clean build
make clean
make build

# Publish
make publish

# The application is now in ./publish/
```

### Troubleshooting Workflow

```bash
# Check system health
make health

# View Docker logs
make docker-logs

# Restart everything
make restart

# If still having issues, do a deep clean
make distclean
make dev
```

### Testing Workflow

```bash
# Run tests once
make test

# Watch tests during development
make test-watch

# After fixing issues
make rebuild
make test
```

## Environment Variables

Some commands require environment variables:

```bash
# For GitHub API access
export GITHUB_TOKEN='your_github_token'

# For OpenRouter LLM access
export OPENROUTER_API_KEY='your_openrouter_key'

# Then run
make dev
```

## Docker Services

The `make dev` command (and `docker-up`) starts:

- **RabbitMQ** on port 5672 (AMQP protocol)
- **RabbitMQ Management UI** on port 15672 (web interface)

Access the management UI at: `http://localhost:15672`
- Username: `guest`
- Password: `guest`

## Tips and Tricks

### Run Multiple Commands

```bash
# Chain commands with &&
make clean && make build && make docker-up && make run

# Or use combined targets
make all
make dev
```

### View Help Anytime

```bash
# See all available commands
make help

# See project information
make info
```

### Check Status

```bash
# See what's running
make status

# Full health check
make health
```

### Clean Up Properly

```bash
# Light clean (keeps Docker data)
make clean

# Full clean (removes Docker data too)
make distclean
```

### Watching for Changes

```bash
# Auto-rebuild and run on file changes
make watch
```

## Makefile Structure

The Makefile is organized into sections:

- **BUILD TARGETS**: Compilation and dependency management
- **DOCKER TARGETS**: Container orchestration
- **RUN TARGETS**: Application execution
- **TEST TARGETS**: Testing and validation
- **CODE QUALITY**: Linting and formatting
- **UTILITY TARGETS**: Information and status
- **COMBINED TARGETS**: Multi-step workflows
- **DOCUMENTATION**: Help and guides
- **ADVANCED TARGETS**: Publishing and data generation
- **HEALTH CHECKS**: System diagnostics
- **CLEANUP**: Artifact removal

## Customization

You can customize the Makefile by editing these variables at the top:

```makefile
PROJECT_NAME := PullRequestAnalyzer
DOCKER_COMPOSE := docker-compose.yml
DOTNET := dotnet
```

## Troubleshooting

### "make: command not found"

Install Make:

```bash
# Ubuntu/Debian
sudo apt-get install make

# macOS
brew install make

# Windows (use WSL or install GNU Make)
```

### Docker commands fail

Make sure Docker and Docker Compose are installed:

```bash
docker --version
docker-compose --version
```

### API doesn't start

Check if port 5000 is already in use:

```bash
lsof -i :5000
```

### RabbitMQ not connecting

Verify RabbitMQ is running:

```bash
make docker-ps
make docker-logs
```

### `make dev` hangs waiting for RabbitMQ

RabbitMQ might be taking longer to start. Check logs:

```bash
make docker-logs
```

If RabbitMQ is stuck, do a full restart:

```bash
make docker-clean
make dev
```

## Next Steps

After setting up with Make:

1. Read the main documentation: `README.md`
2. Learn about async architecture: `README-ASYNC.md`
3. Understand design decisions: `DESIGN_NOTES.md`
4. Start developing!

```bash
make dev
```

## Summary

**The simplest way to get started:**

```bash
make dev
```

That's it! Everything else is optional. 🚀
