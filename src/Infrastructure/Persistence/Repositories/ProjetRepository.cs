namespace Infrastructure.Persistence.Repositories;

using Domain.IdentityManagement.ValueObjects;
using Domain.ProjectManagement;
using Microsoft.EntityFrameworkCore;

internal sealed class ProjetRepository : IProjetRepository
{
    private readonly TontineDbContext _db;

    public ProjetRepository(TontineDbContext db) => _db = db;

    private IQueryable<Projet> Agregat() => _db.Set<Projet>()
        .Include(p => p.Participants).ThenInclude(p => p.Changements)
        .Include(p => p.Paiements)
        .Include(p => p.Depenses)
        .Include(p => p.Journal)
        .AsSplitQuery();

    public Task<Projet?> GetByIdAsync(ProjetId id, CancellationToken cancellationToken = default) =>
        Agregat().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<Projet?> GetByCodeHashAsync(string codeHash, CancellationToken cancellationToken = default) =>
        Agregat().FirstOrDefaultAsync(p => p.CodeInvitationHash == codeHash, cancellationToken);

    public async Task<IReadOnlyList<Projet>> GetByUtilisateurAsync(UtilisateurId utilisateurId, CancellationToken cancellationToken = default) =>
        await Agregat().AsNoTracking()
            .Where(p => p.GestionnaireId == utilisateurId || p.Participants.Any(x => x.UtilisateurId == utilisateurId))
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Projet>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await Agregat().AsNoTracking().OrderByDescending(p => p.CreatedAt).ToListAsync(cancellationToken);

    public async Task AddAsync(Projet projet, CancellationToken cancellationToken = default) =>
        await _db.Set<Projet>().AddAsync(projet, cancellationToken);
}
