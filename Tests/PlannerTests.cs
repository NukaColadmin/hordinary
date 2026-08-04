using Hordinary.AI.Planning;
using Hordinary.AI.Utility;
using Hordinary.Agents;
using Hordinary.Config;
using Hordinary.World;

namespace Hordinary.Tests;

/// <summary>
/// Planificateur GBFS (specification, livrable 5) et tests exiges par le document C, section
/// « Planification » :
/// un plan valide est genere lorsque les preconditions sont reunies ; aucun plan impossible n'est
/// retourne ; une porte verrouillee exige une action adaptee ; le plan final satisfait l'objectif ;
/// la limite de profondeur est respectee.
///
/// Comme pour la Utility AI, les tests s'appuient sur les vrais <c>config/planner_domain.json</c> et
/// <c>config/game_config.json</c> : c'est le domaine livre qui est verifie, pas une copie de
/// circonstance.
/// </summary>
public class PlannerTests
{
    private static readonly PlanningDomain DeliveredDomain =
        PlanningDomain.FromJson(DemoMap.ReadConfig("planner_domain.json"));

    private static readonly GameConfig DeliveredConfig =
        GameConfig.FromJson(DemoMap.ReadConfig("game_config.json"));

    private static PlannerSettings Limits => DeliveredConfig.Planner;

    /// <summary>
    /// Une pharmacie fermee, une extraction, un depart. Le caractere de la porte est celui de
    /// <see cref="TestMaps"/> : <c>L</c> verrouillee, <c>D</c> fermee, <c>o</c> ouverte.
    /// </summary>
    private static readonly string[] Quarter =
    [
        "###########",
        "#.........#",
        "#..#####..#",
        "#..#PPP#..#",
        "#..#PPP#..#",
        "#..##?##..#",
        "#........E#",
        "###########",
    ];

    private static readonly GridPosition PharmacyCell = new(5, 4);
    private static readonly GridPosition DoorCell = new(5, 5);
    private static readonly GridPosition ExtractionCell = new(9, 6);
    private static readonly GridPosition StartCell = new(2, 6);

    // --- Mise en situation ----------------------------------------------------------------------

    /// <summary>
    /// Un survivant qui a deja tout observe du quartier. <paramref name="door"/> decide de l'etat de
    /// la porte de la pharmacie ; <paramref name="hiddenCells"/> permet de laisser des cellules
    /// jamais vues, pour tester l'exploration.
    /// </summary>
    private static Survivor Alice(
        char door = 'L',
        GridPosition? at = null,
        Func<GridPosition, bool>? hiddenCells = null,
        bool knowsExtraction = true)
    {
        var rows = Quarter.Select(row => row.Replace('?', door)).ToArray();
        var map = TestMaps.Parse(rows);

        var knowledge = new AgentKnowledge("alice", map.Width, map.Height, map.Origin, DeliveredConfig.Knowledge);

        foreach (var cell in map.AllPositions())
        {
            if (hiddenCells?.Invoke(cell) == true) continue;
            knowledge.ObserveCell(cell, map.GetCell(cell), map.IsIndoor(cell), 0d);
        }

        // Le briefing du joueur : le survivant sait ou sont les lieux, pas ce qu'ils contiennent.
        knowledge.RememberLocation("Pharmacie", ZoneKind.Pharmacy, PharmacyCell, 0d, KnowledgeSource.Inference);
        knowledge.RememberLocation("Depart", ZoneKind.Start, StartCell, 0d, KnowledgeSource.Inference);

        if (knowsExtraction)
            knowledge.RememberLocation("Extraction", ZoneKind.Extraction, ExtractionCell, 0d, KnowledgeSource.Inference);

        return new Survivor("alice", "Alice", knowledge, settings: DeliveredConfig.Survivor)
        {
            Position = at ?? StartCell,
        };
    }

    private static UtilityContext Context(Survivor survivor, double now = 0d)
        => new(survivor, now, DeliveredConfig.UtilityAi);

    private static Planner NewPlanner(PlannerSettings? settings = null)
        => new(DeliveredDomain, settings ?? Limits);

