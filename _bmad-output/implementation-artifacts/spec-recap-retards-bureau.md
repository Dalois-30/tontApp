---
title: 'Récapitulatif des retards au bureau le lendemain de l''échéance ; rappels réservés à ceux qui doivent'
type: 'feature'
created: '2026-11-17'
status: 'done'
baseline_commit: 'd8e195479b9f75ff7272fbae9de4a52bec09f9da'
route: 'dispatch'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/spec-trop-percu-reporte.md'
  - '{project-root}/_bmad-output/implementation-artifacts/spec-alertes-retard-bureau.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Le bureau (responsable, trésorier) n'est jamais prévenu des retards : aucun code ne lève `PaiementEnRetardEvent`, et les rappels planifiés J-3 / J-1 n'écrivent qu'aux membres — à tous les membres actifs, y compris ceux qui ont déjà payé.

**Approach:** Décision de l'utilisateur (2026-11-17) : **un récapitulatif le lendemain de l'échéance**. Chaque jour, pour chaque tour dont l'échéance est tombée la veille, un seul SMS au responsable et au trésorier effectifs (une fois si c'est la même personne) listant les membres en retard (impayé ou partiel, après report du trop-perçu) et le total dû ; rien si personne n'est en retard. Les rappels J-3 / J-1 ne visent plus que les membres qui doivent encore quelque chose sur ce tour.

## Boundaries & Constraints

**Always:** situation des membres = règle unique de `spec-trop-percu-reporte.md` (aucun calcul parallèle) ; destinataires = `Bureau.RolesEffectifs` (créateur sans bureau) ; idempotent : relancer le job le même jour n'envoie rien de plus ; SMS ≤ gabarit tronqué existant (noms tronqués avec « +N autres ») ; envoi via `INotificationService` (outbox), jamais d'appel SMS direct ; un membre du bureau en retard figure dans la liste mais ne reçoit pas une alerte sur lui seul.

