#!/usr/bin/env bash
# Typage, lint et tests unitaires du web (dashboard) puis du mobile, un projet et un outil à la fois,
# en vérifiant la mémoire disponible avant chacun. Mêmes commandes que la CI. Usage : scripts/test-front.sh [dashboard|mobile...]
#   MIN_MO      mémoire libre minimale (Mo) avant chaque outil (défaut 1024)
#   ATTENTE_MAX secondes d'attente maximale qu'il se libère de la mémoire (défaut 300)
set -uo pipefail
cd "$(dirname "$0")/.."
MIN_MO=${MIN_MO:-1024}; ATTENTE_MAX=${ATTENTE_MAX:-300}

# Free memory in MB; empty when /proc/meminfo is unreadable (macOS…): the guard is then skipped.
libre() { [ -r /proc/meminfo ] && awk '/MemAvailable/ {print int($2/1024)}' /proc/meminfo; }

attendre_memoire() { # nom
  local attente=0
  if [ -z "$(libre)" ]; then echo "  (mémoire libre inconnue : pas de contrôle avant $1)" >&2; return 0; fi
  while [ "$(libre)" -lt "$MIN_MO" ]; do
    if [ "$attente" -ge "$ATTENTE_MAX" ]; then
      echo "Mémoire insuffisante pour $1 : $(libre) Mo libres, $MIN_MO requis. Arrêt." >&2; return 1
    fi
    echo "  $(libre) Mo libres < $MIN_MO Mo : attente avant $1…" >&2
    sleep 15; attente=$((attente + 15))
  done
}

if [ -r .nvmrc ]; then
  attendu=$(tr -d '[:space:]v' < .nvmrc)
  [ "$(node --version)" = "v$attendu" ] || echo "Attention : Node $(node --version), la CI utilise v$attendu (.nvmrc)." >&2
else
  echo "Attention : .nvmrc absent, version de Node de la CI inconnue." >&2
fi

# Install when node_modules is missing, older than the lockfile, or lacks the test runner.
installation_perimee() { # projet
  [ ! -f "$1/node_modules/.package-lock.json" ] || [ "$1/package-lock.json" -nt "$1/node_modules/.package-lock.json" ] \
    || [ ! -x "$1/node_modules/.bin/vitest" ]
}

projets=("$@")
[ ${#projets[@]} -eq 0 ] && projets=(dashboard mobile)

for p in "${projets[@]}"; do
  if installation_perimee "$p"; then
    attendre_memoire "$p npm ci" || exit 1
    echo "▶ $p : npm ci"; (cd "$p" && npm ci --no-audit --no-fund) || exit 1
  fi
  for etape in typecheck lint test; do
    attendre_memoire "$p $etape" || exit 1
    echo "▶ $p : $etape ($(libre || true) Mo libres)"
    (cd "$p" && npm run -s "$etape") || { echo "Échec : $p $etape" >&2; exit 1; }
  done
done
echo "Web et mobile : typage, lint et tests verts."
