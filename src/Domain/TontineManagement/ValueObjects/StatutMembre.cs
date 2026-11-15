namespace Domain.TontineManagement.ValueObjects;

public enum StatutMembre
{
    Actif,
    Suspendu,
    /// <summary>Joined an already started tontine; waits for the gestionnaire's approval.</summary>
    EnAttente
}

/// <summary>Whether people can join a tontine whose cycle has started.</summary>
public enum ModeAdhesionEnCours
{
    Interdite,
    AvecValidation,
    Libre
}
