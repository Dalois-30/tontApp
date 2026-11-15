# Commandes de dev locales (voir scripts/dev.sh)
.PHONY: help up down api test build restart logs psql

help: ## Liste des commandes
	@grep -E '^[a-z]+:.*##' $(MAKEFILE_LIST) | awk -F':.*## ' '{printf "  make %-8s %s\n", $$1, $$2}'

up: ## Démarre Postgres (5433) + Redis (6380)
	./scripts/dev.sh up

down: ## Arrête l'infra de dev
	./scripts/dev.sh down

api: ## Lance l'API sur http://localhost:5243
	./scripts/dev.sh api

test: ## Lance tous les tests
	./scripts/dev.sh test

build: ## Compile la solution
	PATH="$$HOME/.dotnet:$$PATH" DOTNET_ROOT="$$HOME/.dotnet" dotnet build tontApp.slnx

restart: down up ## Redémarre l'infra

logs: ## Logs Postgres + Redis
	docker compose -f docker-compose.dev.yml logs -f

psql: ## Console SQL sur la base de dev
	docker exec -it tontapp-dev-postgres-1 psql -U postgres -d tontinesapp
