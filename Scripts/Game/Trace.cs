using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Hordinary.Game;

/// <summary>
/// Le robinet de la console de sortie, commande par <c>debug.enabled</c>, <c>debug.console_output</c>
/// et <c>debug.console_channels</c> de <c>config/game_config.json</c>.
///
/// Il existe pour une raison precise : le journal de Hordinary n'est pas un journal de mise au point
/// occasionnel, c'est le principal moyen d'observer l'IA — plans, replanifications, bruits, partages.
/// Il y a donc plusieurs dizaines d'appels a <c>GD.Print</c> dans la couche moteur, et un
/// <c>if</c> devant chacun serait un <c>if</c> qu'on finirait par oublier.
///
/// <b>Les canaux.</b> Tout ou rien ne marchait plus : une mission de trois minutes produisait quinze
/// cents lignes, dont la moitie repetait mot pour mot la moitie d'a cote, et le detail qu'on cherchait
/// s'y noyait. Chaque ligne porte deja son canal en tete — <c>[Plan]</c>, <c>[Carte]</c>,
/// <c>[Partage]</c> — et cette convention devient ici la regle : le crochet initial <b>est</b> le nom
/// du canal, et <c>console_channels</c> dit lesquels sortent. Observer la planification seule ne
/// demande donc plus de recompiler ni de commenter des lignes, seulement d'ecrire un nom de plus.
///
/// <see cref="Error"/> ne se coupe jamais, canal ou pas. Un fichier de configuration introuvable ou
/// invalide n'est pas une trace d'observation : c'est un probleme que le joueur doit voir, meme sur
/// une partie livree avec le mode debug eteint.
/// </summary>
internal static class Trace
{
    /// <summary>
    /// Faux jusqu'a la lecture de <c>config/game_config.json</c> : tant qu'on ne sait pas ce que le
    /// fichier demande, on se tait. C'est le seul defaut qui ne peut pas produire de bruit indesirable.
    /// </summary>
    public static bool Enabled { get; set; }

    /// <summary>
    /// Canaux ouverts. <c>null</c> les ouvre tous — c'est le comportement d'avant, et celui qu'on
    /// obtient en laissant <c>console_channels</c> vide.
    /// </summary>
    private static HashSet<string>? _channels;

    /// <summary>Ouvre exactement ces canaux. Une liste vide ou absente les ouvre tous.</summary>
    public static void UseChannels(IEnumerable<string>? channels)
    {
        var wanted = channels?.Where(name => !string.IsNullOrWhiteSpace(name)).ToList();

        _channels = wanted is { Count: > 0 }
            ? new HashSet<string>(wanted, System.StringComparer.OrdinalIgnoreCase)
            : null;
    }

    /// <summary>
    /// Vrai si une ligne de ce canal sortirait. Sert a ne pas <b>fabriquer</b> un message couteux
    /// qu'on jetterait ensuite — la description d'un plan, par exemple.
    /// </summary>
    public static bool Wants(string channel)
        => Enabled && (_channels is null || _channels.Contains(channel));

    public static void Print(string message)
    {
        if (Allows(message)) GD.Print(message);
    }

    public static void Warn(string message)
    {
        if (Allows(message)) GD.PushWarning(message);
    }

    /// <summary>Toujours affiche : une erreur de configuration n'est pas une trace de debug.</summary>
    public static void Error(string message) => GD.PushError(message);

    private static bool Allows(string message)
    {
        if (!Enabled) return false;
        if (_channels is null) return true;

        string channel = ChannelOf(message);
        return channel.Length == 0 || _channels.Contains(channel);
    }

    /// <summary>
    /// Le canal d'une ligne : ce qu'il y a entre les crochets de tete. Une ligne sans crochet n'a pas
    /// de canal et sort toujours — mieux vaut une trace de trop qu'une trace perdue par une
    /// convention oubliee.
    /// </summary>
    private static string ChannelOf(string message)
    {
        if (message.Length == 0 || message[0] != '[') return string.Empty;

        int close = message.IndexOf(']');
        return close < 0 ? string.Empty : message[1..close];
    }
}
