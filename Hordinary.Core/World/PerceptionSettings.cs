namespace Hordinary.World;

/// <summary>
/// Parametres de vision (specification section 9.2), charges depuis <c>config/game_config.json</c>,
/// section <c>perception</c>. Ce sont les valeurs par defaut d'un survivant ; le livrable 7 les
/// fait varier par profil (<c>config/survivors.json</c>).
/// </summary>
public sealed class PerceptionSettings
{
    /// <summary>Portee de vision, en cellules.</summary>
    public int VisionRange { get; init; } = 8;

    /// <summary>Ouverture du champ de vision, en degres. 360 = vision panoramique.</summary>
    public float FieldOfViewDegrees { get; init; } = 180f;

    /// <summary>Periode entre deux mises a jour de la perception, en secondes (5 a 10 fois/s).</summary>
    public float UpdateInterval { get; init; } = 0.15f;

    public static PerceptionSettings Default { get; } = new();
}
