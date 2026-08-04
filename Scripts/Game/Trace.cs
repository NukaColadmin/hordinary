using Godot;

namespace Hordinary.Game;

/// <summary>
/// Le robinet de la console de sortie, commande par <c>debug.enabled</c> et
/// <c>debug.console_output</c> de <c>config/game_config.json</c>.
///
/// Il existe pour une raison precise : le journal de Hordinary n'est pas un journal de mise au point
/// occasionnel, c'est le principal moyen d'observer l'IA — plans, replanifications, bruits, partages.
/// Il y a donc plusieurs dizaines d'appels a <c>GD.Print</c> dans la couche moteur, et un
/// <c>if</c> devant chacun serait un <c>if</c> qu'on finirait par oublier.
///
/// <see cref="Error"/> ne se coupe jamais. Un fichier de configuration introuvable ou invalide n'est
/// pas une trace d'observation : c'est un probleme que le joueur doit voir, meme sur une partie
/// livree avec le mode debug eteint.
/// </summary>
internal static class Trace
{
    /// <summary>
    /// Faux jusqu'a la lecture de <c>config/game_config.json</c> : tant qu'on ne sait pas ce que le
    /// fichier demande, on se tait. C'est le seul defaut qui ne peut pas produire de bruit indesirable.
    /// </summary>
    public static bool Enabled { get; set; }

    public static void Print(string message)
    {
        if (Enabled) GD.Print(message);
    }

    public static void Warn(string message)
    {
        if (Enabled) GD.PushWarning(message);
    }

    /// <summary>Toujours affiche : une erreur de configuration n'est pas une trace de debug.</summary>
    public static void Error(string message) => GD.PushError(message);
}
