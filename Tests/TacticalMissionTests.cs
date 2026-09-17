using Hordinary.AI.Instructions;
using Hordinary.AI.Planning;
using Hordinary.AI.Planning.Pddl;
using Hordinary.Agents;
using Hordinary.Game;
using Hordinary.World;
using Hordinary.World.MapGeneration;

namespace Hordinary.Tests;

/// <summary>
/// Le livrable 15 branche sur le jeu : le vocabulaire de lieux que les instructions nomment, et
/// l'effet reel d'une contrainte sur le repertoire d'un survivant.
///
/// <para>
/// <see cref="InstructionTests"/> verifie les portillons sur le domaine ; ici on verifie qu'ils
/// changent quelque chose a une mission qui tourne. Les deux sont necessaires : une compilation
/// parfaite dont personne ne recoit le produit ne se verrait nulle part.
/// </para>
/// </summary>
public class TacticalMissionTests
{
    /// <summary>Le nom que le planificateur donne a une zone de la carte livree.</summary>
    private static string NameOf(string zone) => SymbolicStateBuilder.ObjectNameOf(zone);

    /// <summary>Les faits que l'agent principal tient pour vrais a cet instant.</summary>
    private static IReadOnlyCollection<string> Facts(MissionRun run)
        => SymbolicStateBuilder.Observe(run.Agent.Context!, MissionRun.Config.Planner).State.Facts;

    // --- Le vocabulaire des lieux ------------------------------------------------------------------

    /// <summary>
    /// Un nom de zone devient un symbole PDDL : sans accent, sans espace, en minuscules. C'est le
    /// point ou le vocabulaire du joueur et celui du planificateur se rejoignent, et un symbole qui
    /// porterait un accent ne traverserait pas un fichier <c>.pddl</c>.
    /// </summary>
    [Fact]
    public void Un_nom_de_zone_devient_un_symbole_sans_accent()
    {
        Assert.Equal("entrepot_ouest", NameOf("Entrepôt ouest"));
        Assert.Equal("rue_principale", NameOf("Rue principale"));
        Assert.Equal("hangar_sud", NameOf("Hangar sud"));

        // Deux zones distinctes restent deux symboles distincts, ponctuation comprise.
        Assert.NotEqual(NameOf("Camp"), NameOf("Camp (retour de mission)"));
    }

    /// <summary>
    /// Apercevoir un bout d'entrepot, c'est savoir qu'il y a un entrepot la. C'est le prerequis
    /// bloquant du livrable 15 : tant que <c>SymbolicStateBuilder</c> ne nommait que la pharmacie,
    /// l'extraction et le depart, une instruction portant sur un batiment quelconque n'avait rien sur
    /// quoi se poser.
    /// </summary>
    [Fact]
    public void Un_batiment_apercu_devient_un_objet_de_planification()
    {
        var run = new MissionRun();
        string wanted = $"known({NameOf("Entrepot ouest")})";

        Assert.True(
            run.RunUntil(() => Facts(run).Contains(wanted), maxTicks: 900),
            $"L'entrepot n'est jamais entre dans l'etat symbolique : {string.Join(", ", Facts(run).Order())}");
    }

    /// <summary>
    /// Et il y entre avec sa <b>categorie</b>, sans quoi « avant d'entrer dans un batiment » ne
    /// designerait toujours rien : la contrainte compilee lit <c>building(?location)</c>, pas le nom
    /// du lieu.
    /// </summary>
    [Fact]
    public void Un_batiment_apercu_porte_sa_categorie()
    {
        var run = new MissionRun();
        string wanted = $"building({NameOf("Entrepot ouest")})";

        Assert.True(
            run.RunUntil(() => Facts(run).Contains(wanted), maxTicks: 900),
            "Le batiment est connu mais sans categorie : une instruction ne peut pas s'y accrocher.");
    }

