---
title: 'Le tableau de bord web suit le bureau (responsable, trésorier), plus seulement le rôle plateforme'
type: 'feature'
created: '2026-11-16'
status: 'done'
baseline_commit: 'a1bd51dc0f367af33e70591f6f674453d9176e1e'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/spec-circuit-paiement-bureau.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Depuis la gouvernance par bureau, le responsable et le trésorier d'une tontine peuvent être des comptes « Membre » (nommés ou élus), mais le tableau de bord web n'accepte que le rôle plateforme « Gestionnaire » et choisit la tontine par son créateur : un trésorier ne peut pas valider depuis le web, un responsable élu ne voit pas sa tontine, et un créateur évincé y garde l'accès.

**Approach:** Le tableau de bord gestionnaire devient l'espace du bureau : accès accordé à qui est responsable ou trésorier d'une tontine (ou administrateur), tontine par défaut choisie d'après le bureau, et chaque action affichée selon le rôle réel (le trésorier valide, le responsable gère). Point reporté de `spec-circuit-paiement-bureau.md`.

## Boundaries & Constraints

**Always:** droits identiques à l'API mobile (source unique : `TontineAccessService` / `Bureau.RolesEffectifs`) ; un membre sans rôle de bureau n'accède pas à l'espace gestionnaire web (403 côté API, redirection claire côté web) ; l'administrateur garde tout ; le créateur qui n'est plus ni responsable ni trésorier perd l'accès ; le rôle plateforme « Gestionnaire » reste nécessaire pour créer une tontine ou un projet.

**Never:** pas de migration ; ne pas changer les droits métier (qui valide, qui gère) ; ne pas toucher à l'espace administrateur ni à l'application mobile.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Trésorier « Membre » | nommé trésorier, compte Membre | se connecte au web, voit la tontine, valide/rejette ; pas d'actions de responsable | N/A |
| Responsable élu « Membre » | élu par vote | voit sa tontine par défaut, accepte les adhésions, relance | N/A |
| Créateur évincé | n'est plus ni responsable ni trésorier | n'accède plus à cette tontine | 403 / « aucune tontine » |
| Simple membre | aucun rôle de bureau | refusé sur l'espace gestionnaire | 403 API, message web |
| Plusieurs rôles | responsable d'une tontine, trésorier d'une autre | tontine par défaut déterministe, `tontineId` explicite accepté pour chacune | N/A |

</frozen-after-approval>

## Code Map

- `src/Api/Controllers/DashboardController.cs` -- `gestionnaire/tontine|versements|audit` (:33-58) gardés par `[Authorize(Roles="Gestionnaire,Admin")]` ; `ResolveTontineAsync` (:78) exige `NiveauAcces.Gestionnaire` puis `GetTontineGestionnaireAsync`.
- `src/Application/Dashboard/DashboardReadService.cs` -- `GetTontineGestionnaireAsync` (:65-75) choisit via `GetByGestionnaireIdReadOnlyAsync` (créateur) ; `DashTontineDto` à compléter avec les rôles de l'appelant.
- `src/Domain/Gouvernance/Bureau.cs` (`IBureauRepository`, `RolesEffectifs`) + `src/Infrastructure/Persistence/Repositories/BureauRepository.cs` -- ajouter la recherche des tontines où l'utilisateur est au bureau.
- `src/Application/TontineManagement/Services/TontineAccessService.cs` -- `AccesTontine.EstBureau`, `EstTresorier` : à réutiliser.
- `dashboard/middleware.ts` (:44-55) et `dashboard/app/login/page.tsx` (:31) -- filtrage par rôle JWT et redirection.
- `dashboard/app/gestionnaire/page.tsx`, `paiements/page.tsx`, `components/AdhesionsEnAttente.tsx`, `ReglesAdhesionTontine.tsx`, `RelancerButton.tsx`, `components/Sidebar.tsx` -- actions à conditionner au rôle.
- `tests/ApiTests/` -- `ApiFactory`, `TableauDeBord_LeCreateurEncaissePuisValide_CommeAvant` comme modèle.

## Tasks & Acceptance

**Execution:**
- [x] `IBureauRepository` + `BureauRepository` -- tontines où l'utilisateur est responsable ou trésorier (rôles effectifs).
- [x] `DashboardReadService` + `DashTontineDto` -- tontine par défaut d'après le bureau ; rôles de l'appelant (`mesRoles`) dans la réponse.
- [x] `DashboardController` -- plus de rôle plateforme sur l'espace gestionnaire ; accès = bureau (ou admin).
- [x] `dashboard/middleware.ts`, `login/page.tsx`, `Sidebar.tsx` -- espace ouvert aux comptes authentifiés, l'API tranche ; message clair si aucune tontine au bureau ; libellés « Espace du bureau ».
- [x] pages et composants gestionnaire -- valider/rejeter réservés au trésorier, gestion (adhésions, règles, relances, activation) au responsable ; badge du rôle.
- [x] `tests/ApiTests/` -- une ligne de matrice = au moins un test.

**Acceptance Criteria:**
- Given un trésorier « Membre » nommé, when il valide un paiement depuis le web, then le paiement passe validé, et les boutons de gestion lui sont masqués.
- Given un vote qui change le responsable, when l'ancien responsable-créateur ouvre le tableau de bord, then il ne voit plus la tontine (sauf s'il reste trésorier).

