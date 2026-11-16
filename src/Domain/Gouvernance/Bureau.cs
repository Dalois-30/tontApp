namespace Domain.Gouvernance;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Domain.Common;

public enum TypeGroupe { Tontine, Projet }

public enum RoleBureau { Responsable, Tresorier }

/// <summary>How a vote is decided.</summary>
public enum ModeVote
{
    /// <summary>More "yes" than "no" among the voters, provided the quorum voted.</summary>
    MajoriteDesVotants,
    /// <summary>More than half of all the electors vote "yes".</summary>
    MajoriteDesMembres,
    /// <summary>At least two thirds of all the electors vote "yes".</summary>
    DeuxTiersDesMembres,
}

public enum StatutVote { Ouvert, Adopte, Rejete }

public enum ObjetVote { ChangementResponsable }

/// <summary>Governance rules of a group. Defaults are the ones agreed with the client.</summary>
public sealed record ReglesGouvernance
{
    public static readonly ReglesGouvernance ParDefaut = new();

    public ModeVote ModeVote { get; init; } = ModeVote.MajoriteDesVotants;
    /// <summary>Share of the electors (percent) who must vote for a "majority of voters" vote to count.</summary>
    public int QuorumPourcent { get; init; } = 50;
    public int DureeVoteJours { get; init; } = 7;

    public void Valider()
    {
        if (QuorumPourcent is < 1 or > 100) throw new ArgumentException("The quorum must be between 1 and 100 percent.");
        if (DureeVoteJours is < 1 or > 60) throw new ArgumentException("A vote lasts between 1 and 60 days.");
    }
}

/// <summary>
/// Governance of a tontine or a collective project: who holds the key roles (responsable, trésorier),
/// the votes of the members and the séance reports. Its journal is SHA-256 chained like the group's,
/// so a decision taken long ago can be found and trusted.
///
/// The id is the group's id. The creator holds both roles until they name someone else; the
/// responsable changes only through a vote of the members (or the platform administrator).
/// </summary>
public sealed class Bureau : AggregateRoot<Guid>
{
    private readonly List<Vote> _votes = [];
    private readonly List<Seance> _seances = [];
    private readonly List<JournalBureau> _journal = [];

    public TypeGroupe Type { get; private set; }
    public Guid ResponsableId { get; private set; }
    public Guid TresorierId { get; private set; }
    public ReglesGouvernance Regles { get; private set; } = ReglesGouvernance.ParDefaut;

    public IReadOnlyCollection<Vote> Votes => _votes.AsReadOnly();
    public IReadOnlyCollection<Seance> Seances => _seances.AsReadOnly();
    public IReadOnlyCollection<JournalBureau> Journal => _journal.AsReadOnly();

    private Bureau() { }

    public static Bureau Creer(Guid groupeId, TypeGroupe type, Guid createurId)
    {
        var b = new Bureau { Id = groupeId, Type = type, ResponsableId = createurId, TresorierId = createurId };
        b.Journaliser("BureauConstitue", createurId.ToString(), new { Responsable = createurId, Tresorier = createurId });
        return b;
    }

    /// <summary>
    /// The responsable and trésorier that hold at <paramref name="maintenant"/>, i.e. once the votes past
    /// their deadline are concluded. Pure query: the bureau is not modified (closing a vote and journaling
    /// it is done by <see cref="Actualiser"/>, called by the governance use cases only).
    /// </summary>
    public (Guid ResponsableId, Guid TresorierId) RolesEffectifs(DateTime maintenant)
    {
        var responsable = ResponsableId;
        foreach (var vote in _votes.Where(v => v.Statut == StatutVote.Ouvert && v.FermeLe <= maintenant).OrderBy(v => v.FermeLe))
            if (vote.Objet == ObjetVote.ChangementResponsable && vote.Issue(fin: true) == StatutVote.Adopte)
                responsable = vote.CandidatId;
        return (responsable, TresorierId);
    }

    public bool EstResponsable(Guid utilisateurId) => ResponsableId == utilisateurId;
    public bool EstTresorier(Guid utilisateurId) => TresorierId == utilisateurId;

    // ── Roles ───────────────────────────────────────────────────────

