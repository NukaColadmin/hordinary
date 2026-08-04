namespace Hordinary.AI.Replanning;

/// <summary>
/// Reglages de la replanification (livrable 6), section <c>replanning</c> de
/// <c>config/game_config.json</c>.
///
/// Les trois premieres valeurs repondent au meme risque : la specification interdit de planifier a
/// chaque frame, et un declencheur qui persiste — un zombie en vue, un danger qui ne retombe pas —
/// relancerait GBFS indefiniment si rien ne l'en empechait.
/// </summary>
public sealed class ReplanningSettings
{
    /// <summary>Intervalle minimal entre deux verifications de validite du plan, en secondes.</summary>
    public float CheckInterval { get; init; } = 0.5f;

    /// <summary>
    /// Delai minimal entre deux replanifications declenchees par un evenement, en secondes. Un
    /// changement d'objectif n'y est pas soumis : la Utility AI a deja son propre cooldown et son
    /// hysteresis.
    /// </summary>
    public float MinimumInterval { get; init; } = 0.4f;

    /// <summary>Pression de menace au-dela de laquelle le plan est remis en cause.</summary>
    public float DangerThreshold { get; init; } = 0.6f;

    /// <summary>
    /// De combien la pression doit retomber sous le seuil avant qu'un nouveau franchissement compte.
    /// Sans cette marge, un danger qui oscille autour du seuil declencherait sans fin.
    /// </summary>
    public float DangerHysteresis { get; init; } = 0.1f;

    /// <summary>Danger sur un allie a partir duquel il est repute en danger.</summary>
    public float AllyDangerThreshold { get; init; } = 0.25f;

    /// <summary>Entrees conservees par le journal des decisions de l'IA.</summary>
    public int JournalCapacity { get; init; } = 200;

    /// <summary>Vrai pour verifier la validite du plan ; <c>false</c> ne laisse que les echecs d'action.</summary>
    public bool WatchPlanValidity { get; init; } = true;
}
