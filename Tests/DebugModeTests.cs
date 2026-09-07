using Hordinary.AI.Utility;
using Hordinary.Agents;
using Hordinary.Config;

namespace Hordinary.Tests;

/// <summary>
/// L'interrupteur de panique du mode debug (livrable 8) et ce que le mode debug doit pouvoir
/// montrer : les plans rejetes et la raison d'un echec de planification.
///
/// L'interrupteur n'est pas un filtre d'affichage : il neutralise la panique <b>a la source</b>. La
/// difference compte, parce qu'une panique cachee mais toujours calculee continuerait a faire fuir
/// les survivants — et le mode debug servirait a masquer le probleme au lieu de l'isoler.
/// </summary>
public class DebugModeTests
{
    private static Survivor Alone(bool panicEnabled)
    {
        var mission = new MissionRun(team: false);
        mission.Band.PanicEnabled = panicEnabled;
        return mission.Alice;
    }

    // --- Panique et stress ----------------------------------------------------------------------

    [Fact]
    public void Par_defaut_la_panique_monte_avec_le_danger_percu()
    {
        var alice = Alone(panicEnabled: true);

        alice.UpdatePanic(1f, 1f);

        Assert.True(alice.Panic > 0f, "La panique devrait monter face a un danger maximal.");
    }

    [Fact]
    public void Desactivee_la_panique_reste_a_zero_quel_que_soit_le_danger()
    {
        var alice = Alone(panicEnabled: false);

        for (int i = 0; i < 20; i++) alice.UpdatePanic(1f, 1f);

        Assert.Equal(0f, alice.Panic);
    }

    [Fact]
    public void Desactivee_une_blessure_ne_fait_plus_paniquer()
    {
        var alice = Alone(panicEnabled: false);

        float before = alice.Health;
        alice.TakeDamage(20f);

        // La blessure, elle, reste bien reelle : c'est la panique qui est neutralisee, pas les degats.
        Assert.Equal(before - 20f, alice.Health);
        Assert.Equal(0f, alice.Panic);
    }

    /// <summary>
    /// La consigne de configuration s'applique au groupe entier, et fait retomber la panique deja
    /// accumulee. Le second point n'est pas theorique : un survivant enrole puis mis en situation
    /// avant que le reglage ne soit lu garderait sinon une peur que plus rien ne peut faire baisser.
    /// </summary>
    [Fact]
    public void Le_reglage_s_applique_a_tout_le_groupe_et_efface_la_panique_accumulee()
    {
        var mission = new MissionRun(team: true);

        foreach (var survivor in mission.Band.Survivors) survivor.UpdatePanic(1f, 2f);
        Assert.All(mission.Band.Survivors, survivor => Assert.True(survivor.Panic > 0f));

        mission.Band.PanicEnabled = false;

        Assert.All(mission.Band.Survivors, survivor => Assert.Equal(0f, survivor.Panic));
        Assert.All(mission.Band.Survivors, survivor => Assert.False(survivor.PanicEnabled));
    }

    /// <summary>
    /// L'effet qui justifie l'interrupteur : la panique n'entre plus dans les scores. Sans elle,
    /// « threat_pressure » se reduit au danger reellement percu, et un survivant cesse de fuir des
    /// que le zombie sort de sa vue au lieu de courir encore dix secondes.
    /// </summary>
    [Fact]
    public void Desactivee_la_panique_n_influence_plus_les_scores()
    {
        var mission = new MissionRun(team: false);
        var alice = mission.Alice;

        alice.UpdatePanic(1f, 3f);
        var stressed = new UtilityContext(alice, mission.World.Time, MissionRun.Config.UtilityAi);

        Assert.Equal(alice.Panic, UtilityInputs.Read("panic", stressed));
        Assert.True(stressed.ThreatPressure >= alice.Panic);

        mission.Band.PanicEnabled = false;
        var calm = new UtilityContext(alice, mission.World.Time, MissionRun.Config.UtilityAi);

        Assert.Equal(0f, UtilityInputs.Read("panic", calm));
        Assert.Equal(calm.PerceivedDanger, calm.ThreatPressure);
    }

    /// <summary>
    /// L'interrupteur se regle dans le fichier et nulle part ailleurs : c'est donc la lecture du
    /// fichier qui est verifiee ici, dans les deux sens.
    ///
    /// La valeur <b>livree</b>, elle, n'est deliberement pas testee. Un reglage d'observation est
    /// fait pour etre change au gre de ce qu'on cherche a voir, et un test qui le figerait
    /// deviendrait rouge chaque fois que quelqu'un s'en sert pour son usage normal.
    /// </summary>
    [Fact]
    public void L_etat_de_depart_de_l_interrupteur_vient_de_la_configuration()
    {
        // Le fichier livre reste lisible, quel que soit le regime qu'il decrit.
        var delivered = GameConfig.FromJson(DemoMap.ReadConfig("game_config.json"));
        Assert.NotNull(delivered.Debug);

        Assert.False(GameConfig.FromJson("""{ "debug": { "panic_enabled": false } }""").Debug.PanicEnabled);
        Assert.True(GameConfig.FromJson("""{ "debug": { "panic_enabled": true } }""").Debug.PanicEnabled);

        // Sans section debug, la panique joue : le mode debug est un choix explicite, pas un defaut.
        Assert.True(GameConfig.FromJson("{}").Debug.PanicEnabled);
    }

    // --- L'interrupteur general ------------------------------------------------------------------

