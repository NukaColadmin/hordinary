using System.Numerics;
using Hordinary.AI.Navigation;
using Hordinary.Agents;
using Hordinary.World;

namespace Hordinary.Tests;

/// <summary>
/// Vision et carte mentale (specification section 9.2), et critere de validation du livrable 2 :
/// « le survivant ne peut pas utiliser les informations d'une zone qu'il n'a jamais observee ».
/// </summary>
public class PerceptionTests
{
    private static readonly PerceptionSettings Panoramic =
        new() { VisionRange = 4, FieldOfViewDegrees = 360f };

    private static Survivor Alice(GridMap world, GridPosition position, PerceptionSettings? settings = null)
    {
        var knowledge = new AgentKnowledge("alice", world.Width, world.Height, world.Origin);
        var survivor = new Survivor("alice", "Alice", knowledge, settings ?? Panoramic)
        {
            Position = position,
        };
        return survivor;
    }

    // --- Ligne de vue ---------------------------------------------------------------------

    [Fact]
    public void Un_mur_arrete_la_vision()
    {
        var map = TestMaps.Parse(
            ".....",
            "..#..",
            ".....");

        var observer = new GridPosition(0, 1);

        Assert.True(LineOfSight.IsVisible(map, observer, new GridPosition(1, 1)));

        // Le mur lui-meme est visible ; ce qu'il cache ne l'est pas.
        Assert.True(LineOfSight.IsVisible(map, observer, new GridPosition(2, 1)));
        Assert.False(LineOfSight.IsVisible(map, observer, new GridPosition(3, 1)));
        Assert.False(LineOfSight.IsVisible(map, observer, new GridPosition(4, 1)));
    }

    [Fact]
    public void Une_porte_fermee_arrete_la_vision_mais_pas_une_porte_ouverte()
    {
        var closed = TestMaps.Parse("#D#", "...");
        var open = TestMaps.Parse("#D#", "...");
        open.SetCell(new GridPosition(1, 0), CellType.DoorOpen);

        var observer = new GridPosition(1, 1);

        Assert.Contains(
            new GridPosition(1, 0),
            new PerceptionSystem().ComputeVisibleCells(closed, observer, Vector2.Zero, 3, 360f));

        Assert.False(LineOfSight.IsVisible(closed, new GridPosition(1, 1), new GridPosition(1, -1)));
        Assert.True(LineOfSight.IsVisible(open, new GridPosition(1, 1), new GridPosition(1, -1)));
    }

    // --- Champ de vision ------------------------------------------------------------------

    [Fact]
    public void La_vision_est_limitee_par_la_portee()
    {
        var map = new GridMap(21, 21, new GridPosition(-10, -10), CellType.Floor);
        var visible = new PerceptionSystem()
            .ComputeVisibleCells(map, GridPosition.Zero, Vector2.Zero, range: 3, fieldOfViewDegrees: 360f);

        Assert.Contains(new GridPosition(3, 0), visible);
        Assert.DoesNotContain(new GridPosition(4, 0), visible);
        Assert.All(visible, cell => Assert.True(cell.ChebyshevDistanceTo(GridPosition.Zero) <= 3));
    }

    [Fact]
    public void Le_cone_de_vision_exclut_ce_qui_se_trouve_derriere()
    {
        var map = new GridMap(21, 21, new GridPosition(-10, -10), CellType.Floor);

        // Regard vers la droite, ouverture de 90 degres.
        var visible = new PerceptionSystem()
            .ComputeVisibleCells(map, GridPosition.Zero, new Vector2(1f, 0f), range: 4, fieldOfViewDegrees: 90f);

        Assert.Contains(new GridPosition(3, 0), visible);
        Assert.DoesNotContain(new GridPosition(-3, 0), visible);
        Assert.DoesNotContain(new GridPosition(0, 3), visible);

        // La cellule de l'observateur reste toujours visible.
        Assert.Contains(GridPosition.Zero, visible);
    }

    [Fact]
    public void Une_vision_panoramique_couvre_toutes_les_directions()
    {
        var map = new GridMap(21, 21, new GridPosition(-10, -10), CellType.Floor);
        var visible = new PerceptionSystem()
            .ComputeVisibleCells(map, GridPosition.Zero, new Vector2(1f, 0f), range: 3, fieldOfViewDegrees: 360f);

        Assert.Contains(new GridPosition(3, 0), visible);
        Assert.Contains(new GridPosition(-3, 0), visible);
        Assert.Contains(new GridPosition(0, -3), visible);
    }

    // --- Observation ----------------------------------------------------------------------

