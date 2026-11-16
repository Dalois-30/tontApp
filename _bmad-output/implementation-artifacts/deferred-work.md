# Travaux reportés

- source_spec: `_bmad-output/implementation-artifacts/spec-circuit-paiement-bureau.md`
  summary: Le tableau de bord web doit suivre le bureau (responsable/trésorier élus ou nommés avec un compte « Membre ») au lieu du rôle plateforme et du créateur.
  evidence: DashboardController garde [Authorize(Roles="Gestionnaire,Admin")] et ResolveTontineAsync choisit la tontine par GestionnaireId.
- source_spec: `_bmad-output/implementation-artifacts/spec-circuit-paiement-bureau.md`
  summary: Décider si le montant déclaré pour un tour de tontine doit égaler la cotisation (ou gérer un partiel).
  evidence: Le handler accepte tout montant ; un versement de 1 FCFA validé marque le membre « payé ».
- source_spec: `_bmad-output/implementation-artifacts/spec-circuit-paiement-bureau.md`
  summary: Sérialiser les déclarations concurrentes (index unique tontine/tour/payeur hors Echoue, verrou sur la chaîne de hash).
  evidence: Lecture du dernier hash puis écriture sans verrou : deux requêtes simultanées peuvent dupliquer et fourcher la chaîne.
- source_spec: `_bmad-output/implementation-artifacts/spec-circuit-paiement-bureau.md`
  summary: Envoyer les alertes de retard au responsable et au trésorier du bureau plutôt qu'au créateur.
  evidence: PaiementEnRetardEventHandler utilise tontine.GestionnaireId.
- source_spec: `_bmad-output/implementation-artifacts/spec-circuit-paiement-bureau.md`
  summary: CI : exécuter tests/ApiTests (Docker requis) et ajouter l'Id du projet dans tontApp.slnx.
  evidence: Aucun pipeline ne lance ces tests ; slnx sans Id contrairement aux voisins.
