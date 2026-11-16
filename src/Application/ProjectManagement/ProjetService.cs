namespace Application.ProjectManagement;

using Domain.Common;
using Domain.IdentityManagement.Repositories;
using Domain.IdentityManagement.ValueObjects;
using Domain.ProjectManagement;
using Domain.TontineManagement.ValueObjects;

// ── DTOs (camelCase JSON, consumed by the dashboard and the mobile app) ─────

/// <summary>Configurable rules (JSON). Missing fields take the defaults agreed with the client.</summary>
public sealed record ReglesProjetDto(
    bool PaiementPartielAutorise = true,
    string TropPercu = "Reporter",
    bool HausseRetroactive = true,
    bool HausseSoumiseAValidation = false,
    bool BaisseAutorisee = true,
    bool BaisseSoumiseAValidation = true,
    bool BaisseRetroactive = false,
    bool RattrapageAdhesionTardive = false)
{
    public static ReglesProjetDto From(ReglesProjet r) => new(r.PaiementPartielAutorise, r.TropPercu.ToString(),
        r.HausseRetroactive, r.HausseSoumiseAValidation, r.BaisseAutorisee, r.BaisseSoumiseAValidation, r.BaisseRetroactive,
        r.RattrapageAdhesionTardive);

    public ReglesProjet ToDomain() => new()
    {
        PaiementPartielAutorise = PaiementPartielAutorise,
        TropPercu = Enum.TryParse<TraitementTropPercu>(TropPercu, true, out var t)
            ? t : throw new ArgumentException("TropPercu must be Reporter or Refuser."),
        HausseRetroactive = HausseRetroactive,
        HausseSoumiseAValidation = HausseSoumiseAValidation,
        BaisseAutorisee = BaisseAutorisee,
        BaisseSoumiseAValidation = BaisseSoumiseAValidation,
        BaisseRetroactive = BaisseRetroactive,
        RattrapageAdhesionTardive = RattrapageAdhesionTardive,
    };
}

public sealed record CreerProjetRequest(string Nom, string? Description, decimal Objectif, List<decimal> Paliers,
    string Periodicite, DateTime DateDebut, int NombreEcheances, ReglesProjetDto? Regles = null);

public sealed record ProjetResumeDto(Guid Id, string Nom, string Statut, string Role, decimal Objectif, decimal TotalCollecte,
    double Progression, int Participants, decimal? MonPalier, decimal? MonResteDu, DateTime DateDebut);

public sealed record EcheanceDto(int Numero, DateTime Date, decimal Palier, decimal Paye, decimal Reste, string Statut);

public sealed record ParticipantDto(Guid Id, string Nom, decimal PalierActuel, decimal? PalierDemande, decimal TotalPaye,
    decimal EngagementTotal, decimal ResteDuExigible, decimal Avance, DateTime DateAdhesion, IReadOnlyList<EcheanceDto>? Echeances);

public sealed record PaiementProjetDto(Guid Id, Guid ParticipantId, string ParticipantNom, decimal Montant, string Preuve,
    string? Reference, string Statut, DateTime SaisiLe, DateTime? DecideLe, string? Motif);

public sealed record DepenseDto(Guid Id, string Libelle, decimal Montant, string Justificatif, string Statut,
    DateTime ProposeeLe, DateTime? DecideeLe, string? Motif);

public sealed record JournalDto(int Sequence, string Action, string Acteur, DateTime Horodatage, string Hash);

public sealed record ProjetDetailsDto(
    Guid Id, string Nom, string? Description, decimal Objectif, string Devise, IReadOnlyList<decimal> Paliers,
    string Periodicite, DateTime DateDebut, int NombreEcheances, IReadOnlyList<DateTime> Echeancier, string Statut,
    string Role, Guid? MonParticipantId, decimal TotalCollecte, decimal TotalDepense, decimal Solde, double Progression,
    bool? JournalValide, DateTime? CodeInvitationExpireLe, ReglesProjetDto Regles,
    IReadOnlyList<ParticipantDto> Participants, IReadOnlyList<PaiementProjetDto> Paiements, IReadOnlyList<DepenseDto> Depenses,
    IReadOnlyList<JournalDto>? Journal);

