namespace Application.Gouvernance;

using System.Globalization;
using System.Text.Json;
using Application.ProjectManagement;
using Domain.Common;
using Domain.Gouvernance;
using Domain.ProjectManagement;
using Domain.TontineManagement.Repositories;
using Domain.TontineManagement.ValueObjects;

// ── DTOs ────────────────────────────────────────────────────────────

public sealed record ReglesGouvernanceDto(string ModeVote = "MajoriteDesVotants", int QuorumPourcent = 50, int DureeVoteJours = 7)
{
    public static ReglesGouvernanceDto From(ReglesGouvernance r) => new(r.ModeVote.ToString(), r.QuorumPourcent, r.DureeVoteJours);

    public ReglesGouvernance ToDomain() => new()
    {
        ModeVote = Enum.TryParse<ModeVote>(ModeVote, true, out var m)
            ? m : throw new ArgumentException("ModeVote must be MajoriteDesVotants, MajoriteDesMembres or DeuxTiersDesMembres."),
        QuorumPourcent = QuorumPourcent,
        DureeVoteJours = DureeVoteJours,
    };
}

public sealed record MembreGroupeDto(Guid UtilisateurId, string Nom, bool Actif, IReadOnlyList<string> Roles);

public sealed record VoteDto(Guid Id, string Objet, Guid CandidatId, string CandidatNom, string ProposeParNom, DateTime OuvertLe,
    DateTime FermeLe, string Statut, int Pour, int Contre, int Electeurs, bool? MonVote, bool PeutVoter, string Mode, int QuorumPourcent,
    DateTime? ClosLe);

public sealed record SeanceDto(Guid Id, DateTime Date, string Titre, string? Lieu, IReadOnlyList<string> Presents, string? OrdreDuJour,
    string CompteRendu, IReadOnlyList<string> Decisions, string AuteurNom, DateTime RedigeeLe);

public sealed record GouvernanceDto(string Type, Guid GroupeId, string GroupeNom, BureauDto Bureau, ReglesGouvernanceDto Regles,
    IReadOnlyList<string> MesRoles, IReadOnlyList<MembreGroupeDto> Membres, IReadOnlyList<VoteDto> Votes,
    IReadOnlyList<SeanceDto> Seances, bool JournalValide);

/// <summary>One line of the group's history, in plain French, whatever its source journal.</summary>
public sealed record EvenementDto(DateTime Horodatage, string Categorie, string Action, string Resume, string ActeurNom,
    Guid? ParticipantId, string Source, int Sequence);

public sealed record SeanceRequest(DateTime Date, string Titre, string? Lieu, List<string>? Presents, string? OrdreDuJour,
    string CompteRendu, List<string>? Decisions);

public sealed record UtilisateurRequest(Guid UtilisateurId);
public sealed record RoleRequest(string Role, Guid UtilisateurId);
public sealed record VoteRequest(bool Pour);

/// <summary>
/// Governance of a tontine or a collective project: roles, votes, séance reports and the readable
/// history of everything that happened. Every member of the group reads everything.
/// </summary>
public sealed class GouvernanceService
{
    private readonly IBureauRepository _bureaux;
    private readonly IProjetRepository _projets;
    private readonly ITontineRepository _tontines;
    private readonly IUnitOfWork _uow;
    private readonly Annuaire _annuaire;

    public GouvernanceService(IBureauRepository bureaux, IProjetRepository projets, ITontineRepository tontines, IUnitOfWork uow, Annuaire annuaire)
    {
        _bureaux = bureaux;
        _projets = projets;
        _tontines = tontines;
        _uow = uow;
        _annuaire = annuaire;
    }

    private static DateTime Maintenant => DateTime.UtcNow;

    private sealed record Membre(Guid UtilisateurId, string Nom, bool Actif);

    private sealed record Groupe(TypeGroupe Type, Guid Id, string Nom, Bureau Bureau, List<Membre> Membres, Projet? Projet)
    {
        public bool EstMembre(Guid u) => Membres.Any(m => m.UtilisateurId == u);
        public IReadOnlyCollection<Guid> Electeurs => Membres.Where(m => m.Actif).Select(m => m.UtilisateurId).ToList();
    }

    public static TypeGroupe TypeDepuis(string type) => type.ToLowerInvariant() switch
    {
        "projets" or "projet" => TypeGroupe.Projet,
        "tontines" or "tontine" => TypeGroupe.Tontine,
        _ => throw new ArgumentException("Group type must be 'projets' or 'tontines'."),
    };

