namespace Infrastructure.Persistence.Configurations;

using Domain.IdentityManagement.ValueObjects;
using Domain.ProjectManagement;
using Domain.TontineManagement.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

// Dates taken from the schedule (DateDebut, ApplicableDes) are calendar dates without time zone;
// instants (UTC) use timestamptz. Journal payloads are text, never jsonb: the hash covers the exact string.

internal sealed class ProjetConfiguration : IEntityTypeConfiguration<Projet>
{
    public void Configure(EntityTypeBuilder<Projet> b)
    {
        b.ToTable("projets");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).HasColumnName("id").HasConversion(id => id.Value, v => ProjetId.From(v)).ValueGeneratedNever();
        b.Property(p => p.Nom).HasColumnName("nom").HasMaxLength(150).IsRequired();
        b.Property(p => p.Description).HasColumnName("description").HasMaxLength(1000);
        b.Property(p => p.Objectif).HasColumnName("objectif").HasPrecision(18, 2);
        b.Property(p => p.Devise).HasColumnName("devise").HasMaxLength(3);
        b.Property(p => p.Periodicite).HasColumnName("periodicite").HasMaxLength(20)
            .HasConversion(v => v.ToString(), v => Enum.Parse<TontinePeriodicity>(v));
        b.Property(p => p.DateDebut).HasColumnName("date_debut").HasColumnType("timestamp without time zone");
        b.Property(p => p.NombreEcheances).HasColumnName("nombre_echeances");
        b.Property(p => p.Statut).HasColumnName("statut").HasMaxLength(20)
            .HasConversion(v => v.ToString(), v => Enum.Parse<StatutProjet>(v));
        b.Property(p => p.GestionnaireId).HasColumnName("gestionnaire_id")
            .HasConversion(id => id.Value, v => UtilisateurId.From(v));
        b.Property(p => p.CreatedAt).HasColumnName("created_at");
        b.Property(p => p.CodeInvitationHash).HasColumnName("code_invitation_hash").HasMaxLength(128);
        b.Property(p => p.CodeInvitationExpireLe).HasColumnName("code_invitation_expire_le");
        b.HasIndex(p => p.CodeInvitationHash);
        b.HasIndex(p => p.GestionnaireId);

        b.Property<List<decimal>>("_paliers").HasColumnName("paliers").HasColumnType("numeric(18,2)[]");
        b.Ignore(p => p.Paliers);
        b.Ignore(p => p.DomainEvents);
        b.Ignore(p => p.TotalCollecte);
        b.Ignore(p => p.TotalDepense);
        b.Ignore(p => p.Solde);