    [Fact]
    public void Observer_remplit_la_carte_mentale_et_laisse_le_reste_inconnu()
    {
        var map = TestMaps.Parse(
            ".....",
            ".....",
            "#####",
            ".....");

        var world = new WorldState(map);
        var alice = Alice(map, new GridPosition(0, 0));

        var result = new PerceptionSystem().Observe(alice, world, time: 0d);

        Assert.NotEmpty(result.VisibleCells);
        Assert.Equal(result.VisibleCells.Count, result.DiscoveredCells);

        // Ce qui est derriere le mur n'a jamais ete observe.
        Assert.False(alice.Knowledge.HasObserved(new GridPosition(0, 3)));
        Assert.Equal(CellType.Unknown, alice.Knowledge.MentalMap.GetCell(new GridPosition(0, 3)));

        // Une seconde passe ne decouvre plus rien de nouveau.
        Assert.Equal(0, new PerceptionSystem().Observe(alice, world, 1d).DiscoveredCells);
    }

    [Fact]
    public void Une_entite_hors_de_vue_n_est_pas_memorisee()
    {
        var map = TestMaps.Parse(
            ".....",
            "#####",
            ".....");

        var world = new WorldState(map);
        world.SetEntity("zombie-1", EntityKind.Zombie, new GridPosition(2, 2));

        var alice = Alice(map, new GridPosition(2, 0));
        new PerceptionSystem().Observe(alice, world, 0d);

        Assert.Empty(alice.Knowledge.KnownEntities);
    }

    [Fact]
    public void Une_entite_visible_est_memorisee_puis_oubliee_quand_la_case_est_vue_vide()
    {
        var map = TestMaps.Parse(".....", ".....", ".....");
        var world = new WorldState(map);
        world.SetEntity("zombie-1", EntityKind.Zombie, new GridPosition(2, 1));

        var alice = Alice(map, new GridPosition(0, 1));
        var perception = new PerceptionSystem();

        perception.Observe(alice, world, 0d);
        Assert.Equal(new GridPosition(2, 1), alice.Knowledge.GetEntity("zombie-1")!.Value);

        // Le zombie disparait ; Alice regarde la case et la trouve vide.
        world.RemoveEntity("zombie-1");
        perception.Observe(alice, world, 1d);

        var belief = alice.Knowledge.GetEntity("zombie-1")!;
        Assert.Equal(0f, belief.Confidence, 3);
        Assert.Empty(alice.Knowledge.BelievedEntities(1d));
    }

    [Fact]
    public void Voir_vide_la_case_ou_se_tenait_un_poursuivant_ne_le_fait_pas_disparaitre()
    {
        var map = TestMaps.Parse(
            ".........",
            ".........",
            ".........");

        var world = new WorldState(map);
        var perception = new PerceptionSystem();

        // Alice regarde vers l'est, cone de 180 degres : ce qui passe derriere elle sort de sa vue.
        var alice = Alice(map, new GridPosition(4, 1), new PerceptionSettings
        {
            VisionRange = 4,
            FieldOfViewDegrees = 180f,
        });
        alice.Facing = new Vector2(1f, 0f);

        world.SetEntity("zombie-1", EntityKind.Zombie, new GridPosition(6, 1));
        perception.Observe(alice, world, 0d);

        world.SetEntity("zombie-1", EntityKind.Zombie, new GridPosition(5, 1));
        perception.Observe(alice, world, 1d);

        // Le zombie la depasse et passe dans son dos. La case (5,1) ou elle l'a vu la derniere fois
        // est toujours devant elle, donc visible, et desormais vide.
        world.SetEntity("zombie-1", EntityKind.Zombie, new GridPosition(3, 1));
        var view = perception.Observe(alice, world, 3d);

        Assert.Contains(new GridPosition(5, 1), view.VisibleCells);
        Assert.DoesNotContain(new GridPosition(3, 1), view.VisibleCells);

        // Constater cette case vide ne prouve rien, sinon qu'il a avance — ce qu'elle croyait deja.
        // Sans cap memorise, la croyance serait tombee a zero et Alice repartirait tranquillement.
        var belief = alice.Knowledge.GetEntity("zombie-1")!;
        Assert.True(belief.Confidence > 0f);
        Assert.Equal(new GridPosition(3, 1), alice.Knowledge.PredictedPositionOf(belief, 3d));
    }

    [Fact]
    public void Regarder_la_ou_le_poursuivant_devrait_etre_dement_bien_la_croyance()
    {
        var map = TestMaps.Parse(
            ".........",
            ".........",
            ".........");

        var world = new WorldState(map);
        var perception = new PerceptionSystem();
        var alice = Alice(map, new GridPosition(0, 1));

        world.SetEntity("zombie-1", EntityKind.Zombie, new GridPosition(4, 1));
        perception.Observe(alice, world, 0d);

        world.SetEntity("zombie-1", EntityKind.Zombie, new GridPosition(3, 1));
        perception.Observe(alice, world, 1d);

        // Il s'est volatilise. Alice voit tout le couloir, y compris l'endroit ou il devrait etre :
        // cette fois l'absence est une information, et la croyance tombe.
        world.RemoveEntity("zombie-1");
        perception.Observe(alice, world, 2d);

        Assert.Equal(0f, alice.Knowledge.GetEntity("zombie-1")!.Confidence, 3);
        Assert.Empty(alice.Knowledge.BelievedEntities(2d));
    }

