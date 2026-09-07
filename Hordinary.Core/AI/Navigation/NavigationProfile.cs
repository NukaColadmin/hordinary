namespace Hordinary.AI.Navigation;

/// <summary>
/// Modulation des couts de navigation par une penalite additionnelle sur les zones inconnues et
/// dangereuses (specification section 16.2).
/// </summary>
public sealed class NavigationProfile
{
    public float UnknownCostMultiplier { get; init; } = 1f;
    public float DangerCostMultiplier { get; init; } = 1f;

    public static NavigationProfile Neutral { get; } = new();
}
