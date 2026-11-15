# Commandes de dev locales (voir scripts/dev.sh)
.PHONY: help up down api test build restart logs psql reset seed otp dashboard mobile

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

reset: ## Efface la base de dev (volume Postgres) et relance l'infra
	docker compose -f docker-compose.dev.yml down -v
	./scripts/dev.sh up

seed: ## Crée les comptes et la tontine de démo (API lancée)
	./scripts/demo.sh seed

otp: ## Affiche le code OTP d'un numéro : make otp TEL=+22670000010
	@./scripts/demo.sh otp $(TEL)

dashboard: ## Lance le dashboard web sur http://localhost:3000
	cd dashboard && npm run dev

mobile: ## Lance Metro pour l'app Android branchée en USB (adb)
	adb reverse tcp:5243 tcp:5243 && adb reverse tcp:8081 tcp:8081
	cd mobile && EXPO_PUBLIC_API_URL=http://localhost:5243 npx expo start --localhost
