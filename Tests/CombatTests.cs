using Hordinary.AI.Planning;
using Hordinary.Agents;
using Hordinary.Config;
using Hordinary.World;

namespace Hordinary.Tests;

/// <summary>
/// Le combat simple du livrable 8 : « les survivants peuvent fuir, repousser un zombie, effectuer
/// une attaque simple, aider un allie ».
///
/// Fuir et aider existent depuis les livrables 4 et 7. Restent les deux actions offensives, et elles
/// sont ecrites en configuration comme toutes les autres — aucune classe de combat n'existe dans le
/// code. Ces tests verifient donc trois choses : que les deux actions sont bien des donnees, qu'elles
/// s'executent dans le monde, et qu'on ne peut pas les employer a distance.
/// </summary>
public class CombatTests
{
    private static PlanningDomain Domain { get; } = PlanningDomain.FromJson(DemoMap.ReadConfig("planner_domain.json"));

    private static GameConfig Config { get; } = GameConfig.FromJson(DemoMap.ReadConfig("game_config.json"));

    /// <summary>Un survivant, un zombie, et de quoi executer un plan — sans carte de demonstration.</summary>
    private static (MissionRun Mission, EntityManager Horde, Zombie Zombie) Brawl(int distance)
    {
        var mission = new MissionRun(team: false);

        var horde = new EntityManager(mission.World, Config.Zombies, Config.Knowledge, Config.Perception);
        mission.Band.Horde = horde;

        // Le zombie est place a la distance voulue du survivant, sur une case praticable.
        var here = mission.Alice.Position;
        var cell = new GridPosition(here.X + distance, here.Y);

        Assert.True(mission.Map.IsWalkable(cell), $"La case de combat {cell} n'est pas praticable.");

        var zombie = horde.Spawn("zombie-duel", cell);
        return (mission, horde, zombie);
    }

    // --- Les actions sont des donnees -----------------------------------------------------------

    [Fact]
    public void Les_deux_actions_offensives_sont_declarees_en_configuration()
    {
        var attack = Domain.FindAction("AttackZombie");
        var push = Domain.FindAction("PushBackZombie");

        Assert.NotNull(attack);
        Assert.NotNull(push);

        // Frapper coute plus cher et s'entend de plus loin que repousser : c'est ce qui laisse GBFS
        // preferer la poussee quand elle suffit.
        Assert.True(attack.Cost > push.Cost, "Attaquer devrait couter plus cher que repousser.");
        Assert.True(attack.NoiseLevel > push.NoiseLevel, "Attaquer devrait faire plus de bruit que repousser.");
    }

    /// <summary>
    /// Il n'existe aucune action pour rejoindre un zombie. C'est ce qui empeche un survivant de
    /// planifier une chasse : le combat ne peut etre qu'une reponse a ce qui est deja sur lui.
    /// </summary>
    [Fact]
    public void Aucune_action_ne_permet_d_aller_au_contact_d_un_zombie()
    {
        var reaching = Domain.Active
            .Where(action => action.ParsedEffects.Any(effect =>
                effect.Pattern.StartsWith("zombie_adjacent", StringComparison.OrdinalIgnoreCase)))
            .Select(action => action.Name)
            .ToList();

        Assert.True(reaching.Count == 0, $"Ces actions rendent un zombie adjacent : {string.Join(", ", reaching)}.");
    }

    // --- Execution dans le monde ----------------------------------------------------------------

