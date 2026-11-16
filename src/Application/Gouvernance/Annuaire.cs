namespace Application.Gouvernance;

using Domain.Gouvernance;
using Domain.IdentityManagement.Repositories;
using Domain.IdentityManagement.ValueObjects;
using Domain.ProjectManagement;

/// <summary>
/// Turns the user ids stored in journals ("acteur") into the names members know, so that everyone can
/// read who did what. Participants are named as in the group's register; others by their account.
/// </summary>
public sealed class Annuaire
{
    public const string Systeme = "systeme";
    private readonly IUtilisateurRepository _utilisateurs;

    public Annuaire(IUtilisateurRepository utilisateurs) => _utilisateurs = utilisateurs;

    public Task<Dictionary<string, string>> NomsAsync(Projet p, Bureau b, CancellationToken ct) =>
        NomsAsync(
            p.Participants.Where(x => x.UtilisateurId is not null).Select(x => (x.UtilisateurId!.Value, x.Nom)),
            p.Journal.Select(j => j.Acteur)
                .Concat(p.Paiements.SelectMany(x => new[] { x.SaisiPar, x.DecidePar }))
                .Concat(b.Journal.Select(j => j.Acteur))
                .Concat([b.ResponsableId.ToString(), b.TresorierId.ToString()]),
            ct);

    public async Task<Dictionary<string, string>> NomsAsync(IEnumerable<(Guid Id, string Nom)> connus, IEnumerable<string?> acteurs, CancellationToken ct)
    {
        var noms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, nom) in connus) noms.TryAdd(id.ToString(), nom);
        foreach (var a in acteurs.Where(a => a is not null).Distinct())
        {
            if (noms.ContainsKey(a!) || !Guid.TryParse(a, out var id)) continue;
            var u = await _utilisateurs.GetByIdAsync(UtilisateurId.From(id), ct);
            noms[a!] = u?.Nom ?? "Compte supprimé";
        }
        return noms;
    }

    public string Nom(IReadOnlyDictionary<string, string> noms, string? acteur) => acteur switch
    {
        null => "",
        Systeme => "Décision automatique",
        _ => noms.TryGetValue(acteur, out var n) ? n : "Inconnu",
    };
}
