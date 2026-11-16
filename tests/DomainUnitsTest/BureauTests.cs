namespace DomainUnitsTest;

using Domain.Gouvernance;

public class BureauTests
{
    private static readonly DateTime T0 = new(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Createur = Guid.NewGuid();

    private static (Bureau, List<Guid>) Nouveau(int membres = 5, ReglesGouvernance? regles = null)
    {
        var b = Bureau.Creer(Guid.NewGuid(), TypeGroupe.Projet, Createur);
        if (regles is not null) b.ModifierRegles(regles, Createur.ToString());
        var liste = new List<Guid> { Createur };
        liste.AddRange(Enumerable.Range(1, membres - 1).Select(_ => Guid.NewGuid()));
        return (b, liste);
    }

    [Fact]
    public void Creator_HoldsBothRoles_AndNamesTheTreasurer()
    {
        var (b, m) = Nouveau();
        Assert.True(b.EstResponsable(Createur));
        Assert.True(b.EstTresorier(Createur));

        b.NommerTresorier(m[1], Createur.ToString());

        Assert.True(b.EstTresorier(m[1]));
        Assert.False(b.EstTresorier(Createur));
        Assert.Throws<InvalidOperationException>(() => b.NommerTresorier(m[1], Createur.ToString()));
        Assert.True(b.VerifierJournal());
    }

    [Fact]
    public void Vote_AbsoluteMajorityOfYes_ChangesTheResponsableAtOnce()
    {
        var (b, m) = Nouveau(5);
        var vote = b.ProposerChangementResponsable(m[2], m[1], m, T0);

        b.Voter(vote.Id, m[1], true, T0.AddHours(1));
        b.Voter(vote.Id, m[2], true, T0.AddHours(2));
        Assert.Equal(StatutVote.Ouvert, vote.Statut);
        b.Voter(vote.Id, m[3], true, T0.AddHours(3)); // 3 of 5: settled

        Assert.Equal(StatutVote.Adopte, vote.Statut);
        Assert.True(b.EstResponsable(m[2]));
        Assert.Contains(b.Journal, j => j.Action == "ResponsableChange");
        Assert.True(b.VerifierJournal());
    }

    [Fact]
    public void Vote_MajorityOfVoters_AtDeadline_NeedsTheQuorum()
    {
        // 6 electors, quorum 50 %: 2 yes / 1 no = 3 voters -> quorum reached, adopted at the deadline.
        var (b, m) = Nouveau(6);
        var vote = b.ProposerChangementResponsable(m[1], m[1], m, T0);
        b.Voter(vote.Id, m[1], true, T0);
        b.Voter(vote.Id, m[2], true, T0);
        b.Voter(vote.Id, m[3], false, T0);

        b.Actualiser(T0.AddDays(6));
        Assert.Equal(StatutVote.Ouvert, vote.Statut);
        b.Actualiser(T0.AddDays(7));
        Assert.Equal(StatutVote.Adopte, vote.Statut);
        Assert.True(b.EstResponsable(m[1]));
    }

    [Fact]
    public void Vote_WithoutQuorum_IsRejectedAtDeadline()
    {
        var (b, m) = Nouveau(6);
        var vote = b.ProposerChangementResponsable(m[1], m[1], m, T0);
        b.Voter(vote.Id, m[1], true, T0); // 1 voter of 6 < 50 %

        b.Actualiser(T0.AddDays(8));

        Assert.Equal(StatutVote.Rejete, vote.Statut);
        Assert.True(b.EstResponsable(Createur));
    }

    [Fact]
    public void Vote_HalfSayNo_IsRejectedAtOnce_AndBallotCanChangeWhileOpen()
    {
        var (b, m) = Nouveau(4);
        var vote = b.ProposerChangementResponsable(m[3], m[3], m, T0);
        b.Voter(vote.Id, m[1], true, T0);
        b.Voter(vote.Id, m[1], false, T0.AddMinutes(5)); // changes their mind
        Assert.Equal(0, vote.Pour);
        b.Voter(vote.Id, m[2], false, T0.AddMinutes(6)); // 2 no of 4: yes cannot win

        Assert.Equal(StatutVote.Rejete, vote.Statut);
        Assert.Throws<InvalidOperationException>(() => b.Voter(vote.Id, m[3], true, T0.AddMinutes(7)));
    }

    [Fact]
    public void Vote_OnlyMembers_OneOpenVoteAtATime()
    {
        var (b, m) = Nouveau(4);
        var etranger = Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(() => b.ProposerChangementResponsable(m[1], etranger, m, T0));
        Assert.Throws<InvalidOperationException>(() => b.ProposerChangementResponsable(etranger, m[1], m, T0));
        Assert.Throws<InvalidOperationException>(() => b.ProposerChangementResponsable(Createur, m[1], m, T0));

        var vote = b.ProposerChangementResponsable(m[1], m[2], m, T0);
        Assert.Throws<InvalidOperationException>(() => b.ProposerChangementResponsable(m[2], m[2], m, T0));
        Assert.Throws<InvalidOperationException>(() => b.Voter(vote.Id, etranger, true, T0));
    }

    [Fact]
    public void TwoThirds_RequiresTwoThirdsOfAllMembers()
    {
        var (b, m) = Nouveau(6, new ReglesGouvernance { ModeVote = ModeVote.DeuxTiersDesMembres });
        var vote = b.ProposerChangementResponsable(m[1], m[1], m, T0);
        foreach (var u in m.Take(3)) b.Voter(vote.Id, u, true, T0);
        Assert.Equal(StatutVote.Ouvert, vote.Statut);
        b.Voter(vote.Id, m[3], true, T0); // 4 of 6
        Assert.Equal(StatutVote.Adopte, vote.Statut);
    }

    [Fact]
    public void Seance_AnyMemberWritesIt_DecisionsAreJournaled()
    {
        var (b, m) = Nouveau();
        var s = b.RedigerSeance(T0.AddDays(-2), "Assemblée de novembre", "Douala", ["Grace", "Dalois", ""], "Point financier",
            "Le trésorier présente les comptes.", ["Relancer les retardataires", "Acheter les médicaments"], m[4], T0);

        Assert.Equal(2, s.Presents.Count);
        Assert.Equal(2, s.Decisions.Count);
        Assert.Contains(b.Journal, j => j.Action == "SeanceRedigee" && j.Payload.Contains("Relancer les retardataires"));
        Assert.Throws<InvalidOperationException>(() => b.RedigerSeance(T0.AddDays(3), "Futur", null, [], null, "x", [], m[1], T0));
        Assert.Throws<ArgumentException>(() => b.RedigerSeance(T0, "Vide", null, [], null, " ", [], m[1], T0));
    }

    [Fact]
    public void RolesEffectifs_AppliesAnExpiredVote_WithoutModifyingTheBureau()
    {
        var (b, m) = Nouveau(5);
        var vote = b.ProposerChangementResponsable(m[2], m[1], m, T0);
        b.Voter(vote.Id, m[1], true, T0.AddHours(1));
        b.Voter(vote.Id, m[2], true, T0.AddHours(2));
        b.Voter(vote.Id, m[3], false, T0.AddHours(3));
        var journal = b.Journal.Count;

        // Before the deadline: undecided, nothing changes.
        Assert.Equal((Createur, Createur), b.RolesEffectifs(T0.AddDays(1)));
        // After it: quorum reached and more yes than no, the candidate would be responsable...
        Assert.Equal((m[2], Createur), b.RolesEffectifs(T0.AddDays(8)));
        // ...but the bureau itself is untouched until a governance use case concludes the vote.
        Assert.Equal(StatutVote.Ouvert, vote.Statut);
        Assert.True(b.EstResponsable(Createur));
        Assert.Equal(journal, b.Journal.Count);

        b.Actualiser(T0.AddDays(8));
        Assert.True(b.EstResponsable(m[2]));
    }

    [Fact]
    public void RolesEffectifs_IgnoresAnExpiredRejectedVote()
    {
        var (b, m) = Nouveau(5);
        var vote = b.ProposerChangementResponsable(m[2], m[1], m, T0);
        b.Voter(vote.Id, m[1], true, T0.AddHours(1)); // quorum not reached at the deadline

        Assert.Equal((Createur, Createur), b.RolesEffectifs(T0.AddDays(8)));
    }
}
