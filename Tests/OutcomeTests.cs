using Hordinary.Agents;
using Hordinary.Game;
using Hordinary.World;

namespace Hordinary.Tests;

/// <summary>
/// Victoire, defaite, et le critere de validation du livrable 8 : « une partie complete peut etre
/// jouee du depart jusqu'a la victoire ou la defaite ».
///
/// La partie est jouee pour de vrai — perception, Utility AI, GBFS, A*, execution, replanification —
/// sur la carte livree et avec les fichiers de configuration livres. Aucun de ces tests ne place un
/// survivant a la main au camp : c'est la boucle qui l'y ramene, ou personne.
/// </summary>
public class OutcomeTests
{
    // --- Critere de validation du livrable ------------------------------------------------------

    /// <summary>
    /// La boucle complete, du depart a la victoire : le joueur donne « recuperer les medicaments »,
    /// et le reste est de l'initiative. Personne ne dit a Alice de rentrer — c'est la Utility AI qui
    /// bascule sur <c>ReachExtraction</c> une fois la caisse en poche, et le camp est la sortie.
    /// </summary>
    [Fact]
    public void Une_partie_complete_se_joue_du_depart_jusqu_a_la_victoire()
    {
        var mission = new MissionRun();
        var tracker = mission.Tracker;

        mission.Alice.Mission = new MissionOrder(
            "recuperer une caisse de medicaments",
            mission.Spawn(ZoneKind.Pharmacy),
            MissionRun.Config.UtilityAi.PlayerOrderPriority);

        Assert.Equal(MissionOutcome.Running, tracker.Outcome);

        bool won = mission.RunUntil(() => tracker.IsOver);

        Assert.True(won, $"La partie ne s'est pas conclue. {mission.Trace()}");
        Assert.Equal(MissionOutcome.Victory, tracker.Outcome);

        // La caisse est au camp, et c'est elle qui conclut : le porteur est nomme dans le verdict.
        var camp = mission.Definition.CampIn(mission.Map)!;
        Assert.Contains(
            mission.Band.Survivors,
            survivor => survivor.Carries(Survivor.DefaultObjectiveItem) && camp.Contains(survivor.Position));

        Assert.NotNull(tracker.Courier);
        Assert.True(mission.Alice.Alive);

        Assert.Contains("ReachExtraction", mission.Executed);
    }

    [Fact]
    public void Le_verdict_ne_change_plus_une_fois_la_partie_finie()
    {
        var mission = new MissionRun();
        var tracker = mission.Tracker;

        int announcements = 0;
        tracker.Finished += _ => announcements++;

        mission.Alice.Mission = new MissionOrder(
            "recuperer les medicaments", mission.Spawn(ZoneKind.Pharmacy),
            MissionRun.Config.UtilityAi.PlayerOrderPriority);

        mission.RunUntil(() => tracker.IsOver);

        var verdict = tracker.Verdict;

        // Meme si la boucle continue de tourner, le verdict est prononce une fois pour toutes.
        for (int i = 0; i < 50; i++) mission.Tick();

        Assert.Equal(verdict, tracker.Verdict);
        Assert.Equal(1, announcements);
    }

    // --- Victoire ------------------------------------------------------------------------------

    /// <summary>
    /// Rentrer les mains vides ne remplit pas la mission. La specification dit « ce survivant ou un
    /// membre du groupe transporte les medicaments » : c'est la caisse qui doit franchir la ligne.
    /// </summary>
    [Fact]
    public void Rentrer_au_camp_sans_les_medicaments_ne_remplit_pas_la_mission()
    {
        var mission = new MissionRun(withObjective: false);
        var tracker = mission.Tracker;

        var camp = mission.Definition.CampIn(mission.Map)!;
        mission.Alice.Position = mission.Map.FindWalkableIn(camp)!.Value;

        tracker.Evaluate(mission.World.Time);

        Assert.Null(tracker.Courier);
        Assert.NotEqual(MissionOutcome.Victory, tracker.Outcome);
    }

    [Fact]
    public void Porter_les_medicaments_ailleurs_qu_au_camp_ne_remplit_pas_la_mission()
    {
        var mission = new MissionRun();
        var tracker = mission.Tracker;

        mission.Alice.AddToInventory(Survivor.DefaultObjectiveItem);
        mission.Alice.Position = mission.Spawn(ZoneKind.Pharmacy);

        Assert.Equal(MissionOutcome.Running, tracker.Evaluate(mission.World.Time).Outcome);
        Assert.Null(tracker.Courier);
    }

    // --- Defaite -------------------------------------------------------------------------------

    [Fact]
    public void La_partie_est_perdue_quand_plus_personne_ne_peut_agir()
    {
        var mission = new MissionRun(team: true);
        var tracker = mission.Tracker;

        Assert.Equal(MissionOutcome.Running, tracker.Evaluate(mission.World.Time).Outcome);

        // On les met hors de combat un par un : tant qu'il en reste un, la partie continue.
        foreach (var survivor in mission.Band.Survivors.SkipLast(1))
        {
            survivor.TakeDamage(survivor.Settings.MaxHealth);
            Assert.Equal(MissionOutcome.Running, tracker.Evaluate(mission.World.Time).Outcome);
        }

        var last = mission.Band.Survivors[^1];
        last.TakeDamage(last.Settings.MaxHealth);

        var verdict = tracker.Evaluate(mission.World.Time);

        Assert.Equal(MissionOutcome.Defeat, verdict.Outcome);
        Assert.Contains("aucun survivant", verdict.Reason, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// « Les medicaments deviennent definitivement inaccessibles » n'est pas une clause de style : la
    /// derniere caisse peut etre consommee pour soigner quelqu'un, et la mission n'a alors plus
    /// d'objet. C'est une fin de partie que le jeu produit tout seul.
    /// </summary>
    [Fact]
    public void La_partie_est_perdue_quand_la_derniere_caisse_a_ete_consommee()
    {
        var mission = new MissionRun();
        var tracker = mission.Tracker;

        Assert.Equal(MissionOutcome.Running, tracker.Evaluate(mission.World.Time).Outcome);

        // Alice ramasse la caisse a la pharmacie : elle existe toujours, dans son sac. Il faut
        // l'y placer explicitement — laissee au camp, la ramasser remplirait la mission sur place.
        mission.Alice.Position = mission.Spawn(ZoneKind.Pharmacy);
        mission.World.RemoveItem("objective-1");
        mission.Alice.AddToInventory(Survivor.DefaultObjectiveItem);
        Assert.Equal(MissionOutcome.Running, tracker.Evaluate(mission.World.Time).Outcome);

        // Elle s'en sert pour se soigner : il n'y a plus de medicaments nulle part.
        mission.Alice.TakeDamage(50f);
        Assert.True(mission.Alice.RemoveFromInventory(Survivor.DefaultObjectiveItem));
        mission.Alice.Heal(mission.Alice.Settings.HealAmount);

        var verdict = tracker.Evaluate(mission.World.Time);

        Assert.Equal(MissionOutcome.Defeat, verdict.Outcome);
        Assert.Contains("hors d'atteinte", verdict.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Qui_porte_la_caisse_est_lisible_a_tout_instant()
    {
        var mission = new MissionRun();
        var tracker = mission.Tracker;

        Assert.Empty(tracker.Carriers);

        mission.Alice.AddToInventory(Survivor.DefaultObjectiveItem);

        Assert.Equal([mission.Alice], tracker.Carriers);
    }
}
