namespace Domain.TontineManagement.ValueObjects;

public enum StatutMembre
{
    Actif,
    Suspendu,
    /// <summary>Joined an already started tontine; waits for the gestionnaire's approval.</summary>
    EnAttente
}
