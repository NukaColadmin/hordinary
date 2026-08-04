namespace Hordinary.AI.Utility;

/// <summary>
/// Reglages de la Utility AI (specification section 11.4 et annexe B), charges depuis
/// <c>config/game_config.json</c>, section <c>utility_ai</c>. Les deux premieres valeurs sont celles
/// du format impose ; les suivantes normalisent les considerations primitives.
/// </summary>
public sealed class UtilityAiSettings
{
    // --- Cadence et stabilite des decisions (section 11.4) --------------------------------

    /// <summary>Periode entre deux reevaluations, en secondes (1 a 2 fois par seconde).</summary>
    public float EvaluationInterval { get; init; } = 0.75f;

    /// <summary>Hysteresis : marge dont un objectif doit depasser l'objectif courant pour le remplacer.</summary>
    public float SwitchThreshold { get; init; } = 0.15f;

    /// <summary>Seuil minimum : en dessous, un objectif ne vaut pas la peine d'etre poursuivi.</summary>
    public float MinimumScore { get; init; } = 0.05f;

    // --- Normalisation des considerations --------------------------------------------------

    /// <summary>Distance, en cellules, au-dela de laquelle une menace ne pese plus sur une cellule.</summary>
    public float DangerRadius { get; init; } = 8f;

    /// <summary>Distance, en cellules, qui vaut 1 pour <c>distance_to(...)</c>. Au-dela, « c'est loin ».</summary>
    public float DistanceScale { get; init; } = 12f;

    /// <summary>
    /// Poids d'un bruit dans le danger percu, face a un zombie reellement vu. Entendre n'est pas voir :
    /// un fracas inquiete, il ne prouve pas qu'une horde arrive.
    /// </summary>
    public float NoiseDangerWeight { get; init; } = 0.5f;

    /// <summary>Intensite percue a partir de laquelle un bruit compte comme fort (bareme section 18.1).</summary>
    public float LoudNoiseIntensity { get; init; } = 6f;

    /// <summary>Cout, en distance, d'un pas de fuite : au-dela, mieux vaut une cachette plus proche.</summary>
    public float FleeTravelPenalty { get; init; } = 0.5f;

    /// <summary>Priorite attribuee par defaut a un ordre du joueur (entree <c>player_priority</c>).</summary>
    public float PlayerOrderPriority { get; init; } = 0.7f;

    public static UtilityAiSettings Default { get; } = new();
}