public sealed record ProjetApercuDto(string Nom, string? Description, decimal Objectif, IReadOnlyList<decimal> Paliers,
    string Periodicite, DateTime DateDebut, int NombreEcheances, int Participants, ReglesProjetDto Regles);

public sealed record ParticipantSansCompteRequest(string Nom, decimal Palier, DateTime? DateAdhesion);

/// <summary>Bulk import from a previous register (Excel, notebook). References link payments to participants.</summary>
public sealed record ImportProjetRequest(List<ImportParticipant> Participants, List<ImportPaiement> Paiements);
/// <summary>A register line. <c>Telephone</c>, when it matches an account, links the participant to it.</summary>
public sealed record ImportParticipant(string Reference, string Nom, decimal Palier, DateTime DateAdhesion, string? Telephone = null);
public sealed record ImportPaiement(string Reference, decimal Montant, DateTime Date, string? Moyen);
public sealed record ImportResultat(int Participants, int Paiements, decimal Montant, ProjetDetailsDto Projet);

public sealed class ProjetIntrouvableException(string message = "Project not found.") : Exception(message);
public sealed class AccesProjetRefuseException(string message = "You do not have access to this project.") : Exception(message);

/// <summary>
/// Use cases of the collective project. Every call checks the caller's role in the project:
/// the gestionnaire manages everything; a participant reads the project and manages their own tier.
/// Business errors surface as <see cref="InvalidOperationException"/> / <see cref="ArgumentException"/>.
/// </summary>
public sealed class ProjetService
{
    private readonly IProjetRepository _projets;
    private readonly IUnitOfWork _uow;
    private readonly IUtilisateurRepository _utilisateurs;

    public ProjetService(IProjetRepository projets, IUnitOfWork uow, IUtilisateurRepository utilisateurs)
    {
        _projets = projets;
        _uow = uow;
        _utilisateurs = utilisateurs;
    }

    private static DateTime Maintenant => DateTime.UtcNow;

    private enum Role { Aucun, Participant, Gestionnaire }

    private static Role RoleDe(Projet p, Guid userId, bool admin) =>
        admin || p.GestionnaireId.Value == userId ? Role.Gestionnaire
        : p.Participants.Any(x => x.UtilisateurId?.Value == userId) ? Role.Participant
        : Role.Aucun;

    private async Task<Projet> Charger(Guid projetId, Guid userId, bool admin, Role requis, CancellationToken ct)
    {
        var p = await _projets.GetByIdAsync(ProjetId.From(projetId), ct) ?? throw new ProjetIntrouvableException();
        if (RoleDe(p, userId, admin) < requis) throw new AccesProjetRefuseException();
        return p;
    }

    private async Task<ProjetDetailsDto> Enregistrer(Projet p, Guid userId, bool admin, CancellationToken ct)
    {
        await _uow.SaveChangesAsync(ct);
        return Details(p, userId, admin);
    }

    // ── Queries ─────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ProjetResumeDto>> MesProjetsAsync(Guid userId, CancellationToken ct)
    {
        var projets = await _projets.GetByUtilisateurAsync(UtilisateurId.From(userId), ct);
        return projets.Select(p =>
        {
            var moi = p.Participants.FirstOrDefault(x => x.UtilisateurId?.Value == userId);
            var s = moi is null ? null : p.Situation(moi.Id, Maintenant);
            return new ProjetResumeDto(p.Id.Value, p.Nom, p.Statut.ToString(),
                p.GestionnaireId.Value == userId ? "gestionnaire" : "participant",
                p.Objectif, p.TotalCollecte, Progression(p), p.Participants.Count, s?.PalierActuel, s?.ResteDuExigible, p.DateDebut);
        }).ToList();
    }

    public async Task<ProjetDetailsDto> DetailsAsync(Guid projetId, Guid userId, bool admin, CancellationToken ct) =>
        Details(await Charger(projetId, userId, admin, Role.Participant, ct), userId, admin);

