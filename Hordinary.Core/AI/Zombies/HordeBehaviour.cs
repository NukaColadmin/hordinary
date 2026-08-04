using Hordinary.Agents;
using Hordinary.World;

namespace Hordinary.AI.Zombies;

/// <summary>
/// Deplacement collectif des zombies (specification section 17.4) : attraction vers la destination,
/// separation pour ne pas se superposer, legere cohesion avec les voisins. Aucune planification —
/// la specification l'exclut explicitement — juste le choix de la meilleure case adjacente.
///
/// Cette classe prend la <see cref="GridMap"/> reelle, et c'est voulu : elle ne decide pas ou aller
/// (c'est le role de <see cref="ZombieStateMachine"/>, qui ne lit que la memoire) mais si le corps
/// peut physiquement passer. Un zombie ignore ou est sa proie, il ne traverse pas les murs pour
/// autant.
/// </summary>
public sealed class HordeBehaviour(ZombieSettings? settings = null)
{
    public ZombieSettings Settings { get; } = settings ?? ZombieSettings.Default;

    /// <summary>
    /// Prochaine cellule a occuper, ou <c>null</c> si le zombie fait mieux de ne pas bouger.
    /// <paramref name="horde"/> peut contenir le zombie lui-meme : il s'ignore.
    /// </summary>
    public GridPosition? ChooseStep(
        Zombie zombie,
        GridPosition? destination,
        IReadOnlyList<Zombie> horde,
        GridMap map)
    {
        if (destination is not { } goal) return null;

        var from = zombie.Position;
        var centroid = Centroid(zombie, horde);

        var best = from;
        float bestScore = Score(from, goal, zombie, horde, centroid);

        foreach (var offset in GridPosition.OrthogonalOffsets.Concat(GridPosition.DiagonalOffsets))
        {
            var candidate = from + offset;
            if (!CanStep(from, offset, candidate, map)) continue;
            if (IsOccupied(candidate, zombie, horde)) continue;

            float score = Score(candidate, goal, zombie, horde, centroid);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best == from ? null : best;
    }

    /// <summary>Plus le score est eleve, plus la case est attirante.</summary>
    private float Score(
        GridPosition cell,
        GridPosition goal,
        Zombie zombie,
        IReadOnlyList<Zombie> horde,
        (float X, float Y)? centroid)
    {
        float score = -Settings.AttractionWeight * Distance(cell, goal.X, goal.Y);

        score -= Settings.SeparationWeight * Crowding(cell, zombie, horde);

        if (centroid is { } center) score -= Settings.CohesionWeight * Distance(cell, center.X, center.Y);

        return score;
    }

    /// <summary>Nombre de zombies trop proches d'une case : c'est le terme de separation.</summary>
    private int Crowding(GridPosition cell, Zombie zombie, IReadOnlyList<Zombie> horde)
    {
        int crowd = 0;
        foreach (var other in horde)
        {
            if (ReferenceEquals(other, zombie)) continue;
            if (other.Position.ChebyshevDistanceTo(cell) <= Settings.SeparationRadius) crowd++;
        }

        return crowd;
    }

    /// <summary>Centre des zombies proches, ou <c>null</c> si le zombie est seul : terme de cohesion.</summary>
    private (float X, float Y)? Centroid(Zombie zombie, IReadOnlyList<Zombie> horde)
    {
        if (Settings.CohesionWeight <= 0f) return null;

        float sumX = 0f, sumY = 0f;
        int count = 0;

        foreach (var other in horde)
        {
            if (ReferenceEquals(other, zombie)) continue;
            if (other.Position.ChebyshevDistanceTo(zombie.Position) > Settings.CohesionRadius) continue;

            sumX += other.Position.X;
            sumY += other.Position.Y;
            count++;
        }

        return count == 0 ? null : (sumX / count, sumY / count);
    }

    private static bool IsOccupied(GridPosition cell, Zombie zombie, IReadOnlyList<Zombie> horde)
    {
        foreach (var other in horde)
        {
            if (!ReferenceEquals(other, zombie) && other.Position == cell) return true;
        }

        return false;
    }

    /// <summary>Meme regle anti-coupe-de-coin que la navigation : une diagonale ne rase pas un mur.</summary>
    private static bool CanStep(GridPosition from, GridPosition offset, GridPosition candidate, GridMap map)
    {
        if (!map.IsWalkable(candidate)) return false;
        if (offset.X == 0 || offset.Y == 0) return true;

        return map.IsWalkable(new GridPosition(from.X + offset.X, from.Y))
            && map.IsWalkable(new GridPosition(from.X, from.Y + offset.Y));
    }

    private static float Distance(GridPosition cell, float x, float y)
    {
        float dx = cell.X - x;
        float dy = cell.Y - y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}
