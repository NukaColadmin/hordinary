using Hordinary.Config;

namespace Hordinary.World;

/// <summary>Ce qu'un agencement a pose sur la carte, pour qui doit ensuite y mettre des choses.</summary>
/// <param name="Zone">La zone enregistree dans la carte, profil compris.</param>
/// <param name="Doors">Les portes d'entree percees dans l'enceinte.</param>
/// <param name="InnerRoom">La piece du fond, quand l'acces en declare une.</param>
/// <param name="SearchSpots">
/// Les cases ou poser du butin. Elles sont deja praticables et deja atteignables : ce sont des
/// endroits ou l'on fouille, pas des cases prises au hasard dans l'empreinte.
/// </param>
public sealed record ZoneStamp(
    MapZone Zone,
    IReadOnlyList<GridPosition> Doors,
    MapZone? InnerRoom,
    IReadOnlyList<GridPosition> SearchSpots);

/// <summary>
/// Les quatre agencements primitifs appliques a une empreinte (specification, livrable 11).
///
/// C'est le seul code que le catalogue de zones demande — tout le reste est de la donnee. Chacun ne
/// fait que poser des murs, des portes et des obstacles : <b>aucun n'ajoute de mecanique</b>. Ce qu'ils
/// changent, c'est la geometrie, et donc ce que les systemes deja livres — lignes de vue,
/// verrouillage, bruit, fouille, objets lourds — produisent une fois qu'on y joue.
///
/// La pose est deterministe a graine donnee, comme les expeditions : la meme graine sur la meme
/// empreinte redonne exactement le meme batiment.
/// </summary>
public static class ZoneLayouts
{
    /// <summary>
    /// Pose un profil sur une empreinte et enregistre la zone. L'empreinte est inclusive, bords
    /// compris : ce sont les murs.
    /// </summary>
    public static ZoneStamp Apply(ZoneProfile profile, GridMap map, GridPosition min, GridPosition max, int seed = 0)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(map);

        var (from, to) = Order(min, max);

        if (to.X - from.X < 3 || to.Y - from.Y < 3)
        {
            throw new InvalidOperationException(
                $"L'empreinte de « {profile.Id} » fait {to.X - from.X + 1} x {to.Y - from.Y + 1} : "
              + "il n'y tient meme pas une piece avec une porte.");
        }

        var zone = new MapZone(profile.Name, profile.Kind, from, to, profile.Id);
        map.AddZone(zone);

        // Le sol d'abord, puis ce que l'agencement y ajoute : un agencement ne fait jamais de trou
        // dans le terrain qu'il vient de poser.
        bool walled = profile.Layout != ZoneLayout.Forecourt;
        Ground(map, from, to, walled);

        var inner = profile.Access.InnerRoom is null ? null : Backroom(profile, map, from, to, seed);

        var spots = profile.Layout switch
        {
            ZoneLayout.Aisles => Aisles(profile, map, from, to),
            ZoneLayout.Cells => Cells(map, from, to, seed),
            ZoneLayout.Forecourt => Forecourt(profile, map, from, to, seed, ref inner),
            _ => Open(map, from, to),
        };

        var doors = walled ? Doors(profile, map, from, to, seed) : [];

        if (inner is not null) map.AddZone(inner);