    private static IReadOnlyList<string> Steps(Plan plan) => [.. plan.Actions.Select(action => action.Schema.Name)];

    // --- Critere de validation du livrable 5 -----------------------------------------------------

    [Fact]
    public void L_objectif_recuperer_les_medicaments_produit_une_suite_d_actions_valide()
    {
        var alice = Alice();
        var result = NewPlanner().PlanFor("PickUpMedicine", Context(alice));

        Assert.True(result.Found, result.Reason);
        var plan = result.Plan!;

        // La suite attendue est celle de l'exemple de la specification : se rendre a la pharmacie,
        // ouvrir une entree, fouiller, localiser les medicaments, les prendre.
        Assert.Equal(
            ["MoveToLocation", "ForceDoor", "SearchRoom", "LocateMedicine", "PickUpMedicine"],
            Steps(plan));

        Assert.False(plan.IsFallback);
        Assert.True(plan.IsValid(), "Le plan doit etre executable pas a pas depuis l'etat de depart.");
    }

    // --- Tests imposes par le document C ----------------------------------------------------------

    [Fact]
    public void Un_plan_valide_est_genere_lorsque_les_preconditions_sont_reunies()
    {
        // Meme quartier, mais la porte est ouverte et les medicaments deja reperes : il ne reste plus
        // qu'a aller les prendre.
        var alice = Alice(door: 'o');
        alice.Knowledge.RememberItem("medicine-1", ItemKind.Medicine, PharmacyCell, 0d);

        var plan = NewPlanner().PlanFor("PickUpMedicine", Context(alice)).Plan;

        Assert.NotNull(plan);
        Assert.True(plan.IsValid());
        Assert.DoesNotContain("ForceDoor", Steps(plan));
        Assert.DoesNotContain("SearchRoom", Steps(plan));
        Assert.Contains(plan.Actions, action => action.Schema.Name == "MoveToLocation");
    }

    [Fact]
    public void Aucun_plan_impossible_n_est_retourne()
    {
        // Personne n'a dit a Alice ou se trouve l'extraction. Elle a beau avoir observe la cellule,
        // elle ne sait pas que c'est la sortie : « rejoindre l'extraction » n'a donc pas de solution,
        // et le planificateur ne doit pas en inventer une.
        var alice = Alice(knowsExtraction: false);
        var problem = Problem(alice, "ReachExtraction");

        var result = NewPlanner().Solve(problem);

        Assert.False(result.Found);
        Assert.Null(result.Plan);
        Assert.Equal(PlanningFailure.NoPlanFound, result.Search.Failure);

        // Et avec l'information, le meme objectif redevient realisable : c'est bien la connaissance
        // qui manquait, pas le domaine.
        Assert.True(NewPlanner().Solve(Problem(Alice(), "ReachExtraction")).Found);
    }

    [Fact]
    public void Une_porte_verrouillee_exige_une_action_adaptee()
    {
        var locked = NewPlanner().PlanFor("PickUpMedicine", Context(Alice(door: 'L'))).Plan;
        var closed = NewPlanner().PlanFor("PickUpMedicine", Context(Alice(door: 'D'))).Plan;

        Assert.NotNull(locked);
        Assert.NotNull(closed);

        // Verrouillee : il faut la forcer. Simplement fermee : la fouille suffit, la navigation
        // franchit une porte fermee au prix d'une ouverture (livrable 1).
        Assert.Contains("ForceDoor", Steps(locked));
        Assert.DoesNotContain("ForceDoor", Steps(closed));

        // Et l'action adaptee est bien plus bruyante que l'ordinaire : forcer s'entend.
        Assert.True(locked.TotalNoise > closed.TotalNoise);
    }

    [Fact]
    public void Le_plan_final_satisfait_l_objectif()
    {
        var alice = Alice();
        var result = NewPlanner().PlanFor("PickUpMedicine", Context(alice));
        var plan = result.Plan!;

        var final = plan.ExpectedFinalState;

        Assert.True(plan.Problem.IsSatisfiedBy(final));
        Assert.True(final.Holds("has(agent, medicine)"));
        Assert.True(final.Holds("medicine_collected"));

        // Et l'etat de depart, lui, ne le satisfaisait pas : le plan a bien servi a quelque chose.
        Assert.False(plan.Problem.IsSatisfiedBy(plan.Problem.Initial));
    }

