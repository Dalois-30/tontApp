---
title: 'Tontine : le trop-perçu d''un tour est reporté sur les tours suivants, avec une seule règle de calcul partout'
type: 'feature'
created: '2026-11-17'
status: 'done'
baseline_commit: '6914647e02252747c2ea7fbb623298c23c996f48'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/spec-paiement-partiel-tontine.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Un membre peut déclarer plus que ce qu'il doit sur un tour ; le surplus validé disparaît (affiché « Payé ») au lieu de couvrir ce qu'il doit ailleurs. La règle « par montants » est aussi recalculée à plusieurs endroits (statuts, reste dû, retardataires, relance, déclaration) et `GET /tontines/{id}/tours/actuel` compte encore un partiel comme payé.

**Approach:** Décision de l'utilisateur (2026-11-17) : **reporter le surplus**, comme les projets. Le montant validé d'un membre au-delà de la cotisation d'un tour couvre d'abord ses arriérés (tours passés les plus anciens), puis le tour en cours, puis les tours suivants (affiché « avance X FCFA »). Une seule règle de calcul, partagée par tous les écrans et toutes les décisions.

## Boundaries & Constraints

**Always:** cotisation = `Tontine.ContributionAmount` par tour dû (`ToursDusPar`) ; seuls les versements `Confirme` sont reportés, les `EnAttente` restent « à valider » sur leur tour ; ordre de report : arriérés du plus ancien au plus récent, puis tour ouvert, puis avance ; statuts existants conservés (`paye`, `paye_partiel`, `en_attente`, `a_payer`, `en_retard`) mais calculés après report ; une déclaration au-delà du reste dû est acceptée (le surplus sera reporté), une déclaration sur un tour déjà couvert (validé + attente + report) reste refusée ; mêmes résultats sur `/details`, tableau de bord, retardataires, relance SMS, `/tours/actuel`, et dans le contrôle de la déclaration.

**Never:** pas de migration ; ne pas déplacer ni modifier les versements stockés (le report est un calcul) ; ne pas toucher aux projets.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Surplus vers arriéré | tour 1 impayé, 10 000 validés sur le tour 2 (cotisation 5 000) | tour 1 et tour 2 soldés, `ResteDu` 0 | N/A |
| Surplus en avance | à jour, 8 000 validés sur le tour ouvert | `paye`, avance 3 000 affichée ; le tour suivant (une fois ouvert) est déjà couvert de 3 000 | N/A |
| Déclaration au-delà du reste | 2 000 validés, déclaration de 10 000 | acceptée | N/A |
| Tour couvert par report | avance ≥ cotisation sur le tour ouvert | statut `paye`, nouvelle déclaration sur ce tour refusée | 400 clair |
| Tour actuel | 2 000 / 5 000 validés | `/tours/actuel` : non compté payé, statut partiel | N/A |
| Rejet | complément rejeté | aucun report de ce montant | N/A |

</frozen-after-approval>

## Code Map

- `src/Application/Dashboard/DashboardReadService.cs` -- `BuildTontineAsync` (montants par (membre, tour), `Reste`, arriérés), `StatutMembreTour`, `GetRetardatairesAsync`, `RelancerAsync` : chacun refait le calcul → à remplacer par la règle partagée.
- `src/Application/PaymentManagement/Commands/EnregistrerVersementManuel/EnregistrerVersementManuelCommandHandler.cs` -- contrôle « tour couvert » (somme validé + attente sur ce tour) → doit tenir compte du report.
- `src/Application/TontineManagement/Queries/GetTourActuel/GetTourActuelQueryHandler.cs` (:48-58) -- payé = tout versement confirmé → règle partagée.
- `src/Domain/TontineManagement/Tontine.cs` -- `ToursDusPar(membre)` (tours dus, rattrapage) ; bon endroit pour une situation par membre (domaine pur, testable sans base).
- `src/Domain/ProjectManagement/Projet.cs` (`Situation`) -- modèle de report existant (rejeu des paiements validés, avance) à imiter.
- `src/Application/Dashboard/DashboardDtos.cs` (`DashMembreDto`) -- ajouter l'avance (champ additif) ; mobile `tontineService.ts`, `MembreStatusBadge`, `TontineDetailScreen` et web `lib/format.ts`, `gestionnaire/page.tsx` -- afficher « avance X FCFA ».
- Tests : `tests/DomainUnitsTest` (règle pure), `tests/PaymentIntegrationTests/DashboardReadServiceTests.cs`, `Phase0SecurityTests.cs`, `tests/ApiTests/TontineCircuitTests.cs`.

## Tasks & Acceptance

**Execution:**
- [x] Domaine -- situation d'un membre sur ses tours dus (dû, validé après report, en attente, reste, avance) à partir des versements, règle unique et pure.
- [x] `DashboardReadService` (détails, retardataires, relance) + `GetTourActuelQueryHandler` + handler de déclaration -- utilisent cette règle ; plus aucun calcul parallèle.
- [x] DTO + mobile + web -- avance affichée.
- [x] Tests -- chaque ligne de la matrice (domaine + API), non-régression des scénarios partiels existants.

**Acceptance Criteria:**
- Given un membre en retard d'un tour qui paie double sur le tour suivant, when n'importe quel membre ouvre la tontine (mobile ou web), then ce membre est à jour, sans reste dû, et n'apparaît plus parmi les retardataires.
- Given les mêmes versements, when on interroge `/details`, `/dashboard/gestionnaire/tontine`, `/tours/actuel` et la liste des retardataires, then ils donnent tous la même situation.

## Verification