    /// <summary>The responsable names the trésorier (any member, possibly themself).</summary>
    public void NommerTresorier(Guid nouveau, string acteur)
    {
        if (nouveau == TresorierId) throw new InvalidOperationException("This person is already the treasurer.");
        var ancien = TresorierId;
        TresorierId = nouveau;
        Journaliser("TresorierNomme", acteur, new { Ancien = ancien, Nouveau = nouveau });
    }

    /// <summary>The platform administrator reassigns a role directly (dispute, absent member...).</summary>
    public void ReattribuerRole(RoleBureau role, Guid nouveau, string acteurAdmin)
    {
        var ancien = role == RoleBureau.Responsable ? ResponsableId : TresorierId;
        if (ancien == nouveau) throw new InvalidOperationException("This person already holds this role.");
        if (role == RoleBureau.Responsable) ResponsableId = nouveau; else TresorierId = nouveau;
        Journaliser("RoleReattribueParAdmin", acteurAdmin, new { Role = role.ToString(), Ancien = ancien, Nouveau = nouveau });
    }

    public void ModifierRegles(ReglesGouvernance regles, string acteur)
    {
        regles.Valider();
        Regles = regles;
        Journaliser("ReglesGouvernanceModifiees", acteur, new { regles.ModeVote, regles.QuorumPourcent, regles.DureeVoteJours });
    }

    // ── Votes ───────────────────────────────────────────────────────

    /// <summary>
    /// Any member proposes a new responsable; every member at that moment is an elector. One open
    /// vote at a time per subject.
    /// </summary>
    public Vote ProposerChangementResponsable(Guid candidat, Guid proposePar, IReadOnlyCollection<Guid> electeurs, DateTime maintenant)
    {
        Actualiser(maintenant);
        if (!electeurs.Contains(proposePar)) throw new InvalidOperationException("Only a member can propose a vote.");
        if (!electeurs.Contains(candidat)) throw new InvalidOperationException("The candidate must be a member of the group.");
        if (candidat == ResponsableId) throw new InvalidOperationException("This person is already the responsable.");
        if (_votes.Any(v => v.Statut == StatutVote.Ouvert && v.Objet == ObjetVote.ChangementResponsable))
            throw new InvalidOperationException("A vote on the responsable is already open.");

        var vote = new Vote(ObjetVote.ChangementResponsable, candidat, proposePar, electeurs.Distinct().ToList(),
            Regles.ModeVote, Regles.QuorumPourcent, maintenant, maintenant.AddDays(Regles.DureeVoteJours));
        _votes.Add(vote);
        Journaliser("VoteOuvert", proposePar.ToString(), new
        {
            VoteId = vote.Id, Objet = vote.Objet.ToString(), Candidat = candidat, Electeurs = vote.Electeurs.Count,
            Mode = vote.Mode.ToString(), vote.QuorumPourcent, vote.FermeLe,
        });
        return vote;
    }

    /// <summary>An elector casts or changes their ballot while the vote is open.</summary>
    public void Voter(Guid voteId, Guid votant, bool pour, DateTime maintenant)
    {
        Actualiser(maintenant);
        var vote = _votes.FirstOrDefault(v => v.Id == voteId) ?? throw new InvalidOperationException("Vote not found.");
        if (vote.Statut != StatutVote.Ouvert) throw new InvalidOperationException("This vote is closed.");
        if (!vote.Electeurs.Contains(votant)) throw new InvalidOperationException("Only the members at the opening of the vote can vote.");
        vote.Enregistrer(votant, pour, maintenant);
        Journaliser("BulletinDepose", votant.ToString(), new { VoteId = voteId, Pour = pour });
        Conclure(vote, maintenant, fin: false);
    }

    /// <summary>Closes the votes whose deadline has passed. Called on every read and write.</summary>
    public void Actualiser(DateTime maintenant)
    {
        foreach (var vote in _votes.Where(v => v.Statut == StatutVote.Ouvert && v.FermeLe <= maintenant).ToList())
            Conclure(vote, maintenant, fin: true);
    }