    [Fact]
    public void La_limite_de_profondeur_est_respectee()
    {
        var alice = Alice();
        var problem = Problem(alice, "PickUpMedicine");

        // Le plan complet demande cinq actions : deux de profondeur ne peuvent pas suffire.
        var tooShallow = new Gbfs(problem, Limited(maxDepth: 2)).Search();

        Assert.False(tooShallow.Found);
        Assert.Equal(PlanningFailure.DepthLimitReached, tooShallow.Failure);

        // Avec la limite livree, le plan tient largement sous le plafond.
        var found = new Gbfs(problem, Limits).Search();

        Assert.True(found.Found);
        Assert.True(
            found.Actions.Count <= Limits.MaxDepth,
            $"Un plan de {found.Actions.Count} actions depasse la profondeur maximale de {Limits.MaxDepth}.");
    }

    // --- Limites de recherche ---------------------------------------------------------------------

    [Fact]
    public void Les_limites_de_recherche_sont_configurables_et_appliquees()
    {
        var problem = Problem(Alice(), "PickUpMedicine");

        var starved = new Gbfs(problem, Limited(maxNodes: 1)).Search();
        Assert.False(starved.Found);
        Assert.Equal(PlanningFailure.NodeLimitReached, starved.Failure);

        var rushed = new Gbfs(problem, Limited(maxTimeMs: 0)).Search();
        Assert.False(rushed.Found);
        Assert.Equal(PlanningFailure.TimeLimitReached, rushed.Failure);
    }

    [Fact]
    public void Une_planification_tient_dans_le_budget_de_temps_annonce()
    {
        var problem = Problem(Alice(), "PickUpMedicine");
        var search = new Gbfs(problem, Limits).Search();

        Assert.True(search.Found);
        Assert.True(
            search.ExploredNodes <= Limits.MaxNodes,
            $"{search.ExploredNodes} noeuds explores pour un plafond de {Limits.MaxNodes}.");
    }

    [Fact]
    public void Deux_planifications_sur_la_meme_memoire_rendent_le_meme_plan()
    {
        var first = NewPlanner().PlanFor("PickUpMedicine", Context(Alice())).Plan;
        var second = NewPlanner().PlanFor("PickUpMedicine", Context(Alice())).Plan;

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(
            first.Actions.Select(action => action.Name),
            second.Actions.Select(action => action.Name));
        Assert.Equal(first.CreatedFromStateHash, second.CreatedFromStateHash);
    }

    // --- Echec de planification et repli sur l'exploration ------------------------------------------

    [Fact]
    public void Sans_plan_l_agent_se_rabat_sur_un_objectif_d_exploration()
    {
        // La moitie droite du quartier n'a jamais ete observee : il reste une frontiere ou aller.
        var alice = Alice(hiddenCells: cell => cell.X >= 7);

        // Fuir n'a aucun sens sans menace memorisee : la carte mentale ne designe aucun refuge.
        var result = NewPlanner().PlanFor("FleeDanger", Context(alice));

        Assert.True(result.Found, result.Reason);
        Assert.True(result.UsedFallback);
        Assert.Equal("ExploreUnknownArea", result.Plan!.Goal);
        Assert.Contains("repli sur ExploreUnknownArea", result.Reason);
    }

    [Fact]
    public void Un_objectif_absent_du_domaine_est_signale_et_ne_produit_pas_de_plan_muet()
    {
        var alice = Alice(hiddenCells: cell => cell.X >= 7);
        var result = NewPlanner().PlanFor("DanserLaJava", Context(alice));

        // Le repli evite la paralysie, mais la raison dit clairement ce qui manque.
        Assert.True(result.UsedFallback);
        Assert.Contains("DanserLaJava", result.Reason);
    }

