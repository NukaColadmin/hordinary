using Hordinary.AI.Navigation;
using Hordinary.Agents;
using Hordinary.Commands;
using Hordinary.World;

namespace Hordinary.Tests;

/// <summary>
/// Les commandes du joueur (livrable 8, « Commandes disponibles ») sur la carte livree.
///
/// Deux familles y sont verifiees separement, parce qu'elles n'ont pas le meme statut. Un ordre est
/// une proposition : il pose un objectif que la Utility AI met en concurrence, et il peut echouer a
/// etre donne — on n'envoie personne a un endroit qu'il ignore. Une consigne permanente, elle, ne se
/// negocie pas : elle ampute le domaine de planification et la carte de navigation.
/// </summary>
public class CommandTests
{
    private static (MissionRun Mission, CommandManager Commands) Setup(bool team = true)
    {
        var mission = new MissionRun(team: team);
        return (mission, new CommandManager(mission.Band, mission.Map, MissionRun.Config.UtilityAi));
    }

    // --- Selection --------------------------------------------------------------------------

    [Fact]
    public void Le_joueur_selectionne_un_survivant_puis_le_groupe()
    {
        var (mission, commands) = Setup();

        Assert.Equal(mission.Alice, commands.Selected?.Survivor);

        var bob = mission.Band.Agents[1].Survivor;
        var result = commands.Execute(PlayerCommand.Select(bob.Id));

        Assert.True(result.Delivered);
        Assert.Equal(bob, commands.Selected?.Survivor);
        Assert.False(commands.GroupSelected);

        commands.Execute(PlayerCommand.SelectGroup());
        Assert.True(commands.GroupSelected);
    }

    [Fact]
    public void Selectionner_un_inconnu_est_refuse_et_ne_change_rien()
    {
        var (mission, commands) = Setup();
        var before = commands.Selected;

        var result = commands.Execute(PlayerCommand.Select("dominique"));

        Assert.False(result.Delivered);
        Assert.Contains(result.Refused, line => line.Contains("dominique", StringComparison.Ordinal));
        Assert.Same(before, commands.Selected);
    }

    /// <summary>
    /// Cliquer a cote de tout le monde ne selectionne personne — c'est un etat, et l'interface le
    /// lit : <c>Selected</c> nul ferme le panneau lateral et eteint l'anneau au sol.
    /// </summary>
    [Fact]
    public void Cliquer_a_cote_ne_laisse_plus_personne_selectionne()
    {
        var (mission, commands) = Setup();

        var bob = mission.Band.Agents[1].Survivor;
        commands.Execute(PlayerCommand.Select(bob.Id));

        bool announced = false;
        commands.SelectionCleared += () => announced = true;

        Assert.True(commands.Deselect());
        Assert.True(announced);

        Assert.Null(commands.Selected);
        Assert.False(commands.HasSelection);

        // Plus de destinataire : un ordre donne dans le vide est refuse, et le dit.
        var order = commands.Execute(PlayerCommand.ReachExtraction(toAll: false));
        Assert.False(order.Delivered);

        // Deselectionner deux fois ne s'annonce qu'une : rien n'a change la seconde fois.
        announced = false;
        Assert.False(commands.Deselect());
        Assert.False(announced);

        // Et Tab reprend ou l'on en etait — apres Bob, celui d'apres — plutot que de repartir du
        // premier du groupe : le rang survit a la deselection, seul le fait de regarder disparait.
        Assert.Equal(1, commands.SelectedIndex);
        Assert.Same(mission.Band.Agents[2], commands.SelectNext());
        Assert.True(commands.HasSelection);
    }

    // --- Ordres -----------------------------------------------------------------------------

    [Fact]
    public void Un_ordre_pose_un_objectif_negociable_et_non_une_destination()
    {
        var (mission, commands) = Setup(team: false);

        var result = commands.Execute(PlayerCommand.FetchObjective());

        Assert.True(result.Delivered, result.ToString());
        Assert.NotNull(mission.Alice.Mission);

        // C'est bien une priorite, pas un ordre de marche : le score la met en concurrence.
        Assert.Equal(MissionRun.Config.UtilityAi.PlayerOrderPriority, mission.Alice.Mission!.Priority);

        // Et elle vise la pharmacie telle qu'Alice la connait, pas la carte reelle.
        var pharmacy = mission.Alice.Knowledge.GetLocation(ZoneKind.Pharmacy);
        Assert.NotNull(pharmacy);
        Assert.Equal(pharmacy.Value, mission.Alice.Mission.Destination);
    }

    [Fact]
    public void Le_groupe_entier_recoit_l_ordre_quand_il_est_adresse_a_tous()
    {
        var (mission, commands) = Setup();

        var result = commands.Execute(PlayerCommand.ReachExtraction(toAll: true));

        Assert.Equal(mission.Band.Agents.Count, result.Accepted.Count);
        Assert.All(mission.Band.Survivors, survivor => Assert.NotNull(survivor.Mission));
    }

    /// <summary>
    /// Le refus qui compte : un ordre est resolu dans la memoire de celui qui le recoit, et il
    /// echoue quand elle ne sait pas repondre. Un survivant seul ne sait pas ou est « le groupe » —
    /// il n'a vu personne. C'est la perception partielle rendue visible depuis l'interface, et non
    /// un survivant qui desobeit.
    /// </summary>
    [Fact]
    public void Un_ordre_que_la_memoire_ne_sait_pas_resoudre_est_refuse()
    {
        var (mission, commands) = Setup(team: false);

        var result = commands.Execute(PlayerCommand.Regroup());

        Assert.False(result.Delivered, result.ToString());
        Assert.Contains(result.Refused, line => line.Contains("groupe", StringComparison.OrdinalIgnoreCase));
        Assert.Null(mission.Alice.Mission);
    }