    private void Conclure(Vote vote, DateTime maintenant, bool fin)
    {
        var issue = vote.Issue(fin);
        if (issue is null) return;
        vote.Clore(issue.Value, maintenant);
        Journaliser(issue == StatutVote.Adopte ? "VoteAdopte" : "VoteRejete", "systeme", new
        {
            VoteId = vote.Id, Objet = vote.Objet.ToString(), Candidat = vote.CandidatId, vote.Pour, vote.Contre,
            Electeurs = vote.Electeurs.Count,
        });
        if (issue == StatutVote.Adopte && vote.Objet == ObjetVote.ChangementResponsable)
        {
            var ancien = ResponsableId;
            ResponsableId = vote.CandidatId;
            Journaliser("ResponsableChange", "systeme", new { Ancien = ancien, Nouveau = vote.CandidatId, VoteId = vote.Id });
        }
    }

    // ── Séances ─────────────────────────────────────────────────────

    /// <summary>Any member writes the report of a séance: who attended, the agenda, what was decided.</summary>
    public Seance RedigerSeance(DateTime date, string titre, string? lieu, IEnumerable<string> presents, string? ordreDuJour,
        string compteRendu, IEnumerable<string> decisions, Guid auteur, DateTime maintenant)
    {
        var seance = new Seance(date, titre, lieu, presents, ordreDuJour, compteRendu, decisions, auteur, maintenant);
        _seances.Add(seance);
        Journaliser("SeanceRedigee", auteur.ToString(), new
        {
            SeanceId = seance.Id, seance.Date, seance.Titre, Decisions = seance.Decisions,
        });
        return seance;
    }

    // ── Journal ─────────────────────────────────────────────────────

    public bool VerifierJournal()
    {
        var precedent = JournalBureau.GenesisHash;
        foreach (var e in _journal.OrderBy(e => e.Sequence))
        {
            if (!e.Verifier(precedent)) return false;
            precedent = e.HashCourant;
        }
        return true;
    }

    private void Journaliser(string action, string acteur, object payload)
    {
        var dernier = _journal.OrderBy(e => e.Sequence).LastOrDefault();
        _journal.Add(new JournalBureau((dernier?.Sequence ?? 0) + 1, action, acteur,
            JsonSerializer.Serialize(payload), dernier?.HashCourant ?? JournalBureau.GenesisHash));
    }
}

/// <summary>A vote of the members. Electors are frozen at opening; each one has one ballot they can change.</summary>
public sealed class Vote : Entity<Guid>
{
    private readonly List<Bulletin> _bulletins = [];

    public ObjetVote Objet { get; private set; }
    public Guid CandidatId { get; private set; }
    public Guid ProposeParId { get; private set; }
    public List<Guid> Electeurs { get; private set; } = [];
    public ModeVote Mode { get; private set; }
    public int QuorumPourcent { get; private set; }
    public DateTime OuvertLe { get; private set; }
    public DateTime FermeLe { get; private set; }
    public StatutVote Statut { get; private set; }
    public DateTime? ClosLe { get; private set; }
    public IReadOnlyCollection<Bulletin> Bulletins => _bulletins.AsReadOnly();

    public int Pour => _bulletins.Count(b => b.Pour);
    public int Contre => _bulletins.Count(b => !b.Pour);

    private Vote() { }

    internal Vote(ObjetVote objet, Guid candidat, Guid proposePar, List<Guid> electeurs, ModeVote mode, int quorum,
        DateTime ouvert, DateTime ferme) : base(Guid.NewGuid())
    {
        Objet = objet;
        CandidatId = candidat;
        ProposeParId = proposePar;
        Electeurs = electeurs;
        Mode = mode;
        QuorumPourcent = quorum;
        OuvertLe = ouvert;
        FermeLe = ferme;
        Statut = StatutVote.Ouvert;
    }

    internal void Enregistrer(Guid votant, bool pour, DateTime maintenant)
    {
        _bulletins.RemoveAll(b => b.UtilisateurId == votant);
        _bulletins.Add(new Bulletin(votant, pour, maintenant));
    }

