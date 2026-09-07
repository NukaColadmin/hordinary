using System.Numerics;
using Hordinary.AI.Navigation;
using Hordinary.AI.Planning;
using Hordinary.AI.Utility;
using Hordinary.Agents;
using Hordinary.Config;
using Hordinary.World;

namespace Hordinary.Tests;

/// <summary>
/// La memoire d'un agent au-dela des cellules et des entites : les objets et ce qu'ils deviennent,
/// les fouilles infructueuses, la carte de chaleur des menaces, le souvenir d'un bruit, et le coup
/// d'oeil par-dessus l'epaule.
///
/// Toutes ces connaissances partagent la meme regle que le livrable 2 : elles ne viennent que de ce
/// que l'agent a percu, jamais du <c>WorldState</c>.
/// </summary>
public class MemoryTests
{
    private static readonly GameConfig Config = GameConfig.FromJson(DemoMap.ReadConfig("game_config.json"));

    private static (Survivor Alice, WorldState World, PerceptionSystem Perception) Room(params string[] rows)
    {
        var map = TestMaps.Parse(rows);
        var world = new WorldState(map);

        var knowledge = new AgentKnowledge("alice", map.Width, map.Height, map.Origin, Config.Knowledge);
        var alice = new Survivor("alice", "Alice", knowledge, Config.Perception, Config.Survivor)
        {
            Position = new GridPosition(0, 0),
            Facing = new Vector2(1f, 0f),
        };

        return (alice, world, new PerceptionSystem(Config.Perception));
    }

    // --- Objets : coordonnees memorisees, invalidation differee -------------------------------------

    [Fact]
    public void Un_objet_vu_entre_en_memoire_avec_ses_coordonnees()
    {
        var (alice, world, perception) = Room("......");
        var objective = new GridPosition(4, 0);

        world.SetItem("objective-1", Survivor.DefaultObjectiveItem, objective);
        perception.Observe(alice, world, time: 1d);

        var belief = alice.Knowledge.GetItem("objective-1");

        Assert.NotNull(belief);
        Assert.Equal(objective, belief.Value);
        Assert.Equal(Survivor.DefaultObjectiveItem, belief.ItemId);
        Assert.Equal(KnowledgeSource.DirectObservation, belief.Source);
        Assert.False(belief.IsGone);
    }

    [Fact]
    public void Un_objet_ramasse_par_un_autre_reste_en_memoire_jusqu_au_retour_sur_place()
    {
        // Alice voit les objet de la mission au bout du couloir, puis s'en detourne.
        var (alice, world, perception) = Room("..........");
        var objective = new GridPosition(4, 0);

        world.SetItem("objective-1", Survivor.DefaultObjectiveItem, objective);
        perception.Observe(alice, world, time: 1d);

        // Quelqu'un les prend pendant qu'elle regarde ailleurs.
        world.RemoveItem("objective-1");
        alice.Facing = new Vector2(-1f, 0f);
        perception.Observe(alice, world, time: 2d);

        // Rien ne l'a prevenue : elle continue de croire qu'ils sont la, et de pouvoir planifier avec.
        var belief = alice.Knowledge.GetItem("objective-1");
        Assert.NotNull(belief);
        Assert.False(belief.IsGone);
        Assert.Equal(objective, belief.Value);
        Assert.True(new UtilityContext(alice, 2d, Config.UtilityAi).KnowsObjective);

        // Elle se retourne, regarde la case, et constate. C'est seulement la que la croyance tombe.
        alice.Facing = new Vector2(1f, 0f);
        perception.Observe(alice, world, time: 3d);

        Assert.True(alice.Knowledge.GetItem("objective-1")!.IsGone);
        Assert.False(new UtilityContext(alice, 3d, Config.UtilityAi).KnowsObjective);
    }

    [Fact]
    public void Voir_une_case_voisine_n_invalide_pas_un_objet_hors_de_vue()
    {
        // Un mur separe Alice de l'objet : elle ne peut ni le voir, ni conclure qu'il a disparu.
        var (alice, world, perception) = Room("..#...");
        world.SetItem("objective-1", Survivor.DefaultObjectiveItem, new GridPosition(4, 0));

        alice.Knowledge.RememberItem("objective-1", Survivor.DefaultObjectiveItem, new GridPosition(4, 0), 0d);
        world.RemoveItem("objective-1");

        perception.Observe(alice, world, time: 1d);

        Assert.False(alice.Knowledge.GetItem("objective-1")!.IsGone);
    }

