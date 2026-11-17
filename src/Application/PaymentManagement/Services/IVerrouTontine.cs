namespace Application.PaymentManagement.Services;

using Domain.TontineManagement.ValueObjects;

/// <summary>
/// Serialises, per tontine, the operations that write to its versement / audit hash chain
/// (declaration, validation, rejection, mobile money initiation) or change who owes what
/// (round opening / closing, member suspension, membership decision). Two operations on the same tontine
/// run one after the other; operations on different tontines stay parallel.
/// </summary>
/// <remarks>
/// <para>Usage: take the lock <b>before any read</b> the operation depends on, then read, write, save, and
/// <see cref="ITransactionVerrouillee.ValiderAsync"/>. Disposing without validating rolls back.</para>
/// <para>Obligations of every implementation:</para>
/// <list type="number">
/// <item>Everything between <see cref="VerrouillerAsync"/> and <see cref="ITransactionVerrouillee.ValiderAsync"/>
/// (reads, writes, saves) is one database transaction, holding a lock that no other operation on the same
/// tontine can hold at the same time; the lock is released at commit or rollback, never left behind.</item>
/// <item>Reads made after <see cref="VerrouillerAsync"/> return the latest committed state, including what
/// the previous holder committed, even for entities the unit of work had loaded before the lock.</item>
/// <item>The wait for the lock is bounded: past it, <see cref="TontineOccupeeException"/> is thrown.</item>
/// <item>Refuses (with <see cref="VerrouTontineRequisException"/>) to start while a transaction is already
/// open or while unsaved changes are pending.</item>
/// <item>Domain events raised by the saves are published only after the commit, never on rollback, so
/// their handlers neither roll back the operation nor run while the lock is held.</item>
/// <item>Appending to a tontine's versement / audit chain without holding that tontine's lock is refused
/// (<see cref="VerrouTontineRequisException"/>).</item>
/// </list>
/// </remarks>
public interface IVerrouTontine
{
    Task<ITransactionVerrouillee> VerrouillerAsync(TontineId tontineId, CancellationToken cancellationToken = default);
}

/// <summary>A transaction holding the lock of one tontine until it is committed or disposed.</summary>
public interface ITransactionVerrouillee : IAsyncDisposable
{
    /// <summary>Commits the transaction, releases the lock, then publishes the deferred domain events.</summary>
    Task ValiderAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Another operation on the same tontine held its lock longer than the bounded wait. Nothing was written:
/// the caller may simply retry (HTTP 503).
/// </summary>
public sealed class TontineOccupeeException : Exception
{
    public const string MessageUtilisateur = "Opération en cours sur cette tontine, réessayez.";

    public TontineOccupeeException(Guid tontineId, Exception? inner = null)
        : base($"{MessageUtilisateur} (tontine {tontineId})", inner)
    {
        TontineId = tontineId;
    }

    public Guid TontineId { get; }
}

/// <summary>
/// Programming error: the per-tontine lock was misused (taken inside an open transaction or after
/// unsaved changes) or a chain write happened without it. Deliberately not an
/// <see cref="InvalidOperationException"/>, so that no "already processed" / business-refusal handler
/// swallows it.
/// </summary>
public sealed class VerrouTontineRequisException : Exception
{
    public VerrouTontineRequisException(string message) : base(message) { }
}
