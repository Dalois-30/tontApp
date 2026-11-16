#!/usr/bin/env bash
# Lance les tests .NET un projet à la fois, en vérifiant la mémoire disponible avant chaque lot
# pour ne pas saturer la machine. Usage : scripts/test-par-lots.sh [projet...]
#   MIN_MO      mémoire libre minimale (Mo) avant un projet ordinaire (défaut 2048)
#   MIN_MO_API  mémoire libre minimale avant tests/ApiTests, qui démarre PostgreSQL + Redis (défaut 3072)
#   ATTENTE_MAX secondes d'attente maximale qu'il se libère de la mémoire (défaut 300)
set -uo pipefail
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
MIN_MO=${MIN_MO:-2048}; MIN_MO_API=${MIN_MO_API:-3072}; ATTENTE_MAX=${ATTENTE_MAX:-300}

libre() { awk '/MemAvailable/ {print int($2/1024)}' /proc/meminfo; }

attendre_memoire() { # seuil nom
  local seuil=$1 attente=0
  while [ "$(libre)" -lt "$seuil" ]; do
    if [ "$attente" -ge "$ATTENTE_MAX" ]; then
      echo "Mémoire insuffisante pour $2 : $(libre) Mo libres, $seuil requis. Arrêt." >&2; return 1
    fi
    echo "  $(libre) Mo libres < $seuil Mo : attente avant $2…" >&2
    sleep 15; attente=$((attente + 15))
  done
}

projets=("$@")
[ ${#projets[@]} -eq 0 ] && mapfile -t projets < <(ls tests/*/*.csproj | grep -v ApiTests; ls tests/ApiTests/*.csproj 2>/dev/null)

echo "Mémoire libre au départ : $(libre) Mo"
attendre_memoire "$MIN_MO" "la compilation" || exit 1
dotnet build tontApp.slnx --nologo -v q -m:1 >/dev/null || { echo "Échec de compilation" >&2; exit 1; }

echec=0
for p in "${projets[@]}"; do
  seuil=$MIN_MO; [[ "$p" == *ApiTests* ]] && seuil=$MIN_MO_API
  attendre_memoire "$seuil" "$p" || { echec=1; break; }
  echo "▶ $p ($(libre) Mo libres)"
  dotnet test "$p" --no-build --nologo -v q 2>&1 | grep -E " error |Passed!|Failed!|\[FAIL\]" || echec=1
done
exit $echec
