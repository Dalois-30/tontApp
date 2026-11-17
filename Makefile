# Commandes de dev locales (voir scripts/dev.sh)
.PHONY: help up down api test test-lots test-front build restart logs psql reset seed donnees-tutoriel otp dashboard mobile mobile-apk

help: ## Liste des commandes
	@grep -E '^[a-z-]+:.*##' $(MAKEFILE_LIST) | awk -F':.*## ' '{printf "  make %-10s %s\n", $$1, $$2}'

up: ## Démarre Postgres (5433) + Redis (6380)
	./scripts/dev.sh up

down: ## Arrête l'infra de dev
	./scripts/dev.sh down

api: ## Lance l'API sur http://localhost:5243
	./scripts/dev.sh api

test: ## Lance tous les tests
	./scripts/dev.sh test

test-lots: ## Tests un projet à la fois, selon la mémoire disponible
	./scripts/test-par-lots.sh

test-front: ## Typage, lint et tests du web puis du mobile (comme la CI)
	./scripts/test-front.sh

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

seed: ## Crée les comptes et la tontine de démo (API lancée) ; CLASSEUR=fichier.xlsx importe aussi un projet
	DEMO_CLASSEUR="$(CLASSEUR)" ./scripts/demo.sh seed

donnees-tutoriel: ## Après make seed CLASSEUR=… : tontine, trésorière, séance et déclarations filmées dans docs/tutoriel
	python3 scripts/donnees_tutoriel.py

otp: ## Affiche le code OTP d'un numéro : make otp TEL=+237670000010
	@./scripts/demo.sh otp $(TEL)

dashboard: ## Lance le dashboard web sur http://localhost:3000
	cd dashboard && npm run dev

mobile: ## Lance Metro pour l'app Android branchée en USB (adb)
	adb reverse tcp:5243 tcp:5243 && adb reverse tcp:8081 tcp:8081
	cd mobile && EXPO_PUBLIC_API_URL=http://localhost:5243 npx expo start --localhost

# Gradle est gourmand : au moins APK_MIN_MO Mo libres avant de commencer.
APK_MIN_MO ?= 4096
mobile-apk: ## APK debug arm64 : prebuild --clean (requis après un changement de SDK Expo), bundle JS, gradlew
	@libre=$$(awk '/MemAvailable/ {print int($$2/1024)}' /proc/meminfo); \
	  if [ "$$libre" -lt $(APK_MIN_MO) ]; then echo "Mémoire insuffisante : $$libre Mo libres, $(APK_MIN_MO) requis." >&2; exit 1; fi; \
	  echo "▶ $$libre Mo libres"
	cd mobile && CI=1 npx expo prebuild --platform android --clean --no-install
	cd mobile && mkdir -p android/app/src/main/assets && EXPO_PUBLIC_API_URL=http://localhost:5243 npx expo export:embed \
	  --platform android --dev false --entry-file index.ts \
	  --bundle-output android/app/src/main/assets/index.android.bundle --assets-dest android/app/src/main/res
	cd mobile/android && ANDROID_HOME=$${ANDROID_HOME:-$$HOME/Android/Sdk} ./gradlew app:assembleDebug -PreactNativeArchitectures=arm64-v8a
	@echo "APK : mobile/android/app/build/outputs/apk/debug/app-debug.apk"
