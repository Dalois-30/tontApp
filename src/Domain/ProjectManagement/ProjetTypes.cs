namespace Domain.ProjectManagement;

using System.Security.Cryptography;
using System.Text;
using Domain.Common;
using Domain.IdentityManagement.ValueObjects;

public sealed class ProjetId : ValueObject
{
    public Guid Value { get; }
    private ProjetId(Guid value) => Value = value;
    public static ProjetId Create() => new(Guid.NewGuid());
    public static ProjetId From(Guid value) =>
        value == Guid.Empty ? throw new ArgumentException("ProjetId cannot be empty.", nameof(value)) : new ProjetId(value);
    protected override IEnumerable<object?> GetEqualityComponents() { yield return Value; }
    public override string ToString() => Value.ToString();
}

public enum StatutProjet { Brouillon, Ouvert, Clos, Annule }

public enum TraitementTropPercu { Reporter, Refuser }

/// <summary>
/// Business rules of a project, chosen by the gestionnaire (defaults = rules agreed with the client).
/// </summary>
public sealed class ReglesProjet
{
    /// <summary>A payment below the remaining amount of the oldest unpaid instalment is accepted (partial).</summary>
    public bool PaiementPartielAutorise { get; init; } = true;
    /// <summary>What happens to money beyond what is currently due: carried forward, or the payment is refused.</summary>
    public TraitementTropPercu TropPercu { get; init; } = TraitementTropPercu.Reporter;
    /// <summary>A raise also applies to past instalments: the difference becomes due and is filled first.</summary>
    public bool HausseRetroactive { get; init; } = true;
    public bool HausseSoumiseAValidation { get; init; }
    public bool BaisseAutorisee { get; init; } = true;
    public bool BaisseSoumiseAValidation { get; init; } = true;
    /// <summary>A decrease also applies to past instalments: money already received is redistributed.</summary>
    public bool BaisseRetroactive { get; init; }
    /// <summary>A participant joining late owes the instalments due before their arrival.</summary>
    public bool RattrapageAdhesionTardive { get; init; }

    public static ReglesProjet ParDefaut => new();
}

/// <summary>Lifecycle of a payment or an expense: entered, then validated or rejected.</summary>
public enum StatutOperation { Saisie, Validee, Rejetee }

public enum StatutChangementPalier { Applique, EnAttente, Refuse }

public enum StatutEcheance { Soldee, Partielle, EnRetard, AVenir }

/// <summary>Member of a collective project, committed to a contribution tier (palier).</summary>
public sealed class Participant : Entity<Guid>
{
    private readonly List<ChangementPalier> _changements = [];

    public string Nom { get; private set; } = string.Empty;
    public UtilisateurId? UtilisateurId { get; private set; }
    public DateTime DateAdhesion { get; private set; }
    public IReadOnlyCollection<ChangementPalier> Changements => _changements.AsReadOnly();

    private Participant() { }

    internal Participant(string nom, UtilisateurId? utilisateurId, decimal palier, DateTime maintenant) : base(Guid.NewGuid())
    {
        Nom = nom;
        UtilisateurId = utilisateurId;
        DateAdhesion = maintenant;
        // The initial commitment applies to every instalment the participant owes.
        _changements.Add(new ChangementPalier(palier, DateTime.MinValue, StatutChangementPalier.Applique, maintenant, maintenant));
    }

    internal void LierCompte(UtilisateurId utilisateurId) => UtilisateurId = utilisateurId;

    /// <summary>
    /// Tier of the instalment due on <paramref name="date"/> as of <paramref name="au"/>.
    /// A non-retroactive change only counts for instalments that had received no money before it took
    /// effect (<paramref name="premierPaiement"/>); a retroactive change counts for every instalment.
    /// </summary>
    public decimal PalierAu(DateTime date, DateTime au, DateTime? premierPaiement = null) => _changements
        .Where(c => c.Statut == StatutChangementPalier.Applique && c.EffectifLe <= au
                    && (c.Retroactif || (c.ApplicableDes <= date && (premierPaiement is null || c.EffectifLe <= premierPaiement))))
        .OrderBy(c => c.EffectifLe).ThenBy(c => c.ApplicableDes)
        .Last().Palier;

    /// <summary>Tier change waiting for the gestionnaire's approval (at most one).</summary>
    public ChangementPalier? ChangementEnAttente => _changements.FirstOrDefault(c => c.Statut == StatutChangementPalier.EnAttente);

    internal void AjouterChangement(ChangementPalier c)
    {
        ChangementEnAttente?.Decider(false, c.DemandeLe); // a new request replaces a pending one
        _changements.Add(c);
    }
}

public sealed class ChangementPalier : Entity<Guid>
{
    public decimal Palier { get; private set; }
    /// <summary>First instalment date the tier applies to.</summary>
    public DateTime ApplicableDes { get; private set; }
    public StatutChangementPalier Statut { get; private set; }
    public DateTime DemandeLe { get; private set; }
    /// <summary>
    /// Instant the change took effect (raise: when requested; decrease: when approved). Money received
    /// before that instant keeps the allocation it had; null while pending or refused.
    /// </summary>
    public DateTime? EffectifLe { get; private set; }
    /// <summary>Applies to every instalment, including those already paid (per the project's rules when requested).</summary>
    public bool Retroactif { get; private set; }

    private ChangementPalier() { }

    internal ChangementPalier(decimal palier, DateTime applicableDes, StatutChangementPalier statut, DateTime demandeLe,
        DateTime? effectifLe = null, bool retroactif = false) : base(Guid.NewGuid())
    {
        Retroactif = retroactif;
        Palier = palier;
        ApplicableDes = applicableDes;
        Statut = statut;
        DemandeLe = demandeLe;
        EffectifLe = statut == StatutChangementPalier.Applique ? effectifLe ?? demandeLe : null;
    }

