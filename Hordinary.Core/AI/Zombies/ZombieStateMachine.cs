using Hordinary.Agents;
using Hordinary.World;

namespace Hordinary.AI.Zombies;

/// <summary>Etat choisi, cellule visee et motif — ce dernier alimente le journal de decisions.</summary>
public readonly record struct ZombieDecision(ZombieState State, GridPosition? Destination, string Reason);

/// <summary>
/// Machine a etats d'un zombie (specification section 17.1) appliquant l'ordre de priorite de la
/// section 17.3 : survivant visible, bruit recent, derniere position connue, errance.
///
/// Elle ne lit que la memoire du zombie. Aucun parametre, aucun retour, aucun champ ne mentionne
/// <see cref="WorldState"/> ni <see cref="NoiseSystem"/> : un zombie ne peut pas deduire ou se
/// trouve un survivant qu'il n'a ni vu ni entendu — c'est le critere de validation du livrable 3.
/// </summary>
public sealed class ZombieStateMachine(ZombieSettings? settings = null)
{
    public ZombieSettings Settings { get; } = settings ?? ZombieSettings.Default;

    public ZombieDecision Decide(Zombie zombie, double now)
    {
        var knowledge = zombie.Knowledge;
        var sighting = MostRecentSurvivor(knowledge, now);

        // 1. Un survivant est sous ses yeux.
        if (IsInSight(sighting, now))
        {
            zombie.TargetId = sighting!.Id;
            zombie.LastKnownTargetPosition = sighting.Value;
            zombie.SearchAnchor = null;
            zombie.SearchUntil = 0d;

            return sighting.Value.ChebyshevDistanceTo(zombie.Position) <= 1
                ? new ZombieDecision(ZombieState.Attack, sighting.Value, $"{sighting.Id} a portee")
                : new ZombieDecision(ZombieState.Chase, sighting.Value, $"{sighting.Id} en vue");
        }

        // 2. Un bruit, a condition qu'il soit plus recent que ce que le zombie sait deja de sa cible.
        //    Sinon un vieux fracas detournerait un zombie qui vient tout juste de perdre quelqu'un
        //    de vue : « bruit recent » veut dire « information plus fraiche », pas « n'importe quel
        //    bruit encore audible ».
        if (knowledge.MostRecentNoise(now) is { } noise
            && (sighting is null || noise.LastObservedAt >= sighting.LastObservedAt))
        {
            if (zombie.Position != noise.Value)
            {
                return new ZombieDecision(
                    ZombieState.InvestigateNoise, noise.Value,
                    $"bruit d'intensite {noise.PerceivedIntensity:0.#} vers {noise.Value}");
            }

            // Il est arrive a l'endroit d'ou il croyait que venait le bruit : il n'y a rien, et ce
            // souvenir ne lui apprendra plus rien.
            knowledge.ForgetNoise(noise.Id);
        }

        // 3. Il ne voit plus sa cible mais sait encore ou elle etait.
        if (sighting is not null && knowledge.ConfidenceIn(sighting, now) > knowledge.Settings.MinimumConfidence)
        {
            zombie.TargetId = sighting.Id;
            zombie.LastKnownTargetPosition = sighting.Value;

            return new ZombieDecision(
                ZombieState.SearchLastKnownPosition, sighting.Value,
                $"derniere position connue de {sighting.Id}");
        }

        // 4. La piste est froide : il fouille les environs avant de renoncer (section 18.3, etape 4).
        StartSearchIfTrackJustLost(zombie, now);

        if (zombie.SearchAnchor is { } anchor)
        {
            if (now < zombie.SearchUntil)
            {
                return new ZombieDecision(
                    ZombieState.SearchLastKnownPosition,
                    Roam(zombie, anchor, Settings.SearchRadius, now),
                    $"fouille autour de {anchor}");
            }

            zombie.LoseTrack();
        }

        // 5. Errance.
        if (now < zombie.IdleUntil) return new ZombieDecision(ZombieState.Idle, null, "immobile");

        if (NeedsNewRoamTarget(zombie, now))
        {
            // En arrivant au bout d'une errance, le zombie s'arrete parfois un instant.
            if (zombie.RoamTarget is not null && zombie.Rng.NextDouble() < Settings.IdleChance)
            {
                zombie.IdleUntil = now + Settings.IdleDuration;
                zombie.RoamTarget = null;
                return new ZombieDecision(ZombieState.Idle, null, "immobile");
            }

            zombie.RoamTarget = PickRoamCell(zombie, zombie.Position, Settings.WanderRadius);
            zombie.RepickAt = now + Settings.RoamRepickInterval;
        }

        return new ZombieDecision(ZombieState.Wander, zombie.RoamTarget, "errance");
    }

