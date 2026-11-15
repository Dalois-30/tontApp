namespace Api.Security;

/// <summary>
/// Feature toggles bound from the "Features" configuration section.
/// MobileMoney is off by default: in the target model money stays in the bank and
/// payments are entered manually then validated.
/// </summary>
public sealed class FeatureFlags
{
    public const string SectionName = "Features";

    public bool MobileMoney { get; init; }
}