    private async Task<Groupe> Charger(TypeGroupe type, Guid id, Guid userId, bool admin, CancellationToken ct)
    {
        Groupe g;
        if (type == TypeGroupe.Projet)
        {
            var p = await _projets.GetByIdAsync(ProjetId.From(id), ct) ?? throw new ProjetIntrouvableException();
            var b = await _bureaux.GetAsync(id, ct);
            if (b is null) { b = Bureau.Creer(id, TypeGroupe.Projet, p.GestionnaireId.Value); await _bureaux.AddAsync(b, ct); }
            var membres = p.Participants.Where(x => x.UtilisateurId is not null)
                .Select(x => new Membre(x.UtilisateurId!.Value, x.Nom, x.Statut == StatutParticipant.Actif)).ToList();
            g = new Groupe(type, id, p.Nom, b, membres, p);
        }
        else
        {
            var t = await _tontines.GetByIdAsync(TontineId.From(id), ct) ?? throw new ProjetIntrouvableException("Tontine not found.");
            var b = await _bureaux.GetAsync(id, ct);
            if (b is null) { b = Bureau.Creer(id, TypeGroupe.Tontine, t.GestionnaireId.Value); await _bureaux.AddAsync(b, ct); }
            var membres = t.Members.Where(m => m.UtilisateurId is not null)
                .Select(m => new Membre(m.UtilisateurId!.Value, m.Name, m.Statut == StatutMembre.Actif)).ToList();
            g = new Groupe(type, id, t.Name, b, membres, null);
        }
        // Bureau members who are not participants (e.g. the gestionnaire of the association) belong to the group too.
        var autres = new[] { g.Bureau.ResponsableId, g.Bureau.TresorierId }.Distinct().Where(u => !g.EstMembre(u)).ToList();
        if (autres.Count > 0)
        {
            var noms = await _annuaire.NomsAsync([], autres.Select(u => (string?)u.ToString()), ct);
            g.Membres.AddRange(autres.Select(u => new Membre(u, noms[u.ToString()], true)));
        }
        if (!admin && !g.EstMembre(userId)) throw new AccesProjetRefuseException("You are not a member of this group.");
        g.Bureau.Actualiser(Maintenant);
        return g;
    }

    private static void Exiger(bool condition, string message)
    {
        if (!condition) throw new AccesProjetRefuseException(message);
    }

    // ── Queries ─────────────────────────────────────────────────────

    public async Task<GouvernanceDto> ConsulterAsync(TypeGroupe type, Guid id, Guid userId, bool admin, CancellationToken ct)
    {
        var g = await Charger(type, id, userId, admin, ct);
        await _uow.SaveChangesAsync(ct); // votes past their deadline were just closed
        return await Dto(g, userId, admin, ct);
    }

    private async Task<GouvernanceDto> Dto(Groupe g, Guid userId, bool admin, CancellationToken ct)
    {
        var b = g.Bureau;
        var noms = await _annuaire.NomsAsync(g.Membres.Select(m => (m.UtilisateurId, m.Nom)),
            b.Journal.Select(j => j.Acteur).Concat(b.Seances.Select(s => s.AuteurId.ToString()))
                .Concat(b.Votes.SelectMany(v => new[] { v.CandidatId.ToString(), v.ProposeParId.ToString() })), ct);
        string Nom(Guid u) => _annuaire.Nom(noms, u.ToString());

        List<string> RolesDe(Guid u)
        {
            var r = new List<string>();
            if (b.EstResponsable(u)) r.Add("responsable");
            if (b.EstTresorier(u)) r.Add("tresorier");
            return r;
        }

        var mesRoles = RolesDe(userId);
        if (g.EstMembre(userId)) mesRoles.Add("membre");
        if (admin) mesRoles.Add("admin");

        var votes = b.Votes.OrderByDescending(v => v.OuvertLe).Select(v =>
        {
            var bulletin = v.Bulletins.FirstOrDefault(x => x.UtilisateurId == userId);
            return new VoteDto(v.Id, v.Objet.ToString(), v.CandidatId, Nom(v.CandidatId), Nom(v.ProposeParId), v.OuvertLe, v.FermeLe,
                v.Statut.ToString(), v.Pour, v.Contre, v.Electeurs.Count, bulletin?.Pour,
                v.Statut == StatutVote.Ouvert && v.Electeurs.Contains(userId), v.Mode.ToString(), v.QuorumPourcent, v.ClosLe);
        }).ToList();

        var seances = b.Seances.OrderByDescending(s => s.Date).ThenByDescending(s => s.RedigeeLe)
            .Select(s => new SeanceDto(s.Id, s.Date, s.Titre, s.Lieu, s.Presents, s.OrdreDuJour, s.CompteRendu, s.Decisions,
                Nom(s.AuteurId), s.RedigeeLe)).ToList();

        var membres = g.Membres.OrderBy(m => m.Nom).Select(m => new MembreGroupeDto(m.UtilisateurId, m.Nom, m.Actif, RolesDe(m.UtilisateurId))).ToList();

        return new GouvernanceDto(g.Type.ToString(), g.Id, g.Nom,
            new BureauDto(b.ResponsableId, Nom(b.ResponsableId), b.TresorierId, Nom(b.TresorierId)),
            ReglesGouvernanceDto.From(b.Regles), mesRoles, membres, votes, seances, b.VerifierJournal());
    }

