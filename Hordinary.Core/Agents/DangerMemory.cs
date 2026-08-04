using Hordinary.AI.Navigation;
using Hordinary.Config;
using Hordinary.World;

namespace Hordinary.Agents;

/// <summary>
/// Ce qu'un agent croit savoir du danger d'une case : a quel point, depuis quand, et pourquoi.
/// « Niveau de danger d'une zone » figure parmi les informations dynamiques de la specification
/// (section 9.4) — celles qui perdent en fiabilite avec le temps.
/// </summary>
public sealed record DangerBelief(
    GridPosition Position,
    float Value,
    float Confidence,
    double LastObservedAt,
    KnowledgeSource Source) : IBelief
{
    public override string ToString() => $"{Position} : menace {Value:0.00} depuis t={LastObservedAt:0.0}";
}

/// <summary>
/// Carte de chaleur des menaces, propre a un agent (specification, informations dynamiques :
/// « niveau de danger d'une zone »).
///
/// Trois idees, et elles se tiennent :
///
/// <b>Voir un zombie marque un voisinage, pas une case.</b> Une menace deborde de l'endroit ou elle
/// se tient — on evite la rue, pas le pave.
///
/// <b>Le marquage se propage de proche en proche, sur les cases praticables de la carte mentale.</b>
/// Un zombie derriere un mur ne rend pas le couloir d'a cote dangereux : la menace suit les chemins
/// qu'elle pourrait emprunter, pas la ligne droite. C'est ce qui distingue cette carte du danger
/// geometrique que calcule la Utility AI a l'instant present.
///
/// <b>Elle s'efface.</b> Une case marquee il y a vingt secondes ne dit plus grand-chose ; sans
/// oubli, un survivant finirait par croire tout le quartier infrequentable et ne bougerait plus.
///
/// La classe ne connait pas <see cref="WorldState"/> : elle ne recoit que des positions que l'agent
/// a lui-meme percues.
/// </summary>
public sealed class DangerMemory(GridMap mentalMap, KnowledgeSettings? settings = null)
{
    private readonly Dictionary<GridPosition, DangerBelief> _cells = [];
    private readonly GridMap _map = mentalMap ?? throw new ArgumentNullException(nameof(mentalMap));

    public KnowledgeSettings Settings { get; } = settings ?? new KnowledgeSettings();

    public IReadOnlyDictionary<GridPosition, DangerBelief> Cells => _cells;

    /// <summary>
    /// Marque une menace apercue. <paramref name="intensity"/> est ce que l'agent lui accorde :
    /// 1 pour un zombie sous les yeux, moins pour un bruit dont il ignore l'origine.
    /// </summary>
    public int Mark(
        GridPosition origin,
        double time,
        float intensity = 1f,
        KnowledgeSource source = KnowledgeSource.DirectObservation)
    {
        if (intensity <= 0f) return 0;

        int radius = Math.Max(0, Settings.DangerSpreadRadius);
        int marked = 0;

        foreach (var (cell, distance) in Spread(origin, radius))
        {
            // La menace faiblit avec le nombre de pas qui l'en separent, jamais avec la distance a
            // vol d'oiseau : c'est ce qui laisse les murs faire leur travail.
            float value = intensity * (1f - (float)distance / (radius + 1));
            if (value <= 0f) continue;

            if (_cells.TryGetValue(cell, out var known)
                && known.LastObservedAt >= time
                && known.Value >= value)
            {
                continue;
            }

            _cells[cell] = new DangerBelief(cell, Math.Clamp(value, 0f, 1f), 1f, time, source);
            marked++;
        }

        return marked;
    }

    /// <summary>
    /// Menace ressentie sur une case, entre 0 et 1, une fois l'oubli applique. Une case jamais
    /// marquee vaut 0 — ce qui veut dire « rien de connu », pas « sur ».
    /// </summary>
    public float ValueAt(GridPosition cell, double now)
    {
        if (!_cells.TryGetValue(cell, out var belief)) return 0f;

        double elapsed = Math.Max(0d, now - belief.LastObservedAt);
        float remaining = belief.Value - Settings.DangerDecayPerSecond * (float)elapsed;

        return Math.Max(0f, remaining);
    }

    /// <summary>
    /// Traduction du niveau continu vers le bareme de cout de la navigation : c'est ce qui fait
    /// qu'A* prefere le detour a la rue ou l'on a vu passer quelque chose.
    /// </summary>
    public DangerLevel LevelAt(GridPosition cell, double now)
    {
        float value = ValueAt(cell, now);

        if (value >= Settings.DangerConfirmedThreshold) return DangerLevel.ZombieNearby;
        if (value >= Settings.DangerSuspectedThreshold) return DangerLevel.Suspected;

        return DangerLevel.None;
    }

    /// <summary>Case memorisee la plus dangereuse, si elle depasse le seuil de suspicion.</summary>
    public DangerBelief? Worst(double now)
    {
        DangerBelief? worst = null;
        float best = Settings.DangerSuspectedThreshold;

        foreach (var belief in _cells.Values)
        {
            float value = ValueAt(belief.Position, now);
            if (value < best) continue;

            best = value;
            worst = belief;
        }

        return worst;
    }

    /// <summary>Retire les cases dont la menace est retombee a zero. Simple menage.</summary>
    public int Forget(double now)
    {
        var faded = _cells.Keys.Where(cell => ValueAt(cell, now) <= 0f).ToList();
        foreach (var cell in faded) _cells.Remove(cell);
        return faded.Count;
    }

    public void Clear() => _cells.Clear();

    /// <summary>
    /// Parcours en largeur depuis l'origine, en ne traversant que ce que l'agent croit praticable.
    /// L'origine elle-meme est toujours marquee : un zombie dans une embrasure de porte reste une
    /// menace, meme si la case ne se traverse pas.
    /// </summary>
    private IEnumerable<(GridPosition Cell, int Distance)> Spread(GridPosition origin, int radius)
    {
        if (!_map.Contains(origin)) yield break;

        yield return (origin, 0);

        var seen = new HashSet<GridPosition> { origin };
        var frontier = new Queue<(GridPosition Cell, int Distance)>();
        frontier.Enqueue((origin, 0));

        while (frontier.Count > 0)
        {
            var (cell, distance) = frontier.Dequeue();
            if (distance >= radius) continue;

            foreach (var offset in GridPosition.OrthogonalOffsets)
            {
                var neighbour = cell + offset;

                if (!_map.Contains(neighbour)) continue;
                if (!seen.Add(neighbour)) continue;

                // Un mur arrete la propagation ; une case jamais observee, non — l'agent ignore ce
                // qu'elle contient, il ne peut pas s'en servir pour se rassurer.
                if (!_map.IsWalkable(neighbour)) continue;

                yield return (neighbour, distance + 1);
                frontier.Enqueue((neighbour, distance + 1));
            }
        }
    }
}
