namespace Hordinary.AI.Utility;

/// <summary>
/// Une consideration : une entree primitive du monde ou de la memoire, passee dans une courbe de
/// reponse, ramenee dans une plage de sortie.
///
/// C'est la brique dont tous les objectifs sont faits. Le code ne connait que les entrees
/// (<see cref="UtilityInputs"/>) et les courbes (<see cref="ResponseCurve"/>) ; quelle consideration
/// entre dans quel objectif est une donnee de <c>config/utility_goals.json</c>.
/// </summary>
public sealed class Consideration
{
    /// <summary>Nom de l'entree primitive, par exemple <c>perceived_danger</c> ou <c>distance_to(ally)</c>.</summary>
    public string Input { get; init; } = string.Empty;

    public CurveKind Curve { get; init; } = CurveKind.Linear;

    /// <summary>Durcit la courbe. Absent, chaque courbe utilise l'exposant qui la definit.</summary>
    public float? Exponent { get; init; }

    /// <summary>Seuil de bascule de <see cref="CurveKind.Threshold"/>.</summary>
    public float Threshold { get; init; } = 0.5f;

    /// <summary>
    /// Plancher de sortie. Il distingue une consideration qui peut annuler l'objectif (min 0, le
    /// danger percu pour la fuite) d'une consideration qui ne fait que le moduler (min 0,7, la
    /// panique : un survivant calme fuit quand meme un zombie qui lui saute dessus).
    /// </summary>
    public float Min { get; init; } = 0f;

    /// <summary>Plafond de sortie.</summary>
    public float Max { get; init; } = 1f;

    /// <summary>
    /// Poids relatif. Exposant en combinaison multiplicative (0 = consideration neutralisee,
    /// 1 = telle quelle, 2 = deux fois plus penalisante), coefficient en moyenne ponderee.
    /// </summary>
    public float Weight { get; init; } = 1f;

    /// <summary>Valeur de la consideration pour une entree brute deja lue dans le contexte.</summary>
    public float Evaluate(float raw)
    {
        float curved = ResponseCurve.Apply(Curve, raw, Exponent, Threshold);
        return Math.Clamp(Min + (Max - Min) * curved, 0f, 1f);
    }

    public override string ToString() => $"{Input} [{Curve}] {Min:0.##}-{Max:0.##}";
}