    private static double Progression(Projet p) => p.Objectif == 0 ? 0 : Math.Round((double)(p.TotalCollecte / p.Objectif) * 100, 1);

    private static ProjetDetailsDto Details(Projet p, Guid userId, bool admin)
    {
        var role = RoleDe(p, userId, admin);
        var gestion = role == Role.Gestionnaire;
        var moi = p.Participants.FirstOrDefault(x => x.UtilisateurId?.Value == userId);
        var noms = p.Participants.ToDictionary(x => x.Id, x => x.Nom);

        var participants = p.Participants.OrderBy(x => x.DateAdhesion).Select(x =>
        {
            var s = p.Situation(x.Id, Maintenant);
            // Everyone sees the group's standing; the instalment detail is for the gestionnaire and the participant.
            var detail = gestion || x.Id == moi?.Id
                ? s.Echeances.Select(e => new EcheanceDto(e.Numero, e.Date, e.Palier, e.Paye, e.Reste, StatutEcheanceTexte(e.Statut))).ToList()
                : null;
            return new ParticipantDto(x.Id, x.Nom, s.PalierActuel, s.PalierDemande, s.TotalPaye, s.EngagementTotal,
                s.ResteDuExigible, s.Avance, x.DateAdhesion, detail);
        }).ToList();

        var paiements = p.Paiements
            .Where(x => gestion || x.ParticipantId == moi?.Id)
            .OrderByDescending(x => x.SaisiLe)
            .Select(x => new PaiementProjetDto(x.Id, x.ParticipantId, noms.GetValueOrDefault(x.ParticipantId, "?"), x.Montant,
                x.Preuve, x.Reference, StatutTexte(x.Statut), x.SaisiLe, x.DecideLe, x.Motif))
            .ToList();

        var depenses = p.Depenses.OrderByDescending(d => d.ProposeeLe)
            .Select(d => new DepenseDto(d.Id, d.Libelle, d.Montant, d.Justificatif, StatutTexte(d.Statut), d.ProposeeLe, d.DecideeLe, d.Motif))
            .ToList();

        var journal = gestion
            ? p.Journal.OrderByDescending(j => j.Sequence).Take(50)
                .Select(j => new JournalDto(j.Sequence, j.Action, j.Acteur, j.Horodatage, j.HashCourant)).ToList()
            : null;

        return new ProjetDetailsDto(p.Id.Value, p.Nom, p.Description, p.Objectif, p.Devise, p.Paliers, p.Periodicite.ToString(),
            p.DateDebut, p.NombreEcheances, p.Echeancier(), p.Statut.ToString(), gestion ? "gestionnaire" : "participant", moi?.Id,
            p.TotalCollecte, p.TotalDepense, p.Solde, Progression(p), gestion ? p.VerifierJournal() : null,
            gestion ? p.CodeInvitationExpireLe : null, ReglesProjetDto.From(p.Regles), participants, paiements, depenses, journal);
    }

    private static string StatutTexte(StatutOperation s) => s switch
    {
        StatutOperation.Validee => "valide",
        StatutOperation.Rejetee => "rejete",
        _ => "en_attente",
    };

    private static string StatutEcheanceTexte(StatutEcheance s) => s switch
    {
        StatutEcheance.Soldee => "soldee",
        StatutEcheance.Partielle => "partielle",
        StatutEcheance.EnRetard => "en_retard",
        _ => "a_venir",
    };

    // ── Commands ────────────────────────────────────────────────────

    public async Task<ProjetDetailsDto> CreerAsync(CreerProjetRequest r, Guid userId, CancellationToken ct)
    {
        if (!Enum.TryParse<TontinePeriodicity>(r.Periodicite, true, out var periodicite))
            throw new ArgumentException("Periodicity must be Weekly, Biweekly or Monthly.");
        var p = Projet.Creer(r.Nom, r.Description, r.Objectif, r.Paliers, periodicite, r.DateDebut, r.NombreEcheances,
            UtilisateurId.From(userId), (r.Regles ?? new ReglesProjetDto()).ToDomain());
        await _projets.AddAsync(p, ct);
        return await Enregistrer(p, userId, false, ct);
    }

