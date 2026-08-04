using Hordinary.AI.Navigation;
using Hordinary.Agents;
using Hordinary.World;

namespace Hordinary.Tests;

/// <summary>
/// Simulation complete de la boucle du livrable 2 sur la carte livree, sans Godot : perception,
/// planification sur la carte mentale, deplacement, replanification quand un obstacle est decouvert.
///
/// C'est la verification du critere de validation : Alice n'utilise que ce qu'elle a observe, et
/// atteint malgre tout sa destination.
/// </summary>
public class SurvivorJourneyTests
{
    private const float Step = 0.1f;
    private const int MaxSteps = 2000;

    private sealed record Journey(
        bool Arrived,
        int Replans,
        float ExploredAtStart,
        float ExploredAtEnd,
        int Steps);

    private static Journey Travel(GridMap map, GridPosition start, GridPosition goal, float fieldOfView)
    {
        var world = new WorldState(map);
        var knowledge = new AgentKnowledge("alice", map.Width, map.Height, map.Origin);
        var alice = new Survivor(
            "alice", "Alice", knowledge,
            new PerceptionSettings { VisionRange = 8, FieldOfViewDegrees = fieldOfView })
        {
            Position = start,
        };

        var perception = new PerceptionSystem();
        var navigation = new NavigationGrid(knowledge.MentalMap);
        var search = new AStar(navigation);
        var follower = new PathFollower { Speed = 4f };
        follower.Teleport(start);

        perception.Observe(alice, world, 0d);
        float exploredAtStart = knowledge.ExploredRatio;

        int replans = 0;
        int steps = 0;

        for (; steps < MaxSteps; steps++)
        {
            if (follower.CurrentCell == goal) break;

            // Le chemin a ete abandonne (case devenue infranchissable) ou termine : on replanifie.
            if (follower.IsFinished)
            {
                var route = search.FindPath(follower.CurrentCell, goal);
                if (!route.Found) break;

                follower.SetPath(route.Cells);
                replans++;
            }

            follower.Advance(Step, navigation.IsTraversable);

            alice.Position = follower.CurrentCell;
            alice.Facing = follower.Facing;
            world.Advance(Step);
            perception.Observe(alice, world);
        }

        return new Journey(
            follower.CurrentCell == goal, replans, exploredAtStart, knowledge.ExploredRatio, steps);
    }

    [Fact]
    public void Alice_atteint_la_pharmacie_en_ne_connaissant_que_ce_qu_elle_a_vu()
    {
        var map = DemoMap.Load();
        var start = map.FindWalkableIn(map.GetZone(ZoneKind.Start)!)!.Value;
        var goal = map.FindWalkableIn(map.GetZone(ZoneKind.Pharmacy)!)!.Value;

        var journey = Travel(map, start, goal, fieldOfView: 180f);

        Assert.True(journey.Arrived, $"Alice n'a pas atteint {goal} en {journey.Steps} pas de simulation.");

        // Elle est partie en ignorant l'essentiel du terrain et l'a decouvert en chemin.
        Assert.True(journey.ExploredAtStart < 0.3f, $"Depart trop informe : {journey.ExploredAtStart:P0}.");
        Assert.True(
            journey.ExploredAtEnd > journey.ExploredAtStart,
            "La carte mentale ne s'est pas enrichie pendant le trajet.");
    }

    /// <summary>
    /// Le camp etant desormais le depart et le retour, la traversee qui met la replanification a
    /// l'epreuve n'est plus « depart vers extraction » — ce serait un trajet nul — mais le long aller
    /// jusqu'au hangar sud, a l'autre bout de la rue principale.
    /// </summary>
    [Fact]
    public void Alice_atteint_le_hangar_sud_malgre_les_obstacles_decouverts_en_route()
    {
        var map = DemoMap.Load();
        var start = map.FindWalkableIn(map.GetZone(ZoneKind.Start)!)!.Value;

        var hangar = map.Zones.First(zone => zone.Name == "Hangar sud");
        var goal = map.FindWalkableIn(hangar)!.Value;

        var journey = Travel(map, start, goal, fieldOfView: 180f);

        Assert.True(journey.Arrived, $"Alice n'a pas atteint {goal} en {journey.Steps} pas de simulation.");

        // Le trajet initial etait planifie a travers l'inconnu : des murs ont forcement surgi.
        Assert.True(journey.Replans > 1, $"Aucune replanification ({journey.Replans}) : le trajet etait deja connu.");
    }
}
