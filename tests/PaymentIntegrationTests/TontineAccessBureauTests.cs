namespace PaymentIntegrationTests;

using Api.Security;
using Application.TontineManagement.Services;
using Domain.Gouvernance;
using Domain.IdentityManagement.ValueObjects;
using Domain.PaymentManagement.Repositories;
using Domain.TontineManagement;
using Domain.TontineManagement.Repositories;
using Domain.TontineManagement.ValueObjects;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;

/// <summary>
/// Tontine rights come from the group's bureau: the responsable runs the tontine (level Gestionnaire),
/// the trésorier validates payments, members read. Without a bureau the creator holds both roles.
/// </summary>
public class TontineAccessBureauTests
{
    private readonly UtilisateurId _createur = UtilisateurId.Create();
    private readonly UtilisateurId _alice = UtilisateurId.Create();
    private readonly UtilisateurId _bob = UtilisateurId.Create();
    private readonly Mock<ITontineRepository> _tontines = new();
    private readonly Mock<IVersementRepository> _versements = new();
    private readonly Mock<IBureauRepository> _bureaux = new();
    private readonly Tontine _tontine;

    public TontineAccessBureauTests()
    {
        _tontine = Tontine.Create("Test", null, ContributionAmount.Create(5000, "XAF"), TontinePeriodicity.Monthly, 5, _createur);
        var (_, code) = _tontine.GenerateInvitation(nombreUsagesMax: 2);
        _tontine.JoinWithInvitation("Alice", code, _alice);
        _tontine.JoinWithInvitation("Bob", code, _bob);
        _tontine.AddMember("Carol");
        _tontine.Activate();
        _tontines.Setup(r => r.GetByIdReadOnlyAsync(_tontine.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_tontine);
    }

    private TontineAccessService Service() => new(_tontines.Object, _versements.Object, _bureaux.Object);

    private Bureau AvecBureau()
    {
        var b = Bureau.Creer(_tontine.Id.Value, TypeGroupe.Tontine, _createur.Value);
        _bureaux.Setup(r => r.GetAsync(_tontine.Id.Value, It.IsAny<CancellationToken>())).ReturnsAsync(b);
        return b;
    }

    private Task<AccesTontine> Acces(UtilisateurId u) => Service().GetAccesAsync(_tontine.Id.Value, u.Value);

    [Fact]
    public async Task SansBureau_LeCreateurEstResponsableEtTresorier()
    {
        var acces = await Acces(_createur);

        acces.Niveau.Should().Be(NiveauAcces.Gestionnaire);
        acces.EstTresorier.Should().BeTrue();
        acces.EstBureau.Should().BeTrue();
    }

    [Fact]
    public async Task UnMembre_LitMaisNeGereNiNeValide()
    {
        AvecBureau();
        var acces = await Acces(_alice);

        acces.Niveau.Should().Be(NiveauAcces.Membre);
        acces.EstTresorier.Should().BeFalse();
        acces.EstBureau.Should().BeFalse();
        acces.MembreId.Should().Be(_tontine.Members.First(m => m.Name == "Alice").Id.Value);
    }

    [Fact]
    public async Task TresorierNomme_ValideSansGerer_EtLeCreateurNeValidePlus()
    {
        AvecBureau().NommerTresorier(_alice.Value, _createur.Value.ToString());

        var alice = await Acces(_alice);
        alice.EstTresorier.Should().BeTrue();
        alice.Niveau.Should().Be(NiveauAcces.Membre);
        alice.EstBureau.Should().BeTrue();

        var createur = await Acces(_createur);
        createur.Niveau.Should().Be(NiveauAcces.Gestionnaire);
        createur.EstTresorier.Should().BeFalse();
    }

    [Fact]
    public async Task NouveauResponsable_Gere_EtLAncienPerdLaGestion()
    {
        var b = AvecBureau();
        b.NommerTresorier(_alice.Value, _createur.Value.ToString());
        b.ReattribuerRole(RoleBureau.Responsable, _bob.Value, "admin");

        (await Acces(_bob)).Niveau.Should().Be(NiveauAcces.Gestionnaire);
        // The creator is not a member of the tontine: once out of the bureau, no access at all.
        (await Acces(_createur)).Niveau.Should().Be(NiveauAcces.Aucun);
    }

    [Fact]
    public async Task CreateurResteTresorier_GardeLaLectureEtLaValidation()
    {
        AvecBureau().ReattribuerRole(RoleBureau.Responsable, _bob.Value, "admin");

        var createur = await Acces(_createur);
        createur.Niveau.Should().Be(NiveauAcces.Membre);
        createur.EstTresorier.Should().BeTrue();
    }

    [Fact]
    public async Task Etranger_AucunAcces()
    {
        AvecBureau();
        var acces = await Service().GetAccesAsync(_tontine.Id.Value, Guid.NewGuid());

        acces.Niveau.Should().Be(NiveauAcces.Aucun);
        acces.EstTresorier.Should().BeFalse();
    }

    [Fact]
    public void CheckTresorier_MapsToHttpStatus()
    {
        var id = Guid.NewGuid();
        var responsable = new AccesTontine(true, id, NiveauAcces.Gestionnaire, null);
        var tresorier = new AccesTontine(true, id, NiveauAcces.Membre, Guid.NewGuid(), EstTresorier: true);

        AccessResults.CheckTresorier(AccesTontine.Introuvable(id), false).Should().BeOfType<NotFoundObjectResult>();
        AccessResults.CheckTresorier(responsable, false).Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
        AccessResults.CheckTresorier(responsable, estAdmin: true).Should().BeNull();
        AccessResults.CheckTresorier(tresorier, false).Should().BeNull();
    }

    [Fact]
    public async Task VoteExpireAdopte_LeCandidatGere_SansModifierLeBureau()
    {
        var b = AvecBureau();
        var t0 = DateTime.UtcNow.AddDays(-10);
        var electeurs = new List<Guid> { _createur.Value, _alice.Value, _bob.Value, Guid.NewGuid(), Guid.NewGuid() };
        var vote = b.ProposerChangementResponsable(_bob.Value, _alice.Value, electeurs, t0);
        b.Voter(vote.Id, _alice.Value, true, t0.AddHours(1));
        b.Voter(vote.Id, _bob.Value, true, t0.AddHours(2));
        b.Voter(vote.Id, _createur.Value, false, t0.AddHours(3));
        var journal = b.Journal.Count;

        (await Acces(_bob)).Niveau.Should().Be(NiveauAcces.Gestionnaire);
        var createur = await Acces(_createur);
        createur.Niveau.Should().NotBe(NiveauAcces.Gestionnaire);
        createur.EstTresorier.Should().BeTrue();

        // Read-only check: the vote is still open and nothing was journaled.
        vote.Statut.Should().Be(StatutVote.Ouvert);
        b.ResponsableId.Should().Be(_createur.Value);
        b.Journal.Should().HaveCount(journal);
    }
}