    // ── Roles ───────────────────────────────────────────────────────

    public async Task<GouvernanceDto> NommerTresorierAsync(TypeGroupe type, Guid id, Guid tresorier, Guid userId, bool admin, CancellationToken ct)
    {
        var g = await Charger(type, id, userId, admin, ct);
        Exiger(admin || g.Bureau.EstResponsable(userId), "Only the responsable names the treasurer.");
        if (!g.EstMembre(tresorier)) throw new InvalidOperationException("The treasurer must be a member of the group.");
        g.Bureau.NommerTresorier(tresorier, userId.ToString());
        await _uow.SaveChangesAsync(ct);
        return await Dto(g, userId, admin, ct);
    }

    /// <summary>Platform administrator only: reassigns a role directly.</summary>
    public async Task<GouvernanceDto> ReattribuerRoleAsync(TypeGroupe type, Guid id, RoleRequest r, Guid userId, bool admin, CancellationToken ct)
    {
        Exiger(admin, "Only the platform administrator can reassign a role.");
        var g = await Charger(type, id, userId, admin, ct);
        if (!g.EstMembre(r.UtilisateurId)) throw new InvalidOperationException("The person must be a member of the group.");
        var role = Enum.TryParse<RoleBureau>(r.Role, true, out var x) ? x : throw new ArgumentException("Role must be Responsable or Tresorier.");
        g.Bureau.ReattribuerRole(role, r.UtilisateurId, userId.ToString());
        await _uow.SaveChangesAsync(ct);
        return await Dto(g, userId, admin, ct);
    }

    public async Task<GouvernanceDto> ModifierReglesAsync(TypeGroupe type, Guid id, ReglesGouvernanceDto r, Guid userId, bool admin, CancellationToken ct)
    {
        var g = await Charger(type, id, userId, admin, ct);
        Exiger(admin || g.Bureau.EstResponsable(userId), "Only the responsable can change the voting rules.");
        g.Bureau.ModifierRegles(r.ToDomain(), userId.ToString());
        await _uow.SaveChangesAsync(ct);
        return await Dto(g, userId, admin, ct);
    }

    // ── Votes ───────────────────────────────────────────────────────

    public async Task<GouvernanceDto> ProposerResponsableAsync(TypeGroupe type, Guid id, Guid candidat, Guid userId, bool admin, CancellationToken ct)
    {
        var g = await Charger(type, id, userId, admin, ct);
        g.Bureau.ProposerChangementResponsable(candidat, userId, g.Electeurs, Maintenant);
        await _uow.SaveChangesAsync(ct);
        return await Dto(g, userId, admin, ct);
    }

    public async Task<GouvernanceDto> VoterAsync(TypeGroupe type, Guid id, Guid voteId, bool pour, Guid userId, bool admin, CancellationToken ct)
    {
        var g = await Charger(type, id, userId, admin, ct);
        g.Bureau.Voter(voteId, userId, pour, Maintenant);
        await _uow.SaveChangesAsync(ct);
        return await Dto(g, userId, admin, ct);
    }

    // ── Séances ─────────────────────────────────────────────────────

    public async Task<GouvernanceDto> RedigerSeanceAsync(TypeGroupe type, Guid id, SeanceRequest r, Guid userId, bool admin, CancellationToken ct)
    {
        var g = await Charger(type, id, userId, admin, ct);
        Exiger(g.EstMembre(userId), "Only a member of the group can write a séance report.");
        g.Bureau.RedigerSeance(r.Date, r.Titre, r.Lieu, r.Presents ?? [], r.OrdreDuJour, r.CompteRendu, r.Decisions ?? [], userId, Maintenant);
        await _uow.SaveChangesAsync(ct);
        return await Dto(g, userId, admin, ct);
    }

    // ── History ─────────────────────────────────────────────────────

