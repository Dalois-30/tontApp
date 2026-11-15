# Audit tontApp : intégration du volet « Projet collectif »

_Date : 2026-11-15. Base auditée : `master` @ 664296d (fork `Dalois-30/tontApp`)._

Rappel du cadre retenu :
- L'application est un **ledger logique**. L'argent reste en banque et le trésorier valide les preuves.
- Deux volets coexistent :
  - **Tontine classique** : cotisations, tours, distribution.
  - **Projet collectif** : objectif, paliers d'engagement, échéancier, cotisations, dépenses et solde.
- **Règle clé** : le palier choisi est un engagement. Un paiement inférieur est enregistré comme **partiel** avec un reste dû, et le palier ne change pas.

---

## 1. État des lieux

| Couche | Contenu | Volume |
|---|---|---|
| Domain | 6 bounded contexts : Tontine, Payment, Identity, Notification, Billing, CreditScoring | 3 584 lignes, 290 fichiers `.cs` dans `src` |
| Application | CQRS avec MediatR, behaviors de validation et de logging, `Result` | ~30 commandes et requêtes |
| Infrastructure | EF Core + PostgreSQL, Outbox, Hangfire (rappels, renouvellements, vérification de la chaîne d'audit), SMS, FCM, Orange Money, export PDF | 5 migrations |
| API | 9 contrôleurs REST `api/v1`, JWT, rôles globaux `Membre`, `Gestionnaire` et `Admin` | |
| Dashboard | Next.js : pages login, gestionnaire (paiements, audit) et admin (tontines, alertes) | |
| Mobile | Expo : auth OTP, tontines, paiement, invitations, historique, notifications | |
| Tests | 509 tests, tous au vert : domaine, application, intégration avec TestContainers, k6 | |

La base est saine : DDD propre, Value Objects, événements de domaine, Outbox et audit chaîné en SHA-256. L'architecture se prête bien à l'ajout d'un bounded context.

---

## 2. Problèmes à corriger avant d'étendre

### Critiques (sécurité et intégrité du ledger)

1. **`POST /api/v1/versements/confirmer` est `[AllowAnonymous]`.**
   - N'importe qui peut confirmer n'importe quel versement avec son `VersementId`. Il n'y a ni signature ni vérification d'origine.
   - Fichiers : `src/Api/Controllers/VersementController.cs`, `ConfirmVersementCommandHandler.cs`.
   - Impact : le ledger peut être falsifié.
2. **Pas de contrôle d'appartenance.**
   - `POST versements/manuel` vérifie seulement le rôle global `Gestionnaire`. N'importe quel gestionnaire peut donc enregistrer un paiement dans **n'importe quelle** tontine.
   - Le même problème touche `rounds/open`, `rounds/close` et `membres/{id}/suspendre`. Seul `EnvoyerMessage` vérifie `tontine.GestionnaireId`.
3. **Le versement manuel est confirmé immédiatement, sans validation.**
   - C'est contraire au principe « le trésorier valide la preuve ».
   - La preuve se réduit à une chaîne de texte (`DescriptionPreuve`), sans pièce jointe.
   - L'acteur enregistré dans l'audit est le **membre** alors que c'est le gestionnaire qui saisit. Dans `Versement.Create`, l'acteur est `"system"`.
4. **`PayeurId` est incohérent.**
   - Dans `initier`, c'est l'`UtilisateurId` (claim `sub`). Dans `manuel`, c'est le `MembreId`.
   - Les requêtes « mes versements » et le scoring de crédit mélangent donc deux identités.

### Fonctionnels (manques du volet Tontine lui-même)

5. **Pas de paiement partiel ni de « reste dû ».**
   - Le montant versé n'est jamais comparé à `Reglement.ContributionAmount`.
   - Un seul versement confirmé est accepté par membre et par tour.
6. **La distribution n'est pas tracée.** Aucune écriture n'enregistre la remise de la cagnotte au bénéficiaire du tour. Le ledger ne contient que des entrées.
7. **L'audit est couplé au versement.** `AuditEntry` exige un `VersementId` et un `TontineId`. On ne peut donc pas y tracer une dépense, une distribution ou un événement du volet Projet.
8. **Les rôles sont globaux et il n'y a pas de rôle Trésorier.** `RoleUtilisateur` est porté par l'utilisateur et non par son appartenance à un groupe. Or le trésorier est un rôle **par groupe**.
9. **Les membres ne peuvent être ajoutés qu'en `Draft`**, ce qui est acceptable pour une tontine. Pour un projet, il faudra pouvoir accepter des adhésions en cours de route.

### Hygiène

10. `WeatherForecastController` est un reste du template.
11. Orange Money, Billing (abonnements SaaS) et CreditScoring sont hors du périmètre « l'argent reste en banque ». On les garde, mais ils doivent être désactivables par configuration.
12. `ModeAttribution.Aleatoire` utilise `Random.Shared`. Le tirage n'est ni reproductible ni auditable, alors qu'il faudrait au minimum consigner une graine dans l'audit.
13. Les messages d'erreur mélangent le français et l'anglais.

---

## 3. Garder, étendre, ajouter

| Élément | Décision | Note |
|---|---|---|
| `Common` (AggregateRoot, ValueObject, Outbox, UoW) | **Garder** | Socle commun |
| Identity (Utilisateur, OTP, JWT) | **Garder** | Ajouter des rôles par groupe (voir plus bas) |
| Notification (SMS, FCM, templates) | **Garder et étendre** | Nouveaux templates : échéance projet, reste dû, dépense validée |
| Tontine (aggregate, tours, invitations) | **Garder et corriger** | Points 2, 5, 6 et 8 |
| Versement + AuditEntry | **Étendre** vers un **ledger commun** | Point 7 |
| Mobile Money (Orange) | **Garder, désactivé par défaut** | Feature flag |
| Billing, CreditScoring | **Garder, dormants** | Hors MVP |
| Bounded context **ProjectManagement** | **Ajouter** | Voir section 4 |

---

## 4. Domaine cible

### Socle commun (shared kernel)
- `UtilisateurId`, `Montant` (un seul VO monétaire, à fusionner avec `ContributionAmount`).
- **`Groupe`** : notion légère portée par `TypeGroupe` (Tontine ou Projet) et `GroupeId`, utilisée par le ledger et les rôles.
- **`RoleGroupe`** sur chaque appartenance, avec les valeurs `Membre`, `Gestionnaire` et `Tresorier`, à la place du rôle global pour toutes les autorisations métier.
- **Ledger commun : `EcritureLedger`**, qui généralise `AuditEntry` :
  - `GroupeType`, `GroupeId`.
  - `Sens` : `Entree` ou `Sortie`.
  - `Nature` : `Cotisation`, `Distribution`, `Depense`, `Ajustement`.
  - `Montant`, `ReferenceSource` (versement ou dépense), `ActeurId`, `PreuveId?`.
  - Statut : `Saisie`, puis `Validee` ou `Rejetee`.
  - Hash chaîné **par groupe**.
  - Une écriture validée n'est jamais modifiée. On corrige par une contre-écriture.

### Nouveau bounded context `ProjectManagement`

```
Projet (AggregateRoot)
 ├─ Nom, Description, Objectif (Montant cible), DateDebut, DateFin
 ├─ Paliers autorisés (ex. 10 000 / 15 000 / 20 000 XOF)
 ├─ Périodicité de l'échéancier
 ├─ Statut : Brouillon → Ouvert → Clos | Annulé
 ├─ Participants[] (Entity)
 │    ├─ UtilisateurId, RoleGroupe
 │    └─ Engagement (VO, immuable une fois validé) : Palier, DateEngagement
 ├─ Echeances[] (Entity) : Participant, Période, MontantDu (= palier), MontantPaye, ResteDu, Statut
 │    └─ Statut : AVenir | Partielle | Soldee | EnRetard
 └─ Depenses[] (Entity) : Libellé, Montant, Justificatif, Statut (Proposée → Validée/Rejetée par le Trésorier)

Invariants :
 - Palier ∈ paliers autorisés ; il ne change jamais à cause d'un paiement.
 - Paiement < MontantDu ⇒ échéance Partielle, ResteDu = MontantDu − MontantPayé.
 - Paiement > reste dû : le surplus est refusé, ou imputé sur l'échéance suivante (décision à prendre).
 - Une dépense ne peut pas dépasser le solde logique disponible.
 - Solde logique = Σ cotisations validées − Σ dépenses validées.
```

Pour les paiements du projet, deux options :
- **(a)** réutiliser `Versement` en le rattachant à un `GroupeId` ;
- **(b)** créer un `PaiementProjet` qui alimente le ledger commun.

Je recommande **(b)** : le versement de tontine est lié à un tour, et le paiement d'un projet est lié à une échéance. Les deux écrivent dans le **même ledger**.

**Synchronisation entre les volets** : aucune référence directe. Si un jour un tour de tontine doit financer un projet, ce sera un événement de domaine (`DistributionEffectuee` vers `ContributionProjetRecue`) traité par l'Outbox.

---

## 5. Plan d'intégration progressive

| Phase | Contenu | Risque |
|---|---|---|
| **0. Sécurisation** | Corrections 1 à 4 : sécuriser `confirmer` (webhook signé ou rôle trésorier), contrôle d'appartenance via une policy `GroupeMember`/`GroupeTresorier`, statut « en attente de validation » pour le manuel, `PayeurId` toujours égal à `UtilisateurId`. Supprimer WeatherForecast, ajouter le feature flag Orange Money. | Faible, couvert par les tests existants |
| **1. Ledger commun** | `EcritureLedger` généralisée, migration des `audit_entries` (`GroupeType` = Tontine), `VerifierChaineAuditJob` par groupe, rôles par groupe et `Tresorier`. | Moyen : migration de données |
| **2. Tontine complétée** | Paiement partiel et reste dû par tour, écriture de distribution au bénéficiaire, validation des preuves par le trésorier, pièces jointes. | Moyen |
| **3. Domaine Projet** | Aggregate `Projet` avec ses invariants et ses tests unitaires. Aucune exposition API à ce stade. | Faible, isolé |
| **4. Application + API Projet** | Commandes : CreerProjet, Rejoindre, ChoisirPalier, EnregistrerPaiement, ValiderPaiement, ProposerDepense, ValiderDepense. Requêtes : Solde, Échéancier, RestesDus. Migrations, jobs de rappel d'échéance. | Moyen |
| **5. UI** | Dashboard : écrans trésorier (file de validation, solde, dépenses). Mobile : mes projets, mon engagement, mes restes dus. | Moyen |

Chaque phase donne lieu à une PR sur le fork, et les tests restent au vert à chaque étape.

---

## 6. Décisions à prendre avec le client

1. Que fait-on d'un paiement **supérieur** au reste dû : le refuser, ou l'imputer sur l'échéance suivante ?
2. Un participant peut-il **augmenter** son palier en cours de projet ? Le baisser, jamais.
3. Qui valide les dépenses : le trésorier seul, ou une double validation trésorier et gestionnaire ?
4. Un utilisateur peut-il appartenir à plusieurs projets et tontines à la fois ? On suppose que oui.
5. Garde-t-on Orange Money pour une phase ultérieure, ou le retire-t-on ?
6. Faut-il gérer une monnaie unique (XOF/XAF) ou plusieurs ?