    [Fact]
    public void Un_objectif_deja_atteint_ne_produit_aucune_action()
    {
        var alice = Alice();
        alice.AddToInventory(ItemKind.Medicine);

        var result = NewPlanner().PlanFor("PickUpMedicine", Context(alice));

        Assert.True(result.Found);
        Assert.Empty(result.Plan!.Actions);
        Assert.Equal(PlanStatus.Completed, result.Plan.Status);
    }

    // --- Etat symbolique --------------------------------------------------------------------------

    [Fact]
    public void L_etat_symbolique_ne_decrit_que_ce_que_l_agent_a_percu()
    {
        // Alice n'a jamais regarde la porte de la pharmacie.
        var alice = Alice(hiddenCells: cell => cell == DoorCell);
        var state = SymbolicStateBuilder.Observe(Context(alice), Limits).State;

        Assert.DoesNotContain(state.Facts, fact => fact.StartsWith("door_locked", StringComparison.Ordinal));

        // Faute de porte connue, rien ne s'oppose a la pharmacie : l'ignorance ne bloque pas, elle
        // rend seulement optimiste. Le livrable 6 s'occupe de la desillusion.
        Assert.True(state.Holds("accessible(pharmacy)"));
        Assert.True(state.Holds("known(pharmacy)"));
        Assert.True(state.Holds("at(agent, start)"));
        Assert.False(state.Holds("medicine_found"));
    }

    [Fact]
    public void Une_porte_verrouillee_connue_rend_le_lieu_inaccessible()
    {
        var state = SymbolicStateBuilder.Observe(Context(Alice(door: 'L')), Limits).State;

        Assert.True(state.Holds($"door_locked(door_{DoorCell.X}_{DoorCell.Y})"));
        Assert.True(state.Holds($"door_of(door_{DoorCell.X}_{DoorCell.Y}, pharmacy)"));
        Assert.False(state.Holds("accessible(pharmacy)"));

        // La porte est rattachee au lieu qu'elle garde, pas a tout le quartier.
        Assert.True(state.Holds("accessible(start)"));
    }

    [Fact]
    public void Un_etat_symbolique_est_immuable_et_son_empreinte_ne_depend_pas_de_l_ordre()
    {
        var first = new SymbolicState(["at(agent, pharmacy)", "medicine_found"]);
        var second = new SymbolicState(["medicine_found", "at(agent,pharmacy)"]);

        Assert.Equal(first.Hash, second.Hash);

        var moved = first.Apply([Effect.Parse("!at(agent, *)"), Effect.Parse("at(agent, extraction)")]);

        Assert.True(first.Holds("at(agent, pharmacy)"));
        Assert.False(moved.Holds("at(agent, pharmacy)"));
        Assert.True(moved.Holds("at(agent, extraction)"));
        Assert.NotEqual(first.Hash, moved.Hash);
    }

    // --- Domaine data-driven -----------------------------------------------------------------------

    [Fact]
    public void Le_domaine_livre_couvre_les_actions_de_la_specification()
    {
        string[] expected =
        [
            "MoveToLocation", "OpenDoor", "ForceDoor", "BreakWindow", "SearchRoom", "PickUpMedicine",
            "DropItem", "EscapeLocation", "Hide", "HelpAlly", "HealAlly", "CreateNoiseDistraction",
            "Wait", "ReachExtraction", "ShareKnowledge", "Explore", "Observe",
        ];

        foreach (var name in expected)
        {
            Assert.True(DeliveredDomain.FindAction(name) is not null, $"Action « {name} » absente du domaine livre.");
        }

        // Chaque objectif de la Utility AI sait dire a quoi on reconnait qu'il est atteint.
        var goals = UtilityGoalSet.FromJson(DemoMap.ReadConfig("utility_goals.json"));
        foreach (var goal in goals.Active)
        {
            Assert.True(
                DeliveredDomain.FindGoal(goal.Name) is not null,
                $"L'objectif « {goal.Name} » n'est traduit en conditions symboliques dans aucun domaine.");
        }
    }

