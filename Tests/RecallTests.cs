using Hordinary.Agents;
using Hordinary.Game;
using Hordinary.World;

namespace Hordinary.Tests;

/// <summary>
/// Le rappel au camp : ce qui se passe une fois la mission remplie.
///
/// Trois affirmations, et ce sont les trois que le livrable demande :
///
/// <list type="number">
/// <item>Remplir la mission n'arrete pas le jeu. La caisse rentre, et la partie continue.</item>
/// <item>Celui qui l'a rapportee <b>repart chercher les autres</b> pour leur dire de rentrer. Le
/// message se transmet de vive voix, a portee de communication : un allie a l'autre bout de la carte
/// n'apprend rien, parce qu'il n'a aucun moyen de l'apprendre.</item>
/// <item>Un survivant rentre au camp <b>disparait</b> du terrain. Il n'est pas mort : c'est le meme
/// qui repartira, avec la sante et la motivation qu'il rapporte.</item>
/// </list>
/// </summary>
public class RecallTests
{
    /// <summary>Une case de la pharmacie : assez loin du camp pour etre hors de portee de voix.</summary>
    private static readonly GridPosition FarAway = MissionRun.MedicineCell;

    // --- Declenchement ---------------------------------------------------------------------------

    /// <summary>
    /// Ramener la caisse au camp remplit la mission et arme le rappel — sans terminer la partie. Le
    /// porteur devient le messager : c'est lui qui a la nouvelle, c'est lui qui la porte.
    /// </summary>
    [Fact]
    public void Ramener_la_caisse_au_camp_arme_le_rappel_sans_finir_la_partie()
    {
        var mission = new MissionRun(team: true);

        // Le groupe part du camp : y ramasser la caisse, c'est l'y avoir rapportee.
        mission.Alice.AddToInventory(ItemKind.Medicine);
        mission.Tick();

        Assert.True(mission.Tracker.Done, $"La mission n'a pas ete reconnue remplie. {mission.Trace()}");
        Assert.Equal(mission.Alice, mission.Tracker.Courier);

        Assert.True(mission.Recall.Started);
        Assert.Equal(mission.Alice, mission.Recall.Messenger!.Survivor);

        // Et pourtant rien n'est fini : il reste deux personnes dehors.
        Assert.False(mission.Tracker.IsOver);
        Assert.Equal(MissionOutcome.Running, mission.Tracker.Outcome);
    }

    // --- Transmission ----------------------------------------------------------------------------

    /// <summary>
    /// Le message est celui de la specification, mot pour mot, et il ne s'entend qu'a portee de voix.
    /// C'est le meme canal que le partage de connaissances du livrable 7 : le rappel n'a aucun
    /// privilege que la parole n'ait deja.
    /// </summary>
    [Fact]
    public void Le_message_ne_se_transmet_qu_a_portee_de_voix()
    {
        var mission = new MissionRun(team: true);

        var alice = mission.AgentOf("alice");
        var bob = mission.AgentOf("bob");
        var claire = mission.AgentOf("claire");

        // Bob reste au camp avec Alice ; Claire est partie a la pharmacie.
        claire.Survivor.Position = FarAway;

        Assert.True(mission.Recall.Begin(alice));
        mission.Recall.Advance(mission.World.Time);

        Assert.True(bob.Survivor.Recalled);
        Assert.Equal(MissionRecall.Message, bob.Survivor.RecallMessage);

        Assert.False(claire.Survivor.Recalled);
        Assert.DoesNotContain("claire", mission.Recall.Told);
    }

    /// <summary>
    /// « Aller chercher les autres » : le messager previent d'abord ceux qui sont a portee de voix,
    /// puis se met en route vers l'allie encore ignorant le plus proche, la ou il croit qu'il est.
    ///
    /// C'est un ordre comme un autre — la Utility AI le met en concurrence avec le reste, et un
    /// messager poursuivi fuira d'abord.
    /// </summary>
    [Fact]
    public void Le_messager_part_chercher_celui_qui_ignore_encore()
    {
        var mission = new MissionRun(team: true);

        var alice = mission.AgentOf("alice");
        var bob = mission.AgentOf("bob");
        var claire = mission.AgentOf("claire");

        // Claire est restee au camp, a portee de voix ; Bob est parti a la pharmacie. Alice sait ou
        // il est pour l'avoir vu partir — sinon elle n'aurait aucune raison d'aller par la.
        bob.Survivor.Position = FarAway;

        alice.Survivor.Knowledge.RememberEntity(
            "bob", EntityKind.Survivor, FarAway, mission.World.Time);

        mission.Recall.Begin(alice);
        mission.Recall.Advance(mission.World.Time);

        // Celle qui etait la est prevenue sur place ; celui qui est loin ne sait encore rien.
        Assert.True(claire.Survivor.Recalled);
        Assert.False(bob.Survivor.Recalled);

        // Et le messager part le chercher.
        var order = alice.Survivor.Mission;

        Assert.NotNull(order);
        Assert.Contains("Bob", order.Label, StringComparison.Ordinal);
        Assert.Equal(FarAway, order.Destination);
        Assert.Equal(MissionRun.Config.Mission.RecallPriority, order.Priority);
    }