    internal void Decider(bool approuve, DateTime maintenant, DateTime? applicableDes = null)
    {
        if (Statut != StatutChangementPalier.EnAttente)
            throw new InvalidOperationException("Only a pending tier change can be decided.");
        Statut = approuve ? StatutChangementPalier.Applique : StatutChangementPalier.Refuse;
        if (!approuve) return;
        EffectifLe = maintenant;
        if (applicableDes is { } d) ApplicableDes = d;
    }
}

/// <summary>Money a participant handed over (cash or bank), entered then validated by the gestionnaire.</summary>
public sealed class PaiementProjet : Entity<Guid>
{
    public Guid ParticipantId { get; private set; }
    public decimal Montant { get; private set; }
    public string Preuve { get; private set; } = string.Empty;
    public string? Reference { get; private set; }
    public StatutOperation Statut { get; private set; }
    public DateTime SaisiLe { get; private set; }
    public string SaisiPar { get; private set; } = string.Empty;
    public DateTime? DecideLe { get; private set; }
    public string? DecidePar { get; private set; }
    public string? Motif { get; private set; }

    private PaiementProjet() { }

    internal PaiementProjet(Guid participantId, decimal montant, string preuve, string acteur, DateTime maintenant) : base(Guid.NewGuid())
    {
        ParticipantId = participantId;
        Montant = montant;
        Preuve = preuve;
        Statut = StatutOperation.Saisie;
        SaisiLe = maintenant;
        SaisiPar = acteur;
    }

    /// <summary>Historical payment imported from a register: already validated at its own date.</summary>
    internal static PaiementProjet Historique(Guid participantId, decimal montant, string preuve, string acteur, DateTime date) =>
        new(participantId, montant, preuve, acteur, date)
        {
            Statut = StatutOperation.Validee,
            DecideLe = date,
            DecidePar = acteur,
            Reference = "IMPORT",
        };

    internal void Decider(bool valide, string acteur, string? referenceOuMotif, DateTime maintenant)
    {
        if (Statut != StatutOperation.Saisie)
            throw new InvalidOperationException("Only an entered payment can be validated or rejected.");
        Statut = valide ? StatutOperation.Validee : StatutOperation.Rejetee;
        DecidePar = acteur;
        DecideLe = maintenant;
        if (valide) Reference = referenceOuMotif; else Motif = referenceOuMotif;
    }
}

/// <summary>Spending from the project's pot, proposed then validated.</summary>
public sealed class Depense : Entity<Guid>
{
    public string Libelle { get; private set; } = string.Empty;
    public decimal Montant { get; private set; }
    public string Justificatif { get; private set; } = string.Empty;
    public StatutOperation Statut { get; private set; }
    public DateTime ProposeeLe { get; private set; }
    public string ProposeePar { get; private set; } = string.Empty;
    public DateTime? DecideeLe { get; private set; }
    public string? DecideePar { get; private set; }
    public string? Motif { get; private set; }

    private Depense() { }

    internal Depense(string libelle, decimal montant, string justificatif, string acteur, DateTime maintenant) : base(Guid.NewGuid())
    {
        Libelle = libelle;
        Montant = montant;
        Justificatif = justificatif;
        Statut = StatutOperation.Saisie;
        ProposeeLe = maintenant;
        ProposeePar = acteur;
    }

    internal void Decider(bool valide, string acteur, string? motif, DateTime maintenant)
    {
        if (Statut != StatutOperation.Saisie)
            throw new InvalidOperationException("Only a proposed expense can be validated or rejected.");
        Statut = valide ? StatutOperation.Validee : StatutOperation.Rejetee;
        DecideePar = acteur;
        DecideeLe = maintenant;
        Motif = valide ? null : motif;
    }
}

/// <summary>Append-only, SHA-256 chained journal of a project's actions.</summary>
public sealed class JournalProjet : Entity<Guid>
{
    public static readonly string GenesisHash = Sha256("GENESIS_PROJET");

    public int Sequence { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string Acteur { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public DateTime Horodatage { get; private set; }
    public string HashPrecedent { get; private set; } = string.Empty;
    public string HashCourant { get; private set; } = string.Empty;

    private JournalProjet() { }

    internal JournalProjet(int sequence, string action, string acteur, string payload, string hashPrecedent) : base(Guid.NewGuid())
    {
        Sequence = sequence;
        Action = action;
        Acteur = acteur;
        Payload = payload;
        Horodatage = Horloge.MaintenantUtc();
        HashPrecedent = hashPrecedent;
        HashCourant = Calculer();
    }

    public bool Verifier(string hashPrecedentAttendu) =>
        HashPrecedent == hashPrecedentAttendu && HashCourant == Calculer();

    private string Calculer() => Sha256($"{Id}|{Sequence}|{Action}|{Acteur}|{Horodatage:O}|{Payload}|{HashPrecedent}");

    private static string Sha256(string s) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
}

/// <summary>One instalment of a participant: what is due, what was allocated to it, what remains.</summary>
public sealed record EcheanceSituation(int Numero, DateTime Date, decimal Palier, decimal Paye, decimal Reste, StatutEcheance Statut);

/// <summary>
/// Financial position of a participant at a date. Validated payments are allocated to instalments in
/// date order; any surplus is carried forward to the next ones (and kept as credit past the last).
/// </summary>
public sealed record SituationParticipant(
    Guid ParticipantId,
    string Nom,
    decimal PalierActuel,
    decimal? PalierDemande,
    decimal TotalPaye,
    decimal EngagementTotal,
    decimal ResteDuExigible,
    decimal Avance,
    IReadOnlyList<EcheanceSituation> Echeances);