    /// <summary>
    /// Everything that happened in the group, newest first, in plain French: payments, tiers, expenses,
    /// members, roles, votes, séance decisions. Filters: category, a participant, free text.
    /// </summary>
    public async Task<IReadOnlyList<EvenementDto>> ChronologieAsync(TypeGroupe type, Guid id, string? categorie, Guid? participantId,
        string? recherche, Guid userId, bool admin, CancellationToken ct)
    {
        var g = await Charger(type, id, userId, admin, ct);
        var b = g.Bureau;
        var p = g.Projet;
        var acteurs = b.Journal.Select(j => j.Acteur).ToList();
        if (p is not null) acteurs.AddRange(p.Journal.Select(j => j.Acteur));
        var noms = await _annuaire.NomsAsync(g.Membres.Select(m => (m.UtilisateurId, m.Nom))
                .Concat(p?.Participants.Where(x => x.UtilisateurId is not null).Select(x => (x.UtilisateurId!.Value, x.Nom)) ?? []),
            acteurs.Concat(b.Votes.Select(v => v.CandidatId.ToString())), ct);
        string NomU(object? u) => _annuaire.Nom(noms, u?.ToString());

        var evenements = new List<EvenementDto>();
        if (p is not null)
        {
            var participants = p.Participants.ToDictionary(x => x.Id, x => x.Nom);
            var paiements = p.Paiements.ToDictionary(x => x.Id, x => x.ParticipantId);
            foreach (var j in p.Journal)
            {
                var d = JsonDocument.Parse(j.Payload).RootElement;
                Guid? pid = Guid(d, "ParticipantId") ?? (Guid(d, "PaiementId") is { } pay && paiements.TryGetValue(pay, out var x) ? x : null);
                var qui = pid is { } k && participants.TryGetValue(k, out var n) ? n : "";
                var (cat, texte) = ResumerProjet(j.Action, d, qui);
                evenements.Add(new EvenementDto(j.Horodatage, cat, j.Action, texte, NomU(j.Acteur), pid, "projet", j.Sequence));
            }
        }
        foreach (var j in b.Journal)
        {
            var d = JsonDocument.Parse(j.Payload).RootElement;
            var (cat, texte) = ResumerBureau(j.Action, d, NomU, b);
            evenements.Add(new EvenementDto(j.Horodatage, cat, j.Action, texte, NomU(j.Acteur), null, "bureau", j.Sequence));
        }

        IEnumerable<EvenementDto> r = evenements;
        if (!string.IsNullOrWhiteSpace(categorie)) r = r.Where(e => e.Categorie.Equals(categorie, StringComparison.OrdinalIgnoreCase));
        if (participantId is not null) r = r.Where(e => e.ParticipantId == participantId);
        if (!string.IsNullOrWhiteSpace(recherche))
        {
            var q = Simplifier(recherche);
            r = r.Where(e => Simplifier(e.Resume).Contains(q) || Simplifier(e.ActeurNom).Contains(q));
        }
        return r.OrderByDescending(e => e.Horodatage).ThenByDescending(e => e.Sequence).ToList();
    }

    private static string Simplifier(string s) =>
        new string(s.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();

    private static Guid? Guid(JsonElement d, string nom) =>
        d.ValueKind == JsonValueKind.Object && d.TryGetProperty(nom, out var v) && v.ValueKind == JsonValueKind.String
            && System.Guid.TryParse(v.GetString(), out var g) ? g : null;

    private static string Txt(JsonElement d, string nom) =>
        d.ValueKind == JsonValueKind.Object && d.TryGetProperty(nom, out var v)
            ? v.ValueKind switch { JsonValueKind.String => v.GetString() ?? "", JsonValueKind.Null => "", _ => v.ToString() }
            : "";

    private static string Fcfa(JsonElement d, string nom) =>
        decimal.TryParse(Txt(d, nom), NumberStyles.Number, CultureInfo.InvariantCulture, out var m)
            ? m.ToString("N0", CultureInfo.GetCultureInfo("fr-FR")).Replace(' ', ' ').Replace(' ', ' ') + " FCFA" : "";

    private static string Jour(JsonElement d, string nom) =>
        DateTime.TryParse(Txt(d, nom), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t)
            ? t.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("fr-FR")) : "";

