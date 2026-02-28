.PHONY: help build clean restore run docker-up docker-down docker-logs docker-ps test lint format all

# Default target
.DEFAULT_GOAL := help

# Colors for output
BLUE := \033[0;34m
GREEN := \033[0;32m
YELLOW := \033[0;33m
RED := \033[0;31m
NC := \033[0m # No Color

# Project variables
PROJECT_NAME := PullRequestAnalyzer
DOCKER_COMPOSE := docker-compose.yml
DOTNET := dotnet

help: ## Display this help message
	@echo "$(BLUE)╔════════════════════════════════════════════════════════════╗$(NC)"
	@echo "$(BLUE)║  Pull Request Analyzer - Makefile Commands                 ║$(NC)"
	@echo "$(BLUE)╚════════════════════════════════════════════════════════════╝$(NC)"
	@echo ""
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | sort | awk 'BEGIN {FS = ":.*?## "}; {printf "$(GREEN)%-20s$(NC) %s\n", $$1, $$2}'
	@echo ""

# ==================== BUILD TARGETS ====================

restore: ## Restore NuGet dependencies
	@echo "$(YELLOW)Restoring NuGet dependencies...$(NC)"
	@$(DOTNET) restore
	@echo "$(GREEN)✓ Dependencies restored$(NC)"

build: restore ## Build the project
	@echo "$(YELLOW)Building project...$(NC)"
	@$(DOTNET) build --configuration Release
	@echo "$(GREEN)✓ Build completed$(NC)"

clean: ## Clean build artifacts
	@echo "$(YELLOW)Cleaning build artifacts...$(NC)"
	@$(DOTNET) clean
	@rm -rf bin/ obj/
	@echo "$(GREEN)✓ Clean completed$(NC)"

rebuild: clean build ## Clean and rebuild the project

# ==================== DOCKER TARGETS ====================

docker-up: ## Start RabbitMQ and other services
	@echo "$(YELLOW)Starting Docker services...$(NC)"
	@docker-compose -f $(DOCKER_COMPOSE) up -d
	@echo "$(GREEN)✓ Docker services started$(NC)"
	@echo "$(BLUE)RabbitMQ Management UI: http://localhost:15672$(NC)"
	@echo "$(BLUE)Credentials: guest/guest$(NC)"

docker-down: ## Stop Docker services
	@echo "$(YELLOW)Stopping Docker services...$(NC)"
	@docker-compose -f $(DOCKER_COMPOSE) down
	@echo "$(GREEN)✓ Docker services stopped$(NC)"

docker-logs: ## View Docker service logs
	@docker-compose -f $(DOCKER_COMPOSE) logs -f

docker-ps: ## Show running Docker containers
	@docker-compose -f $(DOCKER_COMPOSE) ps

docker-clean: ## Remove Docker containers and volumes
	@echo "$(YELLOW)Removing Docker containers and volumes...$(NC)"
	@docker-compose -f $(DOCKER_COMPOSE) down -v
	@echo "$(GREEN)✓ Docker cleanup completed$(NC)"

docker-rebuild: docker-down docker-up ## Restart Docker services

# ==================== RUN TARGETS ====================

run: ## Run the API server
	@echo "$(YELLOW)Starting API server...$(NC)"
	@$(DOTNET) run
	@echo "$(GREEN)✓ API server running$(NC)"

run-release: build ## Build and run in Release mode
	@echo "$(YELLOW)Starting API server (Release mode)...$(NC)"
	@$(DOTNET) run --configuration Release --no-build

dev: ## Start everything for development (Docker + API)
	@echo "$(BLUE)╔════════════════════════════════════════════════════════════╗$(NC)"
	@echo "$(BLUE)║  Starting Pull Request Analyzer (Development Mode)         ║$(NC)"
	@echo "$(BLUE)╚════════════════════════════════════════════════════════════╝$(NC)"
	@echo ""
	@echo "$(YELLOW)Step 1: Starting Docker services...$(NC)"
	@docker-compose -f $(DOCKER_COMPOSE) up -d
	@echo "$(GREEN)✓ Docker services started$(NC)"
	@echo ""
	@echo "$(YELLOW)Step 2: Waiting for Redis to be ready...$(NC)"
	@for i in 1 2 3 4 5 6 7 8 9 10; do \
		echo "  Attempt $$i/10..."; \
		docker-compose -f $(DOCKER_COMPOSE) exec -T redis redis-cli ping | grep -q PONG && echo "$(GREEN)✓ Redis is ready!$(NC)" && break; \
		sleep 2; \
	done
	@echo "$(YELLOW)Step 3: Waiting for RabbitMQ to be ready...$(NC)"
	@for i in 1 2 3 4 5 6 7 8 9 10; do \
		echo "  Attempt $$i/10..."; \
		docker-compose -f $(DOCKER_COMPOSE) exec -T rabbitmq rabbitmq-diagnostics -q ping && echo "$(GREEN)✓ RabbitMQ is ready!$(NC)" && break; \
		sleep 2; \
	done
	@echo ""
	@echo "$(YELLOW)Step 4: Building project...$(NC)"
	@$(DOTNET) build --configuration Release > /dev/null 2>&1
	@echo "$(GREEN)✓ Build completed$(NC)"
	@echo ""
	@echo "$(BLUE)╔════════════════════════════════════════════════════════════╗$(NC)"
	@echo "$(BLUE)║  Starting API Server                                       ║$(NC)"
	@echo "$(BLUE)╚════════════════════════════════════════════════════════════╝$(NC)"
	@echo ""
	@echo "$(GREEN)✓ Redis              : http://localhost:6379$(NC)"
	@echo "$(GREEN)✓ Redis Commander UI : http://localhost:8081$(NC)"
	@echo "$(GREEN)✓ RabbitMQ UI        : http://localhost:15672 (guest/guest)$(NC)"
	@echo "$(GREEN)✓ API Swagger        : http://localhost:5000/swagger$(NC)"
	@echo "$(GREEN)✓ API Health         : http://localhost:5000/health$(NC)"
	@echo ""
	@echo "$(YELLOW)Starting API + Background Worker...$(NC)"
	@$(DOTNET) run

# ==================== TEST TARGETS ====================

test: ## Run unit tests
	@echo "$(YELLOW)Running tests...$(NC)"
	@$(DOTNET) test --configuration Release
	@echo "$(GREEN)✓ Tests completed$(NC)"

test-watch: ## Run tests in watch mode
	@echo "$(YELLOW)Running tests in watch mode...$(NC)"
	@$(DOTNET) watch test

# ==================== CODE QUALITY ====================

lint: ## Run code analysis
	@echo "$(YELLOW)Running code analysis...$(NC)"
	@$(DOTNET) build --configuration Release /p:EnforceCodeStyleInBuild=true
	@echo "$(GREEN)✓ Code analysis completed$(NC)"

format: ## Format code with dotnet format
	@echo "$(YELLOW)Formatting code...$(NC)"
	@$(DOTNET) format
	@echo "$(GREEN)✓ Code formatted$(NC)"

# ==================== UTILITY TARGETS ====================

info: ## Display project information
	@echo "$(BLUE)Project Information:$(NC)"
	@echo "  Name: $(PROJECT_NAME)"
	@echo "  Framework: .NET 8"
	@echo "  Runtime: $(shell $(DOTNET) --version)"
	@echo ""
	@echo "$(BLUE)Docker Services:$(NC)"
	@echo "  - Redis              (port 6379)"
	@echo "  - Redis Commander UI (port 8081)"
	@echo "  - RabbitMQ           (port 5672)"
	@echo "  - RabbitMQ UI        (port 15672)"
	@echo ""
	@echo "$(BLUE)API Endpoints:$(NC)"
	@echo "  - Health: http://localhost:5000/health"
	@echo "  - Swagger: http://localhost:5000/swagger"
	@echo "  - Info: http://localhost:5000/info"

status: docker-ps ## Show status of Docker services

# ==================== COMBINED TARGETS ====================

setup: restore docker-up ## Setup project (restore deps and start Docker)

all: clean build docker-up ## Full setup: clean, build, and start Docker

start: dev ## Alias for 'dev' - starts everything

stop: docker-down ## Stop everything

restart: docker-rebuild run ## Restart everything

# ==================== DOCUMENTATION ====================

docs: ## Display documentation
	@echo "$(BLUE)╔════════════════════════════════════════════════════════════╗$(NC)"
	@echo "$(BLUE)║  Documentation Files                                       ║$(NC)"
	@echo "$(BLUE)╚════════════════════════════════════════════════════════════╝$(NC)"
	@echo ""
	@echo "$(GREEN)README.md$(NC)           - Main project documentation"
	@echo "$(GREEN)README-ASYNC.md$(NC)     - Asynchronous architecture guide"
	@echo "$(GREEN)DESIGN_NOTES.md$(NC)     - Architecture decisions and tradeoffs"
	@echo ""

# ==================== ADVANCED TARGETS ====================

publish: build ## Publish the application
	@echo "$(YELLOW)Publishing application...$(NC)"
	@$(DOTNET) publish -c Release -o ./publish
	@echo "$(GREEN)✓ Application published to ./publish$(NC)"

watch: ## Watch for changes and rebuild
	@echo "$(YELLOW)Watching for changes...$(NC)"
	@$(DOTNET) watch run

generate-pr-data: ## Generate example PR data from GitHub
	@echo "$(YELLOW)Generating PR data...$(NC)"
	@echo "$(RED)Note: Requires GITHUB_TOKEN environment variable$(NC)"
	@$(DOTNET) run --project . -- $$GITHUB_TOKEN mindsdb mindsdb 8000 mindsdb_pr_8000.json

# ==================== HEALTH CHECKS ====================

health: ## Check system health
	@echo "$(YELLOW)Checking system health...$(NC)"
	@echo ""
	@echo "$(BLUE)Docker Services:$(NC)"
	@docker-compose -f $(DOCKER_COMPOSE) ps || echo "$(RED)Docker not running$(NC)"
	@echo ""
	@echo "$(BLUE)API Health:$(NC)"
	@curl -s http://localhost:5000/health | jq . 2>/dev/null || echo "$(RED)API not responding$(NC)"
	@echo ""

# ==================== CLEANUP ====================

distclean: clean docker-clean ## Deep clean (removes all build artifacts and Docker data)
	@echo "$(YELLOW)Performing deep clean...$(NC)"
	@rm -rf publish/ .vs/ .vscode/
	@echo "$(GREEN)✓ Deep clean completed$(NC)"

.PHONY: help build clean restore run docker-up docker-down docker-logs docker-ps docker-clean \
        docker-rebuild run-release dev test test-watch lint format info status setup all start \
        stop restart docs publish watch generate-pr-data health distclean
