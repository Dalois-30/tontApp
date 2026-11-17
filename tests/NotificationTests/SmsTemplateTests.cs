using Domain.NotificationManagement.ValueObjects;

namespace NotificationTests;

public class SmsTemplateTests
{
    [Fact]
    public void TourOuvert_ReturnsMessageUnder160Chars()
    {
        var message = SmsTemplate.TourOuvert("Tontine Solidaire Burkina", 5);

        Assert.True(message.Length <= ContenuMessage.MaxLength,
            $"Message length {message.Length} exceeds max {ContenuMessage.MaxLength}");
        Assert.Contains("Tour 5", message);
        Assert.Contains("Tontine Solidaire Burkina", message);
    }

    [Fact]
    public void VersementConfirme_ReturnsMessageUnder160Chars()
    {
        var message = SmsTemplate.VersementConfirme(50000, "XOF", "Tontine Solidaire Ouagadougou", "TX-123456");

        Assert.True(message.Length <= ContenuMessage.MaxLength,
            $"Message length {message.Length} exceeds max {ContenuMessage.MaxLength}");
        Assert.Contains("50000", message);
        Assert.Contains("XOF", message);
        Assert.Contains("TX-123456", message);
    }

    [Fact]
    public void RappelJ3_ReturnsMessageUnder160Chars()
    {
        var message = SmsTemplate.RappelJ3("Tontine du Quartier Nord", 10000, "XOF");

        Assert.True(message.Length <= ContenuMessage.MaxLength,
            $"Message length {message.Length} exceeds max {ContenuMessage.MaxLength}");
        Assert.Contains("3 jours", message);
        Assert.Contains("10000", message);
    }

    [Fact]
    public void RappelJ1_ReturnsMessageUnder160Chars()
    {
        var message = SmsTemplate.RappelJ1("Tontine du Quartier Nord", 10000, "XOF");

        Assert.True(message.Length <= ContenuMessage.MaxLength,
            $"Message length {message.Length} exceeds max {ContenuMessage.MaxLength}");
        Assert.Contains("demain", message);
    }

    [Fact]
    public void RecapHebdomadaire_ReturnsMessageUnder160Chars()
    {
        var message = SmsTemplate.RecapHebdomadaire("Tontine Famille Ouedraogo", 8, 10);

        Assert.True(message.Length <= ContenuMessage.MaxLength,
            $"Message length {message.Length} exceeds max {ContenuMessage.MaxLength}");
        Assert.Contains("8/10", message);
    }

    [Fact]
    public void Bienvenue_ReturnsMessageUnder160Chars()
    {
        var message = SmsTemplate.Bienvenue("Tontine des Amis de Bobo-Dioulasso");

        Assert.True(message.Length <= ContenuMessage.MaxLength,
            $"Message length {message.Length} exceeds max {ContenuMessage.MaxLength}");
        Assert.Contains("Bienvenue", message);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("Very Long Tontine Name That Could Possibly Exceed Character Limit With Full Details")]
    public void AllTemplates_NeverExceed160Chars(string longName)
    {
        var templates = new[]
        {
            SmsTemplate.TourOuvert(longName, 99),
            SmsTemplate.VersementConfirme(999999.99m, "XOF", longName, "REF-999"),
            SmsTemplate.RappelJ3(longName, 999999.99m, "XOF"),
            SmsTemplate.RappelJ1(longName, 999999.99m, "XOF"),
            SmsTemplate.RecapHebdomadaire(longName, 999, 999),
            SmsTemplate.Bienvenue(longName)
        };

        foreach (var template in templates)
        {
            Assert.True(template.Length <= ContenuMessage.MaxLength,
                $"Template exceeds {ContenuMessage.MaxLength} chars: '{template}' ({template.Length} chars)");
        }
    }

    // ── Montants : un seul format pour tous les gabarits ────────────

    [Fact]
    public void RappelJ3_TexteExact_MontantFormatePartage()
    {
        Assert.Equal("TontinesApp: Rappel - votre cotisation de 3000 XAF pour Tontine du quartier est due dans 3 jours.",
            SmsTemplate.RappelJ3("Tontine du quartier", 3000.00m, "XAF"));
    }

    [Fact]
    public void RappelJ1_TexteExact_MontantFormatePartage()
    {
        Assert.Equal("TontinesApp: Rappel - cotisation de 2500.5 XAF pour Tontine du quartier due demain. Merci de payer.",
            SmsTemplate.RappelJ1("Tontine du quartier", 2500.50m, "XAF"));
    }