    private static (string, string) ResumerProjet(string action, JsonElement d, string qui) => action switch
    {
        "ProjetCree" => ("projet", $"Projet créé : « {Txt(d, "Nom")} », objectif {Fcfa(d, "Objectif")}"),
        "ProjetOuvert" => ("projet", "Projet ouvert aux paiements"),
        "ProjetClos" => ("projet", $"Projet clos, solde {Fcfa(d, "Solde")}"),
        "ReglesModifiees" => ("projet", "Règles du projet modifiées"),
        "CodeInvitationGenere" => ("membres", "Nouveau code d'invitation"),
        "ParticipantAjoute" => ("membres", $"{qui} rejoint le projet au palier {Fcfa(d, "Palier")}"),
        "ParticipantInscrit" => ("membres", $"{qui} inscrit au palier {Fcfa(d, "Palier")}"),
        "CompteLie" => ("membres", $"{qui} relié à son compte"),
        "StatutParticipantChange" => ("membres", $"{qui} : statut {Txt(d, "Ancien")} → {Txt(d, "Nouveau")}" +
            (Txt(d, "Motif") is { Length: > 0 } m ? $" ({m})" : "")),
        "PaiementImporte" => ("paiements", $"{qui} : {Fcfa(d, "Montant")} le {Jour(d, "Date")} ({Txt(d, "Moyen")}), repris du registre"),
        "PaiementSaisi" => ("paiements", $"{qui} : paiement de {Fcfa(d, "Montant")} déclaré ({Txt(d, "Preuve")})"),
        "PaiementValide" => ("paiements", $"{qui} : paiement de {Fcfa(d, "Montant")} validé"),
        "PaiementRejete" => ("paiements", $"{qui} : paiement de {Fcfa(d, "Montant")} rejeté ({Txt(d, "Detail")})"),
        "ChangementPalierDemande" => ("paliers", $"{qui} demande à passer de {Fcfa(d, "Ancien")} à {Fcfa(d, "Nouveau")}"),
        "PalierAugmente" => ("paliers", $"{qui} passe de {Fcfa(d, "Ancien")} à {Fcfa(d, "Nouveau")}"),
        "PalierBaisse" => ("paliers", $"{qui} passe de {Fcfa(d, "Ancien")} à {Fcfa(d, "Nouveau")}"),
        "ChangementPalierApprouve" => ("paliers", $"{qui} : passage à {Fcfa(d, "Palier")} approuvé"),
        "ChangementPalierRefuse" => ("paliers", $"{qui} : passage à {Fcfa(d, "Palier")} refusé"),
        "DepenseProposee" => ("depenses", $"Dépense proposée : {Txt(d, "Libelle")}, {Fcfa(d, "Montant")}"),
        "DepenseValidee" => ("depenses", $"Dépense de {Fcfa(d, "Montant")} validée"),
        "DepenseRejetee" => ("depenses", $"Dépense de {Fcfa(d, "Montant")} rejetée ({Txt(d, "Motif")})"),
        _ => ("projet", action),
    };

    private static (string, string) ResumerBureau(string action, JsonElement d, Func<object?, string> nom, Bureau b) => action switch
    {
        "BureauConstitue" => ("gouvernance", $"Bureau constitué : {nom(Txt(d, "Responsable"))} responsable et trésorier"),
        "TresorierNomme" => ("gouvernance", $"{nom(Txt(d, "Nouveau"))} nommé trésorier (remplace {nom(Txt(d, "Ancien"))})"),
        "RoleReattribueParAdmin" => ("gouvernance", $"Administrateur : {nom(Txt(d, "Nouveau"))} devient {(Txt(d, "Role") == "Responsable" ? "responsable" : "trésorier")}"),
        "ReglesGouvernanceModifiees" => ("gouvernance", $"Règles de vote modifiées : {Txt(d, "ModeVote")}, quorum {Txt(d, "QuorumPourcent")} %, {Txt(d, "DureeVoteJours")} jours"),
        "VoteOuvert" => ("votes", $"Vote ouvert : {nom(Txt(d, "Candidat"))} comme responsable ({Txt(d, "Electeurs")} électeurs)"),
        "BulletinDepose" => ("votes", "Bulletin déposé"),
        "VoteAdopte" => ("votes", $"Vote adopté ({Txt(d, "Pour")} pour, {Txt(d, "Contre")} contre) : {nom(Txt(d, "Candidat"))} responsable"),
        "VoteRejete" => ("votes", $"Vote rejeté ({Txt(d, "Pour")} pour, {Txt(d, "Contre")} contre) pour {nom(Txt(d, "Candidat"))}"),
        "ResponsableChange" => ("gouvernance", $"{nom(Txt(d, "Nouveau"))} devient responsable (remplace {nom(Txt(d, "Ancien"))})"),
        "SeanceRedigee" => ("seances", $"Séance du {Jour(d, "Date")} : {Txt(d, "Titre")}" +
            (d.TryGetProperty("Decisions", out var dec) && dec.ValueKind == JsonValueKind.Array && dec.GetArrayLength() > 0
                ? " — décisions : " + string.Join(" ; ", dec.EnumerateArray().Select(x => x.GetString())) : "")),
        _ => ("gouvernance", action),
    };
}
