namespace ApiTests;

using Application.PaymentManagement.Services;
using Domain.PaymentManagement;
using Domain.PaymentManagement.ValueObjects;
using Domain.TontineManagement.ValueObjects;
using FluentAssertions;
using Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

/// <summary>The per-tontine PostgreSQL lock itself, on the real database of the API tests.</summary>
[Collection(ApiCollection.Nom)]
public sealed class VerrouTontinePostgresTests(ApiFactory api)
{
    private string ChaineConnexion()
    {
        using var scope = api.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<TontineDbContext>().Database.GetConnectionString()!;
    }

    private TontineDbContext Contexte(IMediator? mediator = null) =>
        new(new DbContextOptionsBuilder<TontineDbContext>().UseNpgsql(ChaineConnexion()).Options,
            mediator ?? Mock.Of<IMediator>());

    private static Versement NouveauVersement(TontineId tontineId) =>
        Versement.Create(tontineId, TourId.Create(), PayeurId.Create(), Montant.Create(500m));

    private async Task<int> NombreVersements(TontineId tontineId)
    {
        await using var db = Contexte();
        return await db.Versements.CountAsync(v => v.TontineId == tontineId);
    }

    [Fact]
    public void Cle_StablePourUnGuid_DifferentePourDeuxGuids()
    {
        var g = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");
        VerrouTontinePostgres.Cle(g).Should().Be(VerrouTontinePostgres.Cle(Guid.Parse(g.ToString())));
        VerrouTontinePostgres.Cle(g).Should().NotBe(VerrouTontinePostgres.Cle(Guid.Parse("0a3b5c7d-1e2f-4a5b-8c9d-0e1f2a3b4c5d")));
    }

    [Fact]
    public async Task ModificationsEnAttente_RefuseesAvecUneExceptionDediee()
    {
        await using var db = Contexte();
        var tontineId = TontineId.Create();
        db.Versements.Add(NouveauVersement(tontineId));

        var act = () => new VerrouTontinePostgres(db).VerrouillerAsync(tontineId);

        await act.Should().ThrowAsync<VerrouTontineRequisException>();
    }

    [Fact]
    public async Task TransactionDejaOuverte_RefuseeAvecUneExceptionDediee()
    {
        await using var db = Contexte();
        await using var _ = await db.Database.BeginTransactionAsync();

        var act = () => new VerrouTontinePostgres(db).VerrouillerAsync(TontineId.Create());

        await act.Should().ThrowAsync<VerrouTontineRequisException>();
    }

    [Fact]
    public async Task SansValider_ToutEstAnnule()
    {
        var tontineId = TontineId.Create();
        await using (var db = Contexte())
        {
            var transaction = await new VerrouTontinePostgres(db).VerrouillerAsync(tontineId);
            db.Versements.Add(NouveauVersement(tontineId));
            await db.SaveChangesAsync();
            await transaction.DisposeAsync();
        }

        (await NombreVersements(tontineId)).Should().Be(0);
    }

    [Fact]
    public async Task Valide_EstEnregistre()
    {
        var tontineId = TontineId.Create();
        await using (var db = Contexte())
        {
            await using var transaction = await new VerrouTontinePostgres(db).VerrouillerAsync(tontineId);
            db.Versements.Add(NouveauVersement(tontineId));
            await db.SaveChangesAsync();
            await transaction.ValiderAsync();
        }

        (await NombreVersements(tontineId)).Should().Be(1);
    }

    [Fact]
    public async Task EcritureDeLaChaineSansVerrou_Refusee()
    {
        var tontineId = TontineId.Create();
        await using var db = Contexte();
        db.Versements.Add(NouveauVersement(tontineId));

        var act = () => db.SaveChangesAsync();

        await act.Should().ThrowAsync<VerrouTontineRequisException>();
        (await NombreVersements(tontineId)).Should().Be(0);
    }

    [Fact]
    public async Task VerrouDUneAutreTontine_NAutorisePasLEcriture()
    {
        await using var db = Contexte();
        await using var transaction = await new VerrouTontinePostgres(db).VerrouillerAsync(TontineId.Create());
        db.Versements.Add(NouveauVersement(TontineId.Create()));

        var act = () => db.SaveChangesAsync();

        await act.Should().ThrowAsync<VerrouTontineRequisException>();
    }

    [Fact]
    public async Task AttenteBornee_TontineOccupee()
    {
        var tontineId = TontineId.Create();
        await using var detenteur = Contexte();
        await using var tenu = await new VerrouTontinePostgres(detenteur).VerrouillerAsync(tontineId);

        await using var db = Contexte();
        var act = () => new VerrouTontinePostgres(db) { DelaiAttente = TimeSpan.FromMilliseconds(200) }
            .VerrouillerAsync(tontineId);

        (await act.Should().ThrowAsync<TontineOccupeeException>()).Which.Message
            .Should().Contain(TontineOccupeeException.MessageUtilisateur);
        db.Database.CurrentTransaction.Should().BeNull("the failed attempt rolls its transaction back");
    }

    [Fact]
    public async Task GestionnaireDEvenementEnEchec_NAnnulePasLaConfirmation()
    {
        var tontineId = TontineId.Create();
        await using (var db = Contexte())
        {
            await using var t = await new VerrouTontinePostgres(db).VerrouillerAsync(tontineId);
            db.Versements.Add(NouveauVersement(tontineId));
            await db.SaveChangesAsync();
            await t.ValiderAsync();
        }

        // Every domain event handler throws (MediatR publishes IDomainEvent through the generic overload).
        var mediator = new Mock<IMediator>();
        var publications = 0;
        var publieAvantCommit = false;
        mediator.Setup(m => m.Publish(It.IsAny<AnyNotification>(), It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                publications++;
                publieAvantCommit |= !ConfirmeEnBase(tontineId);
            })
            .ThrowsAsync(new InvalidOperationException("handler failure"));

        await using (var db = Contexte(mediator.Object))
        {
            await using var t = await new VerrouTontinePostgres(db).VerrouillerAsync(tontineId);
            var versement = await db.Versements.Include(nameof(Versement.AuditTrail)).SingleAsync(v => v.TontineId == tontineId);
            versement.Confirmer("REF-EVT");
            await db.SaveChangesAsync();
            publications.Should().Be(0, "inside the locked transaction events are deferred");

            // The failing handler runs after the commit and does not fail the operation.
            await t.ValiderAsync();
        }

        publications.Should().BeGreaterThan(0, "the confirmation event is really published");
        publieAvantCommit.Should().BeFalse("events are published only after the commit");
        ConfirmeEnBase(tontineId).Should().BeTrue("the confirmation is committed whatever its event handlers do");
    }

    private bool ConfirmeEnBase(TontineId tontineId)
    {
        using var db = Contexte();
        return db.Versements.AsNoTracking().Any(v => v.TontineId == tontineId && v.Statut == VersementStatus.Confirme);
    }

    /// <summary>Moq type matcher: any notification type for MediatR's generic Publish.</summary>
    [TypeMatcher]
    public sealed class AnyNotification : ITypeMatcher, INotification
    {
        public bool Matches(Type typeArgument) => true;
    }
}