        return new ZoneStamp(zone, doors, inner, Reachable(map, spots));
    }

    // --- Le terrain ---------------------------------------------------------------------------

    /// <summary>Sol partout, mur sur le pourtour — sauf pour une esplanade, qui n'a pas d'enceinte.</summary>
    private static void Ground(GridMap map, GridPosition from, GridPosition to, bool walled)
    {
        for (int y = from.Y; y <= to.Y; y++)
        {
            for (int x = from.X; x <= to.X; x++)
            {
                var cell = new GridPosition(x, y);
                if (!map.Contains(cell)) continue;

                bool edge = x == from.X || x == to.X || y == from.Y || y == to.Y;
                map.SetCell(cell, walled && edge ? CellType.Wall : CellType.Floor);
            }
        }
    }

    /// <summary>
    /// Perce les portes d'entree. Une porte verrouillee n'est pas un terrain different : c'est le
    /// meme mur avec un etat de plus, et c'est ce qui rend <c>ForceDoor</c> pertinent ici et pas la.
    /// </summary>
    private static List<GridPosition> Doors(ZoneProfile profile, GridMap map, GridPosition from, GridPosition to, int seed)
    {
        int wanted = Math.Max(1, (int)profile.Access.EntryDoors.Roll(
            () => Deterministic.Unit(seed, profile.Id, 0, "doors")));

        var perimeter = Perimeter(from, to).Where(map.Contains).ToList();
        var doors = new List<GridPosition>();

        for (int i = 0; i < wanted && perimeter.Count > 0; i++)
        {
            var cell = perimeter[Deterministic.Between(seed, profile.Id, i, 0, perimeter.Count - 1, "door")];

            bool locked = Deterministic.Unit(seed, profile.Id, i, "lock") < profile.Access.LockedChance;
            map.SetCell(cell, locked ? CellType.DoorLocked : CellType.DoorClosed);
            doors.Add(cell);

            // Deux portes voisines n'en font qu'une : on ecarte tout le mur autour de celle-ci.
            perimeter.RemoveAll(other => Chebyshev(other, cell) <= 2);
        }

        return doors;
    }

    /// <summary>Les cases de mur ou une porte a un sens : ni les coins, ni les bords de la carte.</summary>
    private static IEnumerable<GridPosition> Perimeter(GridPosition from, GridPosition to)
    {
        for (int x = from.X + 1; x < to.X; x++)
        {
            yield return new GridPosition(x, from.Y);
            yield return new GridPosition(x, to.Y);
        }

        for (int y = from.Y + 1; y < to.Y; y++)
        {
            yield return new GridPosition(from.X, y);
            yield return new GridPosition(to.X, y);
        }
    }

    // --- Les quatre agencements ---------------------------------------------------------------

    /// <summary>
    /// Volume libre. On y voit d'un bout a l'autre et il n'y a nulle part ou se mettre : c'est le
    /// gymnase du centre d'accueil, et c'est un agencement qui ne pose rien du tout — l'absence est
    /// exactement ce qui le definit.
    /// </summary>
    private static List<GridPosition> Open(GridMap map, GridPosition from, GridPosition to)
        => Interior(from, to).Where(cell => map.GetCell(cell) == CellType.Floor).ToList();

    /// <summary>
    /// Allees paralleles. Les rayonnages decoupent l'espace : la vue porte <b>le long</b> d'une allee,
    /// jamais <b>a travers</b>. Une <c>vision_range</c> de huit cases devient sans objet, et le bruit
    /// redevient la seule perception qui porte loin — ce que le livrable 3 a construit et qu'aucune
    /// carte ne mettait a l'epreuve.
    ///
    /// Les rayonnages s'arretent avant les deux murs de bout : sans cela, chaque allee serait un
    /// cul-de-sac et la zone se traverserait en une seule ligne.
    /// </summary>
    private static List<GridPosition> Aisles(ZoneProfile profile, GridMap map, GridPosition from, GridPosition to)
    {
        var spots = new List<GridPosition>();

        for (int x = from.X + 2; x < to.X - 1; x += Math.Max(2, profile.AisleSpacing))
        {
            for (int y = from.Y + 2; y <= to.Y - 2; y++)
            {
                var cell = new GridPosition(x, y);
                if (!map.Contains(cell) || map.GetCell(cell) != CellType.Floor) continue;

                map.SetCell(cell, CellType.Obstacle);

                // Ce qu'on fouille est au pied du rayon, pas dedans.
                var beside = new GridPosition(x + 1, y);
                if (map.Contains(beside) && map.GetCell(beside) == CellType.Floor) spots.Add(beside);
            }
        }

        return spots;
    }

    /// <summary>
    /// Couloirs et petites salles. On ne voit rien, on ne sait jamais ce qu'il y a derriere, et il
    /// faut fouiller partout : c'est ce qui recompense enfin une exploration methodique plutot qu'un
    /// trajet direct.
    /// </summary>
    private static List<GridPosition> Cells(GridMap map, GridPosition from, GridPosition to, int seed)
    {
        var spots = new List<GridPosition>();
        int step = 4;

        // Les refends verticaux, perces d'une ouverture chacun : sans elle, une salle serait murée.
        for (int x = from.X + step; x < to.X; x += step)
        {
            int gap = Deterministic.Between(seed, "cells", x, from.Y + 1, to.Y - 1, "gap");

            for (int y = from.Y + 1; y < to.Y; y++)
            {
                var cell = new GridPosition(x, y);
                if (!map.Contains(cell)) continue;

                map.SetCell(cell, y == gap ? CellType.Floor : CellType.Wall);
            }
        }

        // Un couloir horizontal qui traverse tout : c'est lui qui relie les salles entre elles.
        int corridor = (from.Y + to.Y) / 2;

        for (int x = from.X + 1; x < to.X; x++)
        {
            var cell = new GridPosition(x, corridor);
            if (map.Contains(cell)) map.SetCell(cell, CellType.Floor);
        }

        foreach (var cell in Interior(from, to))
        {
            if (cell.Y != corridor && map.Contains(cell) && map.GetCell(cell) == CellType.Floor) spots.Add(cell);
        }

        return spots;
    }

    /// <summary>
    /// Esplanade et cabane. Aucun couvert entre la route et la boutique : c'est le seul endroit ou
    /// tirer un coup de feu revient a se signaler a tout le monde a la fois, et donc le seul ou
    /// l'arbitrage discretion / puissance se pose sans echappatoire.
    ///
    /// Les pompes sont dehors : gratuites, mais exposees. La boutique a une porte unique.
    /// </summary>
    private static List<GridPosition> Forecourt(
        ZoneProfile profile,
        GridMap map,
        GridPosition from,
        GridPosition to,
        int seed,
        ref MapZone? inner)
    {
        var spots = new List<GridPosition>();

        // Les pompes : des obstacles isoles au milieu du vide, le seul relief de l'esplanade.
        int pumps = Math.Max(2, (to.X - from.X) / 4);

        for (int i = 0; i < pumps; i++)
        {
            int x = Deterministic.Between(seed, profile.Id, i, from.X + 1, to.X - 1, "pump.x");
            int y = Deterministic.Between(seed, profile.Id, i, from.Y + 1, to.Y - 2, "pump.y");

            var cell = new GridPosition(x, y);
            if (!map.Contains(cell) || map.GetCell(cell) != CellType.Floor) continue;

            map.SetCell(cell, CellType.Obstacle);

            var beside = new GridPosition(x, y + 1);
            if (map.Contains(beside) && map.GetCell(beside) == CellType.Floor) spots.Add(beside);
        }

        // La boutique, dans un coin, quand l'acces n'en a pas deja pose une au fond.
        inner ??= Shop(profile, map, from, to, seed);
        if (inner is not null) spots.AddRange(inner.Positions().Where(cell => map.GetCell(cell) == CellType.Floor));

        return spots;
    }

    // --- Les pieces fermees -------------------------------------------------------------------

    /// <summary>
    /// La reserve du fond : le seul <c>ForceDoor</c> de la zone, a l'endroit le plus profond, au bruit
    /// maximal, avec le plus long chemin de retour. C'est ce qui fait de la porte une decision.
    /// </summary>
    private static MapZone? Backroom(ZoneProfile profile, GridMap map, GridPosition from, GridPosition to, int seed)
    {
        int width = Math.Max(2, (to.X - from.X) / 3);
        int height = Math.Max(2, (to.Y - from.Y) / 3);

        var min = new GridPosition(to.X - width, from.Y);
        var max = new GridPosition(to.X, from.Y + height);

        return Room(profile, map, min, max, profile.Access.InnerRoom!, seed, $"{profile.Id}.reserve", "Reserve");
    }

    /// <summary>La boutique d'une esplanade : petite, une seule porte, tout le reste est dehors.</summary>
    private static MapZone? Shop(ZoneProfile profile, GridMap map, GridPosition from, GridPosition to, int seed)
    {
        int width = Math.Max(3, (to.X - from.X) / 3);
        int height = Math.Max(3, (to.Y - from.Y) / 3);

        var min = new GridPosition(from.X, to.Y - height);
        var max = new GridPosition(from.X + width, to.Y);

        return Room(profile, map, min, max, profile.Access, seed, $"{profile.Id}.boutique", "Boutique");
    }

    /// <summary>Une piece fermee, avec une porte et une seule. La porte est ce qui la rend jouable.</summary>
    private static MapZone? Room(
        ZoneProfile profile,
        GridMap map,
        GridPosition min,
        GridPosition max,
        ZoneAccess access,
        int seed,
        string id,
        string name)
    {
        if (max.X - min.X < 2 || max.Y - min.Y < 2) return null;

        Ground(map, min, max, walled: true);

        var candidates = Perimeter(min, max).Where(map.Contains).ToList();
        if (candidates.Count == 0) return null;

        var door = candidates[Deterministic.Between(seed, id, 0, 0, candidates.Count - 1, "door")];
        bool locked = Deterministic.Unit(seed, id, 0, "lock") < access.LockedChance;

        map.SetCell(door, locked ? CellType.DoorLocked : CellType.DoorClosed);

        return new MapZone($"{name} {profile.Name}", profile.Kind, min, max, profile.Id);
    }

    // --- Utilitaires --------------------------------------------------------------------------

    private static IEnumerable<GridPosition> Interior(GridPosition from, GridPosition to)
    {
        for (int y = from.Y + 1; y < to.Y; y++)
        {
            for (int x = from.X + 1; x < to.X; x++)
            {
                yield return new GridPosition(x, y);
            }
        }
    }

    /// <summary>
    /// Ne garde que ce qui est encore praticable une fois tout pose. Un agencement qui murerait une
    /// case de fouille ne s'en apercevrait pas autrement — et le butin qu'on y met serait perdu.
    /// </summary>
    private static List<GridPosition> Reachable(GridMap map, List<GridPosition> spots)
        => spots.Where(cell => map.Contains(cell) && map.GetCell(cell).IsKnownWalkable()).Distinct().ToList();

    private static (GridPosition From, GridPosition To) Order(GridPosition a, GridPosition b)
        => (new GridPosition(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)),
            new GridPosition(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));

    private static int Chebyshev(GridPosition a, GridPosition b)
        => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}
