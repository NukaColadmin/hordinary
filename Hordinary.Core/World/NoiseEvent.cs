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
