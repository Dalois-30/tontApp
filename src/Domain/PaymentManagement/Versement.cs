namespace Domain.PaymentManagement;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Domain.Common;
using Domain.PaymentManagement.Entities;
using Domain.PaymentManagement.Events;
using Domain.PaymentManagement.ValueObjects;
using Domain.TontineManagement.ValueObjects;

public class Versement : AggregateRoot<VersementId>
{
    private readonly List<AuditEntry> _auditTrail = [];

    public TontineId TontineId { get; private set; }
    public TourId TourId { get; private set; }
    public PayeurId PayeurId { get; private set; }
    public Montant Montant { get; private set; }
    public VersementStatus Statut { get; private set; }
    public string? ReferenceExterne { get; private set; }
    public string HashPrecedent { get; private set; }
    public string HashCourant { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? ConfirmedAt { get; private set; }
    public IReadOnlyCollection<AuditEntry> AuditTrail => _auditTrail.AsReadOnly();

    private Versement() : base()
    {
        TontineId = default!;
        TourId = default!;
        PayeurId = default!;
        Montant = default!;
        HashPrecedent = string.Empty;
        HashCourant = string.Empty;
    }

    private Versement(
        VersementId id,
        TontineId tontineId,
        TourId tourId,
        PayeurId payeurId,
        Montant montant,
        string hashPrecedent) : base(id)
    {
        TontineId = tontineId;
        TourId = tourId;
        PayeurId = payeurId;
        Montant = montant;
        Statut = VersementStatus.EnAttente;
        HashPrecedent = hashPrecedent;
        CreatedAt = Horloge.MaintenantUtc();
        HashCourant = CalculerHash(id, montant, CreatedAt, hashPrecedent);
    }

    public static Versement Create(
        TontineId tontineId,
        TourId tourId,
        PayeurId payeurId,
        Montant montant,
        string hashPrecedent = "",
        string? hashPrecedentAudit = null)
    {
        var versement = new Versement(
            VersementId.Create(),
            tontineId,
            tourId,
            payeurId,
            montant,
            hashPrecedent);

        var payload = JsonSerializer.Serialize(new
        {
            TontineId = versement.TontineId.Value,
            TourId = versement.TourId.Value,
            PayeurId = versement.PayeurId.Value,
            Montant = versement.Montant.Valeur,
            Devise = versement.Montant.Devise
        });

        var auditHash = hashPrecedentAudit ?? AuditEntry.GenesisHash;
        versement.AddAuditEntry("system", AuditAction.VersementCree, payload, auditHash);

        versement.AddDomainEvent(new VersementCreatedEvent(
            versement.Id,
            tontineId,
            payeurId,
            montant.Valeur));

        return versement;
    }

    public void Confirmer(string referenceExterne, string? hashPrecedentAudit = null, string acteurId = "system")
    {
        if (Statut != VersementStatus.EnAttente)
            throw new InvalidOperationException("Only a pending versement can be confirmed.");

        Statut = VersementStatus.Confirme;
        ConfirmedAt = DateTime.UtcNow;
        ReferenceExterne = referenceExterne;

        var payload = JsonSerializer.Serialize(new { ReferenceExterne = referenceExterne });
        var auditHash = hashPrecedentAudit ?? GetLastAuditHash();
        AddAuditEntry(acteurId, AuditAction.VersementConfirme, payload, auditHash);

        AddDomainEvent(new VersementConfirmedEvent(
            Id,
            TontineId,
            PayeurId,
            TourId,
            Montant.Valeur,
            referenceExterne));
    }

    public void Rejeter(string raison, string? hashPrecedentAudit = null, string acteurId = "system")
    {
        if (Statut != VersementStatus.EnAttente)
            throw new InvalidOperationException("Only a pending versement can be rejected.");

        Statut = VersementStatus.Echoue;

        var payload = JsonSerializer.Serialize(new { Raison = raison });
        var auditHash = hashPrecedentAudit ?? GetLastAuditHash();
        AddAuditEntry(acteurId, AuditAction.VersementRejete, payload, auditHash);

        AddDomainEvent(new VersementRejectedEvent(Id, TontineId, PayeurId, raison));
    }

    /// <summary>
    /// Appends an audit entry chained on this versement's last entry (e.g. proof details of a manual
    /// entry). Must be called right after <see cref="Create"/> / <see cref="Confirmer"/> within the same
    /// unit of work so that the tontine-level chain stays linear.
    /// </summary>
    public void AjouterEntreeAudit(AuditAction action, string acteurId, string payload)
    {
        AddAuditEntry(acteurId, action, payload, GetLastAuditHash());
    }

    public bool VerifierIntegrite()
    {
        // Verify the versement's own hash
        var expectedHash = CalculerHash(Id, Montant, CreatedAt, HashPrecedent);
        if (HashCourant != expectedHash)
            return false;

        // Each entry must be self-consistent (content + its recorded predecessor hash).
        // Linkage between entries is verified at tontine level (IAuditTrailService.VerifierChaine),
        // because a versement's entries are interleaved with other versements' entries in that chain.
        foreach (var entry in _auditTrail)
        {
            if (!entry.VerifyIntegrity(entry.HashPrecedent))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Computes a SHA-256 hash over (id + montant + date + hashPrecedent).
    /// </summary>
    public static string CalculerHash(VersementId id, Montant montant, DateTime date, string hashPrecedent)
    {
        var input = $"{id.Value}{montant.Valeur:F2}{date:O}{hashPrecedent}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }

    private void AddAuditEntry(string acteurId, AuditAction action, string payload, string hashPrecedent)
    {
        var entry = AuditEntry.Create(TontineId, Id, action, acteurId, payload, hashPrecedent);
        _auditTrail.Add(entry);
    }

    private string GetLastAuditHash()
    {
        return _auditTrail.Count > 0
            ? _auditTrail[^1].HashCourant
            : AuditEntry.GenesisHash;
    }
}
