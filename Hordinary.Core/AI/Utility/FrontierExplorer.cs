using Hordinary.Agents;
using Hordinary.Config;
using Hordinary.World;

namespace Hordinary.AI.Utility;

/// <summary>
/// Une frontiere d'exploration notee, terme par terme. Les quatre composantes sont conservees
/// separement et non fondues dans le seul <see cref="Score"/> : c'est ce qui permet au mode debug
/// d'expliquer pourquoi Alice part au nord — « le gain etait le meme, mais Bob s'occupait deja du
/// sud » est une phrase que seul le detail permet d'ecrire.
/// </summary>
public readonly record struct Frontier(
    GridPosition Cell,
    float InformationGain,
    float DistanceCost,
    float Danger,
    float Duplication)
{
    /// <summary>La formule de la specification, telle quelle.</summary>
    public float Score => InformationGain - DistanceCost - Danger - Duplication;

    public override string ToString()
        => $"{Cell} : {Score:0.00} (gain {InformationGain:0.00}, trajet -{DistanceCost:0.00}, "
         + $"danger -{Danger:0.00}, allie -{Duplication:0.00})";
}

/// <summary>
/// Choisit ou aller voir (specification, livrable 7 : « frontier exploration »).
///
/// Une frontiere est une cellule connue et praticable voisine d'une cellule jamais observee — le bord
/// de ce que le survivant sait. Chacune recoit le score de la specification :
///
/// <code>
/// score = expected_information_gain - distance_cost - perceived_danger - ally_duplication_penalty
/// </code>
///
/// Le quatrieme terme est celui qui fait l'equipe. Sans lui, trois survivants partis du meme endroit
/// avec la meme carte mentale calculent le meme meilleur score et vont tous les trois au meme
/// endroit : ils exploreraient a trois la vitesse d'un seul. La penalite se lit dans les intentions
/// que les allies ont <b>annoncees</b> (<see cref="IntentBelief"/>) et dans les positions qu'on leur
/// connait — donc uniquement dans la memoire du survivant. Deux survivants hors de portee de
/// communication se marchent bel et bien dessus, et c'est le comportement attendu : la repartition
/// est une consequence du partage d'informations, pas une regle du moteur.
///
/// Comme tout ce qui decide ici, la classe ne connait pas <see cref="WorldState"/> : elle ne lit que
/// l'<see cref="AgentKnowledge"/> porte par le <see cref="UtilityContext"/>.
/// </summary>
public sealed class FrontierExplorer(ExplorationSettings? settings = null)
{
    public ExplorationSettings Settings { get; } = settings ?? ExplorationSettings.Default;

    /// <summary>
    /// Vrai si la cellule est au bord du connu : praticable, observee, et voisine orthogonale d'une
    /// cellule du terrain que l'agent n'a jamais vue.
    ///
    /// Le voisinage est orthogonal et non diagonal : une diagonale ne se traverse pas quand ses deux
    /// cotes sont bloques, et une frontiere ou l'on ne peut pas aller n'est pas une frontiere.
    /// </summary>
    public static bool IsFrontier(AgentKnowledge knowledge, CellBelief belief)
    {
        ArgumentNullException.ThrowIfNull(knowledge);
        ArgumentNullException.ThrowIfNull(belief);

        if (!belief.Value.IsKnownWalkable()) return false;

        foreach (var offset in GridPosition.OrthogonalOffsets)
        {
            var neighbour = belief.Position + offset;
            if (knowledge.MentalMap.Contains(neighbour) && !knowledge.HasObserved(neighbour)) return true;
        }

        return false;
    }

    /// <summary>
    /// La meilleure frontiere accessible, ou <c>null</c> si le survivant a fait le tour de ce qu'il
    /// peut atteindre. Un score negatif ne disqualifie pas : quand tout est loin, dangereux et deja
    /// pris, il reste a choisir le moins mauvais — c'est la Utility AI, en amont, qui decide si cela
    /// vaut la peine d'explorer du tout.
    /// </summary>
    public Frontier? Choose(UtilityContext context)
    {
        Frontier? best = null;

        foreach (var candidate in Rank(context))
        {
            if (best is { } current && candidate.Score <= current.Score) continue;
            best = candidate;
        }

        return best;
    }

    /// <summary>
    /// Note les frontieres candidates, de la meilleure a la moins bonne.
    ///
    /// Seules les <see cref="ExplorationSettings.MaxCandidates"/> plus proches sont notees : sur une
    /// carte a moitie decouverte, le bord du connu compte des centaines de cellules, et les noter
    /// toutes a chaque decision couterait bien plus que le choix ne rapporte. Prendre les plus
    /// proches n'est pas neutre — c'est un biais assume vers ce qui est atteignable — mais le terme
    /// de distance allait de toute facon dans ce sens.
    /// </summary>
    public IReadOnlyList<Frontier> Rank(UtilityContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var knowledge = context.Self.Knowledge;
        var origin = context.Self.Position;

        var candidates = knowledge.KnownCells.Values
            .Where(belief => IsFrontier(knowledge, belief))
            .OrderBy(belief => belief.Position.ChebyshevDistanceTo(origin))
            .ThenBy(belief => belief.Position.Y)
            .ThenBy(belief => belief.Position.X)
            .Take(Math.Max(1, Settings.MaxCandidates))
            .Select(belief => Score(context, belief.Position))
            .ToList();

        candidates.Sort((left, right) => right.Score.CompareTo(left.Score));
        return candidates;
    }