## Verification

**Commands:**
- `scripts/test-par-lots.sh` -- expected: tous les projets verts (un à la fois, selon la mémoire)
- `cd dashboard && npx tsc --noEmit -p . && npx next lint` -- expected: 0 erreur

## Implementation Notes

- Checkpoint 1 approuvé sans pause (consigne permanente de l'utilisateur).
- Consigne machine de l'utilisateur : vérifier la mémoire libre (`free -m`) avant toute compilation ou tout test, et lancer les tests par petits lots (`scripts/test-par-lots.sh`, un projet à la fois) ; jamais deux suites lourdes en parallèle.
- Implémentation (sous-agent, spec seule) : `IBureauRepository.GetGroupesOuSiegeAsync` (lecture sans suivi, rôles effectifs) ; `DashboardReadService` réunit tontines créées + sièges au bureau puis garde celles où `TontineAccessService` donne `EstBureau` (défaut : active, puis plus récente, puis id) ; `mesRoles` = `responsable` / `tresorier` / `admin` (mêmes libellés que l'écran de gouvernance).
- Sans `tontineId` et sans tontine au bureau : 404 « Aucune tontine trouvée. » pour un compte « Gestionnaire » ou admin (il peut en créer une, écran « créez votre tontine »), 403 pour tout autre compte (écran « accès réservé au bureau »). Avec `tontineId` : 404 si inconnue, 403 hors bureau.
- Web : le responsable seul voit accepter/refuser, relancer et l'édition des règles (le trésorier les voit en lecture) ; valider/rejeter réservés au trésorier ou à l'admin ; encaisser (déclarer pour un membre) ouvert aux deux rôles comme l'API. Création de projet : message clair en cas de 403 (rôle plateforme toujours exigé). Pas de sélecteur de tontine sur le web (l'API accepte `tontineId`).

- Passe de revue 1 : 7 correctifs (création de projet réservée, erreurs typées par statut HTTP, page paiements sur une seule tontine, instant unique, tests admin / candidat de vote échu / tontine active d'abord). Vérification orchestrateur par lots : 632 tests verts, tsc et lint web propres.

## Spec Change Log

## Review Triage Log

Passe 1 — 3 couches, 27 constats (regroupés).

| # | Constat | Verdict | Preuve | Route |
|---|---------|---------|--------|-------|
| 1 | Un compte « Membre » voit le formulaire de création de projet et n'apprend le refus qu'à l'envoi | medium | `projets/page.tsx` sans garde de rôle depuis l'ouverture du middleware | patch |
| 2 | Page paiements : tontine et versements résolus par deux appels « par défaut » indépendants | medium | `Promise.all([/api/paiements, /api/tontine])` sans `tontineId` | patch |
| 3 | « Hors bureau » détecté sur le texte « API 403 » ; panne réseau/5xx affichée comme « aucune tontine » | medium | `e.message.startsWith("API 403")`, `catch` générique | patch |
| 4 | Refus de création de projet : vrai motif masqué par un message fixe | low | message fixe sur tout 403 ; correction directe | patch |
| 5 | Instant différent entre `GetGroupesOuSiegeAsync` et le contrôle d'accès | low | deux `UtcNow` ; correction directe | patch |
| 6 | Sous-titre et badge erronés pendant le chargement de la page paiements | low | `valide` faux tant que `tontine` est nul ; correction directe | patch |
| 7 | Chemins administrateur (tontine étrangère, `mesRoles = [admin]`, 404 sans bureau) non testés | medium | aucun test ApiTests avec un compte Admin | patch |
| 8 | Clause « candidat d'un vote ouvert » de `GetGroupesOuSiegeAsync` jamais exécutée | medium | vote adopté immédiatement dans les tests | patch |
| 9 | Règle « tontine active avant la plus récente » non testée | medium | le test multi-rôles n'a que des tontines actives | patch |
| 10 | Pas de sélecteur de tontine pour un membre de plusieurs bureaux (pages, audit) | medium | aucun `tontineId` transmis par les pages ni la route audit | defer |
| 11 | Pas de tests front (rôles, écrans hors bureau) | medium | aucun cadre de test dans dashboard/ | defer (déjà reporté) |
| 12 | Requêtes N+1 pour la tontine par défaut | low | quelques tontines par utilisateur | rejet |
| 13 | Double contrôle d'accès avec `tontineId` | low | coût négligeable, résultat identique | rejet |
| 14 | Blocs « hors bureau » et badge dupliqués | low | duplication sans défaut | rejet |
| 15 | « Encaisser » visible pour le trésorier ; modale de rejet conservée | false | l'API autorise le bureau à déclarer ; boutons de rejet masqués donc modale inatteignable | rejet |
| 16 | Middleware sans remplaçant central | low | chaque page traite le 403 ; la page projets est corrigée (#1) | rejet |
| 17 | « Activation » sans contrôle sur le web | false | aucune activation n'existe côté web | rejet |
| 18 | Mocks d'accès non configurés dans DashboardReadServiceTests | false | les tests existants passent et n'empruntent pas ce chemin | rejet |

