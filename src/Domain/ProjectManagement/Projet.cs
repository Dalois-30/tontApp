namespace Domain.ProjectManagement;

using System.Text.Json;
using Domain.Common;
using Domain.IdentityManagement.ValueObjects;
using Domain.TontineManagement.ValueObjects;

/// <summary>
/// Collective project (e.g. buying a shared asset): participants commit to a tier (palier) and pay
/// it at every instalment until the goal is reached; validated expenses are paid out of the pot.
///
/// The chosen tier is a commitment: payments never change it. How partial payments, overpayment,
/// tier changes and late arrivals are handled is configured per project (<see cref="ReglesProjet"/>).
/// Money stays outside the app: payments and expenses are entered, then validated.
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
    public ReglesProjet Regles { get; private set; } = ReglesProjet.ParDefaut;
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
        TontinePeriodicity periodicite, DateTime dateDebut, int nombreEcheances, UtilisateurId gestionnaireId,
        ReglesProjet? regles = null)
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
            Regles = regles ?? ReglesProjet.ParDefaut,
        };
        p._paliers.AddRange(liste);
        p.Journaliser("ProjetCree", gestionnaireId.Value.ToString(), new { p.Nom, p.Objectif, Paliers = liste, p.DateDebut, p.NombreEcheances, p.Regles });
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

    /// <summary>Changes the project's rules (journaled). Tier changes keep the retroactivity chosen when they were made.</summary>
    public void ModifierRegles(ReglesProjet regles, string acteur)
    {
        if (Statut is StatutProjet.Clos or StatutProjet.Annule)
            throw new InvalidOperationException("The rules of a closed project cannot change.");
        Regles = regles;
        Journaliser("ReglesModifiees", acteur, regles);
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
    /// Changes a participant's tier according to the project's rules (raise/decrease, approval,
    /// retroactivity). Returns true when the change is applied right away, false when it awaits approval.
    /// </summary>
    public bool ChangerPalier(Guid participantId, decimal nouveau, string acteur, DateTime maintenant)
    {
        if (Statut is StatutProjet.Clos or StatutProjet.Annule)
            throw new InvalidOperationException("The project is closed.");
        var p = Participant(participantId);
        if (!_paliers.Contains(nouveau)) throw new InvalidOperationException("This tier is not offered by the project.");
        var actuel = PalierCourant(p, maintenant);
        if (nouveau == actuel) throw new InvalidOperationException("This is already the participant's tier.");

        var hausse = nouveau > actuel;
        if (!hausse && !Regles.BaisseAutorisee)
            throw new InvalidOperationException("Lowering the tier is not allowed in this project.");
        var validation = hausse ? Regles.HausseSoumiseAValidation : Regles.BaisseSoumiseAValidation;
        var retroactif = hausse ? Regles.HausseRetroactive : Regles.BaisseRetroactive;
        var applicableDes = retroactif ? DateTime.MinValue : ProchaineEcheance(maintenant);

        p.AjouterChangement(new ChangementPalier(nouveau, applicableDes,
            validation ? StatutChangementPalier.EnAttente : StatutChangementPalier.Applique, maintenant, maintenant, retroactif));
        Journaliser(validation ? "ChangementPalierDemande" : hausse ? "PalierAugmente" : "PalierBaisse", acteur,
            new { ParticipantId = participantId, Ancien = actuel, Nouveau = nouveau, Retroactif = retroactif, ApplicableDes = applicableDes });
        return !validation;
    }

    public void DeciderChangementPalier(Guid participantId, bool approuver, string acteur, DateTime maintenant)
    {
        var demande = Participant(participantId).ChangementEnAttente
            ?? throw new InvalidOperationException("No pending tier change for this participant.");
        // A non-retroactive change approved late applies from the next instalment still to come.
        DateTime? applicable = approuver && !demande.Retroactif && demande.ApplicableDes <= maintenant.Date
            ? ProchaineEcheance(maintenant) : null;
        demande.Decider(approuver, maintenant, applicable);
        Journaliser(approuver ? "ChangementPalierApprouve" : "ChangementPalierRefuse", acteur,
            new { ParticipantId = participantId, demande.Palier, demande.ApplicableDes, demande.Retroactif });
    }

    private decimal PalierCourant(Participant p, DateTime maintenant)
    {
        var echeances = Echeancier();
        var reference = echeances.FirstOrDefault(d => d > maintenant.Date);
        return p.PalierAu(reference == default ? echeances[^1] : reference, maintenant);
    }

    // ── Payments ────────────────────────────────────────────────────

    public PaiementProjet EnregistrerPaiement(Guid participantId, decimal montant, string preuve, string acteur, DateTime maintenant)
    {
        if (Statut != StatutProjet.Ouvert) throw new InvalidOperationException("Payments are only possible on an open project.");
        if (montant <= 0) throw new ArgumentException("Amount must be greater than zero.", nameof(montant));
        var situation = Situation(participantId, maintenant);
        var enAttente = _paiements.Where(x => x.ParticipantId == participantId && x.Statut == StatutOperation.Saisie).Sum(x => x.Montant);
        if (!Regles.PaiementPartielAutorise)
        {
            var plusAncien = situation.Echeances.FirstOrDefault(e => e.Reste > 0);
            if (plusAncien is not null && montant + enAttente < plusAncien.Reste)
                throw new InvalidOperationException(
                    $"Partial payments are not allowed: at least {plusAncien.Reste:N0} {Devise} is expected (instalment {plusAncien.Numero}).");
        }
        if (Regles.TropPercu == TraitementTropPercu.Refuser)
        {
            var prochaine = situation.Echeances.FirstOrDefault(e => e.Date > maintenant.Date && e.Reste > 0);
            var maximum = situation.ResteDuExigible + (prochaine?.Reste ?? 0) - enAttente;
            if (montant > maximum)
                throw new InvalidOperationException(
                    $"Overpayment is not allowed: at most {Math.Max(0, maximum):N0} {Devise} can be received now.");
        }
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

    /// <summary>
    /// Position of a participant. Validated payments are replayed in validation order: each fills the
    /// oldest instalments with the tier in force at that instant. A non-retroactive tier change only
    /// affects instalments that had received no money before it took effect; a retroactive one affects
    /// all of them (a retroactive raise opens a gap on past instalments, filled first by later money;
    /// a retroactive decrease frees money that is carried to the next gaps). Money beyond everything due
    /// is kept as credit.
    /// </summary>
    public SituationParticipant Situation(Guid participantId, DateTime maintenant)
    {
        var p = Participant(participantId);
        var aujourdhui = maintenant.Date;
        var dues = Echeancier()
            .Select((d, i) => (Numero: i + 1, Date: d))
            .Where(e => Regles.RattrapageAdhesionTardive || e.Date >= p.DateAdhesion.Date)
            .ToList();
        var n = dues.Count;
        var premier = new DateTime?[n];
        var paye = new decimal[n];
        decimal credit = 0;

        void Repartir(ref decimal montant, DateTime instant)
        {
            for (var i = 0; i < n && montant > 0; i++)
            {
                var part = Math.Min(p.PalierAu(dues[i].Date, instant, premier[i]) - paye[i], montant);
                if (part <= 0) continue;
                premier[i] ??= instant;
                paye[i] += part;
                montant -= part;
            }
        }

        var paiements = _paiements
            .Where(x => x.ParticipantId == participantId && x.Statut == StatutOperation.Validee)
            .OrderBy(x => x.DecideLe ?? x.SaisiLe)
            .ToList();
        foreach (var paiement in paiements)
        {
            var reste = paiement.Montant;
            Repartir(ref reste, paiement.DecideLe ?? paiement.SaisiLe);
            credit += reste;
        }

        // Amounts due today; money above a (retroactively lowered) amount goes back to the pot.
        var du = new decimal[n];
        for (var i = 0; i < n; i++)
        {
            du[i] = p.PalierAu(dues[i].Date, maintenant, premier[i]);
            if (paye[i] > du[i]) { credit += paye[i] - du[i]; paye[i] = du[i]; }
        }
        // Credit fills the remaining gaps, oldest first (e.g. after a retroactive raise).
        for (var i = 0; i < n && credit > 0; i++)
        {
            var part = Math.Min(du[i] - paye[i], credit);
            if (part <= 0) continue;
            paye[i] += part;
            credit -= part;
        }

        var echeances = Enumerable.Range(0, n).Select(i =>
        {
            var restant = du[i] - paye[i];
            var statut = restant <= 0 ? StatutEcheance.Soldee
                : paye[i] > 0 ? StatutEcheance.Partielle
                : dues[i].Date <= aujourdhui ? StatutEcheance.EnRetard
                : StatutEcheance.AVenir;
            return new EcheanceSituation(dues[i].Numero, dues[i].Date, du[i], paye[i], Math.Max(0, restant), statut);
        }).ToList();

        var exigible = echeances.Where(e => e.Date <= aujourdhui).Sum(e => e.Reste);
        var avance = echeances.Where(e => e.Date > aujourdhui).Sum(e => e.Paye) + credit;

        return new SituationParticipant(participantId, p.Nom, PalierCourant(p, maintenant), p.ChangementEnAttente?.Palier,
            paiements.Sum(x => x.Montant), echeances.Sum(e => e.Palier), exigible, avance, echeances);
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
