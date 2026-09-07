using Hordinary.World;

namespace Hordinary.AI.Navigation;

/// <summary>
/// Ou l'agent peut aller, tout court. C'est la question qu'A* repond en meme temps qu'il repond
/// « par ou », et c'est la seule des deux qui interesse une decision : choisir une destination dont
/// aucun itineraire ne part, c'est batir un plan dont la premiere action echouera.
///
/// Le parcours emprunte exactement les aretes d'<see cref="AStar"/> — <see cref="NavigationGrid"/>
/// tranche du franchissable, coupes de coin comprises — mais ignore les couts : une composante
/// connexe ne depend pas du prix qu'on paie pour la traverser. Une seule passe repond donc pour
/// toutes les destinations candidates, la ou il aurait fallu autant de recherches A* que de
/// candidates, chacune deroulant la composante entiere avant d'echouer.
///
/// Comme le reste de la navigation, elle travaille sur la carte mentale que la grille lui presente :
/// « atteignable » veut dire « que ce survivant-la croit pouvoir atteindre ».
/// </summary>
public static class Reachability
{
    /// <summary>Cellules reliees a l'origine. L'origine en fait partie, meme si elle est bloquee.</summary>
    public static HashSet<GridPosition> From(NavigationGrid grid, GridPosition origin)
    {
        ArgumentNullException.ThrowIfNull(grid);

        var seen = new HashSet<GridPosition> { origin };
        var pending = new Queue<GridPosition>();
        pending.Enqueue(origin);

        while (pending.Count > 0)
        {
            foreach (var (neighbour, _) in grid.GetNeighbours(pending.Dequeue()))
            {
                if (seen.Add(neighbour)) pending.Enqueue(neighbour);
            }
        }

        return seen;
    }

    /// <summary>
    /// Le meme test, mais paresseux : la passe n'est deroulee qu'a la premiere question posee, et
    /// une seule fois. Une decision qui ne demande jamais de destination ne paie donc rien.
    ///
    /// Le resultat vieillit des que la carte mentale change : la fermeture est destinee a vivre le
    /// temps d'un <see cref="Utility.UtilityContext"/>, c'est-a-dire d'une passe de perception.
    /// </summary>
    public static Func<GridPosition, bool> LazyFrom(NavigationGrid grid, GridPosition origin)
    {
        ArgumentNullException.ThrowIfNull(grid);

        HashSet<GridPosition>? reached = null;
        return cell => (reached ??= From(grid, origin)).Contains(cell);
    }
}
