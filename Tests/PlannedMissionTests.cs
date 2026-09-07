using Hordinary.Agents;
using Hordinary.World;

namespace Hordinary.Tests;

/// <summary>
/// Boucle complete du livrable 5 sur la carte livree, sans Godot :
/// perception → connaissances → choix Utility AI → planification GBFS → navigation A* → execution.
///
/// C'est le critere de validation du livrable joue de bout en bout — « l'objectif recuperer les
/// medicaments produit une suite d'actions valide » — mais aussi la garantie que le plan s'execute
/// reellement dans le monde : la pharmacie s'ouvre, la piece est fouillee, les medicaments changent de
/// main. La couche Godot ne fait rien de plus que ce que ce test fait ici.
///
/// Depuis le livrable 6, la boucle deroulee est <see cref="MissionRun"/>, c'est-a-dire
/// <c>Replanner</c> : ces tests verifient donc aussi que la surveillance ajoutee n'a pas casse le
/// deroulement nominal — un plan valide se laisse finir sans etre remis en cause.
/// </summary>
public class PlannedMissionTests
{
    // --- Critere de validation du livrable, joue dans le monde -----------------------------------

    [Fact]
    public void Alice_planifie_et_execute_la_recuperation_des_medicaments()
    {
        var mission = new MissionRun();

        // Le joueur demande les medicaments ; la Utility AI et le planificateur font le reste.
        mission.Alice.Mission = new MissionOrder(
            "recuperer les medicaments",
            mission.Spawn(ZoneKind.Pharmacy),
            MissionRun.Config.UtilityAi.PlayerOrderPriority);

        // Au depart elle ignore ou sont les medicaments : il faut aller fouiller.
        Assert.Empty(mission.Alice.Knowledge.KnownItems);

        bool collected = mission.RunUntil(() => mission.Alice.Carries(Survivor.DefaultObjectiveItem));

        Assert.True(collected, $"Medicaments non recuperes. {mission.Trace()}");

        // Ils ont bien change de main : ils ne sont plus par terre.
        Assert.DoesNotContain(mission.World.Items.Values, item => item.ItemId == Survivor.DefaultObjectiveItem);

        // Et elle est allee les chercher : ils ne se sont pas teleportes dans son sac depuis le depart.
        var pharmacy = mission.Map.GetZone(ZoneKind.Pharmacy)!;
        Assert.True(
            pharmacy.Contains(mission.Alice.Position),
            $"Alice a ramasse les medicaments depuis {mission.Alice.Position}, hors de la pharmacie. {mission.Trace()}");

        // C'est bien une action de plan qui les a pris, pas un effet de bord de la simulation.
        Assert.True(
            mission.Executed.Any(name => name.StartsWith("PickUp", StringComparison.Ordinal)),
            $"Aucune action de ramassage executee. {mission.Trace()}");

        // Le trajet est passe par le planificateur : chaque etape a laisse une trace de recherche.
        Assert.Contains(mission.Journal, line => line.Contains("noeuds explores", StringComparison.Ordinal));
    }

    [Fact]
    public void Puis_elle_rejoint_l_extraction_avec_les_medicaments()
    {
        var mission = new MissionRun();
        mission.Alice.Mission = new MissionOrder(
            "recuperer les medicaments puis sortir",
            mission.Spawn(ZoneKind.Pharmacy),
            MissionRun.Config.UtilityAi.PlayerOrderPriority);

        Assert.True(mission.RunUntil(() => mission.Alice.Carries(Survivor.DefaultObjectiveItem)), "Medicaments non recuperes.");

        var extraction = mission.Map.GetZone(ZoneKind.Extraction)!;
        bool extracted = mission.RunUntil(() => extraction.Contains(mission.Alice.Position));

        Assert.True(extracted, $"Alice s'est arretee en {mission.Alice.Position}. {mission.Trace()}");

        // Une fois les medicaments en poche, c'est bien la sortie que la Utility AI a choisie.
        Assert.Contains("ReachExtraction", mission.Executed);
    }

    [Fact]
    public void Le_journal_explique_chaque_decision_de_planification()
    {
        var mission = new MissionRun();
        mission.RunUntil(() => mission.Executed.Count >= 2);

        Assert.NotEmpty(mission.Journal);

        // Chaque planification laisse une trace lisible : objectif, actions, cout, noeuds explores.
        Assert.Contains(mission.Journal, line => line.Contains("noeuds explores", StringComparison.Ordinal));
    }

    /// <summary>
    /// Le garde-fou du livrable 6 sur le livrable 5 : quand rien ne change, rien ne bouge. Un plan
    /// valide ne doit pas etre remis en cause par la surveillance qui vient d'etre ajoutee, sans quoi
    /// Alice replanifierait sans fin au lieu d'avancer.
    /// </summary>
    [Fact]
    public void Un_plan_valide_n_est_pas_remis_en_cause_sans_raison()
    {
        var mission = new MissionRun();
        mission.RunUntil(() => mission.Alice.Carries(Survivor.DefaultObjectiveItem));

        // Il n'y a ni zombie ni porte verrouillee sur cette carte : chaque replanification doit
        // pouvoir nommer quelque chose qui est reellement arrive — un mur decouvert, une precondition
        // tombee, un objectif change. Aucune ne doit venir de la surveillance elle-meme.
        Hordinary.AI.Replanning.ReplanCause[] surprises =
        [
            Hordinary.AI.Replanning.ReplanCause.ObstacleDiscovered,
            Hordinary.AI.Replanning.ReplanCause.PreconditionFalse,
            Hordinary.AI.Replanning.ReplanCause.ActionFailed,
            Hordinary.AI.Replanning.ReplanCause.PathImpossible,
            Hordinary.AI.Replanning.ReplanCause.ItemMissing,
            Hordinary.AI.Replanning.ReplanCause.GoalChanged,
        ];

        Assert.All(
            mission.Replans,
            record => Assert.Contains(record.Trigger.Cause, surprises));

        // Et elles restent proportionnelles a ce qu'Alice a reellement fait : le garde-fou porte sur
        // l'emballement, pas sur un compte absolu. Depuis le livrable 7 la frontier exploration
        // l'envoie loin dans l'inconnu plutot qu'a la case connue la plus proche : elle y decouvre
        // legitimement plus de murs, donc replanifie plus souvent, sans pour autant tourner en rond.
        Assert.True(
            mission.Brain.ReplanCount <= mission.Executed.Count + 3,
            $"{mission.Brain.ReplanCount} replanifications pour {mission.Executed.Count} actions. {mission.Trace()}");
    }
}