    /// <summary>
    /// <c>debug.enabled</c> coupe tout : console, touches d'observation, panneaux de texte, overlays.
    /// C'est ce qui permet de livrer le jeu sans avoir a se souvenir de la liste des reglages a
    /// eteindre un par un — et le seul endroit du programme ou cette regle s'applique, donc le seul
    /// ou l'on puisse l'oublier.
    /// </summary>
    [Fact]
    public void L_interrupteur_general_coupe_tout_le_reste()
    {
        var off = GameConfig.FromJson("""
            {
              "debug": {
                "enabled": false,
                "console_output": true,
                "commands": true,
                "hud_text": true,
                "show_navigation": true,
                "show_mental_map": true,
                "show_noise": true
              }
            }
            """).Debug;

        Assert.False(off.ShowsConsole);
        Assert.False(off.AcceptsCommands);
        Assert.False(off.ShowsText);
        Assert.False(off.ShowsOverlay(off.ShowNavigation));
        Assert.False(off.ShowsOverlay(off.ShowMentalMap));
        Assert.False(off.ShowsOverlay(off.ShowNoise));

        // Les reglages bruts, eux, sont conserves tels que le fichier les decrit : rallumer
        // l'interrupteur general doit rendre exactement le regime qu'on avait ecrit.
        Assert.True(off.ConsoleOutput);
        Assert.True(off.Commands);
        Assert.True(off.HudText);
    }

    /// <summary>Chaque volet se coupe aussi separement, l'interrupteur general restant allume.</summary>
    [Fact]
    public void Chaque_volet_du_mode_debug_se_coupe_separement()
    {
        var config = GameConfig.FromJson("""
            { "debug": { "enabled": true, "console_output": false, "commands": true, "hud_text": false } }
            """).Debug;

        Assert.False(config.ShowsConsole);
        Assert.True(config.AcceptsCommands);
        Assert.False(config.ShowsText);
    }

    /// <summary>
    /// Sans section <c>debug</c>, le mode debug est entierement allume : c'est le regime de
    /// developpement, et l'eteindre est un choix explicite du fichier livre.
    /// </summary>
    [Fact]
    public void Sans_section_debug_tout_est_allume_sauf_les_overlays()
    {
        var config = GameConfig.FromJson("{}").Debug;

        Assert.True(config.ShowsConsole);
        Assert.True(config.AcceptsCommands);
        Assert.True(config.ShowsText);

        // Les overlays, eux, restent eteints : ils masquent la carte, et on les allume a la demande.
        Assert.False(config.ShowsOverlay(config.ShowNavigation));
    }

    // --- Ce que le mode debug doit montrer ------------------------------------------------------

    /// <summary>
    /// « Les plans rejetes » et « la raison d'un echec de planification » (livrable 8).
    ///
    /// Un objectif que le domaine ne sait pas traduire ne produit aucun plan. Le planificateur se
    /// rabat alors sur l'exploration — et c'est precisement ce repli qui a besoin d'etre explique :
    /// sans la trace du rejet, un survivant qui part explorer alors qu'on lui demandait autre chose
    /// serait incomprehensible.
    /// </summary>
    [Fact]
    public void Un_objectif_sans_plan_laisse_une_trace_expliquant_le_repli()
    {
        var mission = new MissionRun(team: false);
        mission.RunUntil(() => mission.Agent.Context is not null, maxTicks: 20);

        var planner = mission.Agent.Planner;
        int before = planner.Rejected.Count;

        var result = planner.PlanFor("ObjectifQuiNExistePas", mission.Agent.Context!, mission.World.Time);

        Assert.True(result.UsedFallback, "Le planificateur aurait du se rabattre sur l'exploration.");
        Assert.Equal(before + 1, planner.Rejected.Count);

        // La trace nomme l'objectif abandonne et la raison, pas seulement le repli obtenu.
        var rejected = planner.Rejected[^1];
        Assert.Contains("ObjectifQuiNExistePas", rejected.Reason, StringComparison.Ordinal);
        Assert.Contains("sans solution", rejected.Reason, StringComparison.Ordinal);
        Assert.Contains(MissionRun.Config.Planner.ExplorationFallbackGoal, rejected.Reason, StringComparison.Ordinal);
    }

    /// <summary>Une recherche qui echoue sans repli possible conserve sa raison d'echec.</summary>
    [Fact]
    public void Une_recherche_sans_solution_conserve_sa_raison_d_echec()
    {
        var mission = new MissionRun(team: false);
        mission.RunUntil(() => mission.Agent.Context is not null, maxTicks: 20);

        var planner = mission.Agent.Planner;

        // Demander directement le repli lui-meme : il ne peut pas se replier sur lui-meme, donc
        // l'echec, s'il survient, remonte tel quel.
        var goal = planner.Domain.FindGoal("PickUpObjective")!;
        var problem = Hordinary.AI.Planning.SymbolicStateBuilder.Build(
            mission.Agent.Context!, planner.Domain, goal, MissionRun.Config.Planner);

        var result = planner.Solve(problem, mission.World.Time);

        Assert.Equal(result.Found, result.Search.Failure == Hordinary.AI.Planning.PlanningFailure.None);
        Assert.NotEmpty(result.Search.Reason);
        Assert.NotEmpty(result.Reason);
    }

    [Fact]
    public void La_derniere_recherche_gbfs_est_lisible_pour_le_mode_debug()
    {
        var mission = new MissionRun(team: false);
        mission.RunUntil(() => mission.Agent.Planner.Last is not null, maxTicks: 60);

        var last = mission.Agent.Planner.Last;
        Assert.NotNull(last);

        // Les noeuds explores par GBFS, que la specification demande d'afficher.
        Assert.True(last.Value.Search.ExploredNodes > 0, "Aucun noeud explore n'est rapporte.");
        Assert.True(last.Value.Search.GeneratedNodes > 0);
        Assert.NotEmpty(last.Value.Search.Reason);
    }
}
