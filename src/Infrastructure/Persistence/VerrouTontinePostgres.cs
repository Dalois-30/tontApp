namespace Infrastructure.Persistence;

using Application.PaymentManagement.Services;
using Domain.TontineManagement.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

/// <summary>
/// PostgreSQL implementation of <see cref="IVerrouTontine"/>: a transaction on the request's
/// <see cref="TontineDbContext"/> holding <c>pg_advisory_xact_lock(classid, objid)</c>, where
/// <c>classid</c> is a constant reserved for the tontine payment chain and <c>objid</c> is derived from the
/// tontine id. PostgreSQL releases the lock at commit or rollback, so a crashed request never leaves a
/// tontine locked. Not a global lock: each tontine has its own key. The 128-bit id is folded into 32 bits,
/// so two tontines may (rarely) share a key: they are then serialised with each other, which is safe.
/// The wait is bounded by <c>SET LOCAL lock_timeout</c>; past it, <see cref="TontineOccupeeException"/>.
/// </summary>
internal sealed class VerrouTontinePostgres : IVerrouTontine
{
    /// <summary>Advisory lock class of the tontine payment chain ("TONT"); keeps it apart from other users.</summary>
    internal const int ClasseVerrou = 0x544F4E54;

    /// <summary>SQLSTATE lock_not_available, raised when lock_timeout expires.</summary>
    private const string VerrouIndisponible = "55P03";

    private readonly TontineDbContext _dbContext;

    public VerrouTontinePostgres(TontineDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Longest wait for the lock before giving up with <see cref="TontineOccupeeException"/>.</summary>
    internal TimeSpan DelaiAttente { get; init; } = TimeSpan.FromSeconds(10);

    public async Task<ITransactionVerrouillee> VerrouillerAsync(
        TontineId tontineId, CancellationToken cancellationToken = default)
    {
        if (_dbContext.Database.CurrentTransaction is not null)
            throw new VerrouTontineRequisException(
                "The tontine lock opens its own transaction: it cannot be taken inside an already open transaction.");
        // Never silently drop pending writes: the lock must be taken before anything is modified.
        if (_dbContext.ChangeTracker.HasChanges())
            throw new VerrouTontineRequisException("The tontine lock must be taken before any change is made.");

        var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Transaction-local (SET LOCAL equivalent): the bounded wait applies to this transaction only.
            var delaiMs = ((int)Math.Max(1, DelaiAttente.TotalMilliseconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT set_config('lock_timeout', {delaiMs}, true)", cancellationToken);
            var objet = Cle(tontineId.Value);
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({ClasseVerrou}, {objet})", cancellationToken);
        }
        catch (Exception ex)
        {
            await transaction.DisposeAsync();
            if (ex is PostgresException { SqlState: VerrouIndisponible })
                throw new TontineOccupeeException(tontineId.Value, ex);
            throw;
        }

        // Obligation 2: entities read before the lock (e.g. by an access check) may be stale; forget them so
        // that reads under the lock return what the previous holder committed (read committed).
        _dbContext.ChangeTracker.Clear();
        _dbContext.EntrerVerrou(tontineId);

        return new TransactionVerrouillee(_dbContext, transaction);
    }

    /// <summary>32-bit advisory lock object id of a tontine: the four 32-bit words of the GUID XORed.</summary>
    internal static int Cle(Guid tontineId)
    {
        Span<byte> octets = stackalloc byte[16];
        tontineId.TryWriteBytes(octets);
        return BitConverter.ToInt32(octets[..4]) ^ BitConverter.ToInt32(octets[4..8])
            ^ BitConverter.ToInt32(octets[8..12]) ^ BitConverter.ToInt32(octets[12..]);
    }

    private sealed class TransactionVerrouillee : ITransactionVerrouillee
    {
        private readonly TontineDbContext _dbContext;
        private readonly IDbContextTransaction _transaction;
        private bool _termine;

        public TransactionVerrouillee(TontineDbContext dbContext, IDbContextTransaction transaction)
        {
            _dbContext = dbContext;
            _transaction = transaction;
        }

        public async Task ValiderAsync(CancellationToken cancellationToken = default)
        {
            if (_termine)
                return;
            await _transaction.CommitAsync(cancellationToken);
            _termine = true;
            await _transaction.DisposeAsync();
            // After the commit and outside the lock: a failing handler cannot undo the operation.
            await _dbContext.SortirVerrouAsync(valide: true, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            if (_termine)
                return;
            _termine = true;
            // Not validated: rolled back, deferred events discarded.
            await _transaction.DisposeAsync();
            await _dbContext.SortirVerrouAsync(valide: false, CancellationToken.None);
        }
    }
}
