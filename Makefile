.DEFAULT_GOAL := help

COMPOSE := docker compose -f infra/docker-compose.yml
FRONTEND := pnpm --dir frontend
BACKEND_SLN := backend/Amsterfam/Amsterfam.slnx
EF_PROJECT := backend/Amsterfam/Amsterfam.Infrastructure
EF_STARTUP := backend/Amsterfam/Amsterfam.Api

.PHONY: help install up down down-v logs build build-frontend build-backend \
	test test-frontend test-backend lint lint-frontend lint-backend e2e migrate migration clean

help: ## Show this help
	@grep -hE '^[a-zA-Z0-9_-]+:.*?## .*$$' $(MAKEFILE_LIST) | sort | awk 'BEGIN {FS = ":.*?## "}; {printf "\033[36m%-16s\033[0m %s\n", $$1, $$2}'

install: ## Install frontend and backend dependencies
	$(FRONTEND) install --frozen-lockfile
	dotnet restore $(BACKEND_SLN)

up: ## Start local dev infra (db + authentik; api runs via dotnet run)
	$(COMPOSE) up -d

down: ## Stop local dev infra
	$(COMPOSE) down

down-v: ## Stop local dev infra and delete volumes (db data included)
	$(COMPOSE) down -v

logs: ## Tail logs from local dev infra
	$(COMPOSE) logs -f

build: build-frontend build-backend ## Build frontend and backend

build-frontend: ## Build the Angular PWA
	$(FRONTEND) build

build-backend: ## Build the .NET solution
	dotnet build $(BACKEND_SLN)

test: test-frontend test-backend ## Run frontend and backend unit tests

test-frontend: ## Run Angular unit tests once (no watch)
	$(FRONTEND) test --watch=false

test-backend: ## Run .NET unit tests
	dotnet test $(BACKEND_SLN)

lint: lint-frontend lint-backend ## Lint frontend and backend

lint-frontend: ## Lint the frontend
	$(FRONTEND) lint

lint-backend: ## Check backend formatting (csharpier)
	dotnet tool run csharpier check backend/Amsterfam

e2e: ## Run Playwright e2e suite (boots db, api, and frontend itself)
	$(FRONTEND) e2e

migrate: ## Apply pending EF Core migrations to the local db
	dotnet ef database update --project $(EF_PROJECT) --startup-project $(EF_STARTUP)

migration: ## Add a new EF Core migration; usage: make migration name=AddThing
	dotnet ef migrations add $(name) --project $(EF_PROJECT) --startup-project $(EF_STARTUP)

clean: ## Remove build artifacts (frontend dist/cache, backend bin/obj)
	rm -rf frontend/dist frontend/.angular/cache frontend/test-results frontend/playwright-report
	find backend/Amsterfam -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