    /// <summary>
    /// Le messager part <b>du</b> camp : sans precaution, il « rentrerait » a la seconde ou il decide
    /// d'aller chercher les autres, et personne ne serait jamais prevenu.
    /// </summary>
    [Fact]
    public void Le_messager_ne_rentre_pas_tant_qu_il_reste_quelqu_un_a_prevenir()
    {
        var mission = new MissionRun(team: true);

        var alice = mission.AgentOf("alice");
        var bob = mission.AgentOf("bob");
        var claire = mission.AgentOf("claire");

        bob.Survivor.Position = FarAway;
        claire.Survivor.Position = FarAway;

        mission.Recall.Begin(alice);

        for (int pass = 0; pass < 10; pass++) mission.Recall.Advance(mission.World.Time);

        Assert.False(alice.Survivor.Returned, "Le messager est rentre sans avoir prevenu personne.");
        Assert.Contains(alice, mission.Band.Standing);
    }

    // --- Retour ----------------------------------------------------------------------------------

    /// <summary>
    /// Rentrer, c'est disparaitre du terrain — pas mourir. Le survivant quitte le monde reel (les
    /// zombies cessent de le poursuivre, les allies cessent de le voir) mais reste vivant, avec ses
    /// jauges telles qu'il les rapporte.
    /// </summary>
    [Fact]
    public void Un_agent_rentre_au_camp_quitte_le_terrain_sans_mourir()
    {
        var mission = new MissionRun(team: true);
        var alice = mission.AgentOf("alice");

        alice.Survivor.TakeDamage(20f);
        float health = alice.Survivor.Health;

        Assert.True(mission.Recall.Abandon(alice, "rappele pour le test"));
        mission.Recall.Advance(mission.World.Time);

        Assert.True(alice.Survivor.Returned);
        Assert.True(alice.Survivor.Alive);
        Assert.Equal(health, alice.Survivor.Health);

        // Il n'est plus sur le terrain, et le monde ne le porte plus.
        Assert.DoesNotContain(alice, mission.Band.Standing);
        Assert.Contains(alice, mission.Band.Home);
        Assert.Null(mission.World.GetEntity(alice.Survivor.Id));
    }

    /// <summary>
    /// L'annonce du retour est celle que le joueur lit. Elle est produite par le coeur, pas par la
    /// couche d'affichage : le harnais de test l'entend donc exactement comme le HUD.
    /// </summary>
    [Fact]
    public void Rentrer_au_camp_s_annonce()
    {
        var mission = new MissionRun(team: true);
        var alice = mission.AgentOf("alice");

        var said = new List<string>();
        mission.Recall.Said += said.Add;

        mission.Recall.Abandon(alice, "rappele pour le test");
        mission.Recall.Advance(mission.World.Time);

        Assert.Contains(said, line => line.Contains("Alice", StringComparison.Ordinal)
                                   && line.Contains("rentre au camp", StringComparison.Ordinal));
    }

    /// <summary>
    /// Une partie ne se conclut qu'une fois le terrain vide. Tout le monde rentre avec la caisse au
    /// camp : c'est une victoire, prononcee au dernier depart et pas au premier.
    /// </summary>
    [Fact]
    public void La_partie_se_conclut_quand_le_terrain_est_vide()
    {
        var mission = new MissionRun(team: true);

        // Alice rentre la caisse au camp : la mission est remplie, le rappel demarre.
        mission.Alice.AddToInventory(ItemKind.Medicine);
        mission.Tracker.Evaluate(mission.World.Time);

        Assert.True(mission.Tracker.Done);
        Assert.True(mission.Recall.Started);
        Assert.False(mission.Tracker.IsOver);

        // Les deux autres sont a l'autre bout de la carte : ils ne seront pas prevenus sur place, et
        // on peut donc les faire rentrer un par un.
        foreach (var agent in mission.Band.Standing.Where(agent => agent.Survivor != mission.Alice))
            agent.Survivor.Position = FarAway;

        foreach (var agent in mission.Band.Standing.ToList())
        {
            Assert.False(mission.Tracker.IsOver, $"La partie s'est conclue avant le retour de tous.");

            agent.Survivor.Position = mission.Spawn(mission.Definition.Camp);
            mission.Recall.Abandon(agent, "rappele pour le test");
            mission.Recall.Advance(mission.World.Time);
            mission.Tracker.Evaluate(mission.World.Time);
        }

        Assert.True(mission.Tracker.IsOver);
        Assert.Equal(MissionOutcome.Victory, mission.Tracker.Outcome);
        Assert.Contains("camp", mission.Tracker.Verdict.Reason, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Rentrer les mains vides n'est pas une victoire. Le groupe est sauf, la mission a echoue : le
    /// verdict doit dire la seconde chose, pas la premiere.
    /// </summary>
    [Fact]
    public void Rentrer_sans_avoir_rempli_la_mission_est_une_defaite()
    {
        var mission = new MissionRun(team: true);

        foreach (var agent in mission.Band.Standing.ToList())
        {
            mission.Recall.Abandon(agent, "demi-tour");
            mission.Recall.Advance(mission.World.Time);
        }

        var verdict = mission.Tracker.Evaluate(mission.World.Time);

        Assert.Equal(MissionOutcome.Defeat, verdict.Outcome);
        Assert.Contains("mains vides", verdict.Reason, StringComparison.OrdinalIgnoreCase);
    }
}