    /// <summary>
    /// Un coup qui n'abat pas sa cible <b>echoue</b>, et c'est ce qui permet de tuer.
    ///
    /// L'action declare <c>threat_down(?threat)</c> : tant que le zombie tient debout, cet effet n'a
    /// pas eu lieu. Rendre vrai quand meme laissait le survivant repartir satisfait a cote d'une
    /// menace intacte — et aucune arme dont les degats ne couvraient pas les points de vie d'un
    /// zombie d'un seul coup ne pouvait le mettre a terre. L'echec, lui, devient une replanification,
    /// et la replanification reconduit au meme coup.
    /// </summary>
    [Fact]
    public void Un_coup_qui_n_abat_pas_le_zombie_echoue_et_le_suivant_l_acheve()
    {
        var (mission, horde, zombie) = Brawl(distance: 1);

        Assert.Equal(Config.Zombies.MaxHealth, zombie.Health);

        // Le verbe damage, tel que le plan l'appelle. Le coup porte — la sante descend — mais il
        // n'obtient pas l'effet annonce.
        Assert.False(mission.Agent.Runtime.Damage(zombie.Id, 10f));
        Assert.Equal(Config.Zombies.MaxHealth - 10f, zombie.Health);
        Assert.True(zombie.Alive);
        Assert.Single(horde.Zombies);

        Assert.True(mission.Agent.Runtime.Damage(zombie.Id, Config.Zombies.MaxHealth));

        Assert.False(zombie.Alive);
        Assert.Empty(horde.Zombies);

        // Il quitte aussi le monde : sans cela, la perception continuerait a voir un cadavre.
        Assert.Null(mission.World.GetEntity(zombie.Id));
    }

    /// <summary>
    /// Et le bout de la chaine : une arme de corps a corps finit par abattre un zombie, en autant de
    /// coups que ses degats l'exigent. C'est la demande du jeu — « les survivants peuvent tuer des
    /// zombies avec les armes de corps a corps » — et elle ne tient qu'a ce que le coup non fatal
    /// revienne.
    /// </summary>
    [Fact]
    public void Une_arme_de_corps_a_corps_finit_par_mettre_le_zombie_a_terre()
    {
        var (mission, horde, zombie) = Brawl(distance: 1);

        // La plus faible des armes de main : celle qui demande le plus de coups, donc celle qui
        // aurait laisse le zombie debout du temps ou un seul coup suffisait a clore l'objectif.
        var melee = DemoMap.Items.Items
            .Where(item => item.IsWeapon && !item.IsFirearm)
            .OrderBy(item => item.Damage)
            .First();

        Assert.True(mission.Alice.AddToInventory(melee.Id));
        Assert.True(mission.Alice.Inventory.EquipWeapon(melee.Id));

        int expected = (int)Math.Ceiling(Config.Zombies.MaxHealth / melee.Damage);
        int blows = 0;

        // Un montant nul veut dire « ce que je tiens » : c'est ainsi qu'AttackWithWeapon frappe.
        while (zombie.Alive && blows < expected + 1)
        {
            blows++;
            bool downed = mission.Agent.Runtime.Damage(zombie.Id, 0f);

            Assert.Equal(!zombie.Alive, downed);
        }

        Assert.False(zombie.Alive);
        Assert.Equal(expected, blows);
        Assert.Empty(horde.Zombies);
    }

    /// <summary>
    /// « Les survivants peuvent tuer des zombies avec les armes a feu » : une arme a feu chargee
    /// abat a une distance ou le bras ne porte pas.
    /// </summary>
    [Fact]
    public void Une_arme_a_feu_chargee_abat_un_zombie_hors_de_portee_de_bras()
    {
        var firearm = DemoMap.Items.Items
            .Where(item => item.IsFirearm)
            .OrderByDescending(item => item.Range)
            .First();

        int distance = MissionRun.Config.Survivor.MeleeRange + 1;
        Assert.True(firearm.Range >= distance, $"{firearm.Id} ne porte pas au-dela du bras.");

        var (mission, horde, zombie) = Brawl(distance);

        // Le chargeur est rempli par le ramassage : une arme qu'on trouve est une arme qui tire.
        Assert.True(mission.Alice.AddToInventory(firearm.Id));
        Assert.True(mission.Alice.Inventory.EquipWeapon(firearm.Id));

        Assert.True(firearm.Damage >= Config.Zombies.MaxHealth, $"{firearm.Id} n'abat plus d'un coup.");
        Assert.True(mission.Agent.Runtime.Damage(zombie.Id, 0f));

        Assert.False(zombie.Alive);
        Assert.Empty(horde.Zombies);

        // Le coup a coute une munition : sans cela le chargeur n'aurait jamais de fond.
        Assert.Equal(firearm.Ammo!.Value - 1, mission.Alice.Inventory.AmmoOf(firearm.Id));
    }

