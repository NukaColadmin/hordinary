using Hordinary.AI.Instructions;
using Hordinary.AI.Planning;
using Hordinary.AI.Planning.Pddl;
using Hordinary.Agents;
using Hordinary.Config;
using Hordinary.Game;
using Hordinary.World;
using Xunit.Abstractions;

namespace Hordinary.Tests;

/// <summary>
/// Le mode demonstration : des grilles dessinees a la main, et des scenarios qui doivent montrer ce
/// qu'ils annoncent.
///
/// Un scenario de demonstration sert a tourner un clip. S'il cesse de produire le geste qu'il
/// annonce — parce qu'un cout a bouge, qu'une precondition a change, qu'une porte a glisse d'une case —
/// on ne le verrait qu'au montage, sur une prise ou il ne se passe rien. Ces tests lisent les attentes
/// dans <c>config/demo_scenarios.json</c> et deroulent chaque scenario, horde comprise : la promesse
/// ecrite a cote du scenario est celle que la suite verifie.
/// </summary>
public class DemoScenarioTests(ITestOutputHelper output)
{
    private static DemoCatalogue Catalogue => DemoRun.Catalogue;

    public static TheoryData<string> Scenarios()
    {
        var data = new TheoryData<string>();
        foreach (var scenario in DemoRun.Catalogue.Scenarios) data.Add(scenario.Id);
        return data;
    }

    public static TheoryData<string> Contrasts()
    {
        var data = new TheoryData<string>();
        foreach (var scenario in DemoRun.Catalogue.Scenarios.Where(scenario => scenario.Expect.HasContrast)) data.Add(scenario.Id);
        return data;
    }

    // --- Le fichier ------------------------------------------------------------------------------

    [Fact]
    public void Le_catalogue_livre_compte_une_dizaine_de_scenarios_sur_plusieurs_grilles()
    {
        Catalogue.Validate(DemoMap.Items, MissionRun.Roster);

        Assert.True(Catalogue.Grids.Count >= 2, $"{Catalogue.Grids.Count} grille(s).");
        Assert.InRange(Catalogue.Scenarios.Count, 10, 30);

        Assert.Contains(Catalogue.Scenarios, scenario => scenario.Focus == DemoFocus.Constraint);
        Assert.Contains(Catalogue.Scenarios, scenario => scenario.Focus == DemoFocus.Action);
        Assert.True(
            Catalogue.Scenarios.Count(scenario => scenario.Focus == DemoFocus.Cooperation) >= 5,
            "La cooperation entre survivants a son propre jeu de scenarios.");
        Assert.Contains(Catalogue.Scenarios, scenario => scenario.Focus == DemoFocus.Strategy);
        Assert.Contains(Catalogue.Scenarios, scenario => scenario.Focus == DemoFocus.Combination);

        // Une grille sans scenario ne se choisirait que pour afficher une liste vide.
        Assert.All(Catalogue.Grids, grid => Assert.NotEmpty(Catalogue.On(grid)));
    }

    /// <summary>
    /// Les attentes citent des actions par leur nom : une action renommee ou retiree du domaine ferait
    /// passer « never » a vide, pour toujours. Elles sont donc verifiees contre le domaine livre — et
    /// contre les schemas qu'une consigne peut creer, comme <c>EnterBuilding</c>.
    /// </summary>
    [Fact]
    public void Chaque_attente_nomme_une_action_du_domaine_ou_un_evenement_connu()
    {
        foreach (var scenario in Catalogue.Scenarios)
        {
            var known = new HashSet<string>(DemoEvents.All, StringComparer.OrdinalIgnoreCase);
            foreach (var action in MissionRun.Domain.Actions) known.Add(action.Name);
            if (Compile(scenario) is { } compiled) foreach (var action in compiled.Compiled.Actions) known.Add(action.Name);

            var expect = scenario.Expect;
            var cited = expect.With.Shows.Concat(expect.With.Never).Concat(expect.Without.Shows).Concat(expect.Without.Never);

            // Une forme liee — MoveToLocation(group) — se reconnait a son schema : c'est la
            // destination qui la distingue, et le schema qui doit exister.
            Assert.All(cited, name => Assert.True(
                known.Contains(name) || known.Contains(name.Split('(')[0]),
                $"{scenario.Id} : « {name} » n'est ni une action ni un evenement."));
        }
    }

