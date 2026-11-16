namespace Infrastructure.Persistence.Configurations;

using Domain.Gouvernance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

// Governance of a group (tontine or project). Journal payloads are text, never jsonb: the hash covers the exact string.

internal sealed class BureauConfiguration : IEntityTypeConfiguration<Bureau>
{
    public void Configure(EntityTypeBuilder<Bureau> b)
    {
        b.ToTable("bureaux");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("groupe_id").ValueGeneratedNever();
        b.Property(x => x.Type).HasColumnName("type_groupe").HasMaxLength(20)
            .HasConversion(v => v.ToString(), v => Enum.Parse<TypeGroupe>(v));
        b.Property(x => x.ResponsableId).HasColumnName("responsable_id");
        b.Property(x => x.TresorierId).HasColumnName("tresorier_id");
        b.ComplexProperty(x => x.Regles, r =>
        {
            r.Property(x => x.ModeVote).HasColumnName("regle_mode_vote").HasMaxLength(30)
                .HasConversion(v => v.ToString(), v => Enum.Parse<ModeVote>(v)).HasDefaultValue(ModeVote.MajoriteDesVotants);
            r.Property(x => x.QuorumPourcent).HasColumnName("regle_quorum_pourcent").HasDefaultValue(50);
            r.Property(x => x.DureeVoteJours).HasColumnName("regle_duree_vote_jours").HasDefaultValue(7);
        });
        b.Ignore(x => x.DomainEvents);
        b.HasIndex(x => x.ResponsableId);
        b.HasIndex(x => x.TresorierId);

        b.HasMany(x => x.Votes).WithOne().HasForeignKey("groupe_id").OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Votes).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(x => x.Seances).WithOne().HasForeignKey("groupe_id").OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Seances).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(x => x.Journal).WithOne().HasForeignKey("groupe_id").OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Journal).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class VoteConfiguration : IEntityTypeConfiguration<Vote>
{
    public void Configure(EntityTypeBuilder<Vote> b)
    {
        b.ToTable("votes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.Objet).HasColumnName("objet").HasMaxLength(40).HasConversion(v => v.ToString(), v => Enum.Parse<ObjetVote>(v));
        b.Property(x => x.CandidatId).HasColumnName("candidat_id");
        b.Property(x => x.ProposeParId).HasColumnName("propose_par_id");
        b.Property(x => x.Electeurs).HasColumnName("electeurs").HasColumnType("uuid[]");
        b.Property(x => x.Mode).HasColumnName("mode").HasMaxLength(30).HasConversion(v => v.ToString(), v => Enum.Parse<ModeVote>(v));
        b.Property(x => x.QuorumPourcent).HasColumnName("quorum_pourcent");
        b.Property(x => x.OuvertLe).HasColumnName("ouvert_le");
        b.Property(x => x.FermeLe).HasColumnName("ferme_le");
        b.Property(x => x.Statut).HasColumnName("statut").HasMaxLength(20).HasConversion(v => v.ToString(), v => Enum.Parse<StatutVote>(v));
        b.Property(x => x.ClosLe).HasColumnName("clos_le");
        b.Ignore(x => x.Pour);
        b.Ignore(x => x.Contre);
        b.HasMany(x => x.Bulletins).WithOne().HasForeignKey("vote_id").OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Bulletins).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class BulletinConfiguration : IEntityTypeConfiguration<Bulletin>
{
    public void Configure(EntityTypeBuilder<Bulletin> b)
    {
        b.ToTable("bulletins");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.UtilisateurId).HasColumnName("utilisateur_id");
        b.Property(x => x.Pour).HasColumnName("pour");
        b.Property(x => x.Le).HasColumnName("le");
    }
}

internal sealed class SeanceConfiguration : IEntityTypeConfiguration<Seance>
{
    public void Configure(EntityTypeBuilder<Seance> b)
    {
        b.ToTable("seances");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.Date).HasColumnName("date").HasColumnType("timestamp without time zone");
        b.Property(x => x.Titre).HasColumnName("titre").HasMaxLength(200).IsRequired();
        b.Property(x => x.Lieu).HasColumnName("lieu").HasMaxLength(200);
        b.Property(x => x.Presents).HasColumnName("presents").HasColumnType("text[]");
        b.Property(x => x.OrdreDuJour).HasColumnName("ordre_du_jour").HasMaxLength(4000);
        b.Property(x => x.CompteRendu).HasColumnName("compte_rendu").HasMaxLength(20000).IsRequired();
        b.Property(x => x.Decisions).HasColumnName("decisions").HasColumnType("text[]");
        b.Property(x => x.AuteurId).HasColumnName("auteur_id");
        b.Property(x => x.RedigeeLe).HasColumnName("redigee_le");
        b.HasIndex(x => x.Date);
    }
}

internal sealed class JournalBureauConfiguration : IEntityTypeConfiguration<JournalBureau>
{
    public void Configure(EntityTypeBuilder<JournalBureau> b)
    {
        b.ToTable("journal_bureau");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.Sequence).HasColumnName("sequence");
        b.Property(x => x.Action).HasColumnName("action").HasMaxLength(60);
        b.Property(x => x.Acteur).HasColumnName("acteur").HasMaxLength(100);
        b.Property(x => x.Payload).HasColumnName("payload").HasColumnType("text");
        b.Property(x => x.Horodatage).HasColumnName("horodatage");
        b.Property(x => x.HashPrecedent).HasColumnName("hash_precedent").HasMaxLength(64);
        b.Property(x => x.HashCourant).HasColumnName("hash_courant").HasMaxLength(64);
        b.HasIndex(x => x.Sequence);
    }
}
