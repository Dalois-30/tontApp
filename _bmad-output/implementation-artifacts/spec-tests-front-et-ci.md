---
title: 'Tests unitaires du web et du mobile, et CI complète (API sur conteneurs, web, mobile)'
type: 'chore'
created: '2026-11-17'
status: 'draft'
route: 'dispatch'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** La logique côté clients (statuts et restes des tontines, tours dus, libellés « Payé partiellement », droits du bureau `peutGerer`/`peutValider`, écrans hors bureau) n'a aucun test : web et mobile n'ont pas de cadre de test, et la CI ne vérifie ni le typage, ni le lint, ni le comportement du web et du mobile. Deux projets de la solution n'ont pas d'Id dans `tontApp.slnx`.

**Approach:** Ajouter un cadre de tests unitaires léger au web et au mobile (fonctions pures d'abord), les tests de la logique déjà livrée, des scripts `test`, et une CI qui lance pour chaque push : tests .NET (dont `tests/ApiTests` sur Docker), puis typage + lint + tests du web et du mobile.

## Boundaries & Constraints

**Always:** tests rapides et sans réseau ni appareil ; même version de Node en local et en CI ; la CI échoue au moindre test, type ou lint en erreur ; les tests .NET gardent la couverture actuelle ; lancement local documenté dans le Makefile (`make test-front`).

**Never:** pas de tests de bout en bout navigateur ou appareil dans ce chantier (non demandé) ; pas de modification du comportement des applications sauf correction d'un bug révélé par un test (corrigé dans ce cycle).

</frozen-after-approval>

## Code Map

- `dashboard/package.json`, `mobile/package.json` -- aucun script `test`, aucun cadre (`vitest`/`jest` absents de `node_modules/.bin`).
- Web : `dashboard/lib/format.ts` (`libelleStatutMembre` dont « Payé · avance X » et `non_du`, `resteTourOuvert`, `doitSurLeTour`, `peutGerer`, `peutValider`, `libelleRolesBureau`), `dashboard/lib/api.ts` (`ApiError`), `dashboard/lib/tontine.ts` (`tontineDemandee`, `avecTontine`, `echecDuStatut`, `ecranEchec`), `dashboard/lib/proxy.ts` (`pagingQuery` : transmet un `tontineId` valide, écarte un id malformé) et les routes proxy (`app/api/audit`, `app/api/paiements`, `app/api/tontine`).
- Mobile : `mobile/src/utils/format.ts` (`libelleStatutTour` dont l'avance et `non_du`, `libelleStatut`, `formatMontant`), `mobile/src/screens/app/tontine/commun.ts` (`toursDus`), `mobile/src/services/tontineService.ts` (`mapStatutPaiement`), `mobile/src/screens/app/projet/commun.tsx` (constantes de statuts).
- `.github/workflows/ci.yml` -- job .NET existant (`dotnet test tontApp.slnx` inclut déjà `ApiTests`) ; pas de job web/mobile.
- `tontApp.slnx` -- `src/Api` et `tests/ApiTests`, `tests/NotificationTests` sans `Id`.
- `Makefile` -- cibles `test`, `test-lots`.

## Tasks & Acceptance

**Execution:**
- [ ] Web : cadre de test, script `test`, tests des fonctions listées (cas partiels, attente partielle, rôles, erreurs typées).
- [ ] Mobile : cadre de test, script `test`, tests de `toursDus` (tour ouvert partiel, couvert par attente, repli ancienne API, arriérés alignés), libellés, mapping des statuts.
- [ ] CI : jobs web et mobile (install, typage, lint, tests) ; job .NET inchangé sauf si `ApiTests` exige un réglage Docker.
- [ ] `tontApp.slnx` : Id manquants ; `Makefile` : `make test-front`.

**Acceptance Criteria:**
- Given une régression dans `toursDus` ou `peutValider`, when on lance `make test-front` ou la CI, then un test échoue en nommant le cas.

## Verification

**Commands:**
- `make test-front` -- expected: verts (web puis mobile, un à la fois)
- `scripts/test-par-lots.sh` -- expected: verts

## Implementation Notes

- Consignes permanentes : vérifier la mémoire avant toute installation, compilation ou test, et lancer un seul outil lourd à la fois ; **aucun travail reporté**.

## Spec Change Log

## Review Triage Log
