using Hordinary.AI.Instructions;
using Hordinary.AI.Planning;
using Hordinary.AI.Planning.Pddl;
using Hordinary.AI.Utility;
using Hordinary.Agents;
using Hordinary.Game;
using Hordinary.World;

namespace Hordinary.Tests;

/// <summary>
/// L'entraide : ce qu'un survivant fait pour un autre, ou grace a lui.
///
/// Cinq gestes — lancer un petard, emprunter une arme, soigner un camarade, lui donner un ordre,
/// rappeler tout le groupe — que le domaine sait faire et qu'aucun objectif ne reclame : c'est une
/// consigne du joueur qui leur donne un demandeur.
/// Et deux canaux par lesquels l'un sait ce que l'autre a fait : la parole, qui porte la sante et ce
/// qu'on a de trop dans son sac, et la radio, qui la porte plus loin.
/// </summary>
public class CooperationTests
{
    // --- Ce qui ne se decide pas seul ----------------------------------------------------------

    /// <summary>
    /// La regle de l'epuration, verifiee sur les fichiers livres : les cinq faits de l'entraide ne
    /// sont lus par aucune precondition et reclames par aucun but. Aucun objectif ne peut donc faire
    /// remonter le planificateur jusqu'au petard, au pret ou au soin — et c'est une consigne, une
    /// seule, qui l'y fait remonter.
    /// </summary>
    [Theory]
    [InlineData("diversion_made", "ThrowFirecracker", "require_before(diversion_made, enter:building)")]
    [InlineData("armed", "BorrowWeapon", "require_before(armed, enter:building)")]
    [InlineData("band_tended", "TendAlly", "require_before(band_tended, enter:building)")]
    [InlineData("ordered", "OrderAlly", "require_before(ordered(extraction), enter:building)")]
    [InlineData("band_ordered", "OrderBand", "require_before(band_ordered(extraction), enter:building)")]
    public void Un_geste_d_entraide_n_a_pas_d_autre_demandeur_qu_une_consigne(string fact, string gesture, string constraint)
    {
        var domain = MissionRun.Domain;

        // Le geste garde son propre fait en negatif — on ne lance pas un second petard sur une
        // diversion en cours. Ce qui compte est qu'aucun autre ne le reclame en positif.
        static bool Demands(IEnumerable<Condition> conditions, string fact)
            => conditions.Any(condition => condition.Expected && Fact.PredicateOf(condition.Pattern) == fact);

        Assert.NotNull(domain.FindAction(gesture));
        Assert.DoesNotContain(domain.Actions, action => Demands(action.ParsedPreconditions, fact));
        Assert.DoesNotContain(domain.Goals, goal => Demands(goal.ParsedConditions, fact));

        var instructions = new TacticalInstructions(PddlDomainBuilder.From(domain), MissionRun.Config.Instructions);
        Assert.True(instructions.Add(constraint).Accepted, constraint);

        var compiled = instructions.Compiled.ToPlanningDomain();
        Assert.Contains(compiled.Actions, action => Demands(action.ParsedPreconditions, fact));
    }

    /// <summary>Chaque consigne d'entraide est proposee au joueur, et passe les portillons.</summary>
    [Theory]
    [InlineData("diversion_avant_entree")]
    [InlineData("personne_n_entre_desarme")]
    [InlineData("soigner_avant_d_entrer")]
    [InlineData("prevenir_avant_de_rentrer")]
    [InlineData("renvoyer_au_van")]
    [InlineData("rappeler_tout_le_monde")]
    public void Les_consignes_d_entraide_sont_proposees_au_joueur(string id)
    {
        var presets = InstructionPresetCatalogue.FromJson(DemoMap.ReadConfig("instruction_presets.json"));
        var preset = presets.Find(id);

        Assert.NotNull(preset);

        var instructions = new TacticalInstructions(PddlDomainBuilder.From(MissionRun.Domain), MissionRun.Config.Instructions);
        Assert.True(instructions.Add(preset!.Constraint).Accepted, preset.Constraint);
    }

    // --- La radio --------------------------------------------------------------------------------