    [Fact]
    public void Une_action_est_une_donnee_pas_une_classe()
    {
        // Ajouter une action au jeu ne demande qu'une entree de configuration.
        const string json = """
            {
              "actions": [
                {
                  "name": "BarricadeDoor",
                  "verb": "setFlag",
                  "parameters": { "door": "door", "location": "location" },
                  "params": { "condition": "door_barricaded(?door)", "value": true, "target": "?door" },
                  "preconditions": [ "at(agent, ?location)", "door_of(?door, ?location)" ],
                  "effects": [ "door_barricaded(?door)", "safe(?location)" ],
                  "cost": 2, "noise_level": 4, "duration": 5
                }
              ],
              "goals": [ { "name": "Barricade", "conditions": [ "safe(pharmacy)" ] } ]
            }
            """;

        var domain = PlanningDomain.FromJson(json);
        var alice = Alice(at: PharmacyCell);

        var problem = SymbolicStateBuilder.Build(Context(alice), domain, domain.FindGoal("Barricade")!, Limits);
        var result = new Planner(domain, Limits).Solve(problem);

        Assert.True(result.Found, result.Reason);
        Assert.Equal("BarricadeDoor(door_5_5, pharmacy)", result.Plan!.Actions.Single().Name);
        Assert.Equal(4f, result.Plan.TotalNoise, 3);
    }

    [Fact]
    public void Une_action_desactivee_disparait_du_jeu_sans_etre_supprimee()
    {
        string json = DemoMap.ReadConfig("planner_domain.json")
            .Replace("\"name\": \"ForceDoor\",", "\"name\": \"ForceDoor\", \"enabled\": false,");

        var domain = PlanningDomain.FromJson(json);
        var alice = Alice(door: 'L');

        var problem = SymbolicStateBuilder.Build(Context(alice), domain, domain.FindGoal("PickUpMedicine")!, Limits);
        var result = new Planner(domain, Limits).Solve(problem);

        // Sans moyen de forcer la porte et sans fenetre, la pharmacie reste close.
        Assert.False(result.Found);
        Assert.Equal(PlanningFailure.NoPlanFound, result.Search.Failure);
    }

