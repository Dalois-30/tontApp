namespace Application.TontineManagement.Services;

using Domain.Gouvernance;
using Domain.IdentityManagement.ValueObjects;
using Domain.PaymentManagement.Repositories;
using Domain.PaymentManagement.ValueObjects;
using Domain.TontineManagement.Repositories;
using Domain.TontineManagement.ValueObjects;

/// <summary>
/// Access level of a user on a given tontine. Ordered: a higher value includes the lower ones.
/// <see cref="Gestionnaire"/> means "responsable of the tontine's bureau" (runs the tontine).
/// </summary>
public enum NiveauAcces
{
    Aucun = 0,
    Membre = 1,
    Gestionnaire = 2
}

/// <summary>
/// Result of an access check. <see cref="MembreId"/> is set when the user is a member of the tontine.
/// <see cref="EstTresorier"/> is the right to validate or reject payments, independent of the level.
/// </summary>
public sealed record AccesTontine(bool Existe, Guid TontineId, NiveauAcces Niveau, Guid? MembreId, bool EstTresorier = false)
{
    public static AccesTontine Introuvable(Guid tontineId) => new(false, tontineId, NiveauAcces.Aucun, null);

    public bool Permet(NiveauAcces requis) => Existe && Niveau >= requis;

    /// <summary>Responsable or trésorier: may declare a payment for any member.</summary>
    public bool EstBureau => Existe && (Niveau >= NiveauAcces.Gestionnaire || EstTresorier);
}

/// <summary>
/// Resolves what a user is allowed to do on a tontine, from its bureau (<see cref="Bureau"/>): the
/// responsable runs it (level Gestionnaire), the trésorier validates payments, members read everything.
/// Without a bureau yet, the creator holds both roles. Global roles (Admin) are handled by the caller.
/// </summary>
public interface ITontineAccessService
{
    Task<AccesTontine> GetAccesAsync(Guid tontineId, Guid utilisateurId, CancellationToken cancellationToken = default);

    /// <summary>Same as <see cref="GetAccesAsync"/> but starting from a versement id.</summary>
    Task<AccesTontine?> GetAccesParVersementAsync(Guid versementId, Guid utilisateurId, CancellationToken cancellationToken = default);
}

public sealed class TontineAccessService : ITontineAccessService
{
    private readonly ITontineRepository _tontineRepository;
    private readonly IVersementRepository _versementRepository;
    private readonly IBureauRepository _bureauRepository;

    public TontineAccessService(ITontineRepository tontineRepository, IVersementRepository versementRepository,
        IBureauRepository bureauRepository)
    {
        _tontineRepository = tontineRepository;
        _versementRepository = versementRepository;
        _bureauRepository = bureauRepository;
    }

    public async Task<AccesTontine> GetAccesAsync(
        Guid tontineId, Guid utilisateurId, CancellationToken cancellationToken = default)
    {
        var tontine = await _tontineRepository.GetByIdReadOnlyAsync(TontineId.From(tontineId), cancellationToken);
        if (tontine is null)
            return AccesTontine.Introuvable(tontineId);

        var userId = UtilisateurId.From(utilisateurId);
        var membre = tontine.Members.FirstOrDefault(m => m.UtilisateurId is not null && m.UtilisateurId == userId);

        // Same rule as GouvernanceService: no bureau yet means the creator holds both roles.
        var bureau = await _bureauRepository.GetAsync(tontineId, cancellationToken);
        // A vote past its deadline changes the roles now, without modifying the bureau (read-only check).
        var roles = bureau?.RolesEffectifs(DateTime.UtcNow);
        var responsable = roles?.ResponsableId ?? tontine.GestionnaireId.Value;
        var tresorier = roles?.TresorierId ?? tontine.GestionnaireId.Value;
        var estTresorier = tresorier == utilisateurId;

        // A bureau member who is not in the member list (e.g. the association's gestionnaire) still reads everything.
        var niveau = responsable == utilisateurId
            ? NiveauAcces.Gestionnaire
            : membre is not null || estTresorier ? NiveauAcces.Membre : NiveauAcces.Aucun;

        return new AccesTontine(true, tontineId, niveau, membre?.Id.Value, estTresorier);
    }

    public async Task<AccesTontine?> GetAccesParVersementAsync(
        Guid versementId, Guid utilisateurId, CancellationToken cancellationToken = default)
    {
        var versement = await _versementRepository.GetByIdAsync(VersementId.From(versementId), cancellationToken);
        if (versement is null)
            return null;

        return await GetAccesAsync(versement.TontineId.Value, utilisateurId, cancellationToken);
    }
}
