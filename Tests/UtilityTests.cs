using System.Numerics;
using System.Reflection;
using Hordinary.AI.Navigation;
using Hordinary.AI.Utility;
using Hordinary.Agents;
using Hordinary.Config;
using Hordinary.World;

namespace Hordinary.Tests;

/// <summary>
/// Utility AI (specification section 11) et critere de validation du livrable 4 : « un survivant
/// peut abandonner une mission pour fuir un danger immediat ».
///
/// Les tests s'appuient sur les vrais fichiers <c>config/utility_goals.json</c> et
/// <c>config/game_config.json</c> : c'est la configuration livree qui est verifiee, pas une copie.
/// </summary>
public class UtilityTests
{
    private static readonly UtilityGoalSet DeliveredGoals =
        UtilityGoalSet.FromJson(DemoMap.ReadConfig("utility_goals.json"));

    private static readonly GameConfig DeliveredConfig =
        GameConfig.FromJson(DemoMap.ReadConfig("game_config.json"));

    private static UtilityAiSettings Tuning => DeliveredConfig.UtilityAi;

    /// <summary>Terrain degage entierement observe : seules les entites font varier les scores.</summary>
    private static Survivor Alice(GridPosition at)
    {
        const int size = 21;
        var map = TestMaps.Parse(Enumerable.Repeat(new string('.', size), size).ToArray());
        var knowledge = new AgentKnowledge("alice", map.Width, map.Height, map.Origin);

        foreach (var cell in map.AllPositions())
        {
            knowledge.ObserveCell(cell, map.GetCell(cell), map.IsIndoor(cell), 0d);
        }

        return new Survivor("alice", "Alice", knowledge, settings: DeliveredConfig.Survivor, catalogue: DemoMap.Items)
        {
            Position = at,
        };
    }

    private static UtilityContext Context(Survivor survivor, double now = 0d)
        => new(survivor, now, Tuning);

    private static float ScoreOf(IReadOnlyList<GoalScore> scores, string goal)
        => scores.First(score => score.Goal == goal).Score;

    // --- Critere de validation du livrable 4 ------------------------------------------------

    [Fact]
    public void Un_survivant_abandonne_sa_mission_pour_fuir_un_danger_immediat()
    {
        var alice = Alice(new GridPosition(10, 10));
        alice.Mission = new MissionOrder("aller a la pharmacie", new GridPosition(20, 10), Tuning.PlayerOrderPriority);

        var selector = new UtilitySelector(DeliveredGoals, Tuning);

        // Au calme, la consigne du joueur l'emporte.
        var calm = selector.Evaluate(Context(alice), 0d);
        Assert.Equal("CompletePlayerMission", calm.Goal);

        // Un zombie surgit a deux cases : evenement important, on ne l'attend pas le cooldown.
        alice.Knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(12, 10), 0.2d);
        selector.Request("zombie decouvert");
        Assert.True(selector.ShouldEvaluate(0.2d), "Un evenement important doit court-circuiter le cooldown.");

        var alarmed = selector.Evaluate(Context(alice, 0.2d), 0.2d);

        Assert.True(alarmed.Changed);
        Assert.Equal("FleeDanger", alarmed.Goal);
        Assert.Contains("zombie decouvert", alarmed.Reason);

        // Et fuir veut dire aller quelque part : la cellule visee s'eloigne reellement du zombie.
        var context = Context(alice, 0.2d);
        var refuge = context.Locate(selector.Current!.Target);