    /// <summary>
    /// Outcome if it is already certain (or, at the deadline, the final one); null while undecided.
    /// </summary>
    internal StatutVote? Issue(bool fin)
    {
        int n = Electeurs.Count, pour = Pour, contre = Contre, restants = n - pour - contre;
        switch (Mode)
        {
            case ModeVote.MajoriteDesMembres:
                if (pour * 2 > n) return StatutVote.Adopte;
                if ((pour + restants) * 2 <= n || fin) return StatutVote.Rejete;
                return null;
            case ModeVote.DeuxTiersDesMembres:
                if (pour * 3 >= n * 2) return StatutVote.Adopte;
                if ((pour + restants) * 3 < n * 2 || fin) return StatutVote.Rejete;
                return null;
            default:
                // An absolute majority of yes (or of no) settles it whatever the remaining ballots.
                if (pour * 2 > n) return StatutVote.Adopte;
                if (contre * 2 >= n) return StatutVote.Rejete;
                if (restants == 0 || fin)
                {
                    var quorumAtteint = (pour + contre) * 100 >= QuorumPourcent * n;
                    return quorumAtteint && pour > contre ? StatutVote.Adopte : StatutVote.Rejete;
                }
                return null;
        }
    }

    internal void Clore(StatutVote issue, DateTime maintenant)
    {
        Statut = issue;
        ClosLe = maintenant;
    }
}

public sealed class Bulletin : Entity<Guid>
{
    public Guid UtilisateurId { get; private set; }
    public bool Pour { get; private set; }
    public DateTime Le { get; private set; }

    private Bulletin() { }

    internal Bulletin(Guid utilisateurId, bool pour, DateTime le) : base(Guid.NewGuid())
    {
        UtilisateurId = utilisateurId;
        Pour = pour;
        Le = le;
    }
}

/// <summary>Report of a séance (meeting): attendance, agenda, minutes and the decisions taken.</summary>
public sealed class Seance : Entity<Guid>
{
    public DateTime Date { get; private set; }
    public string Titre { get; private set; } = string.Empty;
    public string? Lieu { get; private set; }
    public List<string> Presents { get; private set; } = [];
    public string? OrdreDuJour { get; private set; }
    public string CompteRendu { get; private set; } = string.Empty;
    public List<string> Decisions { get; private set; } = [];
    public Guid AuteurId { get; private set; }
    public DateTime RedigeeLe { get; private set; }

    private Seance() { }

    internal Seance(DateTime date, string titre, string? lieu, IEnumerable<string> presents, string? ordreDuJour,
        string compteRendu, IEnumerable<string> decisions, Guid auteur, DateTime maintenant) : base(Guid.NewGuid())
    {
        if (string.IsNullOrWhiteSpace(titre)) throw new ArgumentException("A séance needs a title.", nameof(titre));
        if (string.IsNullOrWhiteSpace(compteRendu)) throw new ArgumentException("The report must not be empty.", nameof(compteRendu));
        if (date.Date > maintenant.Date) throw new InvalidOperationException("A séance report cannot be dated in the future.");
        Date = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified); // calendar date, no time zone
        Titre = titre.Trim();
        Lieu = string.IsNullOrWhiteSpace(lieu) ? null : lieu.Trim();
        Presents = presents.Select(p => p.Trim()).Where(p => p.Length > 0).Distinct().ToList();
        OrdreDuJour = string.IsNullOrWhiteSpace(ordreDuJour) ? null : ordreDuJour.Trim();
        CompteRendu = compteRendu.Trim();
        Decisions = decisions.Select(d => d.Trim()).Where(d => d.Length > 0).ToList();
        AuteurId = auteur;
        RedigeeLe = maintenant;
    }
}

/// <summary>Append-only, SHA-256 chained journal of a group's governance.</summary>
public sealed class JournalBureau : Entity<Guid>
{
    public static readonly string GenesisHash = Sha256("GENESIS_BUREAU");

    public int Sequence { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string Acteur { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public DateTime Horodatage { get; private set; }
    public string HashPrecedent { get; private set; } = string.Empty;
    public string HashCourant { get; private set; } = string.Empty;

    private JournalBureau() { }

    internal JournalBureau(int sequence, string action, string acteur, string payload, string hashPrecedent) : base(Guid.NewGuid())
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

public interface IBureauRepository
{
    Task<Bureau?> GetAsync(Guid groupeId, CancellationToken cancellationToken = default);
    Task AddAsync(Bureau bureau, CancellationToken cancellationToken = default);
}
