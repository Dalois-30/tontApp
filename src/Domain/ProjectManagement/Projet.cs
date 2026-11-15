namespace Domain.ProjectManagement;

using System.Text.Json;
using Domain.Common;
using Domain.IdentityManagement.ValueObjects;
using Domain.TontineManagement.ValueObjects;

/// <summary>
/// Collective project (e.g. buying a shared asset): participants commit to a tier (palier) and pay
/// it at every instalment until the goal is reached; validated expenses are paid out of the pot.
///
/// Rules agreed with the client:
/// - The chosen tier is a commitment: a payment below it is partial, the rest stays due; the tier
///   does not change because of a payment.
/// - Overpayment is carried forward to the next instalments.
/// - Raising the tier applies from the next instalment; lowering it is a request the gestionnaire
///   must approve (second validation).
/// - Money stays outside the app: payments and expenses are entered, then validated.
/// </summary>
public sealed class Projet : AggregateRoot<ProjetId>
{
    private readonly List<decimal> _paliers = [];
    private readonly List<Participant> _participants = [];
    private readonly List<PaiementProjet> _paiements = [];
    private readonly List<Depense> _depenses = [];
    private readonly List<JournalProjet> _journal = [];

    public string Nom { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public decimal Objectif { get; private set; }
    public string Devise { get; private set; } = "XOF";
    public TontinePeriodicity Periodicite { get; private set; }
    public DateTime DateDebut { get; private set; }
    public int NombreEcheances { get; private set; }
    public StatutProjet Statut { get; private set; }
    public UtilisateurId GestionnaireId { get; private set; } = default!;
    public DateTime CreatedAt { get; private set; }
    public string? CodeInvitationHash { get; private set; }
    public DateTime? CodeInvitationExpireLe { get; private set; }

    public IReadOnlyList<decimal> Paliers => _paliers.AsReadOnly();
    public IReadOnlyCollection<Participant> Participants => _participants.AsReadOnly();
    public IReadOnlyCollection<PaiementProjet> Paiements => _paiements.AsReadOnly();
    public IReadOnlyCollection<Depense> Depenses => _depenses.AsReadOnly();
    public IReadOnlyCollection<JournalProjet> Journal => _journal.AsReadOnly();

    private Projet() { }

    public static Projet Creer(
        string nom, string? description, decimal objectif, IEnumerable<decimal> paliers,
        TontinePeriodicity periodicite, DateTime dateDebut, int nombreEcheances, UtilisateurId gestionnaireId)
    {
        if (string.IsNullOrWhiteSpace(nom)) throw new ArgumentException("Project name must not be empty.", nameof(nom));
        if (objectif <= 0) throw new ArgumentException("The goal must be greater than zero.", nameof(objectif));
        var liste = paliers.Distinct().OrderBy(p => p).ToList();
        if (liste.Count == 0 || liste.Any(p => p <= 0))
            throw new ArgumentException("At least one positive tier is required.", nameof(paliers));
        if (nombreEcheances is < 1 or > 120)
            throw new ArgumentException("The schedule must have between 1 and 120 instalments.", nameof(nombreEcheances));

        var p = new Projet
        {
            Id = ProjetId.Create(),
            Nom = nom.Trim(),
            Description = description,
            Objectif = objectif,
            Periodicite = periodicite,
            DateDebut = dateDebut.Date,
            NombreEcheances = nombreEcheances,
            Statut = StatutProjet.Brouillon,
            GestionnaireId = gestionnaireId,
            CreatedAt = DateTime.UtcNow,
        };
        p._paliers.AddRange(liste);
        p.Journaliser("ProjetCree", gestionnaireId.Value.ToString(), new { p.Nom, p.Objectif, Paliers = liste, p.DateDebut, p.NombreEcheances });
        return p;
    }

    // ── Schedule ────────────────────────────────────────────────────

    public IReadOnlyList<DateTime> Echeancier() =>
        Enumerable.Range(0, NombreEcheances).Select(k => Periodicite switch
        {
            TontinePeriodicity.Weekly => DateDebut.AddDays(7 * k),
            TontinePeriodicity.Biweekly => DateDebut.AddDays(14 * k),
            _ => DateDebut.AddMonths(k),
        }).ToList();

    private DateTime ProchaineEcheance(DateTime maintenant) =>
        Echeancier().FirstOrDefault(d => d > maintenant.Date) is var d && d != default
            ? d
            : throw new InvalidOperationException("No instalment left: the tier can no longer change.");

    // ── Lifecycle ───────────────────────────────────────────────────

    public void Ouvrir(string acteur)
    {
        if (Statut != StatutProjet.Brouillon) throw new InvalidOperationException("Only a draft project can be opened.");
        Statut = StatutProjet.Ouvert;
        Journaliser("ProjetOuvert", acteur, new { });
    }

    public void Clore(string acteur)
    {
        if (Statut != StatutProjet.Ouvert) throw new InvalidOperationException("Only an open project can be closed.");
        Statut = StatutProjet.Clos;
        Journaliser("ProjetClos", acteur, new { Solde = Solde });
    }

    // ── Membership ──────────────────────────────────────────────────

    /// <summary>Generates a multi-use invitation code; returns it in clear (only its hash is kept).</summary>
    public string GenererCodeInvitation(int expirationJours, string acteur)
    {
        if (Statut is not (StatutProjet.Brouillon or StatutProjet.Ouvert))
            throw new InvalidOperationException("Invitations are only possible for a draft or open project.");
        var code = InvitationCode.Generate().Value;
        CodeInvitationHash = InvitationCode.ComputeHash(code);
        CodeInvitationExpireLe = DateTime.UtcNow.AddDays(Math.Clamp(expirationJours, 1, 90));
        Journaliser("CodeInvitationGenere", acteur, new { ExpireLe = CodeInvitationExpireLe });
        return code;
    }

    public bool CodeCorrespond(string code) =>
        CodeInvitationHash is not null && CodeInvitationExpireLe > DateTime.UtcNow
        && CodeInvitationHash == InvitationCode.ComputeHash(code.Trim().ToUpperInvariant());

    /// <summary>
    /// Joins with a code and a tier. The participant owes every instalment dated on or after the
    /// joining day.
    /// </summary>
    public Participant Rejoindre(string code, string nom, UtilisateurId utilisateurId, decimal palier, DateTime maintenant)
    {
        if (Statut is not (StatutProjet.Brouillon or StatutProjet.Ouvert))
            throw new InvalidOperationException("This project no longer accepts participants.");
        if (!CodeCorrespond(code)) throw new InvalidOperationException("Invalid or expired invitation code.");
        if (!_paliers.Contains(palier)) throw new InvalidOperationException("This tier is not offered by the project.");
        if (_participants.Any(p => p.UtilisateurId == utilisateurId))
            throw new InvalidOperationException("This user already participates in the project.");
        if (string.IsNullOrWhiteSpace(nom)) throw new ArgumentException("Name must not be empty.", nameof(nom));

        var participant = new Participant(nom.Trim(), utilisateurId, palier, maintenant);
        _participants.Add(participant);
        Journaliser("ParticipantAjoute", utilisateurId.Value.ToString(), new { ParticipantId = participant.Id, participant.Nom, Palier = palier });
        return participant;
    }

    // ── Tier changes ────────────────────────────────────────────────

    /// <summary>
    /// Raise: applied from the next instalment. Lower: pending until the gestionnaire approves it.
    /// Returns true when the change is applied right away.
    /// </summary>
    public bool ChangerPalier(Guid participantId, decimal nouveau, string acteur, DateTime maintenant)
    {
        var p = Participant(participantId);
        if (!_paliers.Contains(nouveau)) throw new InvalidOperationException("This tier is not offered by the project.");
        var prochaine = ProchaineEcheance(maintenant);
        var actuel = p.PalierAu(prochaine, maintenant);
        if (nouveau == actuel) throw new InvalidOperationException("This is already the participant's tier.");

        var hausse = nouveau > actuel;
        p.AjouterChangement(new ChangementPalier(nouveau, prochaine,
            hausse ? StatutChangementPalier.Applique : StatutChangementPalier.EnAttente, maintenant));
        Journaliser(hausse ? "PalierAugmente" : "BaissePalierDemandee", acteur,
            new { ParticipantId = participantId, Ancien = actuel, Nouveau = nouveau, ApplicableDes = prochaine });
        return hausse;
    }

    public void DeciderBaissePalier(Guid participantId, bool approuver, string acteur, DateTime maintenant)
    {
        var demande = Participant(participantId).BaisseEnAttente
            ?? throw new InvalidOperationException("No pending tier decrease for this participant.");
        // An approval that comes late applies from the next instalment still to come.
        DateTime? applicable = approuver && demande.ApplicableDes <= maintenant.Date ? ProchaineEcheance(maintenant) : null;
        demande.Decider(approuver, maintenant, applicable);
        Journaliser(approuver ? "BaissePalierApprouvee" : "BaissePalierRefusee", acteur,
            new { ParticipantId = participantId, demande.Palier, demande.ApplicableDes });
    }

    // ── Payments ────────────────────────────────────────────────────

    public PaiementProjet EnregistrerPaiement(Guid participantId, decimal montant, string preuve, string acteur, DateTime maintenant)
    {
        if (Statut != StatutProjet.Ouvert) throw new InvalidOperationException("Payments are only possible on an open project.");
        if (montant <= 0) throw new ArgumentException("Amount must be greater than zero.", nameof(montant));
        Participant(participantId);
        var paiement = new PaiementProjet(participantId, montant, preuve, acteur, maintenant);
        _paiements.Add(paiement);
        Journaliser("PaiementSaisi", acteur, new { PaiementId = paiement.Id, ParticipantId = participantId, Montant = montant, Preuve = preuve });
        return paiement;
    }

    public void DeciderPaiement(Guid paiementId, bool valider, string acteur, string? referenceOuMotif, DateTime maintenant)
    {
        var paiement = _paiements.FirstOrDefault(p => p.Id == paiementId)
            ?? throw new InvalidOperationException("Payment not found.");
        if (!valider && string.IsNullOrWhiteSpace(referenceOuMotif))
            throw new InvalidOperationException("A reason is required to reject a payment.");
        paiement.Decider(valider, acteur, referenceOuMotif, maintenant);
        Journaliser(valider ? "PaiementValide" : "PaiementRejete", acteur,
            new { PaiementId = paiementId, paiement.Montant, Detail = referenceOuMotif });
    }

    // ── Expenses ────────────────────────────────────────────────────

    public Depense ProposerDepense(string libelle, decimal montant, string justificatif, string acteur, DateTime maintenant)
    {
        if (Statut != StatutProjet.Ouvert) throw new InvalidOperationException("Expenses are only possible on an open project.");
        if (string.IsNullOrWhiteSpace(libelle)) throw new ArgumentException("Label must not be empty.", nameof(libelle));
        if (montant <= 0) throw new ArgumentException("Amount must be greater than zero.", nameof(montant));
        var depense = new Depense(libelle.Trim(), montant, justificatif, acteur, maintenant);
        _depenses.Add(depense);
        Journaliser("DepenseProposee", acteur, new { DepenseId = depense.Id, depense.Libelle, Montant = montant, Justificatif = justificatif });
        return depense;
    }

    public void DeciderDepense(Guid depenseId, bool valider, string acteur, string? motif, DateTime maintenant)
    {
        var depense = _depenses.FirstOrDefault(d => d.Id == depenseId)
            ?? throw new InvalidOperationException("Expense not found.");
        if (valider && depense.Montant > Solde)
            throw new InvalidOperationException($"Insufficient balance: {Solde:N0} {Devise} available.");
        if (!valider && string.IsNullOrWhiteSpace(motif))
            throw new InvalidOperationException("A reason is required to reject an expense.");
        depense.Decider(valider, acteur, motif, maintenant);
        Journaliser(valider ? "DepenseValidee" : "DepenseRejetee", acteur, new { DepenseId = depenseId, depense.Montant, Motif = motif });
    }

    // ── Figures ─────────────────────────────────────────────────────

    public decimal TotalCollecte => _paiements.Where(p => p.Statut == StatutOperation.Validee).Sum(p => p.Montant);
    public decimal TotalDepense => _depenses.Where(d => d.Statut == StatutOperation.Validee).Sum(d => d.Montant);
    /// <summary>Logical balance: validated payments minus validated expenses.</summary>
    public decimal Solde => TotalCollecte - TotalDepense;

    public SituationParticipant Situation(Guid participantId, DateTime maintenant)
    {
        var p = Participant(participantId);
        var aujourdhui = maintenant.Date;
        var dues = Echeancier()
            .Select((d, i) => (Numero: i + 1, Date: d))
            .Where(e => e.Date >= p.DateAdhesion.Date)
            .ToList();

        // Validated payments are replayed in validation order. Each one fills the oldest instalments
        // with the tier known at that instant; once an instalment has received money its amount is
        // locked, so a later tier change never redistributes money already received.
        var paiements = _paiements
            .Where(x => x.ParticipantId == participantId && x.Statut == StatutOperation.Validee)
            .OrderBy(x => x.DecideLe ?? x.SaisiLe)
            .ToList();
        var totalPaye = paiements.Sum(x => x.Montant);
        var du = new decimal?[dues.Count];   // locked amount due per instalment
        var paye = new decimal[dues.Count];
        decimal credit = 0;
        foreach (var paiement in paiements)
        {
            var instant = paiement.DecideLe ?? paiement.SaisiLe;
            var reste = paiement.Montant;
            for (var i = 0; i < dues.Count && reste > 0; i++)
            {
                du[i] ??= p.PalierAu(dues[i].Date, instant);
                var part = Math.Min(du[i]!.Value - paye[i], reste);
                if (part <= 0) continue;
                paye[i] += part;
                reste -= part;
            }
            credit += reste;
        }

        var echeances = new List<EcheanceSituation>();
        for (var i = 0; i < dues.Count; i++)
        {
            var (numero, date) = dues[i];
            var palier = du[i] ?? p.PalierAu(date, maintenant);
            var restant = palier - paye[i];
            var statut = restant <= 0 ? StatutEcheance.Soldee
                : paye[i] > 0 ? StatutEcheance.Partielle
                : date <= aujourdhui ? StatutEcheance.EnRetard
                : StatutEcheance.AVenir;
            echeances.Add(new EcheanceSituation(numero, date, palier, paye[i], Math.Max(0, restant), statut));
        }
        var aRepartir = credit;

        var exigible = echeances.Where(e => e.Date <= aujourdhui).Sum(e => e.Reste);
        var avance = echeances.Where(e => e.Date > aujourdhui).Sum(e => e.Paye) + aRepartir;
        var prochaine = Echeancier().FirstOrDefault(d => d > aujourdhui);
        var palierActuel = p.PalierAu(prochaine == default ? aujourdhui : prochaine, maintenant);

        return new SituationParticipant(participantId, p.Nom, palierActuel, p.BaisseEnAttente?.Palier, totalPaye,
            echeances.Sum(e => e.Palier), exigible, avance, echeances);
    }

    public bool VerifierJournal()
    {
        var precedent = JournalProjet.GenesisHash;
        foreach (var e in _journal.OrderBy(e => e.Sequence))
        {
            if (!e.Verifier(precedent)) return false;
            precedent = e.HashCourant;
        }
        return true;
    }

    private Participant Participant(Guid id) =>
        _participants.FirstOrDefault(p => p.Id == id) ?? throw new InvalidOperationException("Participant not found.");

    private void Journaliser(string action, string acteur, object payload)
    {
        var dernier = _journal.OrderBy(e => e.Sequence).LastOrDefault();
        _journal.Add(new JournalProjet((dernier?.Sequence ?? 0) + 1, action, acteur,
            JsonSerializer.Serialize(payload), dernier?.HashCourant ?? JournalProjet.GenesisHash));
    }
}
