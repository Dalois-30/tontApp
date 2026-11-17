---
title: 'Déclarations et validations simultanées d''une tontine sérialisées (pas de doublon, chaîne d''audit intacte)'
type: 'bugfix'
created: '2026-11-17'
status: 'done'
baseline_commit: 'bdd7388a051cbb5d23717d1f93f15aef76f5cca5'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/spec-trop-percu-reporte.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** La déclaration d'un versement lit les versements existants et le dernier hash de la chaîne, puis insère, sans verrou : deux requêtes simultanées peuvent toutes deux passer le contrôle « tour couvert » (doublon, tour sur-couvert) et fourcher la chaîne d'audit (même hash précédent), que `audit/verifier` signale alors comme altérée. Même risque entre validations/rejets simultanés sur une même tontine.

**Approach:** Sérialiser, par tontine, les opérations qui écrivent dans la chaîne de versements (déclaration, validation, rejet) : verrou transactionnel PostgreSQL par tontine, contrôle et écriture dans la même transaction. Un doublon simultané est refusé proprement (400) au lieu d'être enregistré.

## Boundaries & Constraints

**Always:** une seule opération d'écriture de versement à la fois par tontine, des tontines différentes restent parallèles ; contrôle (couverture du tour, règle unique de situation) et insertion dans la même transaction que le verrou ; la chaîne d'audit reste linéaire et vérifiable ; abstraction côté Application, implémentation PostgreSQL côté Infrastructure.

**Never:** pas de verrou global ; pas de changement des règles métier ; pas de migration sauf index strictement nécessaire.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Doublon simultané | 2 déclarations identiques couvrant chacune tout le tour, en parallèle | 1 acceptée, 1 refusée | 400 « déjà couvert » |
| Rafale | 10 déclarations parallèles de membres différents | 10 acceptées, chaîne d'audit valide | N/A |
| Validations simultanées | 2 validations du même versement en parallèle | 1 validée, l'autre refusée sans corrompre la chaîne | 400 |
| Tontines différentes | déclarations parallèles sur 2 tontines | aucune attente croisée | N/A |

</frozen-after-approval>

## Code Map

- `src/Application/PaymentManagement/Commands/EnregistrerVersementManuel/EnregistrerVersementManuelCommandHandler.cs` -- lecture des versements, contrôle, `GetDernierHash`, insertion.
- `src/Application/PaymentManagement/Commands/ConfirmVersement/ConfirmVersementCommandHandler.cs`, `RejeterVersement` -- écrivent aussi dans la chaîne.
- `src/Application/PaymentManagement/Services/AuditTrailService.cs` -- `GetDernierHash`, `AjouterEntree`.
- `src/Infrastructure/Persistence/TontineDbContext.cs` (IUnitOfWork, `SaveChangesAsync`) -- transaction et verrou (`pg_advisory_xact_lock`).
- `tests/ApiTests` -- base réelle pour exercer la concurrence (requêtes HTTP en parallèle) ; `GET /tontines/{id}/audit/verifier` pour la chaîne.

## Tasks & Acceptance

**Execution:**
- [x] Abstraction de verrou par tontine (Application) + implémentation PostgreSQL transactionnelle (Infrastructure).
- [x] Handlers de déclaration, validation, rejet -- contrôle + écriture sous verrou dans une transaction.
- [x] Tests de concurrence (matrice) sur base réelle.

**Acceptance Criteria:**
- Given 20 déclarations parallèles sur une même tontine, when elles se terminent, then `audit/verifier` déclare la chaîne intègre et aucun tour n'est couvert deux fois.

## Verification

**Commands:**
- `scripts/test-par-lots.sh` -- expected: tous verts

## Implementation Notes

