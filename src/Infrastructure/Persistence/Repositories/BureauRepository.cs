namespace Infrastructure.Persistence.Repositories;

using Domain.Gouvernance;
using Microsoft.EntityFrameworkCore;

internal sealed class BureauRepository : IBureauRepository
{
    private readonly TontineDbContext _db;

    public BureauRepository(TontineDbContext db) => _db = db;

    public async Task<Bureau?> GetAsync(Guid groupeId, CancellationToken cancellationToken = default) =>
        // A bureau added earlier in the same unit of work is not in the database yet.
        _db.ChangeTracker.Entries<Bureau>().Select(e => e.Entity).FirstOrDefault(b => b.Id == groupeId)
        ?? await _db.Set<Bureau>()
            .Include(b => b.Votes).ThenInclude(v => v.Bulletins)
            .Include(b => b.Seances)
            .Include(b => b.Journal)
            .AsSplitQuery()
            .FirstOrDefaultAsync(b => b.Id == groupeId, cancellationToken);

    public async Task AddAsync(Bureau bureau, CancellationToken cancellationToken = default) =>
        await _db.Set<Bureau>().AddAsync(bureau, cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetGroupesOuSiegeAsync(Guid utilisateurId, TypeGroupe type, DateTime maintenant,
        CancellationToken cancellationToken = default)
    {
        // Candidates: the stored roles, plus the candidate of an open vote (it may be adopted by its deadline).
        // The effective roles are then computed on the loaded bureaux, without tracking (pure read).
        var bureaux = await _db.Set<Bureau>()
            .AsNoTracking()
            .Include(b => b.Votes).ThenInclude(v => v.Bulletins)
            .AsSplitQuery()
            .Where(b => b.Type == type && (b.ResponsableId == utilisateurId || b.TresorierId == utilisateurId
                || b.Votes.Any(v => v.Statut == StatutVote.Ouvert && v.CandidatId == utilisateurId)))
            .ToListAsync(cancellationToken);

        return bureaux
            .Where(b => b.RolesEffectifs(maintenant) is var (responsable, tresorier)
                        && (responsable == utilisateurId || tresorier == utilisateurId))
            .Select(b => b.Id)
            .ToList();
    }
}