**Commands:**
- `scripts/test-par-lots.sh` -- expected: tous verts, un projet à la fois après contrôle mémoire
- `cd mobile && npx tsc --noEmit -p .` ; `cd dashboard && npx tsc --noEmit -p . && npx next lint` -- expected: 0 erreur

## Implementation Notes

- Checkpoint 1 approuvé sans pause (consigne permanente) ; décision métier de l'utilisateur 2026-11-17 : reporter le surplus.
- Consignes permanentes : vérifier la mémoire (`free -m`) avant toute compilation ou test et tester par lots (`scripts/test-par-lots.sh`) ; **aucun travail reporté** — tout constat est corrigé dans ce cycle.
- Implémentation (sous-agent, spec seule) : règle pure `SituationCotisations.Calculer` (`src/Domain/TontineManagement/SituationCotisations.cs`) exposée par `Tontine.SituationDe` ; pont versements → règle dans `Application/TontineManagement/Services/SituationCotisationsExtensions.cs` (`SituationDe`, `Situations`). Lecture retenue de « au-delà de la cotisation d'un tour » : chaque tour garde d'abord ses propres versements validés (jusqu'à la cotisation) ; seul l'excédent est reporté (arriérés du plus ancien, puis tour ouvert, puis avance). Le report remplit le **validé** manquant d'un tour, sans tenir compte de l'attente de ce tour (l'attente reste « à valider » et, si elle est validée plus tard, devient à son tour un surplus).
- `DashMembreDto.Avance` (additif) ; `SituationTourCotisation.Reporte` sert au message de refus (« couverte par le surplus validé sur d'autres tours (X FCFA reportés) »).
- `/tours/actuel` : n'attend plus que les membres qui doivent le tour (bénéficiaire exclu de `MontantAttendu`/`NombreAttendus`, statut « Bénéficiaire »), statuts « Payé » / « Payé partiellement » / « En attente » ; `MontantCollecte` = validé après report.
- Retardataires : un membre qui ne doit pas le tour (bénéficiaire, ou adhérent tardif sans rattrapage) n'est plus listé.
- Affichage : mobile badge/ libellé « Payé · avance X FCFA », ligne « Avance » (détail et déclaration) ; web libellé du badge + mention « avance » dans le tableau.
- Vérifié : `scripts/test-par-lots.sh` — Auth 14, Domain 444 (9 nouveaux), Notification 57, Payment 108 (9 nouveaux), Api 39 (5 nouveaux) ; `tsc` mobile et dashboard 0 erreur, `next lint` propre.

- Correctifs de revue (coordinateur) : statut `non_du` (libellé « Non dû », exclu des payeurs attendus) ; totaux par tour dans `DashTourDto` (`MontantCollecte`, `MontantAttendu`, `NombreAttendus`, `NombrePayes`) calculés par `TotauxDe`, partagés avec `/tours/actuel` et utilisés par le web et le mobile ; relance SMS ignorée si le tour ouvert n'est pas dû ou est couvert ; déclaration sur un tour non dû refusée en français (bénéficiaire / adhésion sans rattrapage) ; attente sur un tour non dû conservée (`EnAttenteHorsTours`) ; `DashMembreDto` gagne `EnAttenteTotal`, `EnAttenteExcedent`, `ReporteTour`, `EnAttenteExcedentTour`, avance envoyée pour tous ; libellés « dont X reportés » / « (dont X en excédent) » ; avance affichée à un seul endroit par écran ; contrat `/tours/actuel` documenté. Le statut « Non dû » du tour ouvert n'est pas atteignable par l'API aujourd'hui (un adhérent tardif doit toujours le tour ouvert) : testé au niveau service.

## Spec Change Log

## Review Triage Log

Passe 1 — 3 couches, 21 constats. Aucun report (consigne de l'utilisateur).

| # | Constat | Verdict | Route |
|---|---------|---------|-------|
| 1 | Tour non dû (bénéficiaire exclu, adhérent tardif sans rattrapage) affiché `a_payer` ; compté dans les attendus web/mobile | high | patch : statut `non_du` partout |
| 2 | Totaux web du tour (collecte, graphique, attendu) et compteur mobile hors règle de report | high | patch : totaux par tour fournis par le serveur |
| 3 | Relance SMS envoyée à qui ne doit rien (tour non dû ou couvert) | high | patch : ignorés |
| 4 | Déclaration acceptée sur un tour non dû, puis invisible | high | patch : refus 400 ; attente hors tours dus rendue visible |
| 5 | Excédent en attente invisible (risque de double paiement) | medium | patch |
| 6 | Montant reporté sur un tour non transmis aux écrans | medium | patch |
| 7 | Avance masquée pour un membre non actif | medium | patch |
| 8 | Avance affichée deux fois (mobile) ; règle d'affichage web arbitraire ; formulaire mobile proposant un tour couvert par report | medium | patch |
| 9 | `/tours/actuel` : nouveaux statuts et sens de `NombreAttendus`/`MontantCollecte` non documentés ni testés comme contrat | medium | patch |
| 10 | Tests manquants (adhérent tardif, bénéficiaire, report sur 3 tours, partiel + attente comblé, suspendu avec avance, retardataires tardifs) | medium | patch |
| 11 | Coquille dans un commentaire de test | low | patch |
| 12 | Libellés « avance » sans test côté clients | medium | planifié dans `spec-tests-front-et-ci.md` (périmètre étendu), pas reporté |
| 13 | Déclarations simultanées | medium | planifié dans `spec-declarations-concurrentes.md`, pas reporté |
| 14 | Historique complet chargé à chaque lecture | low | rejet : tontines de quelques dizaines de versements, requête ciblée = complexité sans gain mesurable |
| 15 | `Du == cotisation` supposé pour chaque tour | false | la cotisation d'une tontine est unique par tour (`ContributionAmount`) |

