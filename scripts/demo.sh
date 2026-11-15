#!/usr/bin/env bash
# Données et utilitaires de démonstration (environnement de dev uniquement).
# Usage :
#   scripts/demo.sh seed               crée les comptes et la tontine de démo
#   scripts/demo.sh otp <telephone>    affiche le code OTP en cours (au lieu du SMS)
#   scripts/demo.sh role <tel> <Role>  change le rôle d'un compte (Membre|Gestionnaire|Admin)
set -euo pipefail
cd "$(dirname "$0")/.."

API="${API:-http://localhost:5243/api/v1}"
PASSWORD="${DEMO_PASSWORD:-Demo2026!}"
PG=tontapp-dev-postgres-1
REDIS=tontapp-dev-redis-1

psql_c() { docker exec "$PG" psql -U postgres -d tontinesapp -tAc "$1"; }
otp()    { docker exec "$REDIS" redis-cli GET "otp:$1"; }
json()   { python3 -c "import sys,json; print(json.load(sys.stdin)$1)"; }
post()   { curl -sf -H 'Content-Type: application/json' "$@"; }

# register + OTP verification (idempotent: skips existing accounts)
compte() { # tel nom role
  local tel=$1 nom=$2 role=$3
  if [ -z "$(psql_c "select 1 from utilisateurs where telephone='$tel'")" ]; then
    post -X POST "$API/auth/register" -d "{\"telephone\":\"$tel\",\"nom\":\"$nom\",\"motDePasse\":\"$PASSWORD\"}" >/dev/null
    post -X POST "$API/auth/verifier-otp" -d "{\"telephone\":\"$tel\",\"code\":\"$(otp "$tel")\"}" >/dev/null
  fi
  psql_c "update utilisateurs set role='$role' where telephone='$tel'" >/dev/null
  echo "  $role  $nom  $tel" >&2
}

token() { post -X POST "$API/auth/login" -d "{\"telephone\":\"$1\",\"motDePasse\":\"$PASSWORD\"}" | json '["accessToken"]'; }

seed() {
  curl -sf "${API%/api/v1}/health" >/dev/null || { echo "API injoignable : lancez 'make api'"; exit 1; }
  echo "Comptes (mot de passe : $PASSWORD)" >&2
  compte +237670000001 "Admin TontApp"    Admin
  compte +237670000002 "Awa Traore"       Gestionnaire
  compte +237670000003 "Ibrahim Sawadogo" Membre
  compte +237670000004 "Mariam Kabore"    Membre
  compte +237670000005 "Seydou Ouedraogo" Membre
  compte +237670000011 "Ousmane Zongo"    Membre

  local g; g=$(token +237670000002)
  local t; t=$(psql_c "select id from tontines where nom='Tontine du Marche' limit 1")
  if [ -z "$t" ]; then
    t=$(post -X POST "$API/tontines" -H "Authorization: Bearer $g" \
      -d '{"name":"Tontine du Marche","description":"Epargne mensuelle des commercantes du marche central","contributionAmount":10000,"periodicity":"Monthly","maxMembers":6}' | json '["id"]')
    local code; code=$(curl -sf "$API/tontines/$t/invitation/generer?nombreUsagesMax=10&expirationJours=30" -H "Authorization: Bearer $g" | json '["code"]')
    for tel in +237670000003 +237670000004 +237670000005; do
      local nom; nom=$(psql_c "select nom from utilisateurs where telephone='$tel'")
      post -X POST "$API/tontines/rejoindre" -H "Authorization: Bearer $(token $tel)" \
        -d "{\"code\":\"$code\",\"memberName\":\"$nom\"}" >/dev/null
    done
    echo "Tontine « Tontine du Marche » créée (3 membres, statut Draft)" >&2
    echo "Code d'invitation (10 usages, 30 jours) : $code"
  else
    echo "Tontine de démo déjà présente : $t" >&2
  fi

  # Optional: collective project imported from a contributions workbook.
  if [ -n "${DEMO_CLASSEUR:-}" ]; then
    python3 -I scripts/import_classeur.py "$DEMO_CLASSEUR" --api "$API"
  fi
}

case "${1:-}" in
  seed) seed ;;
  otp)  otp "${2:?telephone}" ;;
  role) psql_c "update utilisateurs set role='${3:?role}' where telephone='${2:?telephone}'" ;;
  *) sed -n 2,6p "$0"; exit 1 ;;
esac