    /// <summary>Les quatre termes de la specification, pour une cellule donnee.</summary>
    public Frontier Score(UtilityContext context, GridPosition cell)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new Frontier(
            cell,
            InformationGain(context.Self.Knowledge, cell) * Settings.InformationWeight,
            DistanceCost(context, cell) * Settings.DistanceWeight,
            DangerAt(context, cell) * Settings.DangerWeight,
            Duplication(context, cell) * Settings.DuplicationWeight);
    }

    // --- Les quatre termes -----------------------------------------------------------------------

    /// <summary>
    /// Ce que la frontiere promet de reveler : la part de cellules jamais observees dans le carre de
    /// <see cref="ExplorationSettings.InformationRadius"/> qui l'entoure.
    ///
    /// C'est une estimation optimiste, et volontairement : l'agent ne peut pas savoir qu'un mur se
    /// tient juste derriere, puisque justement il n'a pas regarde. Le decompte se fait sur le terrain
    /// declare — hors carte ne compte pas, sinon les bords du monde paraitraient les plus prometteurs.
    /// </summary>
    public float InformationGain(AgentKnowledge knowledge, GridPosition cell)
    {
        ArgumentNullException.ThrowIfNull(knowledge);

        int radius = Math.Max(1, Settings.InformationRadius);
        int unknown = 0, total = 0;

        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                var neighbour = new GridPosition(cell.X + dx, cell.Y + dy);
                if (!knowledge.MentalMap.Contains(neighbour)) continue;

                total++;
                if (!knowledge.HasObserved(neighbour)) unknown++;
            }
        }

        return total == 0 ? 0f : (float)unknown / total;
    }

    /// <summary>Prix du trajet, sature a <see cref="UtilityAiSettings.DistanceScale"/> cellules.</summary>
    private static float DistanceCost(UtilityContext context, GridPosition cell)
    {
        float dx = context.Self.Position.X - cell.X;
        float dy = context.Self.Position.Y - cell.Y;

        return Math.Clamp(MathF.Sqrt(dx * dx + dy * dy) / context.Settings.DistanceScale, 0f, 1f);
    }

    /// <summary>
    /// Ce que la frontiere a de mauvais : les menaces qu'on lui connait maintenant, ou le souvenir
    /// d'y avoir eu peur. Le plus fort des deux — une rue ou l'on a croise une horde reste a eviter
    /// meme quand plus rien n'y est visible.
    /// </summary>
    private static float DangerAt(UtilityContext context, GridPosition cell)
        => Math.Max(context.DangerAt(cell), context.Self.Knowledge.Danger.ValueAt(cell, context.Now));

    /// <summary>
    /// A quel point un allie s'occupe deja de ce coin. Deux sources, et la plus forte l'emporte :
    /// ce qu'il a <b>dit</b> vouloir faire, qui est explicite mais ne survit pas a l'eloignement, et
    /// la ou on le croit, qui est plus faible mais toujours disponible.
    ///
    /// La penalite decroit avec l'ecart et s'annule au-dela de
    /// <see cref="ExplorationSettings.DuplicationRadius"/> : deux survivants qui avancent en
    /// parallele a dix cases l'un de l'autre ne font pas double emploi.
    /// </summary>
    private float Duplication(UtilityContext context, GridPosition cell)
    {
        float radius = Math.Max(1f, Settings.DuplicationRadius);
        float worst = 0f;

        var knowledge = context.Self.Knowledge;

        foreach (var intent in knowledge.AllyIntents(context.Now, knowledge.Settings.IntentMemory))
        {
            float overlap = 1f - Distance(intent.Value, cell) / radius;
            if (overlap <= 0f) continue;

            worst = Math.Max(worst, overlap * intent.Confidence);
        }

        // Un allie dont on ignore les intentions renseigne quand meme : il a des yeux, et ce qu'il
        // longe se decouvre sans nous. On y croit moins qu'a une annonce, d'ou la demi-portee.
        foreach (var ally in context.Allies)
        {
            float overlap = 1f - Distance(ally.Value, cell) / radius;
            if (overlap <= 0f) continue;

            worst = Math.Max(worst, overlap * ally.Confidence * 0.5f);
        }

        return Math.Clamp(worst, 0f, 1f);
    }

    private static float Distance(GridPosition a, GridPosition b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}