    // --- Lecture de la memoire --------------------------------------------------------------

    /// <summary>Croyance la plus recente portant sur un survivant. Les autres zombies sont ignores.</summary>
    private static EntityBelief? MostRecentSurvivor(AgentKnowledge knowledge, double now)
        => knowledge.KnownEntities.Values
            .Where(belief => belief.Kind == EntityKind.Survivor)
            .OrderByDescending(belief => belief.LastObservedAt)
            .ThenByDescending(belief => knowledge.ConfidenceIn(belief, now))
            .FirstOrDefault();

    /// <summary>
    /// « Visible » signifie observe directement a l'instant. Une croyance transmise ou vieille de
    /// plus de <see cref="ZombieSettings.SightMemory"/> ne compte plus comme une vue.
    /// </summary>
    private bool IsInSight(EntityBelief? belief, double now)
        => belief is not null
        && belief.Source == KnowledgeSource.DirectObservation
        && belief.Confidence > 0f
        && now - belief.LastObservedAt <= Settings.SightMemory;

    // --- Fouille et errance -----------------------------------------------------------------

    private void StartSearchIfTrackJustLost(Zombie zombie, double now)
    {
        if (zombie.SearchAnchor is not null) return;
        if (zombie.LastKnownTargetPosition is not { } lastKnown) return;
        if (zombie.State is not (ZombieState.Chase or ZombieState.Attack or ZombieState.SearchLastKnownPosition)) return;

        zombie.SearchAnchor = lastKnown;
        zombie.SearchUntil = now + Settings.SearchDuration;
    }

    private GridPosition Roam(Zombie zombie, GridPosition anchor, int radius, double now)
    {
        if (NeedsNewRoamTarget(zombie, now) || !Within(zombie.RoamTarget, anchor, radius))
        {
            zombie.RoamTarget = PickRoamCell(zombie, anchor, radius);
            zombie.RepickAt = now + Settings.RoamRepickInterval;
        }

        return zombie.RoamTarget ?? anchor;
    }

    private static bool NeedsNewRoamTarget(Zombie zombie, double now)
        => zombie.RoamTarget is null || zombie.RoamTarget == zombie.Position || now >= zombie.RepickAt;

    private static bool Within(GridPosition? cell, GridPosition anchor, int radius)
        => cell is { } value && value.ChebyshevDistanceTo(anchor) <= radius;

    /// <summary>
    /// Cellule tiree au sort dans le voisinage, parmi celles que le zombie <b>a deja vues</b> libres.
    /// Il ne peut pas se donner rendez-vous dans une piece qu'il n'a jamais visitee.
    /// </summary>
    private static GridPosition PickRoamCell(Zombie zombie, GridPosition anchor, int radius)
    {
        var map = zombie.Knowledge.MentalMap;

        for (int attempt = 0; attempt < 12; attempt++)
        {
            var candidate = new GridPosition(
                anchor.X + zombie.Rng.Next(-radius, radius + 1),
                anchor.Y + zombie.Rng.Next(-radius, radius + 1));

            if (candidate != zombie.Position && map.GetCell(candidate).IsKnownWalkable()) return candidate;
        }

        return anchor;
    }
}
