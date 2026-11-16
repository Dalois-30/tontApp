namespace Application.ProjectManagement;

using Domain.Common;
using Application.Gouvernance;
using Domain.Gouvernance;
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
    decimal EngagementTotal, decimal ResteDuExigible, decimal Avance, DateTime DateAdhesion, IReadOnlyList<EcheanceDto>? Echeances,
    Guid? UtilisateurId = null, string Statut = "Actif", string? Sexe = null);

public sealed record PaiementProjetDto(Guid Id, Guid ParticipantId, string ParticipantNom, decimal Montant, string Preuve,
    string? Reference, string Statut, DateTime SaisiLe, DateTime? DecideLe, string? Motif,
    string? SaisiParNom = null, string? DecideParNom = null);

public sealed record DepenseDto(Guid Id, string Libelle, decimal Montant, string Justificatif, string Statut,
    DateTime ProposeeLe, DateTime? DecideeLe, string? Motif);

public sealed record JournalDto(int Sequence, string Action, string Acteur, DateTime Horodatage, string Hash, string? ActeurNom = null);

/// <summary>Who holds the key roles of the group.</summary>
public sealed record BureauDto(Guid ResponsableId, string ResponsableNom, Guid TresorierId, string TresorierNom);

public sealed record ProjetDetailsDto(
    Guid Id, string Nom, string? Description, decimal Objectif, string Devise, IReadOnlyList<decimal> Paliers,
    string Periodicite, DateTime DateDebut, int NombreEcheances, IReadOnlyList<DateTime> Echeancier, string Statut,
    string Role, Guid? MonParticipantId, decimal TotalCollecte, decimal TotalDepense, decimal Solde, double Progression,
    bool? JournalValide, DateTime? CodeInvitationExpireLe, ReglesProjetDto Regles,
    IReadOnlyList<ParticipantDto> Participants, IReadOnlyList<PaiementProjetDto> Paiements, IReadOnlyList<DepenseDto> Depenses,
    IReadOnlyList<JournalDto>? Journal, BureauDto? Bureau = null, IReadOnlyList<string>? MesRoles = null);

public sealed record ProjetApercuDto(string Nom, string? Description, decimal Objectif, IReadOnlyList<decimal> Paliers,
    string Periodicite, DateTime DateDebut, int NombreEcheances, int Participants, ReglesProjetDto Regles);

public sealed record ParticipantSansCompteRequest(string Nom, decimal Palier, DateTime? DateAdhesion);

/// <summary>Bulk import from a previous register (Excel, notebook). References link payments to participants.</summary>
public sealed record ImportProjetRequest(List<ImportParticipant> Participants, List<ImportPaiement> Paiements);
/// <summary>A register line. <c>Telephone</c>, when it matches an account, links the participant to it.</summary>
public sealed record ImportParticipant(string Reference, string Nom, decimal Palier, DateTime DateAdhesion, string? Telephone = null,
    string? Sexe = null, string? Statut = null);
public sealed record ImportPaiement(string Reference, decimal Montant, DateTime Date, string? Moyen);
public sealed record ImportResultat(int Participants, int Paiements, decimal Montant, ProjetDetailsDto Projet);

public sealed class ProjetIntrouvableException(string message = "Project not found.") : Exception(message);
public sealed class AccesProjetRefuseException(string message = "You do not have access to this project.") : Exception(message);

public sealed record StatutParticipantRequest(string Statut, string? Motif);

/// <summary>
/// Use cases of the collective project. Rights follow the group's bureau (<see cref="Bureau"/>):
/// the responsable runs the project (opening, members, rules, expenses), the trésorier validates
/// (payments, tier changes, expenses), each participant declares their own payments and manages their
/// tier. Every member reads everything: all participants' standing, payments, expenses and journal.
/// Business errors surface as <see cref="InvalidOperationException"/> / <see cref="ArgumentException"/>.
/// </summary>
public sealed class ProjetService
{
    private readonly IProjetRepository _projets;
    private readonly IUnitOfWork _uow;
    private readonly IUtilisateurRepository _utilisateurs;
    private readonly IBureauRepository _bureaux;
    private readonly Annuaire _annuaire;

    public ProjetService(IProjetRepository projets, IUnitOfWork uow, IUtilisateurRepository utilisateurs,
        IBureauRepository bureaux, Annuaire annuaire)
    {
        _projets = projets;
        _uow = uow;
        _utilisateurs = utilisateurs;
        _bureaux = bureaux;
        _annuaire = annuaire;
    }

