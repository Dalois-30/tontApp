namespace Domain.ProjectManagement;

using Domain.IdentityManagement.ValueObjects;

public interface IProjetRepository
{
    Task<Projet?> GetByIdAsync(ProjetId id, CancellationToken cancellationToken = default);
    Task<Projet?> GetByCodeHashAsync(string codeHash, CancellationToken cancellationToken = default);
    /// <summary>Projects the user manages or participates in.</summary>
    Task<IReadOnlyList<Projet>> GetByUtilisateurAsync(UtilisateurId utilisateurId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Projet>> GetAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(Projet projet, CancellationToken cancellationToken = default);
}