    public async Task<ProjetDetailsDto> OuvrirAsync(Guid id, Guid userId, bool admin, CancellationToken ct)
    {
        var p = await Charger(id, userId, admin, Role.Gestionnaire, ct);
        p.Ouvrir(userId.ToString());
        return await Enregistrer(p, userId, admin, ct);
    }

    public async Task<ProjetDetailsDto> CloreAsync(Guid id, Guid userId, bool admin, CancellationToken ct)
    {
        var p = await Charger(id, userId, admin, Role.Gestionnaire, ct);
        p.Clore(userId.ToString());
        return await Enregistrer(p, userId, admin, ct);
    }

    public async Task<(string Code, DateTime ExpireLe)> GenererCodeAsync(Guid id, Guid userId, bool admin, int jours, CancellationToken ct)
    {
        var p = await Charger(id, userId, admin, Role.Gestionnaire, ct);
        var code = p.GenererCodeInvitation(jours, userId.ToString());
        await _uow.SaveChangesAsync(ct);
        return (code, p.CodeInvitationExpireLe!.Value);
    }

    /// <summary>What a person sees before joining with a code: the project and the tiers to choose from.</summary>
    public async Task<ProjetApercuDto> ApercuAsync(string code, CancellationToken ct)
    {
        var hash = Domain.TontineManagement.ValueObjects.InvitationCode.ComputeHash(code.Trim().ToUpperInvariant());
        var p = await _projets.GetByCodeHashAsync(hash, ct);
        if (p is null || !p.CodeCorrespond(code)) throw new InvalidOperationException("Invalid or expired invitation code.");
        return new ProjetApercuDto(p.Nom, p.Description, p.Objectif, p.Paliers, p.Periodicite.ToString(), p.DateDebut,
            p.NombreEcheances, p.Participants.Count, ReglesProjetDto.From(p.Regles));
    }

    public async Task<ProjetDetailsDto> RejoindreAsync(string code, string nom, decimal palier, Guid userId, CancellationToken ct)
    {
        var hash = Domain.TontineManagement.ValueObjects.InvitationCode.ComputeHash(code.Trim().ToUpperInvariant());
        var p = await _projets.GetByCodeHashAsync(hash, ct) ?? throw new InvalidOperationException("Invalid or expired invitation code.");
        p.Rejoindre(code, nom, UtilisateurId.From(userId), palier, Maintenant);
        return await Enregistrer(p, userId, false, ct);
    }

    public async Task<ProjetDetailsDto> AjouterParticipantAsync(Guid id, ParticipantSansCompteRequest r, Guid userId, bool admin, CancellationToken ct)
    {
        var p = await Charger(id, userId, admin, Role.Gestionnaire, ct);
        p.AjouterParticipant(r.Nom, r.Palier, r.DateAdhesion ?? Maintenant, userId.ToString());
        return await Enregistrer(p, userId, admin, ct);
    }

