---
title: 'Les alertes de retard de paiement vont au bureau (responsable, trésorier), plus au créateur'
type: 'bugfix'
created: '2026-11-17'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Quand un paiement de tontine est en retard, le SMS d'alerte destiné à la gestion part au créateur de la tontine (`GestionnaireId`), même s'il n'est plus ni responsable ni trésorier (nouveau trésorier nommé, responsable élu).

**Approach:** Envoyer cette alerte au responsable et au trésorier effectifs du bureau (votes échus compris, `Bureau.RolesEffectifs`), une seule fois si c'est la même personne ; sans bureau, au créateur comme aujourd'hui. Point reporté de `spec-circuit-paiement-bureau.md`.

</frozen-after-approval>

## Implementation Notes

- `PaiementEnRetardEventHandler` : destinataires = rôles effectifs du bureau (`Bureau.RolesEffectifs`), dédoublonnés, sans le membre en retard ; repli sur le créateur sans bureau. Nouveau gabarit `SmsTemplate.RetardPourBureau` qui nomme le membre.
- Surprise (revue) : aucun code ne lève `PaiementEnRetardEvent` ; le correctif est juste mais inactif tant qu'un producteur n'existe pas → reporté.
- Tests : 7 nouveaux cas dans `PaiementEnRetardEventHandlerTests` ; NotificationTests 57/57, DomainUnitsTest 435/435 (lancés par lots après contrôle mémoire).

## Review Triage Log

Couche : Blind Hunter seule (voie courte).

- Aucun producteur de `PaiementEnRetardEvent` — medium, antérieur (grep : seul le handler et la sérialisation outbox) → defer.
- Branche `: []` inatteignable — low, correction directe → patch (supprimée).
- Membre du bureau en retard alerté deux fois — medium → patch (exclu des destinataires).
- L'alerte ne nomme pas le membre — medium → patch (`RetardPourBureau`).
- Lecture du bureau lourde et suivie — low ; handler aujourd'hui inactif, à traiter avec le producteur → rejet.
- Horloge `UtcNow` non injectée — low, comportement cohérent avec le reste du code → rejet.
- Résultat de planification ignoré, une exception stoppe la suite — low, identique aux autres handlers → rejet.
- Tests manquants (créateur seul au bureau, payeur au bureau, contenu, tontine introuvable) — medium → patch.
- Votes changeant le trésorier non appliqués — false : `ObjetVote` ne contient que `ChangementResponsable`.

