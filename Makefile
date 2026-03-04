.DEFAULT_GOAL := help

BLUE   := \033[0;34m
GREEN  := \033[0;32m
YELLOW := \033[0;33m
RED    := \033[0;31m
NC     := \033[0m

PROJECT  := PullRequestAnalyzer
COMPOSE  := docker-compose.yml
DOTNET   := dotnet

.PHONY: help
help:
	@echo "$(BLUE)Pull Request Analyzer$(NC)"
	@echo ""
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | sort | \
		awk 'BEGIN {FS = ":.*?## "}; {printf "$(GREEN)%-18s$(NC) %s\n", $$1, $$2}'
	@echo ""

# ── Build ──────────────────────────────────────────────────────────────────────

.PHONY: restore build clean rebuild publish

restore: ## Restore NuGet dependencies
	@$(DOTNET) restore

build: restore ## Build (Release)
	@$(DOTNET) build --configuration Release --no-restore

clean: ## Remove build artifacts
	@$(DOTNET) clean
	@rm -rf bin/ obj/

rebuild: clean build ## Clean then build

publish: build ## Publish to ./publish
	@$(DOTNET) publish -c Release -o ./publish --no-build

# ── Docker ─────────────────────────────────────────────────────────────────────

.PHONY: docker-up docker-down docker-logs docker-ps docker-clean docker-rebuild

docker-up: ## Start all Docker services
	@docker-compose -f $(COMPOSE) up -d

docker-down: ## Stop all Docker services
	@docker-compose -f $(COMPOSE) down

docker-logs: ## Follow Docker service logs
	@docker-compose -f $(COMPOSE) logs -f

docker-ps: ## List running containers
	@docker-compose -f $(COMPOSE) ps

docker-clean: ## Remove containers and volumes
	@docker-compose -f $(COMPOSE) down -v

docker-rebuild: docker-down docker-up ## Restart Docker services

# ── Development ────────────────────────────────────────────────────────────────

.PHONY: dev start stop restart watch run

dev: ## Start everything in Docker (Redis + API with Worker)
	@echo "$(BLUE)Starting Pull Request Analyzer...$(NC)"
	@echo ""
	@echo "$(YELLOW)► Building and starting all services...$(NC)"
	@docker-compose -f $(COMPOSE) up --build -d
	@echo ""
	@echo "$(YELLOW)► Waiting for Redis...$(NC)"
	@until docker-compose -f $(COMPOSE) exec -T redis redis-cli ping 2>/dev/null | grep -q PONG; do \
		printf "."; sleep 1; \
	done
	@echo " $(GREEN)ready$(NC)"
	@echo "$(YELLOW)► Waiting for API...$(NC)"
	@until curl -sf http://localhost:5000/health 2>/dev/null | grep -q healthy; do \
		printf "."; sleep 2; \
	done
	@echo " $(GREEN)ready$(NC)"
	@echo ""
	@echo "$(GREEN)✓ All services running in Docker:$(NC)"
	@echo "$(GREEN)  Redis              → localhost:6379$(NC)"
	@echo "$(GREEN)  Redis Commander    → http://localhost:8081$(NC)"
	@echo "$(GREEN)  API + Swagger      → http://localhost:5000/swagger$(NC)"
	@echo "$(GREEN)  Health             → http://localhost:5000/health$(NC)"
	@echo ""
	@echo "$(YELLOW)► Following API logs (Ctrl+C to stop)...$(NC)"
	@docker-compose -f $(COMPOSE) logs -f api

start: dev ## Alias for dev

stop: docker-down ## Stop everything

restart: stop dev ## Restart everything

run: ## View logs of running containers
	@docker-compose -f $(COMPOSE) logs -f

watch: ## Rebuild and restart API container
	@docker-compose -f $(COMPOSE) up --build -d api
	@docker-compose -f $(COMPOSE) logs -f api

# ── Quality ────────────────────────────────────────────────────────────────────

.PHONY: test test-watch lint format

test: ## Run tests
	@$(DOTNET) test --configuration Release

test-watch: ## Run tests in watch mode
	@$(DOTNET) watch test

lint: ## Static analysis
	@$(DOTNET) build --configuration Release /p:EnforceCodeStyleInBuild=true

format: ## Format code
	@$(DOTNET) format

# ── Utilities ──────────────────────────────────────────────────────────────────

.PHONY: health info status setup distclean

health: ## Check health of all services
	@echo "$(BLUE)Docker:$(NC)"
	@docker-compose -f $(COMPOSE) ps
	@echo ""
	@echo "$(BLUE)API:$(NC)"
	@curl -sf http://localhost:5000/health | jq . 2>/dev/null || echo "$(RED)API not responding$(NC)"

info: ## Show project info
	@echo "$(BLUE)Project:$(NC) $(PROJECT)"
	@echo "$(BLUE).NET:$(NC)    $$($(DOTNET) --version)"
	@echo ""
	@echo "$(BLUE)Services:$(NC)"
	@echo "  Redis           localhost:6379"
	@echo "  Redis UI        http://localhost:8081"
	@echo "  API             http://localhost:5000"
	@echo "  Swagger         http://localhost:5000/swagger"

status: docker-ps ## Show Docker container status

setup: restore docker-up ## First-time setup (restore deps + start Docker)

distclean: clean docker-clean ## Deep clean (artifacts + Docker volumes)
	@rm -rf publish/ .vs/ .vscode/
