---
title: 'Web : choisir la tontine quand on siège au bureau de plusieurs tontines'
type: 'feature'
created: '2026-11-17'
status: 'done'
baseline_commit: '6889dbbfcb49ccfa83c9f31a6829f3c9587399cf'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/spec-tableau-de-bord-bureau.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Un utilisateur responsable ou trésorier de plusieurs tontines ne peut gérer sur le web que la tontine par défaut : les pages tableau de bord, paiements et audit (et la route proxy de l'audit) n'envoient jamais `tontineId`, alors que l'API l'accepte.

**Approach:** Lister les tontines où l'appelant siège au bureau (ses rôles pour chacune) et lui permettre d'en choisir une ; ce choix suit l'utilisateur sur toutes les pages de l'espace du bureau et chaque appel le transmet.

## Boundaries & Constraints

**Always:** liste = mêmes droits que `ResolveTontineAsync` (bureau effectif, admin) ; tontine par défaut inchangée tant que rien n'est choisi ; choix conservé entre les pages (URL `?tontine=`), lien partageable ; sélecteur caché s'il n'y a qu'une tontine ; rôles affichés pour la tontine choisie.

**Never:** pas de changement des droits ; pas de migration ; ne pas toucher au mobile.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Plusieurs tontines | responsable de T1, trésorier de T2 | sélecteur T1/T2 avec rôles ; choisir T1 → tableau, paiements, audit de T1 | N/A |
| Une seule | trésorier d'une tontine | pas de sélecteur | N/A |
| Choix interdit | `?tontine=` d'une tontine hors bureau | écran « réservé au bureau » | 403 |
| Admin | admin sans siège | sélecteur sur les tontines où il siège ; `?tontine=` explicite accepté | N/A |

</frozen-after-approval>

## Code Map

- `src/Api/Controllers/DashboardController.cs` (`ResolveTontineAsync`), `src/Application/Dashboard/DashboardReadService.cs` (`GetTontineGestionnaireAsync`, candidats via `GetGroupesOuSiegeAsync` + créateur) -- base d'un endpoint « mes tontines du bureau ».
- `dashboard/app/api/tontine/route.ts`, `api/paiements/route.ts`, `api/audit/route.ts` (n'envoie pas `tontineId`) -- proxys.
- `dashboard/app/gestionnaire/page.tsx`, `paiements/page.tsx`, `audit/page.tsx`, `components/Sidebar.tsx` -- pages à brancher sur le choix.
- `tests/ApiTests/TableauDeBordBureauTests.cs` -- modèle (`PlusieursRoles_...`).

## Tasks & Acceptance

**Execution:**
- [x] API -- endpoint des tontines du bureau de l'appelant (id, nom, statut, rôles).
- [x] Web -- sélecteur (sidebar), `?tontine=` propagé par les liens, proxys et pages.
- [x] Tests -- endpoint (matrice) en ApiTests ; type-check et lint du web.

**Acceptance Criteria:**
- Given un responsable de T1 et trésorier de T2, when il choisit T1 puis ouvre paiements et audit, then il voit les données de T1 avec ses droits de responsable.

## Verification

**Commands:**
- `scripts/test-par-lots.sh` ; `cd dashboard && npx tsc --noEmit -p . && npx next lint` -- expected: verts / 0 erreur

## Implementation Notes

- Checkpoint 1 approuvé sans pause (consigne permanente).
- Consignes permanentes : vérifier la mémoire avant toute compilation ou test, tester par lots ; **aucun travail reporté**.
- Implémentation : `GET /api/v1/dashboard/gestionnaire/tontines` → `[{ id, nom, status, mesRoles }]`, mêmes candidats et même ordre que la tontine par défaut (`TontinesDuBureauAsync` partagé : la première = défaut) ; admin = tontines où il siège (+ rôle `admin`), toute autre par `tontineId` explicite.
- Web : liste rechargée par `Sidebar` à chaque navigation (proxy `/api/bureau/tontines`, indication discrète si elle échoue) ; sélecteur sous l’en-tête « Espace du bureau » (affiché s’il y a plusieurs tontines ou une tontine choisie autre que celle par défaut, avec statut et rôles), choix en `?tontine=` repris par les liens du menu, des pages et des projets ; pages tableau/paiements/audit transmettent `tontineId` (proxy audit réécrit sur `proxyToApi` + `pagingQuery`). `?tontine=` hors bureau → « Accès réservé au bureau de cette tontine » (403), inconnu ou mal formé → « Tontine introuvable ». Un admin dans l'espace du bureau y voit le menu du bureau (+ lien « Administration »). Helpers : `dashboard/lib/tontine.ts`.

- Vérification orchestrateur : 730 tests verts (par lots), tsc/lint/next build propres, essai réel dans Chromium (Playwright) : liste, choix, `?tontine=` propagé à paiements et audit, rôles affichés. L'essai a révélé un 500 sur l'id vide `00000000-…` (`TontineId.From` le refuse) : corrigé dans `TontineAccessService` et `DashboardReadService`, test `IdentifiantVide_TontineIntrouvable_JamaisErreurServeur`.

## Spec Change Log

## Review Triage Log

Passe 1 — 3 couches, 24 constats. Aucun report (consigne de l'utilisateur).

| # | Constat | Verdict | Route |
|---|---------|---------|-------|
| 1 | Liste du sélecteur figée dans le layout (élection, nouveau trésorier, nouvelle tontine) ; échec de chargement masqué en liste vide | medium | patch |
| 2 | Tontine hors liste (lien partagé, admin) : pas de sélecteur à 0–1 tontine, libellé générique, rôles absents | medium | patch |
| 3 | `status` non affiché dans le sélecteur | low | patch |
| 4 | Accueil : `erreur` serveur avec tontine choisie → écran générique au lieu de l'écran d'erreur dédié ; titre 403 répété | medium | patch |
| 5 | Changement de tontine : requête inutile (ancienne page), état périmé (audit : pagination, total, échec, chargement) | medium | patch |
| 6 | Actions en cours (encaisser, valider, rejeter) qui se terminent sur la tontine nouvellement choisie | high | patch |
| 7 | Lien Administration qui reçoit `?tontine=` | low | patch |
| 8 | `useSearchParams` sans Suspense (Sidebar aussi rendue dans l'espace admin) | medium | patch |
| 9 | Import au milieu de `lib/proxy.ts` ; `UUID_REGEX` dupliqué dans `api/relancer` | low | patch |
| 10 | Sélecteur placé différemment mobile / bureau | low | patch |
| 11 | Test d'acceptation : droits par tontine non prouvés (refus sur T2) ; appels en double ; ordre actif/inactif, créateur évincé, admin responsable non testés | medium | patch |
| 12 | Pas de tests web (`lib/tontine.ts`, `pagingQuery`, proxys) | medium | planifié dans `spec-tests-front-et-ci.md` (périmètre étendu), pas reporté |
| 13 | Sélecteur non vu dans un navigateur, `next build` non lancé | medium | patch : `next build` + essai réel par l'orchestrateur |