**Never:** pas de migration sauf si l'idempotence l'exige (alors une seule, minimale) ; pas de changement des rappels côté projets ; pas d'envoi pour une tontine non active.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Retards | échéance hier, 2 impayés + 1 partiel | 1 SMS au responsable, 1 au trésorier : 3 noms, total dû | N/A |
| Même personne | responsable = trésorier | 1 seul SMS | N/A |
| Personne en retard | tous payés (report compris) | aucun SMS | N/A |
| Relance du job | même jour, déjà envoyé | aucun nouvel SMS | N/A |
| Rappel J-1 | membre déjà payé (ou couvert par report) | pas de rappel ; un membre partiel reçoit un rappel | N/A |
| Échéance pas hier | tour en cours (échéance future ou aujourd'hui) | rien | N/A |
| Jour manqué | récap non envoyé, échéance il y a 2 à 7 jours | envoyé une fois au redémarrage (décision de l'utilisateur, 2026-11-17) ; au-delà de 7 jours, rien | N/A |

</frozen-after-approval>

## Code Map

- `src/Infrastructure/Jobs/RappelJobs.cs` -- `RappelJ3Job` (:36-44) / `RappelJ1Job` (:100-108) : envoient à `GetActiveMembers()` sans regarder les paiements ; modèle pour un nouveau job quotidien.
- `src/Api/Program.cs` (:167-205) -- enregistrement Hangfire des jobs récurrents (`Cron.Daily(8, 0)`).
- `src/Domain/NotificationManagement/ValueObjects/SmsTemplate.cs` + `NotificationType.cs` -- gabarits et types ; `INotificationRepository` (`GetAllAsync`, `CountTodayByDestinataireAsync`) pour l'idempotence.
- `src/Application/NotificationManagement/EventHandlers/PaiementEnRetardEventHandler.cs` -- alerte bureau par membre, jamais déclenchée : le récap devient l'unique alerte bureau (pas de double chemin).
- `src/Domain/Gouvernance/Bureau.cs` (`RolesEffectifs`), `IBureauRepository`.
- Règle de situation : livrée par `spec-trop-percu-reporte.md`.
- Tests : `tests/NotificationTests` (handlers, Moq), `tests/ApiTests` (base réelle, `ApiFactory.Services` pour résoudre et exécuter un job).

## Tasks & Acceptance

**Execution:**
- [x] Nouveau job quotidien de récap au bureau + gabarit SMS + enregistrement Hangfire.
- [x] `RappelJ3Job` / `RappelJ1Job` -- seulement les membres qui doivent encore (reste > 0 après report).
- [x] Alerte bureau unique : retirer ou rediriger la partie bureau de `PaiementEnRetardEventHandler` pour éviter deux chemins.
- [x] Tests -- chaque ligne de la matrice.

**Acceptance Criteria:**
- Given un tour échu hier avec des retards, when le job s'exécute deux fois dans la journée, then le bureau reçoit un seul récapitulatif par personne.

## Verification

**Commands:**
- `scripts/test-par-lots.sh` -- expected: tous verts

## Implementation Notes

- Checkpoint 1 approuvé sans pause (consigne permanente) ; décision de l'utilisateur 2026-11-17 : récap le lendemain de l'échéance.
- Consignes permanentes : vérifier la mémoire avant toute compilation ou test, tester par lots ; **aucun travail reporté**.
- Implémentation (sous-agent, spec seule) : `src/Infrastructure/Jobs/RecapRetardsBureauJob.cs` (`[DisableConcurrentExecution]`), enregistré dans `DependencyInjection` et Hangfire (`recap-retards-bureau-quotidien`, `Cron.Daily(8, 0)`). Membres actifs avec `Reste > 0` sur le tour (même définition que l'écran des retardataires : une déclaration en attente couvre) ; total = somme des `Reste`. Destinataires : `RolesEffectifs` dédoublonnés, créateur sans bureau ; un membre du bureau seul en retard n'est pas alerté.
- Correctifs de revue (coordinateur) :
  - Unicité par clé explicite : migration minimale `NotificationReference` (colonne `reference` nullable + index unique filtré sur (destinataire_id, reference)) ; `INotificationService.PlanifierNotificationUniqueAsync` → `ResultatPlanification` ; clé `recap-retards:{tour}:{destinataire}` ; contrôle puis insertion protégée par l'index (`AjouterSiReferenceAbsenteAsync`, violation d'unicité absorbée).
  - Fenêtre de rattrapage : échéances des 7 derniers jours (hier inclus, aujourd'hui exclu) ; chaque récap envoyé une seule fois par destinataire et par tour. Écart assumé avec la ligne « échu avant-hier → rien » de la matrice : sur demande du coordinateur, un jour manqué est rattrapé.
  - Tontines `Active` et `Completed` ; `TryGetValue` sur les situations ; try/catch + log d'erreur par tontine (récap et rappels J-3/J-1) ; avertissement pour tout récap non planifié (pas de compte, opt-out) ; `RecapRetards` hors quota journalier (`Notification.SoumiseAuQuota`).
  - Gabarit : nom de tontine raccourci (jamais le total, le nombre, ni « +N autre(s) ») ; singulier « +1 autre » ; noms et tontine translittérés en GSM-7 (`SmsTemplate.Gsm7`) ; montant partagé `SmsTemplate.Montant` (invariant, « 0.## ») pour tous les gabarits.
  - Code mort supprimé : `PaiementEnRetardEvent`, son handler, sa branche outbox, `SmsTemplate.PaiementEnRetard` / `RetardPourGestionnaire` / `RetardPourBureau` et leurs tests.
- Rappels J-3 / J-1 : logique commune `RappelEcheance` ; seuls les membres actifs avec `Reste > 0` sur le tour, rappelés du reste.
- Tests : `tests/ApiTests/RappelsEtRecapRetardsTests.cs` (15), `SmsTemplateTests`, `NotificationDestinataireTests` (quota, unicité).

## Spec Change Log

## Review Triage Log

Passe 1 — 3 couches, 24 constats. Aucun report (consigne de l'utilisateur).

| # | Constat | Verdict | Route |
|---|---------|---------|-------|
| 1 | Anti-doublon par préfixe de texte : tontines homonymes, nom long (préfixe tronqué), identifiants différents entre contrôle et envoi, course entre deux exécutions | high | patch : clé stockée par (destinataire, tour) + index unique |
| 2 | Journée manquée jamais rattrapée | high | patch : fenêtre de rattrapage + clé par tour |
| 3 | Une tontine en erreur (`situations[...]` absent) bloque toutes les suivantes | high | patch : `TryGetValue`, isolation et journalisation par tontine |
| 4 | Récap perdu sans trace (compte absent, quota de 10 SMS, refus) | medium | patch : avertissement journalisé ; récap exempté du quota |
| 5 | Tontine terminée (dernier tour clos) exclue du récap | medium | patch |
| 6 | Nom long : total, noms et « +N autres » perdus ; pluriel du repli | medium | patch : tronquer le nom, pas le message |
| 7 | Encodage SMS (noms hors GSM-7 → UCS-2, 70 caractères) | medium | patch : noms translittérés |
| 8 | Formats de montants différents (rappels J-3/J-1 vs récap) | medium | patch : un seul formateur |
| 9 | Code mort : `RetardPourGestionnaire`, `PaiementEnRetardEvent` jamais levé et son handler | low | patch : supprimés |
| 10 | Tests : rôles réattribués / vote échu / créateur évincé ; tour clos avant l'échéance ; deux tours le même jour ; sans bureau ni compte ; récap = écran des retardataires | medium | patch |
| 11 | Statut en dur `'SUSPENDED'` dans un test | low | patch : valeur de l'enum |
| 12 | Requêtes répétées par destinataire (N+1) | low | rejet : quelques destinataires par tontine, coût négligeable |