    // --- Fouilles infructueuses --------------------------------------------------------------------

    [Fact]
    public void Une_fouille_infructueuse_se_retient_et_devient_un_fait()
    {
        var knowledge = new AgentKnowledge("alice", 8, 8, settings: Config.Knowledge);
        var alice = new Survivor("alice", "Alice", knowledge, Config.Perception, Config.Survivor);

        Assert.Null(knowledge.GetSearch("pharmacy"));

        knowledge.RememberSearch("pharmacy", found: false, 5d);

        var belief = knowledge.GetSearch("pharmacy");
        Assert.NotNull(belief);
        Assert.False(belief.Found);

        // Et le fait remonte jusqu'au planificateur : c'est ce qui empeche de refouiller.
        var state = SymbolicStateBuilder.Observe(new UtilityContext(alice, 5d, Config.UtilityAi)).State;
        Assert.True(state.Holds("searched_in_vain(pharmacy)"));

        // Une fouille fructueuse plus recente efface la deception.
        knowledge.RememberSearch("pharmacy", found: true, 6d);
        state = SymbolicStateBuilder.Observe(new UtilityContext(alice, 6d, Config.UtilityAi)).State;
        Assert.False(state.Holds("searched_in_vain(pharmacy)"));
    }

    [Fact]
    public void Une_pharmacie_vide_finit_par_etre_abandonnee()
    {
        // Aucun medicament nulle part : la fouille echouera, encore et encore, si rien ne l'apprend.
        var mission = new MissionRun(withObjective: false);
        mission.Alice.Mission = new MissionOrder(
            "recuperer les objet de la mission",
            mission.Spawn(ZoneKind.Pharmacy),
            MissionRun.Config.UtilityAi.PlayerOrderPriority);

        bool learned = mission.RunUntil(
            () => mission.Alice.Knowledge.GetSearch("pharmacy") is { Found: false },
            maxTicks: MissionRun.DefaultMaxTicks);

        Assert.True(learned, $"Alice n'a jamais retenu que la pharmacie etait vide. {mission.Trace()}");

        // Elle a cesse de la reclamer : plus aucun plan ne prevoit d'y localiser quoi que ce soit.
        int before = mission.Executed.Count(name => name == "LocateObjective");
        mission.RunUntil(() => false, maxTicks: 300);
        int after = mission.Executed.Count(name => name == "LocateObjective");

        Assert.Equal(before, after);
    }

    // --- Carte de chaleur des menaces ---------------------------------------------------------------

    [Fact]
    public void Voir_un_zombie_marque_son_voisinage_et_le_souvenir_s_efface()
    {
        var (alice, world, perception) = Room(
            "..........",
            "..........",
            "..........");

        world.SetEntity("zombie-1", EntityKind.Zombie, new GridPosition(4, 0));
        perception.Observe(alice, world, time: 10d);

        var danger = alice.Knowledge.Danger;

        // La menace deborde de la case ou elle se tient.
        Assert.True(danger.ValueAt(new GridPosition(4, 0), 10d) > 0f);
        Assert.True(danger.ValueAt(new GridPosition(5, 0), 10d) > 0f);
        Assert.True(
            danger.ValueAt(new GridPosition(4, 0), 10d) > danger.ValueAt(new GridPosition(5, 0), 10d),
            "Le danger doit decroitre a mesure qu'on s'eloigne de la menace.");

        // Le souvenir s'efface : c'est une information dynamique (specification section 9.4).
        float fresh = danger.ValueAt(new GridPosition(4, 0), 10d);
        float later = danger.ValueAt(new GridPosition(4, 0), 20d);

        Assert.True(later < fresh, $"Le danger memorise ne s'efface pas ({fresh:0.00} -> {later:0.00}).");
        Assert.Equal(0f, danger.ValueAt(new GridPosition(4, 0), 1000d), 3);
    }