    [Theory]
    [InlineData("\"verb\": \"teleport\"", "verbe inconnu")]
    [InlineData("\"preconditions\": [ \"at(agent, ?inconnu)\" ]", "sans la declarer")]
    [InlineData("\"parameters\": { \"door\": \"vaisseau\" }", "type inconnu")]
    public void Une_erreur_de_configuration_se_voit_au_chargement(string fragment, string expected)
    {
        string json = $$"""
            {
              "actions": [
                {
                  "name": "Douteuse",
                  "verb": "setFlag",
                  "parameters": { "door": "door" },
                  "params": { "condition": "door_open(?door)", "value": true },
                  "preconditions": [ "door_closed(?door)" ],
                  "effects": [ "door_open(?door)" ],
                  {{fragment}}
                }
              ]
            }
            """;

        var error = Assert.Throws<InvalidOperationException>(() => PlanningDomain.FromJson(json));
        Assert.Contains(expected, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Les_limites_gbfs_viennent_du_fichier_de_configuration()
    {
        Assert.Equal(1000, Limits.MaxNodes);
        Assert.Equal(20, Limits.MaxDepth);
        Assert.Equal(50, Limits.MaxTimeMs);
        Assert.Equal("ExploreUnknownArea", Limits.ExplorationFallbackGoal);
    }

    // --- Heuristique ---------------------------------------------------------------------------------

    [Fact]
    public void L_heuristique_simple_compte_les_conditions_non_satisfaites()
    {
        var problem = Problem(Alice(), "PickUpMedicine");

        // Poids de la specification dans sa version la plus simple.
        var simple = new Heuristic(problem, new PlannerSettings
        {
            DistanceWeight = 0f,
            DangerWeight = 0f,
            NoiseWeight = 0f,
        });

        Assert.Equal(1f, simple.Estimate(problem.Initial, 0f), 3);
        Assert.Equal(0f, simple.Estimate(problem.Initial.With("has(agent, medicine)"), 0f), 3);
    }

    [Fact]
    public void L_heuristique_enrichie_penalise_le_bruit_et_le_danger()
    {
        var alice = Alice();
        var problem = Problem(alice, "PickUpMedicine");
        var heuristic = new Heuristic(problem, Limits);

        float quiet = heuristic.Estimate(problem.Initial, 0f);
        float loud = heuristic.Estimate(problem.Initial, 10f);

        Assert.True(loud > quiet, "Un debut de plan bruyant doit paraitre moins prometteur.");

        // Et un etat qui satisfait le but ne coute rien, quel qu'ait ete le chemin.
        Assert.Equal(0f, heuristic.Estimate(problem.Initial.With("has(agent, medicine)"), 10f), 3);
    }

    [Fact]
    public void L_heuristique_sait_expliquer_son_estimation()
    {
        // Alice a repere les medicaments a l'autre bout du quartier : la distance compte, le danger non.
        var alice = Alice();
        alice.Knowledge.RememberItem("medicine-1", ItemKind.Medicine, PharmacyCell, 0d);

        var problem = Problem(alice, "PickUpMedicine");
        var detail = new Heuristic(problem, Limits).Explain(problem.Initial, accumulatedNoise: 5f);

        Assert.Equal(1, detail.Unsatisfied);
        Assert.True(detail.Distance > 0f, "Le but cite un lieu situable : la distance doit peser.");
        Assert.Equal(0f, detail.Danger, 3);
        Assert.Equal(0.5f, detail.Noise, 3);

        // Le total est la somme ponderee : c'est ce que le panneau debug affiche.
        float expected = Limits.UnsatisfiedWeight * detail.Unsatisfied
            + Limits.DistanceWeight * detail.Distance
            + Limits.DangerWeight * detail.Danger
            + Limits.NoiseWeight * detail.Noise;

        Assert.Equal(expected, detail.Total, 3);
        Assert.Contains("h =", detail.ToString());
    }

    [Fact]
    public void Un_zombie_memorise_rend_le_lieu_moins_attirant()
    {
        var alice = Alice();
        alice.Knowledge.RememberEntity("zombie-1", EntityKind.Zombie, PharmacyCell, 0d);

        var state = SymbolicStateBuilder.Observe(Context(alice), Limits);

        Assert.True(state.State.Holds("zombie_near(pharmacy)"));
        Assert.True(state.Danger["pharmacy"] > state.Danger["extraction"]);
    }

    // --- Execution du plan -----------------------------------------------------------------------

    [Fact]
    public void Les_actions_du_plan_s_executent_dans_l_ordre()
    {
        var alice = Alice();
        var plan = NewPlanner().PlanFor("PickUpMedicine", Context(alice)).Plan!;

        var runtime = new RecordingRuntime(alice.Position);
        var executor = new PlanExecutor(runtime, Limits);

        var completed = new List<string>();
        executor.ActionCompleted += action => completed.Add(action.Schema.Name);

        executor.Start(plan);
        RunToCompletion(executor);

        Assert.Equal(PlanStatus.Completed, plan.Status);
        Assert.Equal(Steps(plan), completed);

        // Le plan s'est traduit en gestes concrets, pas seulement en faits.
        Assert.Contains(PharmacyCell, runtime.Visited);
        Assert.Contains("door_open(door_5_5)", runtime.Flags);
        Assert.Contains("medicine", runtime.Taken);

        // Et il a fait le bruit annonce : forcer une porte s'entend a six.
        Assert.Contains(runtime.Noises, noise => Math.Abs(noise - 6f) < 0.001f);
    }

    [Fact]
    public void Une_action_qui_echoue_arrete_le_plan_et_dit_pourquoi()
    {
        var alice = Alice();
        var plan = NewPlanner().PlanFor("PickUpMedicine", Context(alice)).Plan!;

        // La porte resiste : le moteur refuse l'effet.
        var runtime = new RecordingRuntime(alice.Position) { RefuseFlag = "door_open(door_5_5)" };
        var executor = new PlanExecutor(runtime, Limits);

        string? failure = null;
        executor.ActionFailed += (action, reason) => failure = $"{action.Schema.Name} : {reason}";

        executor.Start(plan);
        RunToCompletion(executor);

        Assert.Equal(PlanStatus.Failed, plan.Status);
        Assert.NotNull(failure);
        Assert.StartsWith("ForceDoor", failure);
        Assert.Contains("ForceDoor", plan.FailureReason!);

        // Rien de ce qui suivait n'a ete tente.
        Assert.Empty(runtime.Taken);
    }

    [Fact]
    public void Un_itineraire_impossible_fait_echouer_l_action_de_deplacement()
    {
        var alice = Alice();
        var plan = NewPlanner().PlanFor("PickUpMedicine", Context(alice)).Plan!;

        var runtime = new RecordingRuntime(alice.Position) { RefuseMove = true };
        var executor = new PlanExecutor(runtime, Limits);

        executor.Start(plan);
        RunToCompletion(executor);

        Assert.Equal(PlanStatus.Failed, plan.Status);
        Assert.Contains("MoveToLocation", plan.FailureReason!);
    }

    // --- Outils ---------------------------------------------------------------------------------

    private static PlanningProblem Problem(Survivor survivor, string goal)
        => SymbolicStateBuilder.Build(Context(survivor), DeliveredDomain, DeliveredDomain.FindGoal(goal)!, Limits);

    private static PlannerSettings Limited(int? maxNodes = null, int? maxDepth = null, int? maxTimeMs = null)
        => new()
        {
            MaxNodes = maxNodes ?? Limits.MaxNodes,
            MaxDepth = maxDepth ?? Limits.MaxDepth,
            MaxTimeMs = maxTimeMs ?? Limits.MaxTimeMs,
            UnsatisfiedWeight = Limits.UnsatisfiedWeight,
            DistanceWeight = Limits.DistanceWeight,
            DangerWeight = Limits.DangerWeight,
            NoiseWeight = Limits.NoiseWeight,
        };

    /// <summary>Deroule l'executeur jusqu'a ce que le plan s'arrete, avec un garde-fou anti-boucle.</summary>
    private static void RunToCompletion(PlanExecutor executor, float step = 0.5f, int maxTicks = 400)
    {
        for (int tick = 0; tick < maxTicks && executor.IsBusy; tick++) executor.Advance(step);

        Assert.False(executor.IsBusy, "L'execution du plan ne s'est jamais arretee.");
    }

    /// <summary>
    /// Moteur d'execution de test : il note ce qu'on lui demande au lieu de le faire. Il tient lieu de
    /// la couche Godot, ce qui prouve au passage que l'executeur n'en depend pas.
    /// </summary>
    private sealed class RecordingRuntime(GridPosition start) : IPlanRuntime
    {
        public GridPosition AgentCell { get; private set; } = start;

        public List<GridPosition> Visited { get; } = [];
        public List<string> Flags { get; } = [];
        public List<string> Taken { get; } = [];
        public List<float> Noises { get; } = [];

        /// <summary>Fait echouer l'effet demande, comme une porte qui resiste.</summary>
        public string? RefuseFlag { get; init; }

        public bool RefuseMove { get; init; }

        public bool BeginMove(GridPosition destination)
        {
            if (RefuseMove) return false;

            // Le test ne simule pas le trajet : le pas suivant suffit a arriver.
            AgentCell = destination;
            Visited.Add(destination);
            return true;
        }

        public MoveState PollMove(GridPosition destination)
            => AgentCell == destination ? MoveState.Arrived : MoveState.Moving;

        public bool Take(string item)
        {
            Taken.Add(item);
            return true;
        }

        public bool Put(string item, GridPosition where) => true;

        public bool SetFlag(string fact, bool value, GridPosition? where)
        {
            if (fact == RefuseFlag) return false;

            Flags.Add(fact);
            return true;
        }

        public bool Damage(string target, float amount) => true;

        /// <summary>Nombre d'annulations de trajet : le plan abandonne doit arreter le corps.</summary>
        public int Cancellations { get; private set; }

        public void CancelMove() => Cancellations++;

        /// <summary>Libelles sonores demandes par le plan, dans l'ordre.</summary>
        public List<string?> NoiseLabels { get; } = [];

        public void EmitNoise(float level, GridPosition where, string? label = null)
        {
            Noises.Add(level);
            NoiseLabels.Add(label);
        }
    }
}
