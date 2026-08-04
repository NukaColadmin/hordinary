namespace Hordinary.AI.Utility;

/// <summary>Formes de courbe disponibles en configuration (specification, livrable 4).</summary>
public enum CurveKind
{
    /// <summary>Identite : <c>x</c>. Avec un exposant, equivaut a <see cref="Quadratic"/>.</summary>
    Linear = 0,

    /// <summary>Accelere : <c>x^exposant</c> (exposant 2 par defaut). Ignore les petites valeurs.</summary>
    Quadratic,

    /// <summary>Inverse : <c>1 - x^exposant</c> (exposant 1 par defaut). « Moins il y en a, mieux c'est ».</summary>
    Inverse,

    /// <summary>Tout ou rien : 1 au-dela du seuil, 0 en deca.</summary>
    Threshold,
}

/// <summary>
/// Courbes de reponse : des fonctions pures de <c>[0, 1]</c> vers <c>[0, 1]</c>. C'est tout ce que
/// le code connait de la forme d'une consideration ; le choix de la courbe est une donnee.
/// </summary>
public static class ResponseCurve
{
    /// <summary>
    /// Applique une courbe. <paramref name="exponent"/> est optionnel : chaque courbe a l'exposant
    /// qui la definit (1 pour <see cref="CurveKind.Linear"/> et <see cref="CurveKind.Inverse"/>,
    /// 2 pour <see cref="CurveKind.Quadratic"/>), et le preciser permet de la durcir sans inventer
    /// une nouvelle forme — <c>inverse</c> d'exposant 2 est un « inverse quadratique ».
    /// </summary>
    public static float Apply(CurveKind kind, float input, float? exponent = null, float threshold = 0.5f)
    {
        float x = Math.Clamp(input, 0f, 1f);

        return kind switch
        {
            CurveKind.Linear => Power(x, exponent ?? 1f),
            CurveKind.Quadratic => Power(x, exponent ?? 2f),
            CurveKind.Inverse => 1f - Power(x, exponent ?? 1f),
            CurveKind.Threshold => x >= threshold ? 1f : 0f,
            _ => x,
        };
    }

    private static float Power(float x, float exponent)
        => exponent == 1f ? x : MathF.Pow(x, Math.Max(0.01f, exponent));
}