    private static DateTime Maintenant => DateTime.UtcNow;

    [Flags]
    private enum Droits { Aucun = 0, Participant = 1, Tresorier = 2, Responsable = 4, Admin = 8, Membre = Participant | Tresorier | Responsable | Admin }

    private static Droits DroitsDe(Projet p, Bureau b, Guid userId, bool admin)
    {
        var d = Droits.Aucun;
        if (admin) d |= Droits.Admin;
        // A vote past its deadline changes the roles now, without modifying the bureau (closing votes is
        // the governance use cases' job).
        var (responsable, tresorier) = b.RolesEffectifs(Maintenant);
        if (responsable == userId) d |= Droits.Responsable;
        if (tresorier == userId) d |= Droits.Tresorier;
        if (p.Participants.Any(x => x.UtilisateurId?.Value == userId)) d |= Droits.Participant;
        return d;
    }

    /// <summary>The group's bureau; created on first use with the project's creator in both roles.</summary>
    internal static async Task<Bureau> BureauDe(IBureauRepository bureaux, Projet p, CancellationToken ct)
    {
        var b = await bureaux.GetAsync(p.Id.Value, ct);
        if (b is not null) return b;
        b = Bureau.Creer(p.Id.Value, TypeGroupe.Projet, p.GestionnaireId.Value);
        await bureaux.AddAsync(b, ct);
        return b;
    }

    private sealed record Contexte(Projet Projet, Bureau Bureau, Droits Droits);

    /// <summary>Loads the project and checks that the caller holds one of the <paramref name="requis"/> rights.</summary>
    private async Task<Contexte> Charger(Guid projetId, Guid userId, bool admin, Droits requis, CancellationToken ct)
    {
        var p = await _projets.GetByIdAsync(ProjetId.From(projetId), ct) ?? throw new ProjetIntrouvableException();
        var b = await BureauDe(_bureaux, p, ct);
        var d = DroitsDe(p, b, userId, admin);
        if ((d & (requis | Droits.Admin)) == 0)
            throw new AccesProjetRefuseException(requis switch
            {
                Droits.Tresorier => "Only the treasurer can do this.",
                Droits.Responsable => "Only the responsable can do this.",
                _ => "You do not have access to this project.",
            });
        return new Contexte(p, b, d);
    }

    private async Task<ProjetDetailsDto> Enregistrer(Contexte c, Guid userId, CancellationToken ct)
    {
        await _uow.SaveChangesAsync(ct);
        return await Details(c, userId, ct);
    }

    // ── Queries ─────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ProjetResumeDto>> MesProjetsAsync(Guid userId, CancellationToken ct)
    {
        var projets = await _projets.GetByUtilisateurAsync(UtilisateurId.From(userId), ct);
        var resumes = new List<ProjetResumeDto>();
        foreach (var p in projets)
        {
            var b = await _bureaux.GetAsync(p.Id.Value, ct);
            var gere = b is null ? p.GestionnaireId.Value == userId : b.EstResponsable(userId) || b.EstTresorier(userId);
            var moi = p.Participants.FirstOrDefault(x => x.UtilisateurId?.Value == userId);
            var s = moi is null ? null : p.Situation(moi.Id, Maintenant);
            resumes.Add(new ProjetResumeDto(p.Id.Value, p.Nom, p.Statut.ToString(), gere ? "gestionnaire" : "participant",
                p.Objectif, p.TotalCollecte, Progression(p), p.Participants.Count, s?.PalierActuel, s?.ResteDuExigible, p.DateDebut));
        }
        return resumes;
    }

    public async Task<ProjetDetailsDto> DetailsAsync(Guid projetId, Guid userId, bool admin, CancellationToken ct) =>
        await Details(await Charger(projetId, userId, admin, Droits.Membre, ct), userId, ct);

    private static double Progression(Projet p) => p.Objectif == 0 ? 0 : Math.Round((double)(p.TotalCollecte / p.Objectif) * 100, 1);

