---
title: 'Circuit de paiement piloté par le bureau (tontines + projets), validé par des tests de bout en bout'
type: 'feature'
created: '2026-11-16'
status: 'done'
baseline_commit: 'a3be64c2ffea01d21c207c717f16d71cd7928e39'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/src/Domain/Gouvernance/Bureau.cs'
  - '{project-root}/src/Application/Gouvernance/GouvernanceService.cs'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Les projets suivent déjà le modèle validé avec le client (chaque membre déclare son paiement, le trésorier valide, tout le monde voit tout), mais les tontines utilisent encore l'ancien circuit : seul le « gestionnaire » (créateur) saisit et valide, depuis le web, et le membre ne peut que lire une consigne. Aucun test n'exerce l'API par HTTP : les autorisations des tontines, vérifiées uniquement dans les contrôleurs, ne sont pas couvertes.

**Approach:** Faire passer les droits des tontines par le `Bureau` (responsable = gestion, trésorier = validation), ouvrir la déclaration de paiement aux membres sur mobile, rendre paiements et journal lisibles par tous, intégrer le journal des versements dans l'historique de la tontine, puis valider l'ensemble (tontines, projets, gouvernance) par des tests d'API de bout en bout sur une vraie base.

## Boundaries & Constraints

**Always:** règles du client (mémoire `gouvernance-rules`) : le créateur détient les deux rôles par défaut ; le responsable gère (activation, membres, adhésions, règles, relances, invitations) ; seul le trésorier (ou l'admin plateforme) valide ou rejette, motif obligatoire au rejet ; un membre ne déclare que pour lui-même, responsable et trésorier pour n'importe quel membre ; tout membre lit tous les versements et le journal. Une déclaration porte sur le tour ouvert ou un tour passé impayé du membre (règle de rattrapage existante). L'argent reste hors de l'application (aucun appel Mobile Money).

**Never:** pas de migration ni de changement du modèle `Versement`/`AuditEntry` ; pas de refonte du tableau de bord web (il doit seulement continuer à fonctionner pour un responsable-trésorier) ; ne pas toucher au flux Mobile Money `POST /versements/initier` ; pas de nouveau rôle plateforme.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Déclaration membre | membre actif, tour ouvert, sans versement en cours | 201, versement `en_attente`, statut membre `en_attente` | N/A |
| Déclaration pour autrui | simple membre, `membreId` d'un autre | refus | 403 |
| Double déclaration | versement `en_attente` déjà présent sur ce tour | refus | 400 message clair |
| Validation par non-trésorier | membre ou responsable non trésorier | refus | 403 |
| Validation trésorier | trésorier nommé | `Confirme`, statut membre `paye`, journal chaîné valide | N/A |
| Rejet sans motif | trésorier, motif vide | refus | 400 |
| Lecture transparente | simple membre | liste des versements de la tontine + journal | N/A |
| Non-membre | utilisateur hors groupe | refus | 403 |

</frozen-after-approval>

## Code Map

- `src/Application/TontineManagement/Services/TontineAccessService.cs` -- `GetAccesAsync`/`GetAccesParVersementAsync` (:52/:69) : niveau Gestionnaire = `tontine.GestionnaireId` ; à brancher sur `IBureauRepository` (responsable ⇒ Gestionnaire, + drapeau `EstTresorier`). Bureau absent ⇒ créateur, comme `GouvernanceService.Charger`.
- `src/Api/Security/TontineAccessAttribute.cs` -- filtre `[TontineAccess(niveau)]` + `AccessResults.Check` (:53) ; réutiliser, ne pas dupliquer.
- `src/Api/Controllers/VersementController.cs` -- `manuel` (:51, `[Authorize(Roles="Gestionnaire,Admin")]`), `valider` (:90), `rejeter` (:130) : droits à revoir ; `initier` (:188) inchangé.
- `src/Application/PaymentManagement/Commands/EnregistrerVersementManuel/` -- garde-fous existants (membre/tour de la tontine, doublon :75) ; `ActeurId` = déclarant.
- `src/Api/Controllers/TontineController.cs` -- `GET {id}/versements` (:493, Gestionnaire) à ouvrir aux membres ; `src/Api/Controllers/AuditController.cs` (:37/:63) idem.
- `src/Domain/PaymentManagement/Entities/AuditEntry.cs` + `IAuditEntryRepository.GetByTontineOrderedAsync` -- source du journal des versements pour `GouvernanceService.ChronologieAsync` (catégorie `paiements`).
- `src/Application/Dashboard/DashboardReadService.cs` -- statuts membre (:290) : à réutiliser tels quels.
- `mobile/src/screens/app/TontineDetailScreen.tsx` -- bouton « Payer ma cotisation » (:278, handler :72 = simple Alert).
- `mobile/src/services/tontineService.ts` -- détails (:160), statuts (:73-98).
- `mobile/src/screens/app/projet/*`, `mobile/src/components/kit.tsx` -- modèles d'écrans à réutiliser (déclaration, validations).
- `tests/PaymentIntegrationTests/PostgresPaymentIntegrationTests.cs` -- modèle Testcontainers ; aucun test HTTP existant.

## Tasks & Acceptance

**Execution:**
- [x] `src/Application/TontineManagement/Services/TontineAccessService.cs` -- droits via `Bureau` (responsable/trésorier), résultat enrichi `EstTresorier` -- source unique des droits tontine.
- [x] `src/Api/Controllers/VersementController.cs` -- `manuel` ouvert à tout membre (pour soi), `membreId` facultatif ; `valider`/`rejeter` réservés trésorier/admin -- circuit déclarer → valider.
- [x] `src/Api/Controllers/TontineController.cs`, `AuditController.cs` -- lecture versements/journal niveau Membre -- transparence.
- [x] `src/Application/Gouvernance/GouvernanceService.cs` -- chronologie tontine = journal bureau + journal des versements (noms des membres, montants) -- historique complet.
- [x] `mobile/src/services/tontineService.ts` + `mobile/src/screens/app/tontine/DeclarerVersementScreen.tsx` + `TontineVersementsScreen.tsx` + `TontineDetailScreen.tsx` + navigation -- déclarer (tour dû, montant, moyen, référence), liste de tous les versements, valider/rejeter pour le trésorier.
- [x] `tests/ApiTests/` (nouveau projet, `WebApplicationFactory<Program>` + Testcontainers PostgreSQL et Redis) -- scénarios de bout en bout : matrice ci-dessus (tontine), circuit projet (déclaration, 403, validation trésorière, transparence), gouvernance (nommer trésorier, vote adopté ⇒ responsable change, séance, chronologie recherchée).
- [x] `tests/PaymentIntegrationTests/` -- tests unitaires de `TontineAccessService` avec bureau (Moq).

**Acceptance Criteria:**
- Given un trésorier nommé autre que le créateur, when le créateur tente de valider un versement, then 403, et le trésorier y parvient.
- Given un vote adopté désignant un nouveau responsable, when l'ancien responsable active ou relance, then 403, et le nouveau responsable y parvient.
- Given un versement validé, when un membre ouvre l'historique de la tontine, then il voit la déclaration et la validation avec noms et montant.
- Given le tableau de bord web et le compte créateur (responsable + trésorier), when il encaisse puis valide, then le flux existant fonctionne sans changement.

## Design Notes

Droits tontine : `NiveauAcces.Gestionnaire` signifie désormais « responsable du bureau » ; la validation exige `EstTresorier` (ou admin), indépendamment du niveau. Cela garde intacts les `[TontineAccess(Gestionnaire)]` existants tout en séparant gestion et validation.

## Verification

**Commands:**
- `dotnet test tests/ApiTests` -- expected: tous les scénarios verts sur conteneurs éphémères
- `dotnet test` sur chaque projet de `tests/` -- expected: aucune régression (574+ tests)
- `cd mobile && npx tsc --noEmit -p .` -- expected: 0 erreur

**Manual checks (if no CLI):**
- Redmi : un membre déclare dans une tontine, le trésorier valide, le statut passe à « Payé ».

## Implementation Notes

- Checkpoint 1 : approuvé sans pause selon la consigne permanente de l'utilisateur (« travailler sans attendre mon approbation », 2026-11-16). Spec conservée entière (~2 170 tokens) : objectif unique, les tests en sont la validation.

- Implémentation par sous-agent (spec seule) ; vérification du diff contre le baseline par l'orchestrateur : 7/7 tâches, 4/4 critères, matrice couverte par `tests/ApiTests` (18 tests verts, exécutés).
- Corrections de l'orchestrateur après lecture du diff : un vote échu s'applique aux droits sans attendre l'écran de gouvernance (`Bureau.Actualiser` dans `TontineAccessService` et `ProjetService.Charger`) ; dates calendaires stockées sans fuseau (`Projet.Creer`, `Seance`) — une date « …Z » donnait une 500 ; test `Seance_DateEnvoyeeEnUtcAvecHeure_EstAcceptee`.
- Comportements nouveaux assumés : déclaration refusée pour le bénéficiaire du tour, un tour non dû ou un membre non actif (aussi depuis le web) ; activation, tours, suspension et messages ne demandent plus le rôle plateforme « Gestionnaire » (un responsable élu a un compte « Membre »).
- Bug préexistant hors périmètre : la route d'export PDF de `TontineController` est attachée à l'action d'adhésion.

## Spec Change Log

## Review Triage Log

Passe 1 — 3 couches (aveugle, cas limites, écarts de vérification), 29 constats.

| # | Constat | Verdict | Preuve | Route |
|---|---------|---------|--------|-------|
| B1/E8/E9 | Tableau de bord web encore gardé par le rôle plateforme `Gestionnaire` et le créateur | medium | `DashboardController` `[Authorize(Roles)]` + `ResolveTontineAsync` ; un responsable élu ou un trésorier « Membre » n'y accède pas | defer (l'intention exclut la refonte web) |
| B2/B3 | Le trésorier ne voit ni moyen ni référence déclarés ; le motif de rejet n'est pas dans la liste | high | `DashVersementDto` sans preuve/motif, preuve et raison seulement dans les charges d'audit | patch |
| B4/E3 | Montant déclaré non contrôlé (1 FCFA validé = « payé ») | medium | handler accepte tout `Montant` ; comportement antérieur conservé | defer (règle métier à trancher par l'humain) |
| B5/E5 | Historique : action système attribuée au payeur | medium | repli `: qui` si `ActeurId` n'est pas un Guid | patch |
| B6 | « rejeté () » si raison absente | low | `Txt(d,"Raison")` vide | patch |
| B7 | Journal ouvert à tous sans revue de confidentialité | false | transparence totale = intention explicite du client ; les membres voient déjà noms et numéros dans le détail | rejet |
| B8 | Handler messages sans contrôle métier | medium | contrôle supprimé, seul `[TontineAccess]` protège | patch (tests HTTP, cf. G1) |
| B9/E4 | Contrôle d'accès qui mute un bureau suivi (clôture de vote) | medium | `GetAsync` renvoie l'entité suivie ; deux requêtes concurrentes peuvent journaliser deux fois → chaîne fourchue | patch |
| B10/G4 | Règles « membre actif » et rattrapage non testées | medium | aucune recherche ne trouve ces cas | patch |
| B11/G3 | Historique : absence de `VersementCree` et ligne de rejet non vérifiées | medium | assertions `>= 2` seulement | patch |
| B12 | Pas de pagination ; séquences mélangées | low | ordre arbitraire seulement à horodatage égal | rejet |
| B13 | Réinitialisation du montant ; pas de confirmation pour autrui | low | ne remplit que si vide ; confirmation = ajout d'UI | rejet |
| B14 | Sélecteur de payeur montre des membres sans dû | low | filtre `adhesion_en_attente`/`suspendu` seulement | patch |
| B15/E1 | Bouton déclarer : absent sans tour ouvert malgré des arriérés, présent sans dû | medium | rendu sous `tour ? (...)` | patch |
| B16 | slnx sans Id ; tests Docker sans CI/doc | low | `tontApp.slnx` | defer |
| E2 | Déclarations concurrentes → doublon/chaîne fourchue | medium | antérieur (lecture puis écriture sans verrou) | defer |
| E6 | Échec Mobile Money affiché « rejeté » | low | Mobile Money désactivé par défaut | rejet |
| E7 | `tourId` périmé après rafraîchissement | low | effet basé sur `dus.length` | patch |
| G1 | open/close/suspendre/messages sans test d'autorisation | medium | aucun test n'appelle ces routes | patch |
| G2 | Clôture d'un vote échu via le contrôle d'accès non testée | medium | aucun test ne passe par l'échéance | patch |
| G5 | Alertes de retard envoyées au créateur | medium | `PaiementEnRetardEventHandler` → `GestionnaireId` | defer |

Note de méthode : B2/B3 ajoutent deux champs en lecture à un DTO existant ; classés `patch` plutôt que `bad_spec` (pas de retour à zéro du code) — écart assumé à la règle « dans le doute, bad_spec ».