    /// <summary>
    /// Une radio n'est utile qu'aux deux bouts. L'effet etait declare dans le catalogue depuis le
    /// livrable 11 et personne ne le lisait : une radio emportee ne changeait rien a qui entendait quoi.
    /// </summary>
    [Fact]
    public void Une_radio_ne_relie_que_deux_postes()
    {
        var alice = MissionRun.Lone("alice", new GridPosition(0, 0));
        var bob = MissionRun.Lone("bob", new GridPosition(12, 0));
        var sharing = new KnowledgeSharing(MissionRun.Config.Knowledge);

        Assert.False(sharing.CanTalk(alice, bob));

        Carry(alice, "radio");
        Assert.False(sharing.CanTalk(alice, bob));

        Carry(bob, "radio");
        Assert.True(sharing.CanTalk(alice, bob));

        KnowledgeExchange? heard = null;
        sharing.Exchanged += exchange => heard = exchange;
        sharing.Talk(alice, bob, 0d);

        Assert.True(heard?.ByRadio);
        Assert.Contains("par radio", heard!.Value.ToString(), StringComparison.Ordinal);

        // Une radio au fond du sac n'est pas allumee.
        bob.Inventory.Unequip("radio");
        Assert.False(sharing.CanTalk(alice, bob));
    }

    // --- Le pret ---------------------------------------------------------------------------------

    /// <summary>
    /// On voit ce qu'un camarade tient, pas ce qu'il a dans son sac : l'arme de rechange s'apprend en
    /// se parlant — et « je n'en ai plus » efface ce que « j'en ai une » avait appris.
    /// </summary>
    [Fact]
    public void Une_arme_de_rechange_se_dit_en_parlant()
    {
        var alice = MissionRun.Lone("alice", new GridPosition(0, 0));
        var bob = MissionRun.Lone("bob", new GridPosition(1, 0));
        var sharing = new KnowledgeSharing(MissionRun.Config.Knowledge);

        Carry(bob, "baseball_bat");
        Carry(bob, "crowbar");

        Assert.Equal("crowbar", bob.Inventory.EquippedWeapon);
        Assert.Equal("baseball_bat", bob.Inventory.SpareWeapon);

        sharing.Talk(bob, alice, 0d);
        Assert.True(alice.Knowledge.GetEntity("bob")!.CanLend);

        Assert.True(bob.Inventory.HandTo(alice.Inventory, "baseball_bat"));
        sharing.Talk(bob, alice, 1d);

        var belief = alice.Knowledge.GetEntity("bob")!;
        Assert.False(belief.CanLend);
        Assert.Equal(string.Empty, belief.SpareWeapon);
    }

    /// <summary>Une batte fendue pretee reste fendue : sans quoi preter serait une facon de reparer.</summary>
    [Fact]
    public void Une_arme_pretee_garde_son_usure()
    {
        var bob = MissionRun.Lone("bob", new GridPosition(0, 0));
        var alice = MissionRun.Lone("alice", new GridPosition(1, 0));

        Carry(bob, "baseball_bat");
        bob.Inventory.RegisterStrike("baseball_bat");
        bob.Inventory.RegisterStrike("baseball_bat");
        int? worn = bob.Inventory.DurabilityOf("baseball_bat");

        Assert.True(bob.Inventory.HandTo(alice.Inventory, "baseball_bat"));

        Assert.False(bob.Inventory.Carries("baseball_bat"));
        Assert.Equal(worn, alice.Inventory.DurabilityOf("baseball_bat"));
    }

    // --- Les faits de l'entraide -----------------------------------------------------------------

    /// <summary>
    /// « Le groupe est soigne » se lit dans ce qu'on sait : sa propre sante, et celle que les autres
    /// ont dite. Un camarade qui n'a rien dit ne compte pas — on ne soigne pas ce qu'on devine.
    /// </summary>
    [Fact]
    public void Le_groupe_n_est_soigne_que_si_personne_ne_se_dit_blesse()
    {
        var claire = MissionRun.Lone("claire", new GridPosition(5, 5));

        Assert.True(Holds(claire, "band_tended"));

        claire.Knowledge.RememberEntity("bob", EntityKind.Survivor, new GridPosition(6, 5), 0d);
        Assert.True(Holds(claire, "band_tended"));

        claire.Knowledge.RememberEntity("bob", EntityKind.Survivor, new GridPosition(6, 5), 1d, health: 0.3f);
        Assert.False(Holds(claire, "band_tended"));
        Assert.Equal(new GridPosition(6, 5), Context(claire, 1d).Locate("patient"));

        // Soi compris : un blesse ne passe pas la porte le premier.
        var bob = MissionRun.Lone("bob", new GridPosition(5, 5));
        bob.TakeDamage(70f);
        Assert.False(Holds(bob, "band_tended"));
    }