    private async Task<ProjetDetailsDto> Details(Contexte c, Guid userId, CancellationToken ct)
    {
        var (p, b, d) = (c.Projet, c.Bureau, c.Droits);
        var gestion = (d & (Droits.Responsable | Droits.Tresorier | Droits.Admin)) != 0;
        var moi = p.Participants.FirstOrDefault(x => x.UtilisateurId?.Value == userId);
        var noms = p.Participants.ToDictionary(x => x.Id, x => x.Nom);
        var personnes = await _annuaire.NomsAsync(p, b, ct);
        string Nom(string? acteur) => _annuaire.Nom(personnes, acteur);

        // Everyone in the group sees everything: each participant's instalments and every payment.
        var participants = p.Participants.OrderBy(x => x.DateAdhesion).ThenBy(x => x.Nom).Select(x =>
        {
            var s = p.Situation(x.Id, Maintenant);
            var detail = s.Echeances.Select(e => new EcheanceDto(e.Numero, e.Date, e.Palier, e.Paye, e.Reste, StatutEcheanceTexte(e.Statut))).ToList();
            return new ParticipantDto(x.Id, x.Nom, s.PalierActuel, s.PalierDemande, s.TotalPaye, s.EngagementTotal,
                s.ResteDuExigible, s.Avance, x.DateAdhesion, detail, x.UtilisateurId?.Value, x.Statut.ToString(), x.Sexe);
        }).ToList();

        var paiements = p.Paiements
            .OrderByDescending(x => x.SaisiLe)
            .Select(x => new PaiementProjetDto(x.Id, x.ParticipantId, noms.GetValueOrDefault(x.ParticipantId, "?"), x.Montant,
                x.Preuve, x.Reference, StatutTexte(x.Statut), x.SaisiLe, x.DecideLe, x.Motif, Nom(x.SaisiPar), Nom(x.DecidePar)))
            .ToList();

        var depenses = p.Depenses.OrderByDescending(x => x.ProposeeLe)
            .Select(x => new DepenseDto(x.Id, x.Libelle, x.Montant, x.Justificatif, StatutTexte(x.Statut), x.ProposeeLe, x.DecideeLe, x.Motif))
            .ToList();

        var journal = p.Journal.OrderByDescending(j => j.Sequence).Take(100)
            .Select(j => new JournalDto(j.Sequence, j.Action, j.Acteur, j.Horodatage, j.HashCourant, Nom(j.Acteur))).ToList();

        var roles = new List<string>();
        if ((d & Droits.Responsable) != 0) roles.Add("responsable");
        if ((d & Droits.Tresorier) != 0) roles.Add("tresorier");
        if ((d & Droits.Participant) != 0) roles.Add("participant");
        if ((d & Droits.Admin) != 0) roles.Add("admin");

        return new ProjetDetailsDto(p.Id.Value, p.Nom, p.Description, p.Objectif, p.Devise, p.Paliers, p.Periodicite.ToString(),
            p.DateDebut, p.NombreEcheances, p.Echeancier(), p.Statut.ToString(), gestion ? "gestionnaire" : "participant", moi?.Id,
            p.TotalCollecte, p.TotalDepense, p.Solde, Progression(p), p.VerifierJournal() && b.VerifierJournal(),
            (d & (Droits.Responsable | Droits.Admin)) != 0 ? p.CodeInvitationExpireLe : null, ReglesProjetDto.From(p.Regles),
            participants, paiements, depenses, journal,
            new BureauDto(b.ResponsableId, Nom(b.ResponsableId.ToString()), b.TresorierId, Nom(b.TresorierId.ToString())), roles);
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

    // ── Commands: running the project (responsable) ─────────────────

    public async Task<ProjetDetailsDto> CreerAsync(CreerProjetRequest r, Guid userId, CancellationToken ct)
    {
        if (!Enum.TryParse<TontinePeriodicity>(r.Periodicite, true, out var periodicite))
            throw new ArgumentException("Periodicity must be Weekly, Biweekly or Monthly.");
        var p = Projet.Creer(r.Nom, r.Description, r.Objectif, r.Paliers, periodicite, r.DateDebut, r.NombreEcheances,
            UtilisateurId.From(userId), (r.Regles ?? new ReglesProjetDto()).ToDomain());
        await _projets.AddAsync(p, ct);
        var b = Bureau.Creer(p.Id.Value, TypeGroupe.Projet, userId);
        await _bureaux.AddAsync(b, ct);
        return await Enregistrer(new Contexte(p, b, Droits.Responsable | Droits.Tresorier), userId, ct);
    }

    public async Task<ProjetDetailsDto> OuvrirAsync(Guid id, Guid userId, bool admin, CancellationToken ct)
    {
        var c = await Charger(id, userId, admin, Droits.Responsable, ct);
        c.Projet.Ouvrir(userId.ToString());
        return await Enregistrer(c, userId, ct);
    }

    public async Task<ProjetDetailsDto> CloreAsync(Guid id, Guid userId, bool admin, CancellationToken ct)
    {
        var c = await Charger(id, userId, admin, Droits.Responsable, ct);
        c.Projet.Clore(userId.ToString());
        return await Enregistrer(c, userId, ct);
    }

    public async Task<(string Code, DateTime ExpireLe)> GenererCodeAsync(Guid id, Guid userId, bool admin, int jours, CancellationToken ct)
    {
        var c = await Charger(id, userId, admin, Droits.Responsable, ct);
        var code = c.Projet.GenererCodeInvitation(jours, userId.ToString());
        await _uow.SaveChangesAsync(ct);
        return (code, c.Projet.CodeInvitationExpireLe!.Value);
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
        var b = await BureauDe(_bureaux, p, ct);
        return await Enregistrer(new Contexte(p, b, DroitsDe(p, b, userId, false)), userId, ct);
    }

    public async Task<ProjetDetailsDto> AjouterParticipantAsync(Guid id, ParticipantSansCompteRequest r, Guid userId, bool admin, CancellationToken ct)
    {
        var c = await Charger(id, userId, admin, Droits.Responsable, ct);
        c.Projet.AjouterParticipant(r.Nom, r.Palier, r.DateAdhesion ?? Maintenant, userId.ToString());
        return await Enregistrer(c, userId, ct);
    }

    /// <summary>Imports participants and their historical payments in one transaction (all or nothing).</summary>
    public async Task<ImportResultat> ImporterAsync(Guid id, ImportProjetRequest r, Guid userId, bool admin, CancellationToken ct)
    {
        var c = await Charger(id, userId, admin, Droits.Responsable, ct);
        var p = c.Projet;
        var acteur = userId.ToString();
        var refs = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var x in r.Participants)
        {
            var existant = p.Participants.FirstOrDefault(y => string.Equals(y.Nom, x.Nom.Trim(), StringComparison.OrdinalIgnoreCase));
            var pid = existant?.Id ?? p.AjouterParticipant(x.Nom, x.Palier, DateTime.SpecifyKind(x.DateAdhesion, DateTimeKind.Utc), acteur).Id;
            refs[x.Reference] = pid;
            p.RenseignerSexe(pid, x.Sexe);
            if (!string.IsNullOrWhiteSpace(x.Statut))
                p.ChangerStatutParticipant(pid, StatutDepuis(x.Statut), "Registre importé", acteur);
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
        var details = await Enregistrer(c, userId, ct);
        return new ImportResultat(r.Participants.Count, r.Paiements.Count, r.Paiements.Sum(x => x.Montant), details);
    }

    private static StatutParticipant StatutDepuis(string s) =>
        new string(s.Normalize(System.Text.NormalizationForm.FormD).Where(ch => char.IsLetter(ch) && ch < 128).ToArray()).ToLowerInvariant() switch
        {
            "actif" => StatutParticipant.Actif,
            "suspendu" => StatutParticipant.Suspendu,
            "demissionnaire" => StatutParticipant.Demissionnaire,
            "exclu" => StatutParticipant.Exclu,
            _ => throw new ArgumentException($"Unknown participant status '{s}' (Actif, Suspendu, Demissionnaire or Exclu)."),
        };

    /// <summary>The responsable changes a participant's standing (suspended, resigned, excluded). Journaled.</summary>
    public async Task<ProjetDetailsDto> ChangerStatutParticipantAsync(Guid id, Guid participantId, StatutParticipantRequest r, Guid userId, bool admin, CancellationToken ct)
    {
        var c = await Charger(id, userId, admin, Droits.Responsable, ct);
        c.Projet.ChangerStatutParticipant(participantId, StatutDepuis(r.Statut), r.Motif, userId.ToString());
        return await Enregistrer(c, userId, ct);
    }

    public async Task<ProjetDetailsDto> ModifierReglesAsync(Guid id, ReglesProjetDto regles, Guid userId, bool admin, CancellationToken ct)
    {
        var c = await Charger(id, userId, admin, Droits.Responsable, ct);
        c.Projet.ModifierRegles(regles.ToDomain(), userId.ToString());
        return await Enregistrer(c, userId, ct);
    }

    // ── Commands: tiers ─────────────────────────────────────────────

    /// <summary>The participant changes their own tier, according to the project's rules.</summary>
    public async Task<(bool Applique, ProjetDetailsDto Projet)> ChangerMonPalierAsync(Guid id, decimal palier, Guid userId, CancellationToken ct)
    {
        var c = await Charger(id, userId, false, Droits.Participant, ct);
        var moi = c.Projet.Participants.FirstOrDefault(x => x.UtilisateurId?.Value == userId)
            ?? throw new AccesProjetRefuseException("Only a participant can change their tier.");
        var applique = c.Projet.ChangerPalier(moi.Id, palier, userId.ToString(), Maintenant);
        return (applique, await Enregistrer(c, userId, ct));
    }

    /// <summary>The trésorier approves or refuses a pending tier change.</summary>
    public async Task<ProjetDetailsDto> DeciderChangementPalierAsync(Guid id, Guid participantId, bool approuver, Guid userId, bool admin, CancellationToken ct)
    {
        var c = await Charger(id, userId, admin, Droits.Tresorier, ct);
        c.Projet.DeciderChangementPalier(participantId, approuver, userId.ToString(), Maintenant);
        return await Enregistrer(c, userId, ct);
    }

    // ── Commands: payments ──────────────────────────────────────────

    /// <summary>
    /// Declares a payment, pending validation by the trésorier. A participant declares their own; the
    /// responsable or the trésorier can declare for anyone (member without a smartphone).
    /// </summary>
    public async Task<ProjetDetailsDto> EnregistrerPaiementAsync(Guid id, Guid? participantId, decimal montant, string preuve, string? moyen,
        Guid userId, bool admin, CancellationToken ct)
    {
        var c = await Charger(id, userId, admin, Droits.Membre, ct);
        var moi = c.Projet.Participants.FirstOrDefault(x => x.UtilisateurId?.Value == userId);
        var cible = participantId ?? moi?.Id ?? throw new InvalidOperationException("Choose the participant who paid.");
        var bureau = (c.Droits & (Droits.Responsable | Droits.Tresorier | Droits.Admin)) != 0;
        if (!bureau && cible != moi?.Id)
            throw new AccesProjetRefuseException("You can only declare your own payments.");
        var texte = string.Join(" · ", new[] { moyen, preuve }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));
        if (texte.Length == 0) throw new ArgumentException("Give the payment method or a receipt reference.");
        c.Projet.EnregistrerPaiement(cible, montant, texte, userId.ToString(), Maintenant);
        return await Enregistrer(c, userId, ct);
    }