    /// <summary>
    /// Le barillet vide, la meme arme ne porte plus qu'a bout de bras : le zombie qui etait sous le
    /// feu ne l'est plus. C'est le pendant du test precedent, et ce qui donne un prix aux munitions.
    /// </summary>
    [Fact]
    public void Une_arme_a_feu_vide_ne_porte_plus_jusqu_au_zombie()
    {
        var firearm = DemoMap.Items.Items
            .Where(item => item.IsFirearm)
            .OrderByDescending(item => item.Range)
            .First();

        var (mission, _, zombie) = Brawl(MissionRun.Config.Survivor.MeleeRange + 1);

        Assert.True(mission.Alice.AddToInventory(firearm.Id));
        Assert.True(mission.Alice.Inventory.EquipWeapon(firearm.Id));

        // Le chargeur y passe, coup par coup : c'est la seule facon de le vider.
        for (int round = 0; round < firearm.Ammo!.Value; round++) mission.Alice.Inventory.RegisterStrike(firearm.Id);

        Assert.Equal(0, mission.Alice.Inventory.AmmoOf(firearm.Id));
        Assert.False(mission.Agent.Runtime.Damage(zombie.Id, 0f));
        Assert.Equal(Config.Zombies.MaxHealth, zombie.Health);
    }

    [Fact]
    public void On_ne_frappe_pas_un_zombie_hors_de_portee()
    {
        var (mission, _, zombie) = Brawl(distance: 4);

        Assert.False(mission.Agent.Runtime.Damage(zombie.Id, 10f));
        Assert.Equal(Config.Zombies.MaxHealth, zombie.Health);
    }

    [Fact]
    public void On_ne_frappe_pas_un_zombie_qui_n_existe_pas()
    {
        var (mission, _, _) = Brawl(distance: 1);

        Assert.False(mission.Agent.Runtime.Damage("zombie-fantome", 10f));
    }

    /// <summary>
    /// Repousser eloigne sans blesser : c'est la reponse d'un survivant qui veut partir. Le zombie
    /// perd aussi sa piste, sans quoi le recul ne servirait a rien.
    /// </summary>
    [Fact]
    public void Repousser_eloigne_le_zombie_sans_l_entamer()
    {
        var (mission, _, zombie) = Brawl(distance: 1);

        zombie.TargetId = mission.Alice.Id;
        var before = zombie.Position;

        Assert.True(mission.Agent.Runtime.SetFlag($"pushed_back({zombie.Id})", true, null));

        Assert.Equal(Config.Zombies.MaxHealth, zombie.Health);
        Assert.True(zombie.Alive);
        Assert.Null(zombie.TargetId);

        Assert.True(
            zombie.Position.ChebyshevDistanceTo(mission.Alice.Position)
                > before.ChebyshevDistanceTo(mission.Alice.Position),
            $"Le zombie est passe de {before} a {zombie.Position} sans s'eloigner.");
    }

    [Fact]
    public void Sans_horde_une_action_offensive_echoue_au_lieu_de_frapper_dans_le_vide()
    {
        var mission = new MissionRun(team: false);
        Assert.Null(mission.Band.Horde);

        Assert.False(mission.Agent.Runtime.Damage("zombie-1", 10f));
    }

    // --- Le planificateur sait s'en servir ------------------------------------------------------