    /// <summary>
    /// Une diversion s'entend, et se raconte : c'est le leurre lui-meme, pas un bruit d'action, qui
    /// dit que la voie se libere. Le moteur etiquette « Distraction » tout bruit d'action sans
    /// libelle ; si ce fait s'y lisait, le pas de depart d'un deplacement passerait pour un petard.
    /// </summary>
    [Fact]
    public void Une_diversion_s_entend_se_raconte_et_s_eteint()
    {
        var bob = MissionRun.Lone("bob", new GridPosition(0, 0));
        var alice = MissionRun.Lone("alice", new GridPosition(1, 0));

        bob.Knowledge.RememberNoise("noise-1", new GridPosition(10, 0), 2f, 0d, action: NoiseAction.Distraction);
        Assert.False(Holds(bob, "diversion_made"));

        bob.Knowledge.RememberNoise("noise-2", new GridPosition(10, 0), 10f, 0d, action: NoiseAction.Lure);
        Assert.True(Holds(bob, "diversion_made", 5d));

        float memory = MissionRun.Config.UtilityAi.Cooperation.DiversionMemory;
        Assert.False(Holds(bob, "diversion_made", memory + 1d));

        // Alice etait trop loin pour l'entendre : Bob le lui dit.
        Assert.False(Holds(alice, "diversion_made", 5d));
        alice.Knowledge.LearnFrom(bob.Knowledge);
        Assert.True(Holds(alice, "diversion_made", 5d));
        Assert.DoesNotContain(alice.Knowledge.KnownNoises.Values, noise => noise.Action == NoiseAction.Distraction);
    }

    /// <summary>
    /// Le petard part du cote des zombies qu'on sait la, et loin du lieu vise. La premiere version
    /// cherchait le point le plus eloigne de soi et du but : le fracas tombait a dix-huit cases de
    /// la horde, qui ne l'entendait pas et gardait la porte.
    /// </summary>
    [Fact]
    public void Le_leurre_part_du_cote_des_zombies_et_loin_du_but()
    {
        var map = TestMaps.Parse([.. Enumerable.Repeat(new string('.', 30), 20)]);
        var bob = MissionRun.Lone("bob", new GridPosition(10, 3), size: 30);

        foreach (var cell in map.AllPositions()) bob.Knowledge.ObserveCell(cell, CellType.Floor, indoor: false, 0d);

        bob.ObjectiveZone = new ObjectiveSite("Superette", ZoneKind.Building);
        bob.Knowledge.RememberLocation("Superette", ZoneKind.Building, new GridPosition(8, 12), 0d, KnowledgeSource.Inference);

        // Sans petard dans le sac, il n'y a nulle part ou le lancer.
        Assert.Null(Context(bob).Locate("decoy"));

        Carry(bob, "firecracker");

        var zombie = new GridPosition(14, 12);
        bob.Knowledge.RememberEntity("zombie-1", EntityKind.Zombie, zombie, 0d);

        var decoy = Context(bob).Locate("decoy")!.Value;
        var cooperation = MissionRun.Config.UtilityAi.Cooperation;

        Assert.True(Distance(decoy, zombie) <= cooperation.LureReach, $"leurre en {decoy}, hors de portee du zombie");
        Assert.True(Distance(decoy, bob.Position) <= cooperation.ThrowRange, $"leurre en {decoy}, hors de portee de jet");
        Assert.True(
            Distance(decoy, new GridPosition(8, 12)) > Distance(zombie, new GridPosition(8, 12)),
            $"leurre en {decoy} : il ramene la horde vers le but au lieu de l'en eloigner");
    }

    // --- Les prises : ce que le geste a change dans le monde ------------------------------------

    [Fact]
    public void Apres_l_emprunt_Alice_est_armee_et_Bob_n_a_plus_rien_a_preter()
    {
        var run = new DemoRun("pret_d_arme");
        var alice = run.Band.Survivors.Single(survivor => survivor.Id == "alice");
        var bob = run.Band.Survivors.Single(survivor => survivor.Id == "bob");

        Assert.Null(alice.Inventory.EquippedWeapon);

        Assert.True(run.RunUntil(() => run.Saw("BorrowWeapon"), 60d), run.Trace());

        Assert.True(alice.Inventory.IsArmed);
        Assert.Null(bob.Inventory.SpareWeapon);
        Assert.True(bob.Inventory.IsArmed);
    }