    /// <summary>Le meme ordre aboutit des que quelqu'un est la pour etre rejoint.</summary>
    [Fact]
    public void Le_meme_ordre_aboutit_quand_le_groupe_existe()
    {
        var (mission, commands) = Setup();
        mission.RunUntil(() => mission.Agent.Context?.Allies.Count > 0, maxTicks: 40);

        var result = commands.Execute(PlayerCommand.Regroup(toAll: false));

        Assert.True(result.Delivered, result.ToString());
        Assert.NotNull(mission.Alice.Mission);
    }

    [Fact]
    public void Explorer_une_zone_designee_vise_une_case_praticable_de_cette_zone()
    {
        var (mission, commands) = Setup(team: false);

        var hangar = mission.Map.Zones.First(zone => zone.Kind == ZoneKind.Building);
        var result = commands.Execute(PlayerCommand.Explore(hangar));

        Assert.True(result.Delivered, result.ToString());
        Assert.NotNull(mission.Alice.Mission?.Destination);
        Assert.True(
            hangar.Contains(mission.Alice.Mission!.Destination!.Value),
            $"La destination {mission.Alice.Mission.Destination} est hors de {hangar.Name}.");
    }

    // --- Consignes permanentes ----------------------------------------------------------------

    [Fact]
    public void Interdire_le_bruit_retire_les_actions_bruyantes_du_repertoire()
    {
        var (mission, commands) = Setup(team: false);
        var orders = mission.Alice.Orders;

        Assert.True(orders.NoiseAllowed);
        Assert.True(orders.AllowsNoise(10f));

        commands.Execute(PlayerCommand.ForbidNoise());

        Assert.False(orders.NoiseAllowed);

        // Le bareme de la section 18.1 : marche 1 et porte 2 passent, course 3, porte forcee 6,
        // vitre 8 et distraction 10 ne passent plus.
        Assert.True(orders.AllowsNoise(1f));
        Assert.True(orders.AllowsNoise(2f));
        Assert.False(orders.AllowsNoise(3f));
        Assert.False(orders.AllowsNoise(8f));

        commands.Execute(PlayerCommand.AllowNoise());
        Assert.True(orders.AllowsNoise(10f));
    }

    /// <summary>
    /// Le vrai effet de la consigne : le planificateur ne voit plus les actions bruyantes. Sans cela,
    /// « interdire le bruit » ne serait qu'une ligne de HUD.
    /// </summary>
    [Fact]
    public void Le_planificateur_ignore_les_actions_bruyantes_quand_elles_sont_interdites()
    {
        var mission = new MissionRun(team: false);
        var commands = new CommandManager(mission.Band, mission.Map, MissionRun.Config.UtilityAi);

        mission.RunUntil(() => mission.Agent.Context is not null, maxTicks: 20);

        var context = mission.Agent.Context!;
        var domain = mission.Agent.Domain;
        var goal = domain.FindGoal("PickUpObjective")!;

        var before = Hordinary.AI.Planning.SymbolicStateBuilder
            .Build(context, domain, goal, MissionRun.Config.Planner)
            .Actions;

        commands.Execute(PlayerCommand.ForbidNoise());

        var after = Hordinary.AI.Planning.SymbolicStateBuilder
            .Build(context, domain, goal, MissionRun.Config.Planner)
            .Actions;

        Assert.All(after, action => Assert.True(
            action.NoiseLevel <= StandingOrders.QuietThreshold,
            $"{action.Name} fait {action.NoiseLevel} de bruit malgre l'interdiction."));

        Assert.True(
            after.Count < before.Count,
            "L'interdiction n'a retire aucune action : la consigne serait purement decorative.");
    }

    [Fact]
    public void Une_zone_interdite_devient_infranchissable_pour_A_etoile()
    {
        var (mission, commands) = Setup(team: false);

        var alley = mission.Map.GetZone(ZoneKind.Alley)!;
        var navigation = mission.Agent.Navigation;

        // La ruelle est praticable tant que le joueur n'a rien dit.
        var inside = mission.Map.FindWalkableIn(alley)!.Value;
        Assert.True(navigation.IsTraversable(inside));

        commands.Execute(PlayerCommand.ForbidZone(alley));

        Assert.False(navigation.IsTraversable(inside));
        Assert.Equal(float.PositiveInfinity, navigation.GetEnterCost(inside));

        // Une interdiction est un mur, pas un cout : aucun chemin ne doit plus la traverser.
        var route = new AStar(navigation).FindPath(
            mission.Spawn(ZoneKind.Start), mission.Spawn(ZoneKind.Extraction));

        if (route.Found) Assert.DoesNotContain(route.Cells, alley.Contains);

        commands.Execute(PlayerCommand.AllowZone(alley));
        Assert.True(navigation.IsTraversable(inside));
    }

    [Fact]
    public void Une_consigne_ne_vaut_que_pour_ceux_a_qui_elle_est_adressee()
    {
        var (mission, commands) = Setup();

        var alley = mission.Map.GetZone(ZoneKind.Alley)!;
        commands.Execute(PlayerCommand.ForbidZone(alley, toAll: false));

        Assert.NotEmpty(mission.Alice.Orders.ForbiddenZones);
        Assert.All(
            mission.Band.Survivors.Where(survivor => survivor != mission.Alice),
            survivor => Assert.Empty(survivor.Orders.ForbiddenZones));
    }
}