    [Theory]
    [InlineData("5000.00", "5000")]
    [InlineData("13000", "13000")]
    [InlineData("2500.5", "2500.5")]
    [InlineData("999999.99", "999999.99")]
    public void Montant_InvariantSansSeparateur(string valeur, string attendu)
    {
        Assert.Equal(attendu, SmsTemplate.Montant(decimal.Parse(valeur, System.Globalization.CultureInfo.InvariantCulture)));
    }

    // ── Récapitulatif des retards au bureau ─────────────────────────

    [Fact]
    public void RecapRetardsBureau_TexteExact_NomsEtTotal()
    {
        var message = SmsTemplate.RecapRetardsBureau("Tontine du quartier", 3, ["Bruno Mbarga", "Chloé Fotso", "David Kamga"], 13000.00m, "XAF");

        Assert.Equal("TontinesApp: Retards Tontine du quartier tour 3: 3 en retard, 13000 XAF du. Bruno Mbarga, Chloé Fotso, David Kamga", message);
    }

    [Fact]
    public void RecapRetardsBureau_TropDeNoms_TronqueAvecPlusNAutres_EtResteDansUnSms()
    {
        var noms = Enumerable.Range(1, 20).Select(i => $"Membre Numero {i:00}").ToList();

        var message = SmsTemplate.RecapRetardsBureau("Tontine Solidaire Ouagadougou", 12, noms, 100000.00m, "XOF");

        Assert.True(message.Length <= ContenuMessage.MaxLength, $"{message.Length} chars: {message}");
        Assert.Contains("20 en retard, 100000 XOF du.", message);
        Assert.Contains("Membre Numero 01", message);
        Assert.DoesNotContain("Membre Numero 20", message);
        var gardes = noms.Count(n => message.Contains(n));
        Assert.EndsWith($" +{20 - gardes} autres", message);
    }

    [Fact]
    public void RecapRetardsBureau_UnSeulAutre_AuSingulier()
    {
        var message = SmsTemplate.RecapRetardsBureau("T", 1, ["Ana", new string('x', 110)], 10000m, "XAF");

        Assert.Equal("TontinesApp: Retards T tour 1: 2 en retard, 10000 XAF du. Ana +1 autre", message);
    }

    [Fact]
    public void RecapRetardsBureau_NomDeTontineTresLong_LeNomEstRaccourci_LeResteToujoursPresent()
    {
        var message = SmsTemplate.RecapRetardsBureau(new string('T', 200), 12, ["Ana", "Bob"], 10000m, "XAF");

        Assert.True(message.Length <= ContenuMessage.MaxLength, $"{message.Length} chars: {message}");
        Assert.StartsWith("TontinesApp: Retards TTT", message);
        Assert.Contains(" tour 12: 2 en retard, 10000 XAF du.", message);
        Assert.True(message.EndsWith(" +2 autres") || message.EndsWith(" +1 autre") || message.EndsWith("Ana, Bob"), message);
    }

    [Fact]
    public void RecapRetardsBureau_NomTresLongEtUnSeulRetard_SingulierDansLeRepli()
    {
        var message = SmsTemplate.RecapRetardsBureau(new string('T', 200), 1, [new string('n', 150)], 5000m, "XAF");

        Assert.True(message.Length <= ContenuMessage.MaxLength, $"{message.Length} chars: {message}");
        Assert.EndsWith(" 1 en retard, 5000 XAF du. +1 autre", message);
    }

    [Fact]
    public void RecapRetardsBureau_TranslittereEnGsm7()
    {
        var message = SmsTemplate.RecapRetardsBureau("Tontine Fraternité Côte", 1, ["Jérôme Ngô", "François Ëlle"], 10000m, "XAF");

        Assert.Equal("TontinesApp: Retards Tontine Fraternité Cote tour 1: 2 en retard, 10000 XAF du. Jérome Ngo, Francois Elle", message);
    }

    [Theory]
    [InlineData("Chloé", "Chloé")]          // é is GSM-7
    [InlineData("Hélène Gaëtan", "Hélène Gaetan")] // é, è kept; ë stripped
    [InlineData("Ça va ç", "Ça va c")]      // Ç is GSM-7, ç is not
    [InlineData("Anaïs Bâ", "Anais Ba")]
    [InlineData("Ali 😀", "Ali ?")]
    public void Gsm7_RetireLesAccentsNonSupportes(string texte, string attendu)
    {
        Assert.Equal(attendu, SmsTemplate.Gsm7(texte));
    }
}