    /// <summary>The trésorier validates (after checking the money was received) or rejects with a reason.</summary>
    public async Task<ProjetDetailsDto> DeciderPaiementAsync(Guid id, Guid paiementId, bool valider, string? motif, Guid userId, bool admin, CancellationToken ct)
    {
        var c = await Charger(id, userId, admin, Droits.Tresorier, ct);
        var detail = valider ? $"CASH-{DateTime.UtcNow:yyyyMMddHHmmss}-{paiementId.ToString()[..8]}" : motif;
        c.Projet.DeciderPaiement(paiementId, valider, userId.ToString(), detail, Maintenant);
        return await Enregistrer(c, userId, ct);
    }

    // ── Commands: expenses (proposed by the responsable, validated by the trésorier) ──

    public async Task<ProjetDetailsDto> ProposerDepenseAsync(Guid id, string libelle, decimal montant, string justificatif, Guid userId, bool admin, CancellationToken ct)
    {
        var c = await Charger(id, userId, admin, Droits.Responsable, ct);
        c.Projet.ProposerDepense(libelle, montant, justificatif, userId.ToString(), Maintenant);
        return await Enregistrer(c, userId, ct);
    }

    public async Task<ProjetDetailsDto> DeciderDepenseAsync(Guid id, Guid depenseId, bool valider, string? motif, Guid userId, bool admin, CancellationToken ct)
    {
        var c = await Charger(id, userId, admin, Droits.Tresorier, ct);
        c.Projet.DeciderDepense(depenseId, valider, userId.ToString(), motif, Maintenant);
        return await Enregistrer(c, userId, ct);
    }
}
