---
title: 'Tontine : un paiement partiel validé reste visible comme partiel, avec son reste dû'
type: 'feature'
created: '2026-11-16'
status: 'done'
baseline_commit: '29b3bb5ca32b875543a10cdd5e72f75d5a8e641c'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/spec-circuit-paiement-bureau.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Dans une tontine, tout versement validé fait passer le membre à « Payé », quel que soit son montant : un paiement de 4 000 FCFA sur une cotisation de 10 000 est indiscernable d'un paiement complet, et le reste n'est plus dû nulle part (le tour compte comme réglé dès qu'un versement existe).

**Approach:** Décision de l'utilisateur (2026-11-16) : un montant partiel peut être validé, mais le statut doit indiquer que ce n'est pas un paiement conventionnel. Le statut d'un membre sur un tour se calcule sur la somme validée par rapport à la cotisation : complet → « Payé », partiel → « Payé partiellement » avec le reste à payer ; le reste reste dû (tour en cours et arriérés) et le membre peut déclarer un complément.

## Boundaries & Constraints

**Always:** cotisation de référence = `Tontine.ContributionAmount` ; seuls les versements `Confirme` comptent comme payés, les `EnAttente` comme « à valider » ; un tour n'est soldé que lorsque validé + en attente ≥ cotisation ; le reste d'un tour partiellement payé s'ajoute au « reste dû » et le tour reste proposé à la déclaration ; libellé visible « Payé partiellement » + montant restant, sur mobile comme sur le web ; la chaîne d'audit et les droits (trésorier) restent inchangés.

**Never:** ne pas refuser un montant inférieur à la cotisation ; pas de migration ; ne pas toucher aux projets (ils gèrent déjà les échéances partielles) ; ne pas modifier les versements existants.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Partiel validé | cotisation 10 000, 4 000 validés | statut `paye_partiel`, reste du tour 6 000 | N/A |
| Complément | partiel validé, nouvelle déclaration 6 000 | déclaration acceptée (pas de « doublon ») ; après validation : `paye`, reste 0 | N/A |
| Partiel + attente | 4 000 validés, 6 000 en attente | `paye_partiel` avec 6 000 « à valider », tour non proposé à une nouvelle déclaration | nouvelle déclaration 400 (déjà couvert) |
| Complet | 10 000 validés | `paye`, comme aujourd'hui | N/A |
| Arriéré partiel | tour passé, 4 000 validés | tour dans les impayés, reste dû 6 000 | N/A |
| Tout couvert | validé + attente ≥ cotisation | nouvelle déclaration refusée | 400 message clair |

</frozen-after-approval>

## Code Map

- `src/Application/Dashboard/DashboardReadService.cs` -- `BuildTontineAsync` (:252, `regles` = tour réglé dès qu'un versement non échoué existe ; `ResteDu = impayes.Count * cotisation`) et `StatutMembreTour` (:292) : à calculer sur les montants.
- `src/Application/Dashboard/DashboardDtos.cs` -- `DashMembreDto` (:24) : ajouter en fin de record le montant restant du tour en cours (champ additif, le web continue de fonctionner).
- `src/Application/PaymentManagement/Commands/EnregistrerVersementManuel/EnregistrerVersementManuelCommandHandler.cs` -- refus de doublon (:71) : refuser seulement si validé + en attente ≥ cotisation.
- `mobile/src/components/MembreStatusBadge.tsx`, `mobile/src/utils/format.ts` (`libelleStatut`), `mobile/src/services/tontineService.ts` (statuts :73-98), `mobile/src/screens/app/tontine/commun.ts` (tours dus) et `DeclarerVersementScreen.tsx` (montant proposé = reste du tour) -- nouveau statut et reste.
- `dashboard/components/ui.tsx` (Badge), `dashboard/lib/format.ts`, `dashboard/app/gestionnaire/paiements/page.tsx` (`STATUTS_A_ENCAISSER`) -- libellé et encaissement du complément sur le web.
- `tests/ApiTests/TontineCircuitTests.cs`, `tests/PaymentIntegrationTests/Phase0SecurityTests.cs`, `DashboardReadServiceTests.cs` -- modèles de tests.

## Tasks & Acceptance

**Execution:**
- [x] `DashboardReadService.cs` + `DashboardDtos.cs` -- statut et reste calculés sur les sommes (validé / en attente / cotisation), statut `paye_partiel`, reste du tour en cours, arriérés partiels dans `ResteDu`/`ToursImpayes`.
- [x] `EnregistrerVersementManuelCommandHandler.cs` -- accepter un complément tant que le tour n'est pas couvert ; message clair sinon.
- [x] mobile (badge, libellés, service, tours dus, montant proposé) -- « Payé partiellement · reste X FCFA ».
- [x] dashboard web (badge, libellé, membres à encaisser) -- même affichage, complément encaissable.
- [x] tests -- chaque ligne de la matrice : API de bout en bout (`tests/ApiTests`) et service de lecture (`DashboardReadServiceTests`).

**Acceptance Criteria:**
- Given un paiement partiel validé, when n'importe quel membre ouvre la tontine (mobile ou web), then il lit « Payé partiellement » et le montant restant, distinct de « Payé ».
- Given le reste d'un tour partiel, when le trésorier valide le complément, then le membre passe à « Payé » et l'historique montre les deux versements.

## Verification

**Commands:**
- `dotnet test tests/ApiTests` et `dotnet test tests/PaymentIntegrationTests` -- expected: verts, matrice couverte
- `dotnet test` sur les autres projets de `tests/` -- expected: aucune régression
- `cd mobile && npx tsc --noEmit -p .` et `cd dashboard && npx tsc --noEmit -p .` -- expected: 0 erreur

## Implementation Notes

- Checkpoint 1 approuvé sans pause (consigne permanente de l'utilisateur). Décision métier de l'utilisateur, 2026-11-16 : « si on valide un montant partiel, même si le membre passe à payé, le statut doit bien indiquer que ce n'est pas un paiement conventionnel ».
- Implémentation (sous-agent, spec seule) : `DashMembreDto` gagne trois champs additifs en fin de record — `ResteTour` (cotisation − validé − en attente sur le tour ouvert), `EnAttenteTour` (montant « à valider ») et `RestesImpayes` (reste de chaque tour de `ToursImpayes`, même ordre, pour proposer le bon montant au rattrapage). `paye_partiel` = au moins un versement validé mais < cotisation ; seulement en attente ⇒ `en_attente` (inchangé).
- Cohérence ajoutée hors liste : `GetRetardatairesAsync` ne considère un membre à jour que si validé + en attente ≥ cotisation (un partiel reste relançable).
- Message de refus : conserve « déjà déclaré » quand un versement en attente couvre le tour (test historique `DoubleDeclaration_*`), sinon « déjà entièrement payée et validée ».
- Vérifié : ApiTests 23/23 (5 nouveaux), PaymentIntegrationTests 98/98 (10 nouveaux), Domain 435, Auth 14, Notification 50 ; `tsc` mobile et dashboard 0 erreur, `next lint` propre.

- Passe de revue 1 : 6 correctifs appliqués (attente partielle avec reste, petits restes > 0 via `Montant.CreateDeclare`, relance SMS avec le reste, sélection web, pré-remplissage mobile, complément rejeté) ; vérification orchestrateur : 624 tests verts, tsc mobile/web 0 erreur.

## Spec Change Log

## Review Triage Log

Passe 1 — 3 couches, 26 constats (regroupés).

| # | Constat | Verdict | Preuve | Route |
|---|---------|---------|--------|-------|
| 1 | Paiement en attente seulement partiel (rien de validé) : statut « À valider » sans le reste ; pas de relance proposée | high | `StatutMembreTour` renvoie `en_attente` alors que `ResteTour` > 0 ; libellés web/mobile et `RelancerButton` ne regardent que `paye_partiel` | patch |
| 2 | Un reste ≤ 100 FCFA ne peut jamais être déclaré | medium | validateur `Montant > 100` ; champ web `min={100}` | patch |
| 3 | Relance SMS réclame la cotisation complète à un payeur partiel | medium | `RelancerAsync` : message fixe avec `ContributionAmount` | patch |
| 4 | Web : en changeant de membre, le montant du membre précédent reste ; tour présélectionné absent de la liste | medium | `choisirMembre` ne remet pas à zéro si reste 0 ; `tourOuvert` présélectionné | patch |
| 5 | Mobile : le montant saisi est écrasé par un rafraîchissement | low | effet sur `resteChoisi` sans indicateur de saisie | patch |
| 6 | Montant supérieur au reste accepté (trop-perçu sur un tour) | medium | le handler ne compare pas `Montant` au reste | defer (règle métier : question posée à l'humain) |
| 7 | Déclarations de complément concurrentes | medium | lecture puis insertion sans verrou ; antérieur | defer (déjà reporté) |
| 8 | `paye_partiel` quand validé + attente couvre la cotisation | false | conforme à la matrice gelée (« Partiel + attente → paye_partiel ») | rejet |
| 9 | Compteur mobile « paiements reçus » ignore les partiels | low | cohérent avec « à jour » ; cosmétique | rejet |
| 10 | Libellé partiel écrit trois fois | low | dette de duplication sans défaut observé | rejet |
| 11 | Listes parallèles `ToursImpayes`/`RestesImpayes` | low | construites ensemble côté serveur | rejet |
| 12 | Aucun test JS côté clients | medium | aucun cadre de test dans mobile/dashboard | defer |
| 13 | `GET /tours/actuel` compte un partiel comme « Payé » | medium | `GetTourActuelQueryHandler` ; aucun écran ne l'appelle | defer |
| 14 | Badge mobile : statut inconnu ferait planter | low | statuts filtrés en amont ; antérieur | rejet |
| 15 | `ResteTour` non nullable | false | toujours renseigné par le serveur | rejet |
| 16 | Tests serveur aux bornes (partiel en attente seul, petit reste, complément rejeté) | medium | absents | patch (avec 1–2) |