    [Fact]
    public void Un_mur_arrete_la_zone_de_danger()
    {
        // Deux couloirs paralleles, separes par un mur : le zombie est dans celui du haut.
        var map = TestMaps.Parse(
            "......",
            "######",
            "......");

        var knowledge = new AgentKnowledge("alice", map.Width, map.Height, map.Origin, Config.Knowledge);
        foreach (var cell in map.AllPositions()) knowledge.ObserveCell(cell, map.GetCell(cell), false, 0d);

        knowledge.Danger.Mark(new GridPosition(2, 0), 1d);

        Assert.True(knowledge.Danger.ValueAt(new GridPosition(3, 0), 1d) > 0f);

        // De l'autre cote du mur, a une case a vol d'oiseau mais hors d'atteinte : rien.
        Assert.Equal(0f, knowledge.Danger.ValueAt(new GridPosition(2, 2), 1d), 3);
    }

    [Fact]
    public void Le_danger_memorise_renchérit_le_trajet()
    {
        var map = TestMaps.Parse(
            "......",
            "......");

        var knowledge = new AgentKnowledge("alice", map.Width, map.Height, map.Origin, Config.Knowledge);
        foreach (var cell in map.AllPositions()) knowledge.ObserveCell(cell, map.GetCell(cell), false, 0d);

        var navigation = new NavigationGrid(knowledge.MentalMap, Config.Navigation)
        {
            Sensed = cell => knowledge.Danger.LevelAt(cell, 1d),
        };

        var target = new GridPosition(3, 0);
        float quiet = navigation.GetEnterCost(target);

        knowledge.Danger.Mark(target, 1d);

        Assert.True(
            navigation.GetEnterCost(target) > quiet,
            "Une case ou l'agent se souvient d'un zombie doit couter plus cher a traverser.");
    }

    // --- Memoire auditive -----------------------------------------------------------------------------

    [Fact]
    public void Un_bruit_entendu_retient_son_type_sans_trahir_son_auteur()
    {
        // Trois rangees : un bruit n'est jamais situe exactement, et l'erreur doit avoir de la place
        // pour s'exprimer sans sortir du terrain.
        var (alice, world, perception) = Room(
            "..........",
            "..........",
            "..........");

        alice.Position = new GridPosition(0, 1);

        world.Noises.Emit(NoiseAction.BreakWindow, new GridPosition(3, 1), 1d, sourceId: "zombie-7");
        var heard = perception.Listen(alice, world, new Random(1), time: 1d);

        Assert.NotEmpty(heard);

        var belief = Assert.Single(alice.Knowledge.KnownNoises.Values);

        Assert.Equal(NoiseAction.BreakWindow, belief.Action);
        Assert.True(belief.IsRemarkable);
        Assert.True(belief.PerceivedIntensity > 0f);
        Assert.Equal(KnowledgeSource.Sound, belief.Source);

        // Ce qu'elle ne sait pas : qui. Aucune entite n'est entree en memoire par le bruit.
        Assert.Empty(alice.Knowledge.KnownEntities);

        // Et un bruit marque aussi la carte de chaleur, plus faiblement qu'une vue.
        Assert.True(alice.Knowledge.Danger.ValueAt(belief.Value, 1d) > 0f);
    }

    [Fact]
    public void Des_pas_ne_sont_pas_un_bruit_remarquable()
    {
        // Un pas ne porte pas : au bareme de la section 18.1 il vaut 1, et perd 1 par cellule. Pour
        // qu'Alice l'entende, il faut qu'il soit tout pres.
        var (alice, world, perception) = Room("....");

        world.Noises.Emit(NoiseAction.Walk, alice.Position, 1d, sourceId: "zombie-1");
        perception.Listen(alice, world, new Random(1), time: 1d);

        var belief = Assert.Single(alice.Knowledge.KnownNoises.Values);

        Assert.Equal(NoiseAction.Walk, belief.Action);
        Assert.False(belief.IsRemarkable);
    }

    [Fact]
    public void Une_action_de_plan_s_entend_pour_ce_qu_elle_est()
    {
        var mission = new MissionRun(lockedDoors: [MissionRun.PharmacyDoor]);
        mission.Alice.Mission = new MissionOrder(
            "recuperer les objet de la mission",
            mission.Spawn(ZoneKind.Pharmacy),
            MissionRun.Config.UtilityAi.PlayerOrderPriority);

        mission.RunUntil(() => mission.Executed.Contains("ForceDoor"), maxTicks: MissionRun.DefaultMaxTicks);

        Assert.Contains("ForceDoor", mission.Executed);

        // Le bruit d'une porte forcee n'est plus etiquete « distraction ».
        Assert.Contains(mission.Journal, line => line.Contains("ForceDoor (6", StringComparison.Ordinal));
    }

