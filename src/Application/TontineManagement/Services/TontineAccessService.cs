namespace Application.TontineManagement.Services;

using Domain.IdentityManagement.ValueObjects;
using Domain.PaymentManagement.Repositories;
using Domain.PaymentManagement.ValueObjects;
using Domain.TontineManagement.Repositories;
using Domain.TontineManagement.ValueObjects;

/// <summary>
/// Access level of a user on a given tontine. Ordered: a higher value includes the lower ones.
/// </summary>
public enum NiveauAcces
{
    Aucun = 0,
    Membre = 1,
    Gestionnaire = 2
}

/// <summary>
/// Result of an access check. <see cref="MembreId"/> is set when the user is a member of the tontine.
/// </summary>
public sealed record AccesTontine(bool Existe, Guid TontineId, NiveauAcces Niveau, Guid? MembreId)
{
    public static AccesTontine Introuvable(Guid tontineId) => new(false, tontineId, NiveauAcces.Aucun, null);

    public bool Permet(NiveauAcces requis) => Existe && Niveau >= requis;
}

/// <summary>
/// Resolves what a user is allowed to do on a tontine (ownership / membership).
/// Global roles (Admin) are handled by the caller; this service only looks at the tontine itself.
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

    public TontineAccessService(ITontineRepository tontineRepository, IVersementRepository versementRepository)
    {
        _tontineRepository = tontineRepository;
        _versementRepository = versementRepository;
    }

    public async Task<AccesTontine> GetAccesAsync(
        Guid tontineId, Guid utilisateurId, CancellationToken cancellationToken = default)
    {
        var tontine = await _tontineRepository.GetByIdReadOnlyAsync(TontineId.From(tontineId), cancellationToken);
        if (tontine is null)
            return AccesTontine.Introuvable(tontineId);

        var userId = UtilisateurId.From(utilisateurId);
        var membre = tontine.Members.FirstOrDefault(m => m.UtilisateurId is not null && m.UtilisateurId == userId);

        var niveau = tontine.GestionnaireId == userId
            ? NiveauAcces.Gestionnaire
            : membre is not null ? NiveauAcces.Membre : NiveauAcces.Aucun;

        return new AccesTontine(true, tontineId, niveau, membre?.Id.Value);
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