    /// <summary>Imports participants and their historical payments in one transaction (all or nothing).</summary>
    public async Task<ImportResultat> ImporterAsync(Guid id, ImportProjetRequest r, Guid userId, bool admin, CancellationToken ct)
    {
        var p = await Charger(id, userId, admin, Role.Gestionnaire, ct);
        var acteur = userId.ToString();
        var refs = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var x in r.Participants)
        {
            var existant = p.Participants.FirstOrDefault(y => string.Equals(y.Nom, x.Nom.Trim(), StringComparison.OrdinalIgnoreCase));
            var pid = existant?.Id ?? p.AjouterParticipant(x.Nom, x.Palier, DateTime.SpecifyKind(x.DateAdhesion, DateTimeKind.Utc), acteur).Id;
            refs[x.Reference] = pid;
            if (!string.IsNullOrWhiteSpace(x.Telephone))
            {
                var compte = await _utilisateurs.GetByTelephoneAsync(x.Telephone.Trim(), ct)
                    ?? throw new InvalidOperationException($"No account with phone number '{x.Telephone}' (participant '{x.Nom}').");
                p.LierCompte(pid, compte.Id, acteur);
            }
        }
        foreach (var x in r.Paiements.OrderBy(x => x.Date))
        {
            if (!refs.TryGetValue(x.Reference, out var pid))
                throw new InvalidOperationException($"Payment for unknown participant reference '{x.Reference}'.");
            p.ImporterPaiement(pid, x.Montant, DateTime.SpecifyKind(x.Date, DateTimeKind.Utc), x.Moyen ?? "non précisé", acteur);
        }
        var details = await Enregistrer(p, userId, admin, ct);
        return new ImportResultat(r.Participants.Count, r.Paiements.Count, r.Paiements.Sum(x => x.Montant), details);
    }

    /// <summary>The participant changes their own tier, according to the project's rules.</summary>
    public async Task<(bool Applique, ProjetDetailsDto Projet)> ChangerMonPalierAsync(Guid id, decimal palier, Guid userId, CancellationToken ct)
    {
        var p = await Charger(id, userId, false, Role.Participant, ct);
        var moi = p.Participants.FirstOrDefault(x => x.UtilisateurId?.Value == userId)
            ?? throw new AccesProjetRefuseException("Only a participant can change their tier.");
        var applique = p.ChangerPalier(moi.Id, palier, userId.ToString(), Maintenant);
        return (applique, await Enregistrer(p, userId, false, ct));
    }

    public async Task<ProjetDetailsDto> DeciderChangementPalierAsync(Guid id, Guid participantId, bool approuver, Guid userId, bool admin, CancellationToken ct)
    {
        var p = await Charger(id, userId, admin, Role.Gestionnaire, ct);
        p.DeciderChangementPalier(participantId, approuver, userId.ToString(), Maintenant);
        return await Enregistrer(p, userId, admin, ct);
    }

    public async Task<ProjetDetailsDto> ModifierReglesAsync(Guid id, ReglesProjetDto regles, Guid userId, bool admin, CancellationToken ct)
    {
        var p = await Charger(id, userId, admin, Role.Gestionnaire, ct);
        p.ModifierRegles(regles.ToDomain(), userId.ToString());
        return await Enregistrer(p, userId, admin, ct);
    }

    public async Task<ProjetDetailsDto> EnregistrerPaiementAsync(Guid id, Guid participantId, decimal montant, string preuve, Guid userId, bool admin, CancellationToken ct)
    {
        var p = await Charger(id, userId, admin, Role.Gestionnaire, ct);
        p.EnregistrerPaiement(participantId, montant, preuve, userId.ToString(), Maintenant);
        return await Enregistrer(p, userId, admin, ct);
    }

    public async Task<ProjetDetailsDto> DeciderPaiementAsync(Guid id, Guid paiementId, bool valider, string? motif, Guid userId, bool admin, CancellationToken ct)
    {
        var p = await Charger(id, userId, admin, Role.Gestionnaire, ct);
        var detail = valider ? $"CASH-{DateTime.UtcNow:yyyyMMddHHmmss}-{paiementId.ToString()[..8]}" : motif;
        p.DeciderPaiement(paiementId, valider, userId.ToString(), detail, Maintenant);
        return await Enregistrer(p, userId, admin, ct);
    }

    public async Task<ProjetDetailsDto> ProposerDepenseAsync(Guid id, string libelle, decimal montant, string justificatif, Guid userId, bool admin, CancellationToken ct)
    {
        var p = await Charger(id, userId, admin, Role.Gestionnaire, ct);
        p.ProposerDepense(libelle, montant, justificatif, userId.ToString(), Maintenant);
        return await Enregistrer(p, userId, admin, ct);
    }

    public async Task<ProjetDetailsDto> DeciderDepenseAsync(Guid id, Guid depenseId, bool valider, string? motif, Guid userId, bool admin, CancellationToken ct)
    {
        var p = await Charger(id, userId, admin, Role.Gestionnaire, ct);
        p.DeciderDepense(depenseId, valider, userId.ToString(), motif, Maintenant);
        return await Enregistrer(p, userId, admin, ct);
    }
}