        b.HasMany(p => p.Participants).WithOne().HasForeignKey("projet_id").OnDelete(DeleteBehavior.Cascade);
        b.Navigation(p => p.Participants).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(p => p.Paiements).WithOne().HasForeignKey("projet_id").OnDelete(DeleteBehavior.Cascade);
        b.Navigation(p => p.Paiements).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(p => p.Depenses).WithOne().HasForeignKey("projet_id").OnDelete(DeleteBehavior.Cascade);
        b.Navigation(p => p.Depenses).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(p => p.Journal).WithOne().HasForeignKey("projet_id").OnDelete(DeleteBehavior.Cascade);
        b.Navigation(p => p.Journal).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ParticipantConfiguration : IEntityTypeConfiguration<Participant>
{
    public void Configure(EntityTypeBuilder<Participant> b)
    {
        b.ToTable("participants_projet");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(p => p.Nom).HasColumnName("nom").HasMaxLength(100).IsRequired();
        b.Property(p => p.UtilisateurId).HasColumnName("utilisateur_id")
            .HasConversion(id => id != null ? id.Value : (Guid?)null, v => v.HasValue ? UtilisateurId.From(v.Value) : null);
        b.Property(p => p.DateAdhesion).HasColumnName("date_adhesion");
        b.Ignore(p => p.BaisseEnAttente);
        b.HasMany(p => p.Changements).WithOne().HasForeignKey("participant_id").OnDelete(DeleteBehavior.Cascade);
        b.Navigation(p => p.Changements).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ChangementPalierConfiguration : IEntityTypeConfiguration<ChangementPalier>
{
    public void Configure(EntityTypeBuilder<ChangementPalier> b)
    {
        b.ToTable("changements_palier");
        b.HasKey(c => c.Id);
        b.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(c => c.Palier).HasColumnName("palier").HasPrecision(18, 2);
        b.Property(c => c.ApplicableDes).HasColumnName("applicable_des").HasColumnType("timestamp without time zone");
        b.Property(c => c.Statut).HasColumnName("statut").HasMaxLength(20)
            .HasConversion(v => v.ToString(), v => Enum.Parse<StatutChangementPalier>(v));
        b.Property(c => c.DemandeLe).HasColumnName("demande_le");
        b.Property(c => c.EffectifLe).HasColumnName("effectif_le");
    }
}

internal sealed class PaiementProjetConfiguration : IEntityTypeConfiguration<PaiementProjet>
{
    public void Configure(EntityTypeBuilder<PaiementProjet> b)
    {
        b.ToTable("paiements_projet");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(p => p.ParticipantId).HasColumnName("participant_id");
        b.Property(p => p.Montant).HasColumnName("montant").HasPrecision(18, 2);
        b.Property(p => p.Preuve).HasColumnName("preuve").HasMaxLength(500);
        b.Property(p => p.Reference).HasColumnName("reference").HasMaxLength(100);
        b.Property(p => p.Statut).HasColumnName("statut").HasMaxLength(20)
            .HasConversion(v => v.ToString(), v => Enum.Parse<StatutOperation>(v));
        b.Property(p => p.SaisiLe).HasColumnName("saisi_le");
        b.Property(p => p.SaisiPar).HasColumnName("saisi_par").HasMaxLength(100);
        b.Property(p => p.DecideLe).HasColumnName("decide_le");
        b.Property(p => p.DecidePar).HasColumnName("decide_par").HasMaxLength(100);
        b.Property(p => p.Motif).HasColumnName("motif").HasMaxLength(500);
    }
}

internal sealed class DepenseConfiguration : IEntityTypeConfiguration<Depense>
{
    public void Configure(EntityTypeBuilder<Depense> b)
    {
        b.ToTable("depenses_projet");
        b.HasKey(d => d.Id);
        b.Property(d => d.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(d => d.Libelle).HasColumnName("libelle").HasMaxLength(200);
        b.Property(d => d.Montant).HasColumnName("montant").HasPrecision(18, 2);
        b.Property(d => d.Justificatif).HasColumnName("justificatif").HasMaxLength(500);
        b.Property(d => d.Statut).HasColumnName("statut").HasMaxLength(20)
            .HasConversion(v => v.ToString(), v => Enum.Parse<StatutOperation>(v));
        b.Property(d => d.ProposeeLe).HasColumnName("proposee_le");
        b.Property(d => d.ProposeePar).HasColumnName("proposee_par").HasMaxLength(100);
        b.Property(d => d.DecideeLe).HasColumnName("decidee_le");
        b.Property(d => d.DecideePar).HasColumnName("decidee_par").HasMaxLength(100);
        b.Property(d => d.Motif).HasColumnName("motif").HasMaxLength(500);
    }
}

internal sealed class JournalProjetConfiguration : IEntityTypeConfiguration<JournalProjet>
{
    public void Configure(EntityTypeBuilder<JournalProjet> b)
    {
        b.ToTable("journal_projet");
        b.HasKey(j => j.Id);
        b.Property(j => j.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(j => j.Sequence).HasColumnName("sequence");
        b.Property(j => j.Action).HasColumnName("action").HasMaxLength(50);
        b.Property(j => j.Acteur).HasColumnName("acteur").HasMaxLength(100);
        b.Property(j => j.Payload).HasColumnName("payload").HasColumnType("text");
        b.Property(j => j.Horodatage).HasColumnName("horodatage");
        b.Property(j => j.HashPrecedent).HasColumnName("hash_precedent").HasMaxLength(64);
        b.Property(j => j.HashCourant).HasColumnName("hash_courant").HasMaxLength(64);
        b.HasIndex(j => j.Sequence);
    }
}
