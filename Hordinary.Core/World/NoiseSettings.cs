namespace Hordinary.World;

/// <summary>
/// Bareme et propagation du bruit (specification section 18), charges depuis
/// <c>config/game_config.json</c>, section <c>noise</c>.
/// </summary>
public sealed class NoiseSettings
{
    // --- Bareme de la section 18.1 --------------------------------------------------------

    public float Walk { get; init; } = 1f;
    public float Run { get; init; } = 3f;
    public float OpenDoor { get; init; } = 2f;
    public float ForceDoor { get; init; } = 6f;
    public float BreakWindow { get; init; } = 8f;
    public float Distraction { get; init; } = 10f;

    /// <summary>
    /// Barricader : le meme vacarme que forcer une porte, et pour cause — c'est le meme bois et le
    /// meme outil. On n'entend pas la difference entre entrer de force et condamner derriere soi.
    /// </summary>
    public float BarricadeDoor { get; init; } = 6f;

    // --- Propagation ----------------------------------------------------------------------

    /// <summary>Intensite perdue par cellule opaque traversee, en plus de la distance.</summary>
    public float ObstacleAttenuation { get; init; } = 2f;

    /// <summary>Duree de vie d'un bruit, en secondes : au-dela il n'est plus audible du tout.</summary>
    public float Lifetime { get; init; } = 6f;

    /// <summary>Intensite percue minimale pour qu'un bruit soit entendu.</summary>
    public float HearingThreshold { get; init; } = 0.5f;

    // --- Localisation approximative -------------------------------------------------------

    /// <summary>
    /// Rayon d'erreur ajoute par point d'intensite perdu en chemin. Plus un bruit arrive attenue,
    /// moins l'auditeur sait d'ou il vient : c'est ce qui empeche un zombie de deduire la position
    /// exacte de sa source (critere de validation du livrable 3).
    /// </summary>
    public float LocalisationErrorFactor { get; init; } = 0.35f;

    /// <summary>Plafond du rayon d'erreur, en cellules.</summary>
    public int MaxLocalisationError { get; init; } = 4;

    public static NoiseSettings Default { get; } = new();

    public float LevelOf(NoiseAction action) => action switch
    {
        NoiseAction.Walk => Walk,
        NoiseAction.Run => Run,
        NoiseAction.OpenDoor => OpenDoor,
        NoiseAction.ForceDoor => ForceDoor,
        NoiseAction.BreakWindow => BreakWindow,
        NoiseAction.Distraction => Distraction,
        NoiseAction.BarricadeDoor => BarricadeDoor,
        NoiseAction.Lure => Distraction,
        _ => 0f,
    };
}
