namespace Hordinary.AI.Navigation;

/// <summary>
/// Modulation des couts par la personnalite du survivant (specification section 16.2 :
/// « un survivant prudent applique une penalite plus importante aux zones inconnues »).
/// Les coefficients sont affines au livrable 7 ; ici seule la prudence est branchee.
/// </summary>
public sealed class NavigationProfile
{
    public float UnknownCostMultiplier { get; init; } = 1f;
    public float DangerCostMultiplier { get; init; } = 1f;

    public static NavigationProfile Neutral { get; } = new();

    /// <summary>
    /// Derive un profil depuis le coefficient <c>caution</c> (0 = temeraire, 1 = tres prudent).
    /// Un caution de 0,5 redonne le profil neutre.
    /// </summary>
    public static NavigationProfile FromCaution(float caution)
    {
        float c = Math.Clamp(caution, 0f, 1f);
        float multiplier = 0.5f + 1.5f * c; // 0,5 -> 1,0 -> 2,0
        return new NavigationProfile
        {
            UnknownCostMultiplier = multiplier,
            DangerCostMultiplier = multiplier,
        };
    }
}