    /// <summary>
    /// Le critere qui compte : un zombie au contact rend le combat planifiable, et GBFS en tire un
    /// plan. Sans le fait <c>zombie_adjacent</c>, les deux actions resteraient inaccessibles.
    /// </summary>
    [Fact]
    public void Un_zombie_au_contact_rend_le_combat_planifiable()
    {
        var (mission, _, zombie) = Brawl(distance: 1);

        // Alice doit d'abord le voir : le planificateur ne raisonne que sur sa memoire.
        mission.RunUntil(
            () => mission.Alice.Knowledge.KnownEntities.ContainsKey(zombie.Id),
            maxTicks: 30);

        Assert.True(
            mission.Alice.Knowledge.KnownEntities.ContainsKey(zombie.Id),
            "Alice n'a pas percu le zombie au contact.");

        var context = mission.Agent.Context!;
        var goal = Domain.FindGoal("FightZombie")!;
        var problem = SymbolicStateBuilder.Build(context, Domain, goal, Config.Planner);

        Assert.Contains(problem.Initial.Facts, fact =>
            fact.StartsWith("zombie_adjacent", StringComparison.OrdinalIgnoreCase));

        var result = mission.Agent.Planner.Solve(problem, mission.World.Time);

        Assert.True(result.Found, $"Aucun plan de combat : {result.Reason}");
        Assert.Contains(
            result.Plan!.Actions,
            action => action.Schema.Name is "AttackZombie" or "PushBackZombie");
    }

    /// <summary>
    /// Et le pendant : sans personne au contact, le combat n'est pas planifiable du tout. Un
    /// survivant ne part pas chercher la bagarre a l'autre bout de la rue.
    /// </summary>
    [Fact]
    public void Un_zombie_lointain_ne_rend_pas_le_combat_planifiable()
    {
        var (mission, _, zombie) = Brawl(distance: 4);

        mission.RunUntil(
            () => mission.Alice.Knowledge.KnownEntities.ContainsKey(zombie.Id),
            maxTicks: 30);

        var context = mission.Agent.Context!;
        var goal = Domain.FindGoal("FightZombie")!;
        var problem = SymbolicStateBuilder.Build(context, Domain, goal, Config.Planner);

        Assert.DoesNotContain(problem.Initial.Facts, fact =>
            fact.StartsWith("zombie_adjacent", StringComparison.OrdinalIgnoreCase));

        Assert.False(mission.Agent.Planner.Solve(problem, mission.World.Time).Found);
    }

    /// <summary>
    /// Ce que le survivant tient decide de ce qu'il fait, et c'est GBFS qui en decide — pas une
    /// branche de code qui regarderait dans son sac.
    ///
    /// Les trois reponses au contact aboutissent toutes a <c>threat_handled</c> : un etat qui
    /// satisfait le but recoit <c>h = 0</c>, et elles se valent donc pour l'heuristique. Ce qui les
    /// departage est le cout, et lui seul — d'ou le second terme de la priorite de
    /// <see cref="Gbfs"/>. Sans lui, c'etait l'ordre de declaration dans
    /// <c>config/planner_domain.json</c> qui tranchait : le recul l'emportait toujours, et une
    /// machette en main ne changeait rien a rien.
    /// </summary>
    [Fact]
    public void Un_survivant_arme_frappe_la_ou_un_survivant_a_mains_nues_repousse()
    {
        var melee = DemoMap.Items.Items.First(item => item.IsWeapon && !item.IsFirearm);

        Assert.Equal("PushBackZombie", FirstBlow(null));
        Assert.Equal("AttackWithWeapon", FirstBlow(melee.Id));
    }

    /// <summary>
    /// Le nom de la premiere action du plan de combat, l'arme donnee en main ou les mains vides.
    /// L'arme est mise en main et non posee dans le sac : c'est ce que fait le depart du van
    /// (<c>CampLoadout.HandOver</c>), et une arme au fond du sac demanderait d'abord de la sortir.
    /// </summary>
    private static string FirstBlow(string? weaponId)
    {
        var (mission, _, zombie) = Brawl(distance: 1);

        if (weaponId is not null)
        {
            Assert.True(mission.Alice.AddToInventory(weaponId));
            Assert.True(mission.Alice.Inventory.EquipWeapon(weaponId));
        }

        mission.RunUntil(
            () => mission.Alice.Knowledge.KnownEntities.ContainsKey(zombie.Id),
            maxTicks: 30);

        var problem = SymbolicStateBuilder.Build(
            mission.Agent.Context!, Domain, Domain.FindGoal("FightZombie")!, Config.Planner);

        var result = mission.Agent.Planner.Solve(problem, mission.World.Time);

        Assert.True(result.Found, $"Aucun plan de combat : {result.Reason}");
        return result.Plan!.Actions[0].Schema.Name;
    }
}