    /// <summary>
    /// Les categories emises sont exactement celles que <c>planner_domain.json</c> declare dans
    /// <c>places.kinds</c>. Un fait dont le predicat n'est pas declare serait un predicat orphelin —
    /// celui qu'aucune precondition ne peut lire, et qu'un planificateur tiers refuserait.
    /// </summary>
    [Fact]
    public void Les_categories_emises_sont_celles_que_le_domaine_declare()
    {
        var run = new MissionRun(team: true);
        run.RunUntil(() => false, maxTicks: 600);

        var declared = MissionRun.Domain.Places.Kinds;
        Assert.NotEmpty(declared);

        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var agent in run.Band.Agents)
        {
            foreach (string fact in SymbolicStateBuilder.Observe(agent.Context!, MissionRun.Config.Planner).State.Facts)
            {
                string predicate = Fact.PredicateOf(fact);
                if (declared.Contains(predicate, StringComparer.OrdinalIgnoreCase)) emitted.Add(predicate);
            }
        }

        Assert.NotEmpty(emitted);
        Assert.All(emitted, kind => Assert.Contains(kind, declared, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Le lieu de la mission garde son nom unique — <c>objective_zone</c> — et recoit quand meme sa
    /// categorie. Sans cela, « avant d'entrer dans un batiment » vaudrait pour tous les batiments
    /// sauf celui qu'on envoie fouiller, ce qui est exactement l'inverse de l'intention du joueur.
    /// </summary>
    [Fact]
    public void Le_lieu_de_la_mission_porte_sa_categorie_sans_perdre_son_nom()
    {
        var run = new MissionRun();
        var facts = Facts(run);

        Assert.Contains("known(objective_zone)", facts);

        string category = SymbolicStateBuilder.KindNameOf(run.Alice.ObjectiveZone!.Kind)!;
        Assert.Contains($"{category}(objective_zone)", facts);

        // Et pas de jumeau au meme endroit : une porte ne se rattache qu'a UN lieu.
        Assert.DoesNotContain(facts, fact => fact.Equals($"known({NameOf(run.Alice.ObjectiveZone.Name)})", StringComparison.Ordinal));
    }

    /// <summary>
    /// Une rue ne recoit pas de porte. Lui en rattacher une ferait passer un quartier entier pour une
    /// piece verrouillee des qu'un survivant apercevrait une porte close depuis le trottoir.
    /// </summary>
    [Fact]
    public void Une_rue_n_est_jamais_fermee_par_une_ouverture()
    {
        var run = new MissionRun(team: true);
        run.RunUntil(() => false, maxTicks: 900);

        var outdoors = run.Map.Zones
            .Where(zone => zone.Kind.IsOpenAir())
            .Select(zone => NameOf(zone.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.NotEmpty(outdoors);

        foreach (var agent in run.Band.Agents)
        {
            foreach (string fact in SymbolicStateBuilder.Observe(agent.Context!, MissionRun.Config.Planner).State.Facts)
            {
                string predicate = Fact.PredicateOf(fact);
                if (predicate is not ("door_of" or "window_of")) continue;

                string guarded = Fact.ArgumentsOf(fact)[1];
                Assert.DoesNotContain(guarded, outdoors);
            }
        }
    }

    /// <summary>
    /// Nommer tous les batiments, toutes les rues et toutes les ruelles augmente le nombre d'objets
    /// <c>location</c>, donc le facteur de branchement de <c>MoveToLocation</c>. La spec du livrable
    /// demande de verifier que <c>planner.max_nodes</c> et <c>planner.max_time_ms</c> tiennent
    /// encore — la reponse doit venir d'une mesure, pas d'une intuition.
    ///
    /// <para>
    /// Le test porte sur un <b>quartier genere</b> et non sur la carte dessinee : celle-ci compte
    /// sept zones, un quartier genere en compte bien davantage, et c'est lui le cas defavorable. Un
    /// plafond atteint ici ne serait pas un test qui casse mais un budget a relever dans
    /// <c>config/game_config.json</c> — jamais en dur.
    /// </para>
    /// </summary>
    [Fact]
    public void Nommer_tous_les_lieux_ne_fait_pas_deborder_les_budgets_du_planificateur()
    {
        var mapgen = MapGenConfig.FromJson(DemoMap.ReadConfig("mapgen.json"));
        var profile = mapgen.Profiles[0];

        var mission = new MissionDefinition
        {
            Id = "test-budgets",
            Name = "Mission de test",
            Target = new ZoneTarget(ZoneKind.Pharmacy),
            MapProfile = profile.Id,
        };

        var district = new DistrictGenerator(mapgen).Generate(
            new DistrictRequest
            {
                Mission = mission,
                Seed = 20260728,
                ZombieCount = 12,
                MinimumSpawnDistance = MissionRun.Config.Zombies.MinimumSpawnDistance,
            });

        Assert.True(district.Map.Zones.Count > MissionRun.Domain.Places.Kinds.Count, "Le quartier genere ne compte pas plus de lieux que de categories : le cas n'est pas defavorable.");

        var run = new MissionRun(map: district.Map, definition: mission, team: true);
        run.RunUntil(() => false, maxTicks: 1500);

        // Le plafond de NOEUDS, et pas celui de temps : le premier dit quelque chose du domaine — le
        // facteur de branchement que ce livrable augmente — le second dit surtout que la machine
        // etait chargee. Faire echouer la suite sur une horloge en ferait un test de humeur.
        var starved = run.Band.Agents
            .SelectMany(agent => agent.Planner.Rejected)
            .Where(result => result.Search.Failure is PlanningFailure.NodeLimitReached)
            .ToList();

        Assert.True(
            starved.Count == 0,
            $"{starved.Count} recherche(s) a court de noeuds : {string.Join(" ", starved.Select(result => result.Reason))}");
    }

    // --- Ce qu'une contrainte fait a une mission ---------------------------------------------------

    /// <summary>
    /// La demonstration du livrable, sans modele de langage : une instruction pre-ecrite est
    /// appliquee, et le repertoire du groupe change. Le survivant ne recoit pas un ordre de plus —
    /// le plan qui cassait une vitre n'existe simplement plus.
    /// </summary>
    [Fact]
    public void Une_instruction_pre_ecrite_change_le_repertoire_du_groupe()
    {
        var libre = new MissionRun();
        Assert.NotNull(libre.Agent.Domain.FindAction("BreakWindow"));

        var tenu = new MissionRun(instructions: ["forbid(BreakWindow)"]);

        Assert.Null(tenu.Agent.Domain.FindAction("BreakWindow"));
        Assert.Equal(libre.Agent.Domain.Active.Count() - 1, tenu.Agent.Domain.Active.Count());
    }

    /// <summary>
    /// Les contraintes sont collectives : une instruction s'applique aux trois survivants, et a ceux
    /// qui s'enrolent apres elle. Un domaine compile par agent est possible et hors perimetre.
    /// </summary>
    [Fact]
    public void Une_instruction_vaut_pour_tout_le_groupe()
    {
        var run = new MissionRun(team: true, instructions: ["forbid(BreakWindow)"]);

        Assert.Equal(3, run.Band.Agents.Count);
        Assert.All(run.Band.Agents, agent => Assert.Null(agent.Domain.FindAction("BreakWindow")));
        Assert.All(run.Band.Agents, agent => Assert.NotNull(agent.Instructions));
    }

    /// <summary>
    /// Retirer les instructions rend le repertoire d'origine, et non le dernier domaine reecrit.
    /// C'est la promesse du livrable : sans contrainte, le jeu est celui d'avant.
    /// </summary>
    [Fact]
    public void Retirer_les_instructions_rend_le_repertoire_d_origine()
    {
        var run = new MissionRun(instructions: ["forbid(BreakWindow)"]);
        Assert.Null(run.Agent.Domain.FindAction("BreakWindow"));

        run.Band.Instruct(null);

        Assert.Null(run.Band.Instructions);
        Assert.NotNull(run.Agent.Domain.FindAction("BreakWindow"));
        Assert.Equal(MissionRun.Domain.Active.Count(), run.Agent.Domain.Active.Count());
    }

    /// <summary>
    /// Une contrainte posee en cours de mission fait replanifier. Sans cela le groupe continuerait de
    /// jouer le plan que la consigne vient d'invalider, et le joueur verrait sa regle ignoree pendant
    /// une action entiere — c'est-a-dire au pire moment.
    /// </summary>
    [Fact]
    public void Une_contrainte_posee_en_cours_de_mission_fait_replanifier()
    {
        var run = new MissionRun();
        run.RunUntil(() => run.Plan is not null, maxTicks: 200);

        int before = run.Replans.Count;

        var instructions = new TacticalInstructions(PddlDomainBuilder.From(MissionRun.Domain), MissionRun.Config.Instructions);
        Assert.True(instructions.Add("forbid(BreakWindow)").Accepted);

        run.Band.Instruct(instructions);
        run.RunUntil(() => run.Replans.Count > before, maxTicks: 200);

        Assert.True(run.Replans.Count > before, "La consigne n'a fait replanifier personne.");
        Assert.Null(run.Agent.Domain.FindAction("BreakWindow"));
    }

    /// <summary>
    /// Portillon 4 sur une mission qui tourne : le plan produit par GBFS depuis le domaine reecrit
    /// satisfait la forme <b>declarative</b> de la contrainte, celle qu'aucun planificateur n'a vue.
    /// Une compilation fausse produirait un plan valide au regard du domaine et faux au regard de la
    /// phrase du joueur, et rien d'autre dans la chaine ne le verrait.
    /// </summary>
    [Fact]
    public void Le_portillon_4_ne_releve_rien_sur_une_mission_conforme()
    {
        var run = new MissionRun(team: true, instructions:
        [
            "require_before(at(agent, group), enter:building)",
            "forbid(BreakWindow)",
        ]);

        var violations = new List<string>();
        foreach (var agent in run.Band.Agents) agent.Violated += violations.Add;

        run.RunUntil(() => violations.Count > 0, maxTicks: 1200);

        Assert.True(violations.Count == 0, string.Join(" ", violations));
    }

    // --- La trace sur disque -----------------------------------------------------------------------

    /// <summary>
    /// Le domaine compile d'une mission s'ecrit sur disque, et il s'y ecrit en deux exemplaires :
    /// celui que Fast Downward accepte — sans bloc <c>(:constraints …)</c>, hors de son fragment — et
    /// celui qui dit ce que le joueur voulait, que VAL corrobore.
    ///
    /// Ni <c>config/planner_domain.json</c> ni <c>tools/pddl/domain.pddl</c> ne bougent : c'est la
    /// raison d'etre de ce dossier par mission.
    /// </summary>
    [Fact]
    public void Le_domaine_compile_s_ecrit_pour_la_mission()
    {
        var source = PddlDomainBuilder.From(MissionRun.Domain);
        var instructions = new TacticalInstructions(source, MissionRun.Config.Instructions);

        var entry = instructions.Add("forbid(BreakWindow)", utterance: "Ne cassez aucune vitre.");
        Assert.True(entry.Accepted, entry.Reason);

        string root = Path.Combine(Path.GetTempPath(), $"hordinary-{Guid.NewGuid():N}");

        try
        {
            var written = MissionDomainFile.Write(root, "Ravitaillement pharmacie", instructions.Compiled, instructions.Active);

            Assert.Equal(2, written.Count);
            Assert.All(written, path => Assert.True(File.Exists(path), path));

            string compiled = File.ReadAllText(written[0]);
            string declared = File.ReadAllText(written[1]);

            Assert.DoesNotContain("(:constraints", compiled, StringComparison.Ordinal);
            Assert.Contains("(:constraints", declared, StringComparison.Ordinal);

            // La phrase du joueur est en tete des deux : le fichier doit pouvoir s'ouvrir seul.
            Assert.All([compiled, declared], text => Assert.Contains("; Ne cassez aucune vitre.", text, StringComparison.Ordinal));

            // Et ce que la contrainte a fait s'y lit : le schema retire n'y est plus.
            Assert.DoesNotContain("(:action BreakWindow", compiled, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Sans contrainte active, rien n'est ecrit : un domaine que personne n'a reecrit vit deja dans <c>tools/pddl/</c>.</summary>
    [Fact]
    public void Un_domaine_sans_contrainte_ne_s_ecrit_pas()
    {
        string root = Path.Combine(Path.GetTempPath(), $"hordinary-{Guid.NewGuid():N}");

        Assert.Empty(MissionDomainFile.Write(root, "mission", PddlDomainBuilder.From(MissionRun.Domain), []));
        Assert.False(Directory.Exists(root));
    }
}
