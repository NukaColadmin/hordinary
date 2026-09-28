namespace Hordinary.World;

/// <summary>
/// Actions bruyantes et leur niveau (specification section 18.1). Les niveaux eux-memes ne sont pas
/// ici mais dans <see cref="NoiseSettings"/> : ils viennent de <c>config/game_config.json</c>.
/// </summary>
public enum NoiseAction
{
    Walk = 0,
    Run,
    OpenDoor,
    ForceDoor,
    BreakWindow,
    Distraction,

    /// <summary>
    /// Ajoute apres coup, donc en fin de liste : inserer une valeur au milieu renumeroterait
    /// <see cref="Distraction"/>, et le bareme de <see cref="NoiseSettings.LevelOf"/> se lit par nom
    /// mais rien ne garantit qu'un futur enregistrement fera de meme.
    /// </summary>
    BarricadeDoor,

    /// <summary>
    /// Un petard lance expres pour attirer la horde ailleurs (<c>ThrowFirecracker</c>). Il sonne
    /// comme une <see cref="Distraction"/>, mais porte son propre nom pour une raison precise : le
    /// moteur etiquette <see cref="Distraction"/> tout bruit d'action qui n'en declare pas — le pas
    /// de depart d'un deplacement, une fouille —, et <c>diversion_made</c> ne doit pas se lire dans
    /// le bruit d'une porte qu'on pousse. Ajoute en dernier, comme <see cref="BarricadeDoor"/>.
    /// </summary>
    Lure,
}

/// <summary>
/// Un bruit emis dans le monde (specification section 18.2). Les trois champs du modele impose sont
/// <see cref="Position"/>, <see cref="Intensity"/> et <see cref="CreatedAt"/> ; <see cref="Id"/>,
/// <see cref="Action"/> et <see cref="SourceId"/> servent au suivi et au journal de decisions.
///
/// Un bruit est un evenement du monde, pas une connaissance : ce que l'agent en retient est une
/// <see cref="Agents.NoiseBelief"/>, toujours approximative.
/// </summary>
public sealed record NoiseEvent(
    string Id,
    GridPosition Position,
    float Intensity,
    double CreatedAt,
    NoiseAction Action = NoiseAction.Walk,
    string? SourceId = null)
{
    public override string ToString() => $"{Action} ({Intensity:0.#}) en {Position} a t={CreatedAt:0.0}";
}
