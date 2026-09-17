using Hordinary.Config;
using Hordinary.World;

namespace Hordinary.Agents;

/// <summary>
/// Ou chaque survivant pose le pied en descendant du van.
///
/// Les faire naitre tous sur la case du camp donnait un groupe empile sur un seul point : trois
/// cartes mentales identiques a la premiere passe de perception, trois plans qui partaient du meme
/// endroit, et un debut de mission ou le partage de connaissances n'avait rien a echanger. Les
/// disperser dans le carre autour du van suffit a rendre les trois points de vue differents des la
/// premiere seconde.
///
/// La dispersion ne choisit que des cases que le van <b>touche reellement</b> : le parcours part du
/// camp et n'avance que de proche en proche, orthogonalement, sans jamais sortir du carre. Un mur
/// qui traverse le carre arrete donc le parcours, et personne ne naitra de l'autre cote — ni dans
/// une piece fermee voisine, ni au-dela d'une facade. Les limites de la carte se gardent toutes
/// seules : <see cref="GridMap.IsWalkable"/> commence par verifier que la case existe.
///
/// Le tirage passe par <see cref="Deterministic"/> : la meme graine rejoue exactement le meme
/// depart, ce qui est la seule facon de comparer deux missions rejouees.
/// </summary>
public static class BandSpawn
{
    /// <summary>Demi-cote du carre de depart. 2 donne les 5x5 cases centrees sur le van.</summary>
    public const int DefaultRadius = 2;

    /// <summary>Canal de tirage, pour que le placement ne se confonde avec aucun autre hasard.</summary>
    private const string Channel = "band-spawn";

    /// <summary>
    /// Tire <paramref name="count"/> positions de depart autour de <paramref name="camp"/>, sans
    /// doublon tant que le carre offre assez de cases. Le camp lui-meme est un candidat comme un
    /// autre — c'est une case praticable du carre.
    ///
    /// S'il manque des places — un van dans un couloir, un carre presque entierement bati — les
    /// survivants en trop reviennent sur la case du camp : un depart serre reste jouable, un depart
    /// dans un mur non.
    /// </summary>
    public static IReadOnlyList<GridPosition> Scatter(
        WorldState world,
        GridPosition camp,
        int count,
        int seed,
        int radius = DefaultRadius)
    {
        ArgumentNullException.ThrowIfNull(world);

        var free = Around(world, camp, radius);
        var chosen = new List<GridPosition>(Math.Max(count, 0));

        for (int i = 0; i < count; i++)
        {
            if (free.Count == 0)
            {
                chosen.Add(camp);
                continue;
            }

            // Le tirage a une adresse — graine, canal, rang du survivant — et non un rang dans une
            // suite : ajouter un quatrieme profil ne deplace pas les trois premiers.
            int pick = Deterministic.Between(seed, Channel, i, 0, free.Count - 1);
            chosen.Add(free[pick]);
            free.RemoveAt(pick);
        }

        return chosen;
    }

    /// <summary>
    /// Les cases du carre que l'on atteint depuis le camp sans le quitter. Parcours en largeur,
    /// voisinage orthogonal : une case en diagonale derriere un angle de mur n'est pas voisine, et
    /// le van ne fait naitre personne dans la piece d'a cote.
    ///
    /// L'ordre du resultat est celui du parcours, donc stable a graine egale : c'est ce qui rend le
    /// tirage rejouable.
    /// </summary>
    private static List<GridPosition> Around(WorldState world, GridPosition camp, int radius)
    {
        var found = new List<GridPosition>();
        if (!Free(world, camp)) return found;

        var seen = new HashSet<GridPosition> { camp };
        var pending = new Queue<GridPosition>();
        pending.Enqueue(camp);

        while (pending.Count > 0)
        {
            var cell = pending.Dequeue();
            found.Add(cell);

            foreach (var offset in GridPosition.OrthogonalOffsets)
            {
                var neighbour = cell + offset;

                if (neighbour.ChebyshevDistanceTo(camp) > radius) continue;
                if (!Free(world, neighbour)) continue;
                if (!seen.Add(neighbour)) continue;

                pending.Enqueue(neighbour);
            }
        }

        return found;
    }

    /// <summary>
    /// Une case praticable et vide. <see cref="GridMap.IsWalkable"/> ecarte les murs et le hors
    /// carte ; l'armoire ou l'etabli qu'un survivant ne peut que pousser l'ecartent aussi — naitre
    /// dessus le poserait dans un decor qu'il devrait deplacer pour en sortir.
    /// </summary>
    private static bool Free(WorldState world, GridPosition cell)
        => world.Grid.IsWalkable(cell) && !world.Heavy.BlocksMovementAt(cell);
}
