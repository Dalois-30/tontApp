namespace Infrastructure.Persistence.Repositories;

using Domain.PaymentManagement;
using Domain.PaymentManagement.Repositories;
using Domain.PaymentManagement.ValueObjects;
using Domain.TontineManagement.ValueObjects;
using Microsoft.EntityFrameworkCore;

internal sealed class VersementRepository : IVersementRepository
{
    private const string AuditTrailNavigation = nameof(Versement.AuditTrail);
    private readonly TontineDbContext _dbContext;

    public VersementRepository(TontineDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Versement?> GetByIdAsync(VersementId id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Versements
            .Include(AuditTrailNavigation)
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
    }

    public async Task<Versement?> GetByReferenceExterneAsync(string referenceExterne, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Versements
            .Include(AuditTrailNavigation)
            .FirstOrDefaultAsync(v => v.ReferenceExterne == referenceExterne, cancellationToken);
    }

    public async Task<IReadOnlyList<Versement>> GetByTontineAndTourAsync(
        TontineId tontineId, TourId tourId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Versements
            .Where(v => v.TontineId == tontineId && v.TourId == tourId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Versement>> GetByTontineAsync(
        TontineId tontineId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Versements
            .Include(AuditTrailNavigation)
            .Where(v => v.TontineId == tontineId)
            .OrderBy(v => v.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Versement>> GetByPayeurAsync(
        PayeurId payeurId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Versements
            .Where(v => v.PayeurId == payeurId)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The tail of the tontine's versement chain: the versement whose hash no other versement of the tontine
    /// chains on (not the latest by date, which a clock skew could get wrong). Under the tontine lock the chain
    /// is linear, so there is one; on a chain forked before the lock existed, the most recent tail.
    /// </summary>
    public async Task<Versement?> GetLastByTontineAsync(
        TontineId tontineId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Versements
            .AsNoTracking()
            .Where(v => v.TontineId == tontineId
                && !_dbContext.Versements.Any(s => s.TontineId == tontineId && s.HashPrecedent == v.HashCourant))
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<TontineId?> GetTontineIdAsync(VersementId id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Versements
            .AsNoTracking()
            .Where(v => v.Id == id)
            .Select(v => v.TontineId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Versement?> GetByIdSansSuiviAsync(VersementId id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Versements
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
    }

    public async Task AddAsync(Versement versement, CancellationToken cancellationToken = default)
    {
        _dbContext.ExigerVerrou(versement.TontineId);
        await _dbContext.Versements.AddAsync(versement, cancellationToken);
    }

    public Task UpdateAsync(Versement versement, CancellationToken cancellationToken = default)
    {
        _dbContext.ExigerVerrou(versement.TontineId);
        _dbContext.Versements.Update(versement);
        return Task.CompletedTask;
    }
}