    [Fact]
    public void Un_objet_dans_une_piece_non_observee_reste_ignore()
    {
        var map = TestMaps.Parse(
            ".....",
            "#####",
            ".....");

        var world = new WorldState(map);
        world.SetItem("objective-1", Survivor.DefaultObjectiveItem, new GridPosition(2, 2));

        var alice = Alice(map, new GridPosition(0, 0));
        var perception = new PerceptionSystem();

        perception.Observe(alice, world, 0d);
        Assert.Empty(alice.Knowledge.KnownItems);

        // Une fois de l'autre cote du mur, les medicaments sont reperes.
        alice.Position = new GridPosition(1, 2);
        perception.Observe(alice, world, 1d);

        Assert.Equal(new GridPosition(2, 2), alice.Knowledge.GetItem("objective-1")!.Value);
        Assert.Equal(KnowledgeSource.DirectObservation, alice.Knowledge.GetItem("objective-1")!.Source);
    }

    // --- Critere de validation du livrable 2 ----------------------------------------------

    [Fact]
    public void A_star_navigue_sur_la_carte_mentale_et_ignore_les_murs_jamais_vus()
    {
        // Un mur barre la route, mais Alice ne l'a jamais observe : elle planifie tout droit.
        var map = TestMaps.Parse(
            ".....",
            "..#..",
            ".....");
        map.SetCell(new GridPosition(2, 0), CellType.Wall);
        map.SetCell(new GridPosition(2, 2), CellType.Wall);

        var alice = Alice(map, new GridPosition(0, 1), new PerceptionSettings { VisionRange = 1, FieldOfViewDegrees = 360f });

        var start = new GridPosition(0, 1);
        var goal = new GridPosition(4, 1);

        var mental = new AStar(new NavigationGrid(alice.Knowledge.MentalMap));
        var truth = new AStar(new NavigationGrid(map));

        var planned = mental.FindPath(start, goal);
        Assert.True(planned.Found);
        Assert.Contains(new GridPosition(2, 1), planned.Cells);

        // Sur la carte reelle, ce couloir est mure : aucun itineraire n'existe.
        Assert.False(truth.FindPath(start, goal).Found);
    }

    [Fact]
    public void Le_survivant_decouvre_le_mur_puis_le_contourne()
    {
        var map = TestMaps.Parse(
            ".....",
            "..#..",
            ".....");

        var world = new WorldState(map);
        var alice = Alice(map, new GridPosition(0, 1), new PerceptionSettings { VisionRange = 4, FieldOfViewDegrees = 360f });
        var search = new AStar(new NavigationGrid(alice.Knowledge.MentalMap));

        var goal = new GridPosition(4, 1);

        var blind = search.FindPath(alice.Position, goal);
        Assert.Contains(new GridPosition(2, 1), blind.Cells);

        new PerceptionSystem().Observe(alice, world, 0d);

        var informed = search.FindPath(alice.Position, goal);
        Assert.True(informed.Found);
        Assert.DoesNotContain(new GridPosition(2, 1), informed.Cells);
    }

    [Fact]
    public void Le_survivant_ne_peut_pas_cibler_un_lieu_dont_il_ignore_l_existence()
    {
        var map = TestMaps.Parse(".....", ".....");
        map.AddZone(new MapZone("Pharmacie", ZoneKind.Pharmacy, new GridPosition(4, 0), new GridPosition(4, 1)));

        var alice = Alice(map, new GridPosition(0, 0));

        // La zone existe dans le monde, mais pas dans la tete d'Alice.
        Assert.NotNull(map.GetZone(ZoneKind.Pharmacy));
        Assert.Null(alice.Knowledge.GetLocation(ZoneKind.Pharmacy));

        // Le briefing du joueur la lui apprend : c'est une inference, pas une observation.
        alice.Knowledge.RememberLocation(
            "Pharmacie", ZoneKind.Pharmacy, new GridPosition(4, 0), 0d, KnowledgeSource.Inference);

        var known = alice.Knowledge.GetLocation(ZoneKind.Pharmacy);
        Assert.NotNull(known);
        Assert.Equal(KnowledgeSource.Inference, known.Source);
    }

    [Fact]
    public void Le_regard_suit_le_deplacement()
    {
        var follower = new PathFollower { Speed = 1f };
        follower.Teleport(new GridPosition(0, 0));
        follower.SetPath([new GridPosition(0, 0), new GridPosition(3, 0)]);

        follower.Advance(0.5f);

        Assert.Equal(1f, follower.Facing.X, 3);
        Assert.Equal(0f, follower.Facing.Y, 3);
    }
}
