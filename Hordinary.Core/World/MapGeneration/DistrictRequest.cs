using Hordinary.Game;

namespace Hordinary.World.MapGeneration;

/// <summary>
/// Ce qu'on demande au generateur : une mission, une ambiance, une graine, et les deux reglages de
/// horde qui appartiennent a <c>config/game_config.json</c> plutot qu'a un profil de carte.
///
/// Ces deux-la sont transmis au lieu d'etre relus parce qu'ils ne sont pas des reglages de carte : la
/// taille de la horde est celle de la mission, et la distance minimale d'apparition est une regle du
/// jeu qui vaut aussi bien sur la carte dessinee. Le generateur n'a pas a en connaitre la source, et
/// surtout pas a en poser une seconde.
/// </summary>
public sealed record DistrictRequest
{
    public required MissionDefinition Mission { get; init; }

    /// <summary>Identifiant d'un profil de <c>config/mapgen.json</c>. Absent, celui de la mission sert.</summary>
    public string? ProfileId { get; init; }

    /// <summary>
    /// La graine demandee. Le quartier rendu peut porter une graine plus grande : un quartier rejete
    /// est regenere a <c>seed + 1</c>, et c'est la graine acceptee qui est affichee et rejouable.
    /// </summary>
    public int Seed { get; init; }

    public int ZombieCount { get; init; }

    public int MinimumSpawnDistance { get; init; } = 9;

    /// <summary>Le profil demande, ou celui que la mission nomme.</summary>
    public string? Profile => ProfileId ?? Mission.MapProfile;
}
