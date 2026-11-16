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
}
