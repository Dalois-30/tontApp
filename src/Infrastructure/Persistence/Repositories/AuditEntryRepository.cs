namespace Infrastructure.Persistence.Repositories;

using Domain.PaymentManagement.Entities;
using Domain.PaymentManagement.Repositories;
using Domain.TontineManagement.ValueObjects;
using Microsoft.EntityFrameworkCore;

internal sealed class AuditEntryRepository : IAuditEntryRepository
{
    private readonly TontineDbContext _dbContext;

    public AuditEntryRepository(TontineDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<AuditEntry>> GetByTontinePagedAsync(
        TontineId tontineId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.AuditEntries
            .AsNoTracking()
            .Where(e => e.TontineId == tontineId)
            .OrderByDescending(e => e.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditEntry>> GetByTontineOrderedAsync(
        TontineId tontineId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.AuditEntries
            .AsNoTracking()
            .Where(e => e.TontineId == tontineId)
            .OrderBy(e => e.Timestamp)
            .ToListAsync(cancellationToken);
    }

    public async Task<AuditEntry?> GetLastByTontineAsync(
        TontineId tontineId,
        CancellationToken cancellationToken = default)
    {
        // The tail of the chain: the entry no other entry of the tontine chains on (not the latest timestamp,
        // which ties or clock skew could get wrong). Linear under the tontine lock; on a chain forked before
        // the lock existed, the most recent tail.
        return await _dbContext.AuditEntries
            .AsNoTracking()
            .Where(e => e.TontineId == tontineId
                && !_dbContext.AuditEntries.Any(s => s.TontineId == tontineId && s.HashPrecedent == e.HashCourant))
            .OrderByDescending(e => e.Timestamp)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<int> CountByTontineAsync(
        TontineId tontineId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.AuditEntries
            .Where(e => e.TontineId == tontineId)
            .CountAsync(cancellationToken);
    }

    public async Task AddAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        _dbContext.ExigerVerrou(entry.TontineId);
        await _dbContext.AuditEntries.AddAsync(entry, cancellationToken);
    }
}
