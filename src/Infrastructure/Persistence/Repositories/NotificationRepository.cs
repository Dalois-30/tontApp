namespace Infrastructure.Persistence.Repositories;

using Domain.NotificationManagement;
using Domain.NotificationManagement.Repositories;
using Domain.NotificationManagement.ValueObjects;
using Domain.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

internal sealed class NotificationRepository : INotificationRepository
{
    private readonly TontineDbContext _dbContext;

    public NotificationRepository(TontineDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Notification?> GetByIdAsync(NotificationId id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Notifications
            .FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Notification>> GetPendingAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Notifications
            .Where(n => n.Statut == NotificationStatus.EnAttente)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Notification>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Notifications
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        await _dbContext.Notifications.AddAsync(notification, cancellationToken);
    }

    public Task UpdateAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        _dbContext.Notifications.Update(notification);
        return Task.CompletedTask;
    }

    public async Task<int> CountTodayByDestinataireAsync(string destinataireId, CancellationToken cancellationToken = default)
    {
        var todayUtc = DateTime.UtcNow.Date;
        return await _dbContext.Notifications
            .Where(n => n.DestinataireId == destinataireId && n.CreatedAt >= todayUtc)
            .CountAsync(cancellationToken);
    }

    public async Task<bool> AjouterSiReferenceAbsenteAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        if (notification.Reference is null)
            throw new ArgumentException("The notification has no reference.", nameof(notification));

        if (await _dbContext.Notifications.AnyAsync(
                n => n.DestinataireId == notification.DestinataireId && n.Reference == notification.Reference, cancellationToken))
            return false;

        await _dbContext.Notifications.AddAsync(notification, cancellationToken);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Stored meanwhile by a concurrent caller: forget this copy and its outbox message.
            foreach (var entry in _dbContext.ChangeTracker.Entries()
                         .Where(e => e.State == EntityState.Added && (e.Entity == notification || e.Entity is OutboxMessage)).ToList())
                entry.State = EntityState.Detached;
            return false;
        }
    }
}