    [Fact]
    public void Apres_le_soin_Bob_n_est_plus_un_blesse()
    {
        var run = new DemoRun("soin_d_un_camarade");
        var bob = run.Band.Survivors.Single(survivor => survivor.Id == "bob");
        float wounded = MissionRun.Config.UtilityAi.Cooperation.WoundedRatio;

        Assert.True(bob.HealthRatio < wounded);
        Assert.True(run.RunUntil(() => run.Saw("TendAlly"), 90d), run.Trace());
        Assert.True(bob.HealthRatio >= wounded, $"Bob a {bob.HealthRatio:P0} apres le soin.");
    }

    /// <summary>
    /// Le petard est consomme, les zombies vont voir, et Alice — qui ne portait rien — sait que la
    /// diversion a eu lieu. C'est tout le clip, verifie ailleurs que sur le nom d'une action.
    /// </summary>
    [Fact]
    public void Le_petard_detourne_la_horde_et_Alice_le_sait()
    {
        var run = new DemoRun("diversion");
        var alice = run.Band.Survivors.Single(survivor => survivor.Id == "alice");
        var bob = run.Band.Survivors.Single(survivor => survivor.Id == "bob");

        bool lured = false;
        run.Horde.StateChanged += (_, decision) => lured |= decision.State == AI.Zombies.ZombieState.InvestigateNoise;

        Assert.True(run.RunUntil(() => run.Saw("ThrowFirecracker"), 30d), run.Trace());
        run.Run(2d);

        Assert.False(bob.Carries("firecracker"));
        Assert.True(lured, "aucun zombie n'est alle voir le petard");
        Assert.Contains(alice.Knowledge.KnownNoises.Values, noise => noise.Action == NoiseAction.Lure);
    }

    // --- L'ordre --------------------------------------------------------------------------------

    /// <summary>
    /// L'ordre d'un camarade passe devant tout ce que le survivant poursuivait de lui-meme, et
    /// s'efface une fois rempli : c'est une parenthese, pas une laisse. Bob envoie un camarade au lieu
    /// de la mission ; celui-ci n'a plus d'autre objectif que celui-la jusqu'a destination.
    /// </summary>
    [Fact]
    public void L_ordre_d_un_camarade_passe_devant_ses_objectifs_et_s_efface_une_fois_rempli()
    {
        var run = new MissionRun(team: true, settings: Untimed);
        var bob = run.AgentOf("bob");
        var site = run.Spawn(ZoneKind.Pharmacy);

        Assert.True(bob.Runtime.SetFlag("ordered(objective_zone)", true, site), run.Trace());

        var soldier = run.Band.Survivors.Single(survivor => survivor.Mission?.Issuer == "bob");
        var order = soldier.Mission!;

        Assert.Equal(MissionRun.Config.UtilityAi.Cooperation.OrderPriority, order.Priority);
        Assert.Equal(site, order.Destination);

        // Une passe pour decider, puis plus rien d'autre que l'ordre tant qu'il est pendant. La
        // carte de test n'a pas de horde : aucun reflexe n'a de raison de passer devant.
        run.Tick();
        var goals = new HashSet<string>();

        Assert.True(run.RunUntil(() =>
        {
            if (soldier.Mission?.FromAlly != true) return true;
            if (soldier.CurrentGoal is { } goal) goals.Add(goal);
            return false;
        }), run.Trace());

        Assert.Equal(["CompletePlayerMission"], goals);
        Assert.True(order.ReachedFrom(soldier.Position), $"{soldier.Name} a lache l'ordre en {soldier.Position}, loin de {site}.");
        Assert.Null(soldier.Mission);
        Assert.Contains(run.Journal, line => line.Contains($"{soldier.Name} a rempli", StringComparison.Ordinal));
    }

