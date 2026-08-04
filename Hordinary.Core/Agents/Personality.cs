namespace Hordinary.Agents;

/// <summary>
/// Coefficients de personnalite (specification section 10.2) : ils ponderent les scores de la
/// Utility AI et les couts A*. Les trois profils nommes (Alice, Bob, Claire) arrivent au livrable 7 ;
/// ici les valeurs viennent de <c>config/game_config.json</c>, section <c>survivor.personality</c>.
/// </summary>
public sealed record Personality
{
    /// <summary>Gout du risque : pousse a explorer, a distraire une horde, a ne pas se regrouper.</summary>
    public float Courage { get; init; } = 0.5f;

    /// <summary>Attachement au groupe : pousse a aider, soigner et rejoindre un allie.</summary>
    public float Loyalty { get; init; } = 0.5f;

    /// <summary>Prudence : pousse a fuir, a se cacher, et majore le cout des zones inconnues.</summary>
    public float Caution { get; init; } = 0.5f;

    public static Personality Default { get; } = new();

    public override string ToString()
        => $"courage {Courage:0.00}, loyaute {Loyalty:0.00}, prudence {Caution:0.00}";
}
