namespace Domain.PaymentManagement.ValueObjects;

using Domain.Common;

/// <summary>
/// Value object representing a monetary amount in a specific currency.
/// Minimum amount is 100 FCFA. Only decimal type is used (never float/double).
/// </summary>
public sealed class Montant : ValueObject
{
    public const decimal MontantMinimum = 100m;

    public decimal Valeur { get; }
    public string Devise { get; }

    private Montant(decimal valeur, string devise)
    {
        Valeur = valeur;
        Devise = devise;
    }

    public static Montant Create(decimal valeur, string devise = Domain.Common.Devises.ParDefaut)
    {
        if (valeur < MontantMinimum)
            throw new ArgumentException($"Le montant minimum est {MontantMinimum} {devise}.", nameof(valeur));

        if (string.IsNullOrWhiteSpace(devise))
            throw new ArgumentException("La devise ne peut pas être vide.", nameof(devise));

        return new Montant(valeur, devise);
    }

    /// <summary>
    /// Amount of a payment declared by hand (cash, complement of a partial payment): any positive amount,
    /// so that a small remainder (≤ 100 FCFA) can still be settled. The 100 FCFA minimum stays for Mobile Money.
    /// </summary>
    public static Montant CreateDeclare(decimal valeur, string devise = Domain.Common.Devises.ParDefaut)
    {
        if (valeur <= 0)
            throw new ArgumentException("Le montant doit être supérieur à 0.", nameof(valeur));

        if (string.IsNullOrWhiteSpace(devise))
            throw new ArgumentException("La devise ne peut pas être vide.", nameof(devise));

        return new Montant(valeur, devise);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Valeur;
        yield return Devise;
    }

    public override string ToString() => $"{Valeur} {Devise}";
}