        Assert.NotNull(refuge);
        Assert.True(
            context.DistanceToNearestThreat(refuge.Value) > context.DistanceToNearestThreat(alice.Position),
            $"Le refuge {refuge} n'eloigne pas du zombie.");
    }

    // --- Tests exiges par C-tests.md --------------------------------------------------------

    [Fact]
    public void La_fuite_obtient_un_score_eleve_lorsque_le_danger_est_fort()
    {
        var alice = Alice(new GridPosition(10, 10));
        alice.Mission = new MissionOrder("aller a la pharmacie", new GridPosition(20, 10), Tuning.PlayerOrderPriority);
        alice.Knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(12, 10), 0d);

        var context = Context(alice);
        var scores = new UtilityScorer(DeliveredGoals).ScoreAll(context);

        Assert.True(context.PerceivedDanger > 0.5f, $"Danger percu trop faible : {context.PerceivedDanger:0.00}.");
        Assert.Equal("FleeDanger", scores[0].Goal);
        Assert.True(scores[0].Score > 0.3f, $"Score de fuite trop faible : {scores[0].Score:0.00}.");
        Assert.True(ScoreOf(scores, "FleeDanger") > ScoreOf(scores, "CompletePlayerMission"));
    }

    [Fact]
    public void La_mission_reste_prioritaire_lorsque_le_danger_est_faible()
    {
        var alice = Alice(new GridPosition(10, 10));
        alice.Mission = new MissionOrder("aller a la pharmacie", new GridPosition(20, 10), Tuning.PlayerOrderPriority);

        // Le zombie est memorise, mais bien au-dela du rayon de danger.
        alice.Knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(20, 20), 0d);

        var context = Context(alice);
        var scores = new UtilityScorer(DeliveredGoals).ScoreAll(context);

        Assert.Equal(0f, context.PerceivedDanger, 3);
        Assert.Equal("CompletePlayerMission", scores[0].Goal);
        Assert.True(ScoreOf(scores, "CompletePlayerMission") > ScoreOf(scores, "FleeDanger"));
    }

    [Fact]
    public void L_hysteresis_evite_les_changements_constants_d_objectif()
    {
        // Deux objectifs epures, pilotes par deux entrees que le test controle directement.
        var goals = UtilityGoalSet.FromJson("""
            {
              "goals": [
                { "name": "Consigne", "considerations": [ { "input": "player_priority", "curve": "linear" } ] },
                { "name": "Fuite",    "considerations": [ { "input": "panic", "curve": "linear" } ] }
              ]
            }
            """);

        var alice = Alice(new GridPosition(10, 10));
        alice.Mission = new MissionOrder("tenir la position", null, 0.5f);

        var selector = new UtilitySelector(goals, Tuning);
        int changes = 0;
        selector.GoalChanged += _ => changes++;

        Assert.Equal("Consigne", selector.Evaluate(Context(alice), 0d).Goal);
        Assert.Equal(1, changes);

        // Vingt reevaluations ou la fuite passe devant, mais jamais de plus de 0,15.
        for (int i = 0; i < 20; i++)
        {
            SetPanic(alice, i % 2 == 0 ? 0.60f : 0.55f);
            var decision = selector.Evaluate(Context(alice), 1d + i);

            Assert.False(decision.Changed);
            Assert.Equal("Consigne", decision.Goal);
        }

        Assert.Equal(1, changes);
        Assert.Equal(21, selector.EvaluationCount);

        // Au-dela de l'hysteresis, la bascule se fait sans hesiter.
        SetPanic(alice, 0.8f);
        var switched = selector.Evaluate(Context(alice), 30d);

        Assert.True(switched.Changed);
        Assert.Equal("Fuite", switched.Goal);
        Assert.Equal(2, changes);
    }

    // --- Cooldown de decision (section 11.4) ------------------------------------------------

    [Fact]
    public void Un_objectif_n_est_pas_reevalue_a_chaque_frame()
    {
        var alice = Alice(new GridPosition(10, 10));
        var selector = new UtilitySelector(DeliveredGoals, Tuning);

        Assert.True(selector.ShouldEvaluate(0d), "La toute premiere decision ne doit pas attendre.");
        selector.Evaluate(Context(alice), 0d);

        // Une frame plus tard : le cooldown n'est pas ecoule.
        Assert.False(selector.ShouldEvaluate(0.016d));
        Assert.False(selector.ShouldEvaluate(Tuning.EvaluationInterval - 0.01d));
        Assert.True(selector.ShouldEvaluate(Tuning.EvaluationInterval));

        // Un evenement important rend la main immediatement.
        selector.Evaluate(Context(alice, 1d), 1d);
        Assert.False(selector.ShouldEvaluate(1.1d));
        selector.Request("survivant blesse");
        Assert.True(selector.ShouldEvaluate(1.1d));
    }

    [Fact]
    public void Un_objectif_devenu_inapplicable_est_lache_sans_attendre_l_hysteresis()
    {
        var goals = UtilityGoalSet.FromJson("""
            {
              "goals": [
                {
                  "name": "PorterLesMedicaments",
                  "preconditions": [ { "input": "has_objective", "min": 0.5 } ],
                  "considerations": [ { "input": "constant", "curve": "linear" } ]
                },
                { "name": "Explorer", "considerations": [ { "input": "player_priority", "curve": "linear" } ] }
              ]
            }
            """);

        var alice = Alice(new GridPosition(10, 10));
        alice.Mission = new MissionOrder("explorer", null, 0.2f);
        alice.AddToInventory(Survivor.DefaultObjectiveItem);

        var selector = new UtilitySelector(goals, Tuning);
        Assert.Equal("PorterLesMedicaments", selector.Evaluate(Context(alice), 0d).Goal);

        // Les medicaments changent de mains : la precondition tombe, l'objectif avec.
        alice.RemoveFromInventory(Survivor.DefaultObjectiveItem);
        var decision = selector.Evaluate(Context(alice), 1d);

        Assert.True(decision.Changed);
        Assert.Equal("Explorer", decision.Goal);
        Assert.Contains("n'est plus applicable", decision.Reason);

        // 0,2 reste au-dessus du seuil minimum ; sans objectif viable, il n'en reste aucun.
        alice.Mission = null;
        var empty = selector.Evaluate(Context(alice), 2d);

        Assert.Null(empty.Goal);
        Assert.True(empty.Changed);
    }

    // --- Considerations et courbes ----------------------------------------------------------

    [Theory]
    [InlineData(CurveKind.Linear, 0.5f, 0.5f)]
    [InlineData(CurveKind.Quadratic, 0.5f, 0.25f)]
    [InlineData(CurveKind.Inverse, 0.25f, 0.75f)]
    [InlineData(CurveKind.Threshold, 0.6f, 1f)]
    [InlineData(CurveKind.Threshold, 0.4f, 0f)]
    public void Les_courbes_de_reponse_restent_bornees_entre_0_et_1(CurveKind curve, float input, float expected)
    {
        Assert.Equal(expected, ResponseCurve.Apply(curve, input), 3);
        Assert.Equal(ResponseCurve.Apply(curve, 0f), ResponseCurve.Apply(curve, -5f), 3);
        Assert.Equal(ResponseCurve.Apply(curve, 1f), ResponseCurve.Apply(curve, 5f), 3);
    }

    [Fact]
    public void Une_plage_de_sortie_transforme_un_veto_en_simple_modulateur()
    {
        var veto = new Consideration { Input = "panic", Curve = CurveKind.Linear };
        var modulator = new Consideration { Input = "panic", Curve = CurveKind.Linear, Min = 0.7f };

        Assert.Equal(0f, veto.Evaluate(0f), 3);
        Assert.Equal(0.7f, modulator.Evaluate(0f), 3);
        Assert.Equal(1f, modulator.Evaluate(1f), 3);
    }

    [Fact]
    public void Une_seule_consideration_a_zero_annule_un_objectif_multiplicatif()
    {
        var goals = UtilityGoalSet.FromJson("""
            {
              "goals": [
                {
                  "name": "Sortir",
                  "considerations": [
                    { "input": "constant", "curve": "linear" },
                    { "input": "player_priority", "curve": "linear" }
                  ]
                }
              ]
            }
            """);

        var alice = Alice(new GridPosition(10, 10));
        var scorer = new UtilityScorer(goals);

        // Sans ordre du joueur, player_priority vaut 0 et emporte tout le produit.
        Assert.Equal(0f, scorer.ScoreAll(Context(alice))[0].Score, 3);

        alice.Mission = new MissionOrder("sortir", null, 0.8f);
        Assert.Equal(0.8f, scorer.ScoreAll(Context(alice))[0].Score, 3);
    }

    [Fact]
    public void Le_detail_du_score_est_lisible_pour_le_mode_debug()
    {
        var alice = Alice(new GridPosition(10, 10));
        alice.Knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(12, 10), 0d);

        var flee = new UtilityScorer(DeliveredGoals).ScoreAll(Context(alice))
            .First(score => score.Goal == "FleeDanger");

        Assert.Equal(3, flee.Breakdown.Count);
        Assert.Contains(flee.Breakdown, c => c.Input == "threat_pressure");
        Assert.Contains("threat_pressure", flee.Describe());
        Assert.Contains("FleeDanger", flee.Describe());
    }

    // --- Perception du danger ---------------------------------------------------------------

    [Fact]
    public void Le_danger_percu_s_accumule_avec_le_nombre_de_zombies()
    {
        var alone = Alice(new GridPosition(10, 10));
        alone.Knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(13, 10), 0d);

        var surrounded = Alice(new GridPosition(10, 10));
        surrounded.Knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(13, 10), 0d);
        surrounded.Knowledge.RememberEntity("zombie-2", EntityKind.Zombie, new GridPosition(7, 10), 0d);
        surrounded.Knowledge.RememberEntity("zombie-3", EntityKind.Zombie, new GridPosition(10, 13), 0d);

        Assert.True(Context(surrounded).PerceivedDanger > Context(alone).PerceivedDanger);
    }

    [Fact]
    public void Un_zombie_oublie_ne_fait_plus_peur()
    {
        var alice = Alice(new GridPosition(10, 10));
        alice.Knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(12, 10), 0d);

        float now = Context(alice).PerceivedDanger;

        // La confiance dans la position d'un zombie decroit (livrable 2) : le danger suit.
        float later = Context(alice, 6d).PerceivedDanger;

        Assert.True(later < now, $"Le danger devrait s'estomper ({now:0.00} -> {later:0.00}).");
    }

    [Fact]
    public void Un_bruit_inquiete_moins_qu_un_zombie_sous_les_yeux()
    {
        var seen = Alice(new GridPosition(10, 10));
        seen.Knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(13, 10), 0d);

        var heard = Alice(new GridPosition(10, 10));
        heard.Knowledge.RememberNoise("noise-1", new GridPosition(13, 10), Tuning.LoudNoiseIntensity, 0d);

        Assert.True(Context(heard).PerceivedDanger > 0f);
        Assert.True(Context(heard).PerceivedDanger < Context(seen).PerceivedDanger);
    }

    [Fact]
    public void Un_zombie_perdu_de_vue_ne_fait_pas_retomber_la_fuite_dans_la_seconde()
    {
        var alice = Alice(new GridPosition(10, 10));
        alice.Mission = new MissionOrder("aller a la pharmacie", new GridPosition(20, 10), Tuning.PlayerOrderPriority);
        alice.Knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(12, 10), 0d);

        var selector = new UtilitySelector(DeliveredGoals, Tuning);
        alice.UpdatePanic(Context(alice).PerceivedDanger, 1f);
        Assert.Equal("FleeDanger", selector.Evaluate(Context(alice), 1d).Goal);

        // Elle regarde la case ou elle croyait le voir : plus personne. Le danger percu tombe a
        // zero, mais la panique, elle, met plusieurs secondes a retomber.
        alice.Knowledge.ForgetEntityAt("zombie-1", 1.5d);
        var context = Context(alice, 1.5d);

        Assert.Equal(0f, context.PerceivedDanger, 3);
        Assert.Equal("FleeDanger", selector.Evaluate(context, 1.5d).Goal);

        // Une fois le calme revenu, la mission reprend ses droits.
        alice.UpdatePanic(0f, 10f);
        Assert.Equal("CompletePlayerMission", selector.Evaluate(Context(alice, 12d), 12d).Goal);
    }

    // --- Memoire de poursuite ---------------------------------------------------------------

    [Fact]
    public void Une_menace_pese_la_ou_elle_devrait_etre_et_non_la_ou_elle_a_ete_vue()
    {
        var alice = Alice(new GridPosition(10, 10));

        // Vu a huit cases, il fonce vers elle a deux cellules par seconde.
        alice.Knowledge.RememberEntity(
            "zombie-1", EntityKind.Zombie, new GridPosition(18, 10), 0d, heading: new Vector2(-2f, 0f));

        // Au bout de trois secondes, elle ne le croit plus a huit cases mais a deux — et le danger
        // ressenti suit la supposition, pas le souvenir.
        Assert.Equal(new GridPosition(12, 10), Context(alice, 3d).Threats[0].Position);
        Assert.True(Context(alice, 3d).PerceivedDanger > Context(alice, 0d).PerceivedDanger);
    }

    [Fact]
    public void Etre_poursuivi_se_distingue_d_etre_pres_d_un_zombie()
    {
        var chased = Alice(new GridPosition(10, 10));
        chased.Knowledge.RememberEntity(
            "zombie-1", EntityKind.Zombie, new GridPosition(13, 10), 0d, heading: new Vector2(-1f, 0f));

        var ignored = Alice(new GridPosition(10, 10));
        ignored.Knowledge.RememberEntity(
            "zombie-1", EntityKind.Zombie, new GridPosition(13, 10), 0d, heading: new Vector2(1f, 0f));

        var idle = Alice(new GridPosition(10, 10));
        idle.Knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(13, 10), 0d);

        // Meme distance, meme danger percu : seul le cap change.
        Assert.True(Context(chased).PursuitPressure > 0.5f);
        Assert.Equal(0f, Context(ignored).PursuitPressure, 3);
        Assert.Equal(0f, Context(idle).PursuitPressure, 3);
    }

    [Fact]
    public void Se_cacher_perd_son_interet_quand_on_est_deja_suivi()
    {
        // Un abri connu juste a cote, sans quoi « Hide » vaut zero dans les deux cas.
        static Survivor WithShelter(GridPosition at)
        {
            var survivor = Alice(at);
            survivor.Knowledge.ObserveCell(at + new GridPosition(-1, 0), CellType.Floor, indoor: true, 0d);
            return survivor;
        }

        var hiding = WithShelter(new GridPosition(10, 10));
        hiding.Knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(13, 10), 0d);
        hiding.UpdatePanic(Context(hiding).PerceivedDanger, 1f);

        var chased = WithShelter(new GridPosition(10, 10));
        chased.Knowledge.RememberEntity(
            "zombie-1", EntityKind.Zombie, new GridPosition(13, 10), 0d, heading: new Vector2(-1f, 0f));
        chased.UpdatePanic(Context(chased).PerceivedDanger, 1f);

        var scorer = new UtilityScorer(DeliveredGoals);
        float calmly = ScoreOf(scorer.ScoreAll(Context(hiding)), "Hide");
        float pursued = ScoreOf(scorer.ScoreAll(Context(chased)), "Hide");

        // Se terrer marche contre ce qui ne vous a pas vu, pas contre ce qui vous suit.
        Assert.True(pursued < calmly);
    }

    [Fact]
    public void Une_blessure_fait_monter_la_panique_et_le_besoin_de_fuir()
    {
        var alice = Alice(new GridPosition(10, 10));
        alice.Knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(11, 10), 0d);

        var scorer = new UtilityScorer(DeliveredGoals);
        float intact = ScoreOf(scorer.ScoreAll(Context(alice)), "FleeDanger");

        alice.TakeDamage(60f);

        Assert.Equal(0.4f, alice.HealthRatio, 2);
        Assert.True(alice.Panic > 0f, "Etre blesse doit faire monter la panique (section 19.3).");
        Assert.True(ScoreOf(scorer.ScoreAll(Context(alice)), "FleeDanger") > intact);
    }

    [Fact]
    public void La_panique_retombe_plus_lentement_qu_elle_ne_monte()
    {
        var alice = Alice(new GridPosition(10, 10));

        alice.UpdatePanic(1f, 1f);
        float risen = alice.Panic;

        alice.UpdatePanic(0f, 1f);
        float fallen = risen - alice.Panic;

        Assert.Equal(DeliveredConfig.Survivor.PanicRisePerSecond, risen, 3);
        Assert.Equal(DeliveredConfig.Survivor.PanicDecayPerSecond, fallen, 3);
    }

    // --- Objectifs pilotes par la configuration ---------------------------------------------

    [Fact]
    public void La_configuration_livree_declare_les_douze_objectifs_de_la_specification()
    {
        // Les douze objectifs de la section 11.2, dans l'ordre. Le catalogue etant une donnee, il
        // peut en porter d'autres — le livrable 7 y ajoute CalmAlly, l'action speciale de Claire —
        // mais aucun des douze ne doit disparaitre.
        string[] required =
        [
            "CompletePlayerMission", "ExploreUnknownArea", "FleeDanger", "HelpAlly", "HealSelf",
            "HealAlly", "SearchForObjective", "PickUpObjective", "ReachExtraction", "Hide",
            "Regroup", "CreateDistraction",
        ];

        var declared = DeliveredGoals.Goals.Select(goal => goal.Name).ToArray();

        Assert.Equal(required, declared.Where(required.Contains).ToArray());

        // Toutes les cibles declarees sont resolubles, et tous les scores restent dans [0, 1].
        var alice = Alice(new GridPosition(10, 10));
        alice.Mission = new MissionOrder("aller a l'extraction", new GridPosition(20, 20), 0.7f);
        var context = Context(alice);

        foreach (var goal in DeliveredGoals.Goals)
        {
            context.Locate(goal.Target);
        }

        foreach (var score in new UtilityScorer(DeliveredGoals).ScoreAll(context))
        {
            Assert.InRange(score.Score, 0f, 1f);
        }
    }

    [Fact]
    public void Sans_ordre_du_joueur_le_survivant_prend_une_initiative()
    {
        // Carte a peine entamee : le joueur n'a rien demande, il reste tout a decouvrir.
        var map = TestMaps.Parse(Enumerable.Repeat(new string('.', 20), 20).ToArray());
        var knowledge = new AgentKnowledge("alice", map.Width, map.Height, map.Origin);

        foreach (var cell in map.AllPositions().Where(cell => cell.X < 5 && cell.Y < 5))
        {
            knowledge.ObserveCell(cell, map.GetCell(cell), map.IsIndoor(cell), 0d);
        }

        var alice = new Survivor("alice", "Alice", knowledge, settings: DeliveredConfig.Survivor, catalogue: DemoMap.Items)
        {
            Position = new GridPosition(2, 2),
        };

        var decision = new UtilitySelector(DeliveredGoals, Tuning).Evaluate(Context(alice), 0d);

        Assert.NotNull(decision.Goal);
        Assert.NotEqual("CompletePlayerMission", decision.Goal);
    }

    [Fact]
    public void Retirer_un_objectif_est_une_modification_de_configuration()
    {
        var goals = UtilityGoalSet.FromJson("""
            {
              "goals": [
                { "name": "Fuir", "enabled": false, "considerations": [ { "input": "constant", "curve": "linear" } ] },
                { "name": "Rester", "considerations": [ { "input": "constant", "curve": "linear" } ] }
              ]
            }
            """);

        var scores = new UtilityScorer(goals).ScoreAll(Context(Alice(new GridPosition(10, 10))));

        Assert.Single(scores);
        Assert.Equal("Rester", scores[0].Goal);
    }

    [Fact]
    public void Une_consideration_inconnue_est_signalee_au_chargement()
    {
        var error = Assert.Throws<InvalidOperationException>(() => UtilityGoalSet.FromJson("""
            { "goals": [ { "name": "Fuir", "considerations": [ { "input": "peur_du_noir" } ] } ] }
            """));

        Assert.Contains("peur_du_noir", error.Message);
        Assert.Contains("perceived_danger", error.Message);
    }

    [Fact]
    public void Une_cible_inconnue_est_signalee_au_chargement()
    {
        var error = Assert.Throws<InvalidOperationException>(() => UtilityGoalSet.FromJson("""
            {
              "goals": [
                { "name": "Fuir", "target": "la_plage", "considerations": [ { "input": "constant" } ] }
              ]
            }
            """));

        Assert.Contains("la_plage", error.Message);
    }

    // --- Le choix d'objectif n'a pas acces au monde reel -------------------------------------

    [Fact]
    public void La_utility_ai_n_a_aucun_acces_au_monde_reel()
    {
        foreach (var type in new[]
                 {
                     typeof(UtilityContext), typeof(UtilityScorer), typeof(UtilitySelector),
                     typeof(UtilityInputs), typeof(UtilityGoal),
                 })
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.Public | BindingFlags.NonPublic;

            var referenced = type.GetFields(all).Select(field => field.FieldType)
                .Concat(type.GetProperties(all).Select(property => property.PropertyType))
                .Concat(type.GetMethods(all)
                    .SelectMany(method => method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType)))
                .Concat(type.GetConstructors().SelectMany(c => c.GetParameters().Select(p => p.ParameterType)))
                .ToList();

            Assert.DoesNotContain(typeof(WorldState), referenced);
            Assert.DoesNotContain(typeof(WorldEntity), referenced);
            Assert.DoesNotContain(typeof(WorldItem), referenced);
            Assert.DoesNotContain(typeof(PerceptionSystem), referenced);
            Assert.DoesNotContain(typeof(NoiseSystem), referenced);
        }
    }

    [Fact]
    public void Un_survivant_ne_fuit_pas_vers_une_zone_qu_il_n_a_jamais_observee()
    {
        // Carte connue seulement sur sa moitie gauche : la fuite ne peut viser que du connu.
        var map = TestMaps.Parse(Enumerable.Repeat(new string('.', 20), 5).ToArray());
        var knowledge = new AgentKnowledge("alice", map.Width, map.Height, map.Origin);

        foreach (var cell in map.AllPositions().Where(cell => cell.X < 10))
        {
            knowledge.ObserveCell(cell, map.GetCell(cell), map.IsIndoor(cell), 0d);
        }

        var alice = new Survivor("alice", "Alice", knowledge, settings: DeliveredConfig.Survivor, catalogue: DemoMap.Items)
        {
            Position = new GridPosition(8, 2),
        };
        knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(9, 2), 0d);

        var refuge = Context(alice).Locate("away_from_danger");

        Assert.NotNull(refuge);
        Assert.True(knowledge.HasObserved(refuge.Value), $"La cellule {refuge} n'a jamais ete observee.");
    }

    // --- La boucle complete, sur la carte livree --------------------------------------------

    [Fact]
    public void Sur_la_carte_livree_Alice_lache_sa_mission_quand_un_zombie_apparait()
    {
        const float step = 0.1f;

        var map = DemoMap.Load();
        var world = new WorldState(map);
        var start = map.FindWalkableIn(map.GetZone(ZoneKind.Start)!)!.Value;
        var pharmacy = map.FindWalkableIn(map.GetZone(ZoneKind.Pharmacy)!)!.Value;

        var knowledge = new AgentKnowledge("alice", map.Width, map.Height, map.Origin);
        var alice = new Survivor(
            "alice", "Alice", knowledge,
            // Vision panoramique : ce sont les decisions qu'on teste, pas l'orientation du regard.
            new PerceptionSettings { VisionRange = 8, FieldOfViewDegrees = 360f },
            DeliveredConfig.Survivor)
        {
            Position = start,
        };

        // Briefing du joueur, puis son ordre : rejoindre la pharmacie.
        knowledge.RememberLocation("Pharmacie", ZoneKind.Pharmacy, pharmacy, 0d, KnowledgeSource.Inference);
        alice.Mission = new MissionOrder("rejoindre la pharmacie", pharmacy, Tuning.PlayerOrderPriority);

        var perception = new PerceptionSystem();
        var navigation = new NavigationGrid(knowledge.MentalMap, DeliveredConfig.Navigation);
        var search = new AStar(navigation);
        var follower = new PathFollower { Speed = DeliveredConfig.Survivor.WalkSpeed };
        follower.Teleport(start);

        var selector = new UtilitySelector(DeliveredGoals, Tuning);
        perception.Observe(alice, world, 0d);

        Assert.Equal("CompletePlayerMission", selector.Evaluate(Context(alice), 0d).Goal);

        // Un zombie se poste sur une case visible depuis le depart. Il ne bouge pas : c'est la
        // decision d'Alice qu'on observe, pas la poursuite.
        var lair = map.AllPositions()
            .Where(map.IsWalkable)
            .Where(cell => cell.ChebyshevDistanceTo(start) is >= 2 and <= 3)
            .First(cell => LineOfSight.IsVisible(map, start, cell));
        world.SetEntity("zombie-1", EntityKind.Zombie, lair);

        bool fled = false;
        float distanceAtSwitch = 0f, farthestAfter = 0f;

        for (int tick = 0; tick < 60; tick++)
        {
            world.Advance(step);
            follower.Advance(step, navigation.IsTraversable);

            alice.Position = follower.CurrentCell;
            alice.Facing = follower.Facing;
            perception.Observe(alice, world);

            var context = Context(alice, world.Time);
            alice.UpdatePanic(context.PerceivedDanger, step);

            if (!selector.ShouldEvaluate(world.Time)) continue;

            var decision = selector.Evaluate(context, world.Time);
            float distance = alice.Position.ChebyshevDistanceTo(lair);

            if (decision is { Changed: true, Goal: "FleeDanger" } && !fled)
            {
                fled = true;
                distanceAtSwitch = distance;
            }

            if (fled) farthestAfter = Math.Max(farthestAfter, distance);

            // Le survivant se remet en route vers la cible de son objectif courant.
            if (!decision.Changed && !follower.IsFinished) continue;
            if (selector.Current?.Target is not { } target) continue;
            if (context.Locate(target) is not { } destination || destination == alice.Position) continue;

            var route = search.FindPath(alice.Position, destination);
            if (route.Found) follower.SetPath(route.Cells);
        }

        Assert.True(fled, "Alice a poursuivi sa mission malgre le zombie.");
        Assert.True(
            farthestAfter > distanceAtSwitch,
            $"Alice n'a pas pris de distance avec le zombie ({distanceAtSwitch} -> {farthestAfter}).");
    }

    private static void SetPanic(Survivor survivor, float value)
    {
        // Un pas de temps volontairement enorme : la panique rejoint exactement la valeur visee.
        survivor.UpdatePanic(value, 1000f);
    }
}
