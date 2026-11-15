#!/usr/bin/env bash
# Usage: scripts/dev.sh up|down|api|test
set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
PW="${DEV_DB_PASSWORD:-devpass}"
case "${1:-}" in
  up)   docker compose -f docker-compose.dev.yml up -d --wait ;;
  down) docker compose -f docker-compose.dev.yml down ;;
  api)
    export ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5243
    export ConnectionStrings__DefaultConnection="Host=localhost;Port=5433;Database=tontinesapp;Username=postgres;Password=$PW"
    export ConnectionStrings__HangfireConnection="$ConnectionStrings__DefaultConnection"
    export ConnectionStrings__Redis="localhost:6380,abortConnect=false"
    export Jwt__Secret="dev-only-secret-key-at-least-32-characters-long"
    dotnet run --project src/Api --no-launch-profile ;;
  test) dotnet test tontApp.slnx ;;
  *) echo "usage: $0 up|down|api|test"; exit 1 ;;
esac