    /// <summary>
    /// On ne supplante qu'un ordre plus faible. Le rappel du van ne se discute pas entre survivants,
    /// et l'ordre du joueur, plus faible, est mis de cote et non perdu : il revient une fois l'ordre
    /// du camarade resolu.
    /// </summary>
    [Fact]
    public void Un_ordre_de_camarade_met_de_cote_celui_du_joueur_mais_jamais_le_rappel()
    {
        var alice = MissionRun.Lone("alice", new GridPosition(5, 5));
        float priority = MissionRun.Config.UtilityAi.Cooperation.OrderPriority;
        var comrade = new MissionOrder("ordre de Bob : aller a extraction", new GridPosition(9, 5), priority) { Issuer = "bob" };

        var recall = new MissionOrder("rentrer au van", new GridPosition(0, 0), MissionRun.Config.Mission.RecallPriority);
        alice.Mission = recall;
        Assert.False(alice.Obey(comrade));
        Assert.Same(recall, alice.Mission);

        var player = new MissionOrder("explorer", new GridPosition(1, 1), MissionRun.Config.UtilityAi.PlayerOrderPriority);
        alice.Mission = player;
        Assert.True(alice.Obey(comrade));
        Assert.Equal("bob", alice.Mission!.Issuer);

        // Un second ordre de camarade attend que le premier soit resolu.
        Assert.False(alice.Obey(comrade with { Issuer = "claire" }));

        Assert.Equal("bob", alice.Settle()!.Issuer);
        Assert.Same(player, alice.Mission);

        // Un ordre du joueur ne se resout pas : il reste pose jusqu'au suivant.
        Assert.Null(alice.Settle());
        Assert.Same(player, alice.Mission);
    }

    /// <summary>
    /// Les deux bouts se souviennent de l'ordre. Sans le second, les consignes etant collectives, le
    /// camarade envoye au van devrait a son tour y envoyer quelqu'un avant d'entrer ou que ce soit.
    /// Et un ordre que personne n'entend n'est pas donne.
    /// </summary>
    [Fact]
    public void Un_ordre_se_retient_aux_deux_bouts_et_ne_part_pas_dans_le_vide()
    {
        var run = new MissionRun(team: true, settings: Untimed);
        var bob = run.AgentOf("bob");
        var van = run.Spawn(ZoneKind.Extraction);
        double now = run.World.Time;

        Assert.False(Holds(bob.Survivor, "ordered(extraction)", now));
        Assert.True(bob.Runtime.SetFlag("ordered(extraction)", true, van), run.Trace());

        var soldier = run.Band.Survivors.Single(survivor => survivor.Mission?.Issuer == "bob");

        Assert.True(Holds(bob.Survivor, "ordered(extraction)", now));
        Assert.True(Holds(soldier, "ordered(extraction)", now));

        // Seul au monde, Bob n'a personne a qui parler.
        var alone = new WorldPlanRuntime(
            MissionRun.Lone("bob", new GridPosition(0, 0)), run.World, new AI.Navigation.PathFollower(),
            bob.Astar, band: [], cooperation: MissionRun.Config.UtilityAi.Cooperation);

        Assert.False(alone.SetFlag("ordered(extraction)", true, van));
    }

    /// <summary>
    /// Un rappel part a chacun de ceux qui l'entendent, pas au premier : c'est tout ce qui le separe
    /// d'un ordre. Chacun s'en souvient — sans quoi, la consigne etant collective, chaque rappele
    /// rappellerait les autres a son tour —, et il ne compte pas pour un ordre simple : « renvoyez un
    /// camarade » et « rappelez tout le monde » sont deux consignes, et l'une ne se contente pas de
    /// l'autre.
    /// </summary>
    [Fact]
    public void Un_rappel_part_a_chacun_de_ceux_qui_l_entendent()
    {
        var run = new MissionRun(team: true, settings: Untimed);
        var bob = run.AgentOf("bob");
        var van = run.Spawn(ZoneKind.Extraction);
        double now = run.World.Time;

        Assert.True(bob.Runtime.SetFlag("band_ordered(extraction)", true, van), run.Trace());

        var others = run.Band.Survivors.Where(survivor => survivor.Id != "bob").ToList();
        Assert.Equal(2, others.Count);

        foreach (var survivor in others)
        {
            Assert.Equal("bob", survivor.Mission?.Issuer);
            Assert.Equal(van, survivor.Mission!.Destination);
        }

        Assert.All(run.Band.Survivors, survivor =>
        {
            Assert.True(Holds(survivor, "band_ordered(extraction)", now), survivor.Name);
            Assert.False(Holds(survivor, "ordered(extraction)", now), survivor.Name);
        });

        Assert.Contains(run.Journal, line => line.Contains("rappelle tout le monde", StringComparison.Ordinal));

        // Un ordre simple, lui, ne part qu'a un seul — meme si deux l'entendent.
        var single = new MissionRun(team: true, settings: Untimed);
        Assert.True(single.AgentOf("bob").Runtime.SetFlag("ordered(extraction)", true, single.Spawn(ZoneKind.Extraction)));
        Assert.Single(single.Band.Survivors, survivor => survivor.Mission?.Issuer == "bob");
    }

