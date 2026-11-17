namespace Infrastructure.Persistence;

using System.Text.Json;
using Application.PaymentManagement.Services;
using Domain.BillingManagement;
using Domain.Common;
using Domain.CreditScoringManagement;
using Domain.IdentityManagement;
using Domain.NotificationManagement;
using Domain.NotificationManagement.Events;
using Domain.PaymentManagement;
using Domain.PaymentManagement.Entities;
using Domain.PaymentManagement.Events;
using Domain.TontineManagement;
using Domain.TontineManagement.Events;
using Domain.TontineManagement.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

public sealed class TontineDbContext : DbContext, IUnitOfWork
{
    private readonly IMediator _mediator;

    public DbSet<Tontine> Tontines => Set<Tontine>();
    public DbSet<Versement> Versements => Set<Versement>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Utilisateur> Utilisateurs => Set<Utilisateur>();
    public DbSet<Abonnement> Abonnements => Set<Abonnement>();
    public DbSet<PlanAbonnement> PlansAbonnement => Set<PlanAbonnement>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProfilCredit> ProfilsCredit => Set<ProfilCredit>();
    public DbSet<Domain.ProjectManagement.Projet> Projets => Set<Domain.ProjectManagement.Projet>();

    public TontineDbContext(DbContextOptions<TontineDbContext> options, IMediator mediator)
        : base(options)
    {
        _mediator = mediator;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TontineDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    // ── Per-tontine lock state (set by VerrouTontinePostgres) ─────────────────────
    private TontineId? _tontineVerrouillee;
    private List<IDomainEvent>? _evenementsDifferes;

    /// <summary>The tontine whose lock this unit of work currently holds, if any.</summary>
    internal TontineId? TontineVerrouillee => _tontineVerrouillee;

    internal void EntrerVerrou(TontineId tontineId)
    {
        _tontineVerrouillee = tontineId;
        _evenementsDifferes = [];
    }

    /// <summary>
    /// Leaves the locked transaction. Committed: publishes the domain events deferred during it (after the
    /// commit, outside the lock). Rolled back: discards them.
    /// </summary>
    internal async Task SortirVerrouAsync(bool valide, CancellationToken cancellationToken)
    {
        var evenements = _evenementsDifferes;
        _tontineVerrouillee = null;
        _evenementsDifferes = null;
        if (!valide || evenements is null)
            return;
        foreach (var domainEvent in evenements)
        {
            try
            {
                await _mediator.Publish(domainEvent, cancellationToken);
            }
            catch (Exception ex)
            {
                // The operation is committed: a failing side effect (notification, credit score) is logged,
                // never reported as a failure of the operation the caller would then retry.
                (this.GetInfrastructure().GetService(typeof(ILoggerFactory)) as ILoggerFactory)?.CreateLogger<TontineDbContext>().LogError(ex,
                    "Domain event {Event} handler failed after commit", domainEvent.GetType().Name);
                // Do not let the failed handler's unsaved changes leak into the next handler's save.
                ChangeTracker.Clear();
            }
        }
    }

    /// <summary>Refuses a write to the tontine's versement / audit chain without holding its lock.</summary>
    internal void ExigerVerrou(TontineId tontineId)
    {
        if (_tontineVerrouillee is null || !_tontineVerrouillee.Equals(tontineId))
            throw new VerrouTontineRequisException(
                $"Writing to the payment chain of tontine {tontineId.Value} requires holding its lock (IVerrouTontine).");
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Last line of defence: no versement / audit entry is appended to a chain without that tontine's lock.
        foreach (var ajout in ChangeTracker.Entries<Versement>().Where(e => e.State == EntityState.Added))
            ExigerVerrou(ajout.Entity.TontineId);
        foreach (var ajout in ChangeTracker.Entries<AuditEntry>().Where(e => e.State == EntityState.Added))
            ExigerVerrou(ajout.Entity.TontineId);

        // Every aggregate, whatever its id type (AggregateRoot<TId> implements IHasDomainEvents).
        var domainEvents = ChangeTracker.Entries<IHasDomainEvents>()
            .Where(e => e.Entity.DomainEvents.Count != 0)
            .ToList()
            .SelectMany(e => e.Entity.PopDomainEvents())
            .ToList();

        // Outbox pattern: persist domain events in the same transaction
        foreach (var domainEvent in domainEvents)
        {
            if (domainEvent is VersementConfirmedEvent confirmedEvent)
            {
                var outboxMessage = OutboxMessage.Create(
                    nameof(VersementConfirmedEvent),
                    JsonSerializer.Serialize(new
                    {
                        confirmedEvent.VersementId.Value,
                        TontineId = confirmedEvent.TontineId.Value,
                        PayeurId = confirmedEvent.PayeurId.Value,
                        TourId = confirmedEvent.TourId.Value,
                        confirmedEvent.Montant,
                        confirmedEvent.ReferenceExterne,
                        confirmedEvent.OccurredOn
                    }));

                OutboxMessages.Add(outboxMessage);
            }
            else if (domainEvent is RoundOpenedEvent roundOpenedEvent)
            {
                var outboxMessage = OutboxMessage.Create(
                    nameof(RoundOpenedEvent),
                    JsonSerializer.Serialize(new
                    {
                        TontineId = roundOpenedEvent.TontineId.Value,
                        RoundId = roundOpenedEvent.RoundId.Value,
                        BeneficiaryId = roundOpenedEvent.BeneficiaryId.Value,
                        roundOpenedEvent.RoundNumber,
                        roundOpenedEvent.OccurredOn
                    }));

                OutboxMessages.Add(outboxMessage);
            }
            else if (domainEvent is NotificationCreatedEvent notificationCreatedEvent)
            {
                var outboxMessage = OutboxMessage.Create(
                    nameof(NotificationCreatedEvent),
                    JsonSerializer.Serialize(new
                    {
                        NotificationId = notificationCreatedEvent.NotificationId.Value,
                        notificationCreatedEvent.DestinataireId,
                        Type = notificationCreatedEvent.Type.ToString(),
                        notificationCreatedEvent.OccurredOn
                    }));

                OutboxMessages.Add(outboxMessage);
            }
        }

        var result = await base.SaveChangesAsync(cancellationToken);

        // Inside a locked transaction: published after its commit (SortirVerrouAsync), never on rollback.
        if (_evenementsDifferes is not null)
        {
            _evenementsDifferes.AddRange(domainEvents);
            return result;
        }

        foreach (var domainEvent in domainEvents)
        {
            await _mediator.Publish(domainEvent, cancellationToken);
        }

        return result;
    }
}
