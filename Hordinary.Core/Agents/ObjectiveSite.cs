using Hordinary.World;

namespace Hordinary.Agents;

/// <summary>
/// Le lieu qu'une mission designe comme objectif, du point de vue du survivant qu'on y envoie.
///
/// Deux champs et pas un, parce qu'ils ne repondent pas a la meme question. Le <see cref="Name"/>
/// designe un lieu et un seul — la carte livree compte deux batiments, et « aller fouiller le
/// batiment » n'est pas un ordre. La <see cref="Kind"/> est ce que le terrain sait dire d'une case :
/// c'est par elle qu'un survivant plante au milieu de l'entrepot reconnait qu'il y est.
/// </summary>
public sealed record ObjectiveSite(string Name, ZoneKind Kind)
{
    /// <summary>Vrai si cette zone de la carte est celle qu'on a ete envoye fouiller.</summary>
    public bool Matches(MapZone zone)
        => zone is not null && zone.Name.Equals(Name, System.StringComparison.OrdinalIgnoreCase);

    public override string ToString() => $"{Name} [{Kind}]";
}