    /// <summary>
    /// Une consigne de scenario passe les memes portillons que celle d'un joueur. Refusee, elle
    /// tomberait en jeu avec une ligne de journal — et le clip montrerait un groupe sans consigne en
    /// pretendant le contraire.
    /// </summary>
    [Fact]
    public void Chaque_consigne_de_scenario_est_acceptee_par_les_portillons()
    {
        foreach (var scenario in Catalogue.Scenarios.Where(scenario => scenario.Instructions.Count > 0))
        {
            var instructions = Compile(scenario)!;
            Assert.Equal(scenario.Instructions.Count, instructions.Active.Count);
        }
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Chaque_grille_mene_du_van_a_l_objectif(string id)
    {
        var stage = Catalogue.Stage(Catalogue.Find(id)!, DemoRun.Border);
        var map = stage.District.Map;

        var van = map.FindWalkableIn(stage.Mission.CampIn(map)!)!.Value;
        var objective = stage.Mission.ObjectiveIn(map)!;

        // Une porte verrouillee se force et une vitre se brise : ce sont des passages, pas des murs.
        var open = new GridMap(map.Width, map.Height, map.Origin);
        foreach (var cell in map.AllPositions())
            open.SetCell(cell, map.GetCell(cell) is CellType.DoorLocked or CellType.Window ? CellType.Floor : map.GetCell(cell));

        var reached = Flood(open, van);

        Assert.Contains(open.FindWalkableIn(objective)!.Value, reached);
        Assert.All(stage.District.ZombieSpawns, cell => Assert.True(map.IsWalkable(cell), $"{id} : zombie dans un mur en {cell}."));
        Assert.All(stage.Starts.Values, cell => Assert.Contains(cell, reached));
    }

    [Fact]
    public void Une_grille_se_construit_avec_sa_ceinture_son_van_et_son_retour()
    {
        var stage = Catalogue.Stage(Catalogue.Scenarios[0], DemoRun.Border);
        var map = stage.District.Map;
        var grid = stage.Grid;

        Assert.Equal(grid.Width + (2 * DemoGrid.Belt), map.Width);
        Assert.Equal(CellType.Border, map.GetCell(GridPosition.Zero));

        var van = map.GetZone(ZoneKind.Start)!;
        var home = map.GetZone(ZoneKind.Extraction)!;

        Assert.Equal(van.Min, home.Min);
        Assert.Equal(van.Max, home.Max);
        Assert.Contains(van.Positions(), cell => map.GetCell(cell) == CellType.Extraction);
    }

    [Fact]
    public void Un_lieu_troue_est_refuse_au_chargement()
    {
        var grid = new DemoGrid
        {
            Id = "trouee",
            Terrain = ["....", "...."],
            Zones = ["VV.V", "VVVV"],
            Legend = new() { ["V"] = new DemoZone { Name = "Van", Kind = ZoneKind.Start } },
        };

        var catalogue = new DemoCatalogue { Grids = [grid] };

        var error = Assert.Throws<InvalidOperationException>(() => catalogue.Validate());
        Assert.Contains("rectangle plein", error.Message);
    }

    [Fact]
    public void Un_repere_absent_de_la_grille_est_refuse_au_chargement()
    {
        var scenario = Catalogue.Scenarios[0] with { Zombies = ["Z9"] };
        var catalogue = new DemoCatalogue { Grids = Catalogue.Grids, Scenarios = [scenario] };

        var error = Assert.Throws<InvalidOperationException>(() => catalogue.Validate());
        Assert.Contains("Z9", error.Message);
    }

    /// <summary>
    /// « Jamais EscapeLocation » ne dit rien seul : la fuite n'aurait peut-etre jamais eu lieu. Une
    /// consigne ne peut promettre une absence que si son contraste montre le geste qu'elle retire.
    /// </summary>
    [Fact]
    public void Une_consigne_ne_promet_une_absence_que_si_son_contraste_montre_le_geste()
    {
        var menace = Catalogue.Find("menace_avant_la_fuite")!;
        Assert.Empty(menace.Expect.With.Shows);

        var unproven = menace with
        {
            Expect = menace.Expect with { Without = new DemoOutcome { Shows = ["Victoire"] } },
        };

        var error = Assert.Throws<InvalidOperationException>(
            () => new DemoCatalogue { Grids = Catalogue.Grids, Scenarios = [unproven] }.Validate());
        Assert.Contains("expect.with.shows", error.Message);

        new DemoCatalogue { Grids = Catalogue.Grids, Scenarios = [menace] }.Validate();
        Assert.Equal("jamais : EscapeLocation", menace.Expect.With.ToString());
    }

    [Fact]
    public void Le_paquetage_d_un_scenario_part_en_main_et_la_blessure_est_posee()
    {
        var run = new DemoRun("soin");
        var claire = run.Band.Survivors.Single();

        Assert.Equal(35f, claire.Health, 3);
        Assert.True(claire.Carries("bandage"));

        var bob = new DemoRun("combat_arme").Band.Survivors.Single();
        Assert.Equal("baseball_bat", bob.Inventory.EquippedWeapon);
    }

    [Fact]
    public void Un_ecart_a_l_attente_se_dit_en_toutes_lettres()
    {
        var outcome = new DemoOutcome { Shows = ["BreakWindow", "Victoire"], Never = ["ForceDoor"] };

        Assert.Empty(outcome.Failures(["Explore", "BreakWindow", "Victoire"]));
        Assert.Equal(
            ["BreakWindow attendu, jamais survenu", "ForceDoor interdit, survenu"],
            outcome.Failures(["ForceDoor", "victoire"]));
    }

    [Fact]
    public void Retirer_les_consignes_d_un_scenario_d_action_ne_change_pas_son_attente()
    {
        var action = Catalogue.Scenarios.First(scenario => scenario.Instructions.Count == 0);
        var constraint = Catalogue.Scenarios.First(scenario => scenario.Expect.HasContrast);

        Assert.Same(action.Expect.With, action.Expected(withInstructions: false));
        Assert.Same(constraint.Expect.Without, constraint.Expected(withInstructions: false));
    }

    /// <summary>
    /// Une consigne ecrite dans une carte blanche s'ajouterait en silence a celles du joueur : le bilan
    /// lui attribuerait un effet qu'il n'a pas demande.
    /// </summary>
    [Fact]
    public void Une_carte_blanche_ne_porte_aucune_consigne_ecrite()
    {
        var open = Catalogue.Scenarios.First(scenario => scenario.TakesPlayerInstructions);
        Assert.Empty(open.Instructions);

        var written = open with { Instructions = ["forbid(ForceDoor)"] };

        var error = Assert.Throws<InvalidOperationException>(
            () => new DemoCatalogue { Grids = Catalogue.Grids, Scenarios = [written] }.Validate());
        Assert.Contains("carte blanche", error.Message);
    }

    /// <summary>
    /// Le fichier dit ce que fait le groupe livre a lui-meme, pas ce que fera la consigne d'un joueur :
    /// une prise jouee sous consignes n'a pas d'attente, et son bilan ne peut pas la dire non conforme.
    /// </summary>
    [Fact]
    public void Une_carte_blanche_jouee_sous_consignes_n_a_pas_d_attente()
    {
        var open = Catalogue.Scenarios.First(scenario => scenario.TakesPlayerInstructions);

        Assert.Same(open.Expect.With, open.Expected(withInstructions: true));
        Assert.Same(open.Expect.With, open.Expected(withInstructions: false));
        Assert.Null(open.Expected(withInstructions: true, playerInstructions: 1));

        Assert.Equal(
            ["MoveToLocation", "Partage", "ForceDoor"],
            DemoEvents.Chronicle(["MoveToLocation", "MoveToLocation(group)", "Partage", "ForceDoor", "partage"]));
    }

    // --- Ce que les scenarios montrent -----------------------------------------------------------

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Le_scenario_montre_ce_qu_il_annonce(string id)
    {
        var run = new DemoRun(id);
        var expect = run.Stage.Scenario.Expected(withInstructions: true)!;

        run.Run(Seconds(run));
        output.WriteLine(run.Trace());

        Assert.True(run.Tracker.IsOver, $"{id} : aucun bilan, la prise ne se conclut pas. {run.Trace()}");
        Assert.True(expect.Failures(run.Happened).Count == 0, $"{id} : {string.Join(" ; ", expect.Failures(run.Happened))}. {run.Trace()}");
    }

    /// <summary>
    /// Le contraste d'une consigne : sans elle, le groupe fait ce qu'elle interdit. Sans ce test, un
    /// scenario de consigne pourrait « marcher » parce que le geste interdit n'aurait de toute facon
    /// jamais eu lieu — le clip montrerait alors une consigne sans effet en la presentant comme obeie.
    /// </summary>
    [Theory]
    [MemberData(nameof(Contrasts))]
    public void Sans_sa_consigne_le_scenario_fait_ce_qu_elle_empeche(string id)
    {
        var run = new DemoRun(id, withInstructions: false);
        var expect = run.Stage.Scenario.Expected(withInstructions: false)!;

        run.Run(Seconds(run));
        output.WriteLine(run.Trace());

        Assert.True(run.Tracker.IsOver, $"{id} sans consigne : aucun bilan, la prise ne se conclut pas. {run.Trace()}");
        Assert.True(expect.Failures(run.Happened).Count == 0, $"{id} sans consigne : {string.Join(" ; ", expect.Failures(run.Happened))}. {run.Trace()}");
    }

    /// <summary>
    /// La carte blanche tient ce qu'elle annonce au joueur : chaque consigne d'entraide pre-ecrite y
    /// a de quoi agir. Le groupe seul n'en fait aucun geste (son attente, verifiee plus haut) ; sous
    /// la consigne, le geste arrive et la prise se conclut. Une grille ou une consigne n'aurait rien a
    /// faire donnerait au joueur une strategie « acceptee » sans aucun effet visible — c'est pour
    /// cela que Bob part blesse, sans quoi « soignez les blesses » n'aurait personne a soigner.
    ///
    /// Les consignes sont prises dans <c>config/instruction_presets.json</c> et non recopiees : une
    /// consigne pre-ecrite retiree ou reecrite doit faire echouer ce test.
    /// </summary>
    [Theory]
    [InlineData("require_before(diversion_made, enter:building)", "ThrowFirecracker")]
    [InlineData("require_before(armed, enter:building)", "BorrowWeapon")]
    [InlineData("require_before(band_tended, enter:building)", "TendAlly")]
    [InlineData("require_before(knowledge_shared, ReachExtraction)", "ShareKnowledge")]
    [InlineData("require_before(ordered(extraction), ReachExtraction)", "OrderAlly")]
    [InlineData("require_before(band_ordered(extraction), ReachExtraction)", "OrderBand")]
    public void La_carte_blanche_joue_la_strategie_que_le_joueur_lui_donne(string constraint, string gesture)
    {
        var presets = InstructionPresetCatalogue.FromJson(DemoMap.ReadConfig("instruction_presets.json"));
        Assert.Contains(presets.Presets, preset => preset.Constraint == constraint);

        var open = Catalogue.Scenarios.First(scenario => scenario.TakesPlayerInstructions);
        var run = new DemoRun(open, player: [constraint]);

        run.Run(Seconds(run));
        output.WriteLine(run.Trace());

        Assert.True(run.Tracker.IsOver, $"{constraint} : aucun bilan, la prise ne se conclut pas. {run.Trace()}");
        Assert.True(run.Saw(gesture), $"{constraint} : {gesture} jamais survenu. {run.Trace()}");
    }

    /// <summary>
    /// Ce que l'attente du scenario ne peut pas dire : « OrderBand » survient, mais a combien ? Le
    /// rappel doit atteindre Bob <b>et</b> Claire — un rappel qui n'en toucherait qu'un serait un ordre
    /// simple sous un autre nom, et le clip ne montrerait pas ce qu'il annonce.
    /// </summary>
    [Fact]
    public void Le_rappel_du_square_atteint_les_deux_camarades()
    {
        var run = new DemoRun("rappeler_tout_le_monde");
        run.Run(Seconds(run));
        output.WriteLine(run.Trace());

        Assert.True(run.Saw("OrderBand"), run.Trace());

        // Un camarade ne retient le rappel que s'il y a obei (WorldPlanRuntime.Order) : c'est la trace
        // la plus sure qu'il l'a recu, plus qu'une ligne de journal.
        foreach (string id in new[] { "bob", "claire" })
        {
            var survivor = run.Band.Survivors.Single(survivor => survivor.Id == id);
            Assert.True(survivor.Knowledge.BandOrders.ContainsKey("extraction"), $"{survivor.Name} n'a pas ete rappele. {run.Trace()}");
        }
    }

    /// <summary>
    /// Le temps imparti et le retour. Une prise se deroule jusqu'a son verdict, comme dans le jeu, et
    /// non jusqu'a ce que l'attente soit remplie : le bilan du jeu ne juge qu'a la fin, et une prise
    /// qui ne se conclut pas n'a pas de bilan du tout. Deux minutes apres le rappel : une prise sans
    /// diversion laisse la horde sur le groupe, et rentrer en la semant prend son temps.
    /// </summary>
    private static double Seconds(DemoRun run)
        => (run.Stage.Mission.TimeLimit ?? MissionRun.Config.Mission.TimeLimit) + 120d;

    private static TacticalInstructions? Compile(DemoScenario scenario)
    {
        if (scenario.Instructions.Count == 0) return null;

        var map = Catalogue.Stage(scenario, DemoRun.Border).District.Map;
        var instructions = new TacticalInstructions(PddlDomainBuilder.From(MissionRun.Domain), MissionRun.Config.Instructions);

        var kinds = new HashSet<string>(
            map.Zones.Select(zone => SymbolicStateBuilder.KindNameOf(zone.Kind)).OfType<string>(),
            StringComparer.OrdinalIgnoreCase);

        foreach (string text in scenario.Instructions)
        {
            var entry = instructions.Add(text, new CompilationContext(MissionRun.Config.Planner.Search, kinds));
            Assert.True(entry.Accepted, $"{scenario.Id} : « {text} » refusee au portillon {entry.Gate} — {entry.Reason}");
        }

        return instructions;
    }

    private static HashSet<GridPosition> Flood(GridMap map, GridPosition from)
    {
        var seen = new HashSet<GridPosition> { from };
        var queue = new Queue<GridPosition>([from]);

        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();

            foreach (var offset in GridPosition.OrthogonalOffsets)
            {
                var next = cell + offset;
                if (map.IsWalkable(next) && seen.Add(next)) queue.Enqueue(next);
            }
        }

        return seen;
    }

    /// <summary>
    /// Une prise desserre l'horloge du planificateur sans jamais la resserrer, ne touche pas au
    /// plafond de noeuds, et le fichier livre la desserre : sans quoi le harnais et le jeu
    /// joueraient de nouveau deux budgets differents (<see cref="DemoSettings.PlannerMaxTimeMs"/>).
    /// </summary>
    [Fact]
    public void Une_prise_desserre_l_horloge_du_planificateur_sans_toucher_aux_noeuds()
    {
        var game = new PlannerSettings { MaxTimeMs = 50, MaxNodes = 1000 };

        var staged = new DemoSettings { PlannerMaxTimeMs = 2000 }.PlannerFor(game);
        Assert.Equal(2000, staged.MaxTimeMs);
        Assert.Equal(1000, staged.MaxNodes);

        Assert.Same(game, new DemoSettings { PlannerMaxTimeMs = 10 }.PlannerFor(game));
        Assert.Same(game, new DemoSettings().PlannerFor(game));

        var shipped = MissionRun.Config;
        Assert.True(shipped.Demo.PlannerFor(shipped.Planner).MaxTimeMs > shipped.Planner.MaxTimeMs);
    }
}