    // --- Coup d'oeil en arriere -------------------------------------------------------------------------

    [Fact]
    public void Sans_poursuite_le_survivant_ne_se_retourne_jamais()
    {
        var glance = new RearGlance(Config.Survivor);
        var forward = new Vector2(1f, 0f);

        for (double now = 0d; now < 20d; now += 0.2d)
        {
            Assert.Equal(forward, glance.Gaze(forward, pursuit: 0f, now));
        }

        Assert.Equal(0, glance.Count);
    }

    [Fact]
    public void En_fuite_le_survivant_regarde_periodiquement_derriere_lui()
    {
        var settings = new SurvivorSettings
        {
            RearGlanceThreshold = 0.1f,
            RearGlanceInterval = 1f,
            RearGlanceDuration = 0.3f,
        };

        var glance = new RearGlance(settings);
        var forward = new Vector2(1f, 0f);

        int behind = 0;
        for (double now = 0d; now < 5d; now += 0.1d)
        {
            if (glance.Gaze(forward, pursuit: 0.9f, now).X < 0f) behind++;
        }

        // Cinq secondes, un coup d'oeil par seconde, trois dixiemes a chaque fois.
        Assert.Equal(5, glance.Count);
        Assert.InRange(behind, 10, 20);
    }

    [Fact]
    public void Le_coup_d_oeil_rafraichit_la_position_du_poursuivant()
    {
        var (alice, world, perception) = Room(
            "..........",
            "..........");

        // Alice court vers l'est ; le zombie la suit, dans son dos.
        alice.Position = new GridPosition(5, 0);
        var zombie = new GridPosition(2, 0);
        world.SetEntity("zombie-1", EntityKind.Zombie, zombie);

        var glance = new RearGlance(new SurvivorSettings
        {
            RearGlanceThreshold = 0.1f,
            RearGlanceInterval = 1f,
            RearGlanceDuration = 0.3f,
        });

        var travel = new Vector2(1f, 0f);

        // Le regard devant : elle ne voit rien de ce qui la suit.
        alice.Facing = glance.Gaze(travel, pursuit: 0f, 0d);
        perception.Observe(alice, world, time: 0d);
        Assert.Null(alice.Knowledge.GetEntity("zombie-1"));

        // Le coup d'oeil, lui, le trouve.
        alice.Facing = glance.Gaze(travel, pursuit: 0.9f, 1d);
        Assert.True(glance.IsGlancing);

        perception.Observe(alice, world, time: 1d);

        var belief = alice.Knowledge.GetEntity("zombie-1");
        Assert.NotNull(belief);
        Assert.Equal(zombie, belief.Value);
        Assert.Equal(1d, belief.LastObservedAt, 3);
    }

    // --- Configuration ------------------------------------------------------------------------------------

    [Fact]
    public void Les_reglages_de_memoire_et_de_coup_d_oeil_sont_lus_integralement()
    {
        const string json = """
            {
              "survivor": {
                "rear_glance_threshold": 0.3,
                "rear_glance_interval": 2.5,
                "rear_glance_duration": 0.6
              },
              "knowledge": {
                "danger_spread_radius": 6,
                "danger_decay_per_second": 0.2,
                "danger_noise_weight": 0.35,
                "danger_suspected_threshold": 0.15,
                "danger_confirmed_threshold": 0.75
              }
            }
            """;

        var config = GameConfig.FromJson(json);

        Assert.Equal(0.3f, config.Survivor.RearGlanceThreshold, 3);
        Assert.Equal(2.5f, config.Survivor.RearGlanceInterval, 3);
        Assert.Equal(0.6f, config.Survivor.RearGlanceDuration, 3);

        Assert.Equal(6, config.Knowledge.DangerSpreadRadius);
        Assert.Equal(0.2f, config.Knowledge.DangerDecayPerSecond, 3);
        Assert.Equal(0.35f, config.Knowledge.DangerNoiseWeight, 3);
        Assert.Equal(0.15f, config.Knowledge.DangerSuspectedThreshold, 3);
        Assert.Equal(0.75f, config.Knowledge.DangerConfirmedThreshold, 3);

        // Et la configuration livree porte bien ces sections.
        Assert.True(Config.Knowledge.DangerSpreadRadius > 0);
        Assert.True(Config.Survivor.RearGlanceInterval > 0f);
    }
}