    /// <summary>
    /// Rappeles au van, on y reste : un rappel general se tient une fois rempli, la ou l'ordre simple
    /// s'efface (<c>Sous_consigne_un_survivant_envoie_un_camarade_au_van_avant_d_entrer</c>). Sans
    /// cela, le premier arrive repartait fouiller avant le retour des autres, et la mission ne se
    /// concluait qu'au rappel du temps imparti.
    /// </summary>
    [Fact]
    public void Rappele_au_van_on_y_reste()
    {
        var run = new MissionRun(team: true, settings: Untimed);
        var bob = run.AgentOf("bob");
        var van = run.Spawn(ZoneKind.Extraction);

        Assert.True(bob.Runtime.SetFlag("band_ordered(extraction)", true, van), run.Trace());

        var soldier = run.Band.Survivors.First(survivor => survivor.Mission?.Issuer == "bob");
        Assert.True(soldier.Mission!.Holds);

        Assert.True(run.RunUntil(() => soldier.Mission!.ReachedFrom(soldier.Position), 1500), run.Trace());
        for (int tick = 0; tick < 100; tick++) run.Tick();

        Assert.Equal("bob", soldier.Mission?.Issuer);
        Assert.True(soldier.Mission!.ReachedFrom(soldier.Position), $"{soldier.Name} a quitte le van pour {soldier.Position}. {run.Trace()}");
        Assert.DoesNotContain(run.Journal, line => line.Contains($"{soldier.Name} a rempli", StringComparison.Ordinal));

        // Un ordre simple, lui, ne se tient pas.
        var single = new MissionRun(team: true, settings: Untimed);
        Assert.True(single.AgentOf("bob").Runtime.SetFlag("ordered(extraction)", true, single.Spawn(ZoneKind.Extraction)));
        Assert.False(single.Band.Survivors.Single(survivor => survivor.Mission?.Issuer == "bob").Mission!.Holds);
    }

    /// <summary>
    /// La consigne jouee de bout en bout : personne n'entre dans la pharmacie avant que quelqu'un ait
    /// ete envoye au van. Le planificateur remonte seul de la porte jusqu'a l'ordre. La consigne
    /// pre-ecrite vise « enter:building », mais le lieu de la mission de la carte livree est de la
    /// categorie « pharmacy » : c'est celle-la qu'il faut nommer ici, sans quoi la consigne est
    /// acceptee et n'a aucun effet.
    /// </summary>
    [Fact]
    public void Sous_consigne_un_survivant_envoie_un_camarade_au_van_avant_d_entrer()
    {
        var run = new MissionRun(
            team: true,
            settings: Untimed,
            instructions: ["require_before(ordered(extraction), enter:pharmacy)"]);

        Assert.True(run.RunUntil(() => run.Executed.Contains("OrderAlly"), 3000), run.Trace());
        Assert.Contains(run.Journal, line => line.Contains("ordonne a", StringComparison.Ordinal));

        // Le camarade envoye au van y va, et l'ordre s'eteint : il ne reste pas en faction.
        Assert.True(run.RunUntil(() => run.Band.Survivors.All(survivor => survivor.Mission is null), 1500), run.Trace());
        Assert.Contains(run.Journal, line => line.Contains("a rempli l'ordre de", StringComparison.Ordinal));
        Assert.DoesNotContain(run.Journal, line => line.Contains("renonce", StringComparison.Ordinal));
    }

    // --- Outils ----------------------------------------------------------------------------------

    /// <summary>Remplace un paquetage : l'objet entre au sac et vient en main, comme au depart du van.</summary>
    private static void Carry(Survivor survivor, string itemId)
    {
        Assert.True(survivor.AddToInventory(itemId), itemId);

        var inventory = survivor.Inventory;
        if (!inventory.EquipWeapon(itemId)) inventory.EquipTool(itemId);
    }

    private static MissionSettings Untimed => new() { TimeLimit = 0d };

    private static UtilityContext Context(Survivor survivor, double now = 0d)
        => new(survivor, now, MissionRun.Config.UtilityAi);

    private static bool Holds(Survivor survivor, string fact, double now = 0d)
        => SymbolicStateBuilder.Observe(Context(survivor, now), MissionRun.Config.Planner).State.Holds(fact);

    private static float Distance(GridPosition a, GridPosition b)
        => MathF.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));
}
