namespace Domain.PaymentManagement.Repositories;

using Domain.PaymentManagement.ValueObjects;
using Domain.TontineManagement.ValueObjects;

public interface IVersementRepository
{
    Task<Versement?> GetByIdAsync(VersementId id, CancellationToken cancellationToken = default);
    Task<Versement?> GetByReferenceExterneAsync(string referenceExterne, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Versement>> GetByTontineAndTourAsync(TontineId tontineId, TourId tourId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Versement>> GetByTontineAsync(TontineId tontineId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Versement>> GetByPayeurAsync(PayeurId payeurId, CancellationToken cancellationToken = default);
    /// <summary>The tail of the tontine's versement chain (the versement no other one chains on). Read it under the tontine lock.</summary>
    Task<Versement?> GetLastByTontineAsync(TontineId tontineId, CancellationToken cancellationToken = default);
    /// <summary>The tontine of a versement, without loading or tracking the versement.</summary>
    Task<TontineId?> GetTontineIdAsync(VersementId id, CancellationToken cancellationToken = default);
    /// <summary>The versement as currently committed, not tracked (fresh state check under the tontine lock).</summary>
    Task<Versement?> GetByIdSansSuiviAsync(VersementId id, CancellationToken cancellationToken = default);
    Task AddAsync(Versement versement, CancellationToken cancellationToken = default);
    Task UpdateAsync(Versement versement, CancellationToken cancellationToken = default);
}
