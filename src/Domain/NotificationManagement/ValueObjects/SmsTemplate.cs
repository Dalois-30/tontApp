namespace Domain.NotificationManagement.ValueObjects;

using System.Globalization;
using System.Text;

/// <summary>
/// French SMS templates for each notification type.
/// All templates are &lt; 160 characters to avoid concatenated SMS (cost x2).
/// </summary>
public static class SmsTemplate
{
    /// <summary>Tour ouvert → notifier le bénéficiaire</summary>
    public static string TourOuvert(string nomTontine, int numeroTour)
        => Truncate($"TontinesApp: Tour {numeroTour} de {nomTontine} est ouvert. Vous etes le beneficiaire. Bonne reception!");

    /// <summary>Versement confirmé → accusé de réception</summary>
    public static string VersementConfirme(decimal montant, string devise, string nomTontine, string reference)
        => Truncate($"TontinesApp: Paiement de {Montant(montant)} {devise} recu pour {nomTontine}. Ref: {reference}. Merci!");

    /// <summary>Rappel J-3 avant échéance</summary>
    public static string RappelJ3(string nomTontine, decimal montant, string devise)
        => Truncate($"TontinesApp: Rappel - votre cotisation de {Montant(montant)} {devise} pour {nomTontine} est due dans 3 jours.");

    /// <summary>Rappel J-1 avant échéance</summary>
    public static string RappelJ1(string nomTontine, decimal montant, string devise)
        => Truncate($"TontinesApp: Rappel - cotisation de {Montant(montant)} {devise} pour {nomTontine} due demain. Merci de payer.");

    /// <summary>
    /// Recap to the bureau (responsable, trésorier) of the members late on a round and the total they still
    /// owe. Always one GSM-7 SMS: the tontine name is shortened first so that the round number, the count,
    /// the total and "+N autre(s)" always fit; names are then dropped from the end ("+N autres").
    /// Tontine and member names are transliterated to GSM-7.
    /// </summary>
    public static string RecapRetardsBureau(string nomTontine, int numeroTour, IReadOnlyList<string> nomsEnRetard,
        decimal totalDu, string devise)
    {
        var noms = nomsEnRetard.Select(Gsm7).ToList();
        var k = noms.Count;
        string Corps(string nom) => $"TontinesApp: Retards {nom} tour {numeroTour}: {k} en retard, {Montant(totalDu)} {devise} du.";
        var suffixeMax = k == 0 ? "" : " " + Autres(k);
        var place = Math.Max(1, ContenuMessage.MaxLength - Corps("").Length - suffixeMax.Length);
        var nom = Gsm7(nomTontine);
        if (nom.Length > place)
            nom = nom[..place].TrimEnd();
        var corps = Corps(nom);

        for (var gardes = k; gardes > 0; gardes--)
        {
            var autres = k - gardes;
            var message = corps + " " + string.Join(", ", noms.Take(gardes)) + (autres == 0 ? "" : " " + Autres(autres));
            if (message.Length <= ContenuMessage.MaxLength)
                return message;
        }
        return Truncate(corps + suffixeMax);
    }

    private static string Autres(int n) => n == 1 ? "+1 autre" : $"+{n} autres";

    /// <summary>Amount as written in every SMS: invariant, no thousands separator, decimals only if any ("5000", "2500.5").</summary>
    public static string Montant(decimal montant) => montant.ToString("0.##", CultureInfo.InvariantCulture);

    // GSM 03.38 basic character set (one 7-bit character each); anything else switches the SMS to UCS-2 (70 chars).
    private const string Gsm7Base =
        "@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞÆæßÉ !\"#¤%&'()*+,-./0123456789:;<=>?¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà";

    /// <summary>
    /// Keeps a text within the GSM-7 basic set: unsupported accents are stripped ("ê" → "e", "ç" → "c"),
    /// other unsupported characters become "?".
    /// </summary>
    public static string Gsm7(string texte)
    {
        var sb = new StringBuilder(texte.Length);
        foreach (var rune in texte.EnumerateRunes())
        {
            var c = rune.ToString();
            if (c.Length == 1 && Gsm7Base.Contains(c[0])) { sb.Append(c); continue; }
            var lettres = c.Normalize(NormalizationForm.FormD)
                .Where(x => CharUnicodeInfo.GetUnicodeCategory(x) != UnicodeCategory.NonSpacingMark).ToArray();
            sb.Append(lettres.Length > 0 && lettres.All(x => Gsm7Base.Contains(x)) ? new string(lettres) : "?");
        }
        return sb.ToString();
    }

    /// <summary>Récapitulatif hebdomadaire</summary>
    public static string RecapHebdomadaire(string nomTontine, int membresAJour, int totalMembres)
        => Truncate($"TontinesApp: Recap {nomTontine} - {membresAJour}/{totalMembres} membres a jour cette semaine.");

    /// <summary>Bienvenue</summary>
    public static string Bienvenue(string nomTontine)
        => Truncate($"TontinesApp: Bienvenue dans la tontine {nomTontine}! Bonne epargne avec votre groupe.");

    /// <summary>Confirmation d'abonnement</summary>
    public static string ConfirmationAbonnement(string plan, decimal montant, string devise)
        => Truncate($"TontinesApp: Abonnement {plan} active! Montant: {Montant(montant)} {devise}/mois. Merci de votre confiance.");

    /// <summary>Rappel de renouvellement J-3</summary>
    public static string RappelRenouvellement(string plan, decimal montant, string devise, DateTime dateFin)
        => Truncate($"TontinesApp: Votre abonnement {plan} expire le {dateFin:dd/MM}. Renouvellement: {Montant(montant)} {devise}.");

    /// <summary>Membre suspendu → notifier le membre</summary>
    public static string MembreSuspendu(string nomTontine, string motif)
        => Truncate($"TontinesApp: Vous etes suspendu de {nomTontine}. Motif: {motif}. Contactez le gestionnaire.");

    /// <summary>Renouvellement réussi</summary>
    public static string RenouvellementReussi(string plan, DateTime nouvelleDateFin)
        => Truncate($"TontinesApp: Abonnement {plan} renouvele jusqu'au {nouvelleDateFin:dd/MM/yyyy}. Merci!");

    /// <summary>Échec de renouvellement</summary>
    public static string RenouvellementEchoue(string plan)
        => Truncate($"TontinesApp: Echec du renouvellement {plan}. Verifiez votre solde. Grace de 3 jours accordee.");

    private static string Truncate(string message)
        => message.Length > ContenuMessage.MaxLength
            ? message[..ContenuMessage.MaxLength]
            : message;
}