- Checkpoint 1 approuvé sans pause (consigne permanente).
- Consignes permanentes : vérifier la mémoire avant toute compilation ou test, tester par lots ; **aucun travail reporté**.
- Implémentation (sous-agent, spec seule) : `IVerrouTontine` / `ITransactionVerrouillee` (`Application/PaymentManagement/Services/IVerrouTontine.cs`) ; `VerrouTontinePostgres` (`Infrastructure/Persistence`) ouvre une transaction sur le `TontineDbContext` de la requête et prend `pg_advisory_xact_lock(clé 64 bits dérivée du GUID de la tontine)` ; libéré au commit/rollback (aucun verrou orphelin). Après le verrou, le change tracker est vidé pour relire l'état validé le plus récent (le contrôle d'accès du contrôleur a déjà chargé le versement) ; refus explicite si des modifications sont en attente. Rollback si la transaction n'est pas validée (refus métier, exception).
- Handlers sous verrou : déclaration manuelle (contrôle « tour couvert » + hash + insertion), validation et rejet (versement relu sous le verrou, le second décideur reçoit 400 « Only a pending versement… »), et aussi `CreateVersement` / `InitierVersement` (mêmes écrivains de la chaîne ; pour l'initiation mobile money, commit avant l'appel à l'opérateur). Les gestionnaires d'événements publiés par `SaveChangesAsync` (notifications, score) s'exécutent désormais dans la même transaction.
- Aucune migration, aucune règle métier modifiée.
- Tests : `tests/ApiTests/DeclarationsConcurrentesTests.cs` (6 : doublon simultané, rafale 10, 20 déclarations parallèles + critère d'acceptation, validations simultanées, validation + rejet simultanés, deux tontines sans attente croisée en tenant le verrou de l'une) ; vérifient `audit/verifier`, l'unicité des hash précédents (versements et audit) et un seul versement par membre. Contre-épreuve : sans `pg_advisory_xact_lock`, 5 des 6 échouent. Tests sur mocks : `VerrouSansEffet` (`tests/PaymentIntegrationTests/TestRepositories.cs`).
- Vérifié : `scripts/test-par-lots.sh` — Auth 14, Domain 447, Notification 63, Payment 119, Api 63 (6 nouveaux), tous verts.
- Correctifs de revue (coordinateur) : événements de domaine différés jusqu'après le commit (jetés au rollback) ; attente bornée (`lock_timeout` 10 s, `TontineOccupeeException` → 503 « Opération en cours sur cette tontine, réessayez » via un filtre MVC global, webhook compris) ; clé à deux entiers (classe constante `TONT`, objet = GUID replié sur 32 bits, collision rare = simple sérialisation) ; plus de branche « transaction existante » ; `VerrouTontineRequisException` (pas une `InvalidOperationException`, donc non avalée par le webhook) pour usage hors verrou, y compris toute insertion de versement / entrée d'audit sans le verrou de sa tontine (dépôts + `SaveChangesAsync`) ; queue de chaîne = maillon sur lequel personne ne chaîne ; verrou aussi dans ouverture / clôture de tour, suspension, décision d'adhésion ; `CreateVersement` supprimé (sans appelant) ; Confirm / Rejeter : `GetTontineIdAsync` sans suivi puis contrôle du statut relu sans suivi sous le verrou ; contrat de `IVerrouTontine` explicite. Tests : compteurs du verrou vérifiés, tests déterministes via `pg_locks`, chaînes liées (une genèse, chaque précédent = un courant), tests du verrou sur PostgreSQL réel.
- Second lot de correctifs : les événements de domaine n'étaient jamais collectés (`Entries<AggregateRoot<object>>()` ne correspondait à aucun agrégat) ; collecte via `IHasDomainEvents` (implémentée par `AggregateRoot<TId>`). Inventaire : seuls `VersementConfirmedEvent` (SMS de confirmation au payeur + score crédit), `RoundOpenedEvent` (SMS au bénéficiaire) et `MembreSuspenduEvent` (SMS au membre suspendu) ont des gestionnaires ; aucun autre chemin (contrôleurs, jobs, relances, récapitulatif) n'envoie ces messages : pas de doublon. Après commit, un gestionnaire en échec est journalisé sans faire échouer l'opération validée. Attente du verrou configurable (`VerrouTontine:DelaiAttenteMs`, 3 s dans les tests API) ; test HTTP du 503 avec `Retry-After`.

## Spec Change Log

## Review Triage Log

Passe 1 — 3 couches, 21 constats. Aucun report (consigne de l'utilisateur).

| # | Constat | Verdict | Route |
|---|---------|---------|-------|
| 1 | Événements de domaine (score, notifications) publiés dans la transaction verrouillée : leur échec annule la validation, verrou tenu pendant leur exécution | high | patch : publication après validation de la transaction |
| 2 | Attente du verrou sans borne (pool de connexions épuisable) | high | patch : délai maximal + réponse « réessayez » |
| 3 | Clé de verrou non isolée (espace partagé avec d'autres verrous) ; collisions par repli XOR | medium | patch : forme à deux clés avec espace dédié, collision documentée |
| 4 | `ChangeTracker.Clear()` quand le verrou rejoint une transaction existante (branche sans appelant) | medium | patch : branche supprimée |
| 5 | Garde « modifications en cours » lève `InvalidOperationException`, que le webhook prend pour « déjà traité » | high | patch : exception dédiée |
| 6 | Prédécesseur de chaîne choisi par `CreatedAt` (horloges, égalités) | medium | patch : extrémité réelle de la chaîne |
| 7 | Clôture de tour / changement de statut de membre non verrouillés pendant une déclaration | medium | patch : ces écritures prennent le verrou |
| 8 | `CreateVersementCommand` sans appelant | low | patch : supprimé |
| 9 | Compteurs de `VerrouSansEffet` jamais vérifiés (Initier, Confirm, Rejeter, refus sans validation) | medium | patch |
| 10 | Validation/rejet : tests dépendants du hasard ; pas de test déterministe du verrou tenu | medium | patch |
| 11 | `VerrouTontinePostgres` sans tests (clé, garde, annulation à la libération) ; chaîne non vérifiée comme liée | medium | patch |
| 12 | Test inter-tontines dépendant du temps | low | patch : vérification par `pg_locks` |
| 13 | Chargement complet suivi pour obtenir `TontineId` | low | patch : projection |
| 14 | Écrivain non verrouillé possible (`AuditTrailService.AjouterEntree`) sans garde | medium | patch : garde « transaction active » |
| 15 | Contrat `IVerrouTontine` qui dépend de l'implémentation | low | patch : obligation explicite |

