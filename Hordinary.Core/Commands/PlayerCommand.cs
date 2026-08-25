using Hordinary.World;

namespace Hordinary.Commands;

/// <summary>Les huit commandes de la specification (livrable 8, « Commandes disponibles »).</summary>
public enum PlayerCommandKind
{
    /// <summary>Changer de point de vue : le HUD et les overlays suivent le survivant choisi.</summary>
    SelectSurvivor = 0,

    /// <summary>Adresser les ordres suivants au groupe entier.</summary>
    SelectGroup,

    /// <summary>« Va voir la-bas » : explorer une zone, ou la frontiere si aucune n'est designee.</summary>
    ExploreArea,

    /// <summary>« Rapportez les medicaments ».</summary>
    FetchMedicine,

    /// <summary>« Rejoignez l'extraction ».</summary>
    ReachExtraction,

    /// <summary>« Regroupez-vous ».</summary>
    Regroup,

    /// <summary>Consigne permanente : plus d'action bruyante.</summary>
    ForbidNoise,

    /// <summary>Consigne permanente : le bruit redevient un moyen comme un autre.</summary>
    AllowNoise,

    /// <summary>Consigne permanente : ne mettez pas les pieds la.</summary>
    ForbidZone,

    /// <summary>Leve l'interdiction posee sur une zone.</summary>
    AllowZone,
}

/// <summary>
/// Une commande du joueur (specification section A : <c>Commands/PlayerCommand</c>).
///
/// Elle est <b>declarative</b> : elle dit ce que le joueur demande, jamais comment l'obtenir. C'est
/// <see cref="CommandManager"/> qui la traduit — en objectif que la Utility AI mettra en concurrence
/// avec les siens, ou en consigne permanente qui ampute ses possibilites. Aucune commande ne pilote
/// directement un survivant : le joueur ne conduit pas, il donne des consignes.
/// </summary>
/// <param name="Kind">Ce que le joueur demande.</param>
/// <param name="ToAll">Vrai si la commande s'adresse au groupe plutot qu'au survivant selectionne.</param>
/// <param name="Zone">Zone visee, pour l'exploration et l'interdiction.</param>
/// <param name="SurvivorId">Survivant vise par <see cref="PlayerCommandKind.SelectSurvivor"/>.</param>
public sealed record PlayerCommand(
    PlayerCommandKind Kind,
    bool ToAll = false,
    MapZone? Zone = null,
    string? SurvivorId = null)
{
    public static PlayerCommand Select(string survivorId)
        => new(PlayerCommandKind.SelectSurvivor, SurvivorId: survivorId);

    public static PlayerCommand SelectGroup() => new(PlayerCommandKind.SelectGroup, ToAll: true);

    public static PlayerCommand Explore(MapZone? zone = null, bool toAll = false)
        => new(PlayerCommandKind.ExploreArea, toAll, Zone: zone);

    public static PlayerCommand FetchMedicine(bool toAll = false)
        => new(PlayerCommandKind.FetchMedicine, toAll);

    public static PlayerCommand ReachExtraction(bool toAll = false)
        => new(PlayerCommandKind.ReachExtraction, toAll);

    public static PlayerCommand Regroup(bool toAll = true) => new(PlayerCommandKind.Regroup, toAll);

    public static PlayerCommand ForbidNoise(bool toAll = true) => new(PlayerCommandKind.ForbidNoise, toAll);

    public static PlayerCommand AllowNoise(bool toAll = true) => new(PlayerCommandKind.AllowNoise, toAll);

    public static PlayerCommand ForbidZone(MapZone zone, bool toAll = true)
        => new(PlayerCommandKind.ForbidZone, toAll, Zone: zone);

    public static PlayerCommand AllowZone(MapZone zone, bool toAll = true)
        => new(PlayerCommandKind.AllowZone, toAll, Zone: zone);

    /// <summary>Vrai pour les consignes permanentes, qui ne se traduisent pas en objectif.</summary>
    public bool IsStandingOrder => Kind
        is PlayerCommandKind.ForbidNoise
        or PlayerCommandKind.AllowNoise
        or PlayerCommandKind.ForbidZone
        or PlayerCommandKind.AllowZone;

    public override string ToString() => Kind switch
    {
        PlayerCommandKind.SelectSurvivor => $"selectionner {SurvivorId}",
        PlayerCommandKind.SelectGroup => "selectionner le groupe",
        PlayerCommandKind.ExploreArea => Zone is null ? "explorer" : $"explorer {Zone.Name}",
        PlayerCommandKind.FetchMedicine => "recuperer les medicaments",
        PlayerCommandKind.ReachExtraction => "rejoindre l'extraction",
        PlayerCommandKind.Regroup => "se regrouper",
        PlayerCommandKind.ForbidNoise => "interdire les actions bruyantes",
        PlayerCommandKind.AllowNoise => "autoriser les actions bruyantes",
        PlayerCommandKind.ForbidZone => $"interdire {Zone?.Name ?? "?"}",
        PlayerCommandKind.AllowZone => $"autoriser {Zone?.Name ?? "?"}",
        _ => Kind.ToString(),
    };
}
