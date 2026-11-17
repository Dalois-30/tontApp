# Travaux reportés

- source_spec: `_bmad-output/implementation-artifacts/spec-circuit-paiement-bureau.md`
  summary: CI : exécuter tests/ApiTests (Docker requis) et ajouter l'Id du projet dans tontApp.slnx.
  evidence: Aucun pipeline ne lance ces tests ; slnx sans Id contrairement aux voisins.
- source_spec: `_bmad-output/implementation-artifacts/spec-paiement-partiel-tontine.md`
  summary: Mettre en place des tests unitaires côté mobile et web (toursDus, resteTourOuvert, libellés partiels).
  evidence: Aucun cadre de test JS dans mobile/ et dashboard/.
- source_spec: `_bmad-output/implementation-artifacts/spec-tableau-de-bord-bureau.md`
  summary: Sélecteur de tontine sur le web pour qui siège au bureau de plusieurs tontines (pages, paiements, audit transmettent tontineId).
  evidence: Seule la tontine par défaut est atteignable depuis le web ; l'API accepte déjà tontineId.
