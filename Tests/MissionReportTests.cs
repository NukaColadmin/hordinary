using Hordinary.Agents;
using Hordinary.Game;
using Hordinary.World;

namespace Hordinary.Tests;

/// <summary>
/// Le bilan que l'ecran de camp affiche au retour d'une sortie.
///
/// Ce qui est verifie, c'est qu'il dise vrai sur ce qui compte pour la decision suivante : qui est
/// rentre, ce que le pret a coute, ce que la sortie a rapporte, si les consignes ont tenu — et qu'il
/// taise ce qui n'a pas eu lieu. Les etats de fin sont poses a la main, comme dans
/// <see cref="OutcomeTests"/> ; seul le compteur du portillon 4 demande une boucle deroulee.
/// </summary>
public class MissionReportTests
{
    private static CampSettings Settings { get; } = CampSettings.FromJson(DemoMap.ReadConfig("camp.json"));

    private static string FirstWeapon => DemoMap.Items.Items.First(item => item.IsWeapon).Id;

    private static MissionReport Blank(params string[] loaned) => new()
    {
        Mission = "Essai",
        Verdict = new MissionVerdict(MissionOutcome.Victory, "essai", 222d),
        Loaned = loaned,
    };

    // --- Le groupe au verdict ----------------------------------------------------------------------

    /// <summary>
    /// Une victoire peut couter quelqu'un, et le bilan le dit : « tout le monde rentre » est une regle
    /// sur ceux qui tiennent debout, pas une promesse que personne n'est reste sur le pave. Avant ce
    /// bilan, une perte ne se lisait que si le groupe entier tombait.
    /// </summary>
    [Fact]
    public void Le_bilan_nomme_qui_est_rentre_qui_est_tombe_et_qui_rapporte_la_caisse()
    {
        var mission = new MissionRun(team: true);
        var tracker = mission.Tracker;

        var van = mission.Map.FindWalkableIn(mission.Definition.CampIn(mission.Map)!)!.Value;
        var dehors = mission.Spawn(ZoneKind.Pharmacy);

        foreach (var survivor in mission.Band.Survivors) survivor.Position = dehors;
        tracker.Evaluate(1d);

        var perdu = mission.Band.Survivors[^1];
        perdu.TakeDamage(perdu.Settings.MaxHealth);

        mission.Alice.AddToInventory(Survivor.DefaultObjectiveItem);
        foreach (var agent in mission.Band.Standing) agent.Survivor.Position = van;

        Assert.Equal(MissionOutcome.Victory, tracker.Evaluate(2d).Outcome);

        var report = MissionReport.Of(tracker, mission.Band);

        Assert.Equal(SurvivorFate.Down, report.Survivors.Single(s => s.Name == perdu.Name).Fate);
        Assert.Equal(2, report.Survivors.Count(s => s.Fate == SurvivorFate.Home));

        var courier = report.Survivors.Single(s => s.Brought is not null);
        Assert.Equal(tracker.Courier!.Name, courier.Name);

        string groupe = report.Lines().Single(line => line.StartsWith("Au van", StringComparison.Ordinal));
        Assert.Contains($"Hors de combat : {perdu.Name}", groupe);
        Assert.Contains($"rapporte {Survivor.DefaultObjectiveItem}", groupe);
    }

    /// <summary>
    /// Une defaite tombee pendant que le groupe est dehors laisse des survivants debout hors du van :
    /// ils ne sont ni rentres ni tombes, et le bilan ne les range dans aucune des deux cases.
    /// </summary>
    [Fact]
    public void Un_survivant_debout_hors_du_van_est_dit_dehors()
    {
        var mission = new MissionRun();
        mission.Alice.Position = mission.Spawn(ZoneKind.Pharmacy);
        mission.Tracker.Evaluate(1d);

        var report = MissionReport.Of(mission.Tracker, mission.Band);

        Assert.Equal(SurvivorFate.Outside, report.Survivors.Single().Fate);
        Assert.Contains(report.Lines(), line => line.StartsWith("Dehors : Alice", StringComparison.Ordinal));
    }

    // --- Le pret --------------------------------------------------------------------------------

    /// <summary>
    /// Le bilan lit le pret dans le paquetage et non dans les sacs : a l'instant du verdict, celui
    /// qui est tombe a deja tout laisse au sol, et son sac ne dit plus ce que le camp lui avait confie.
    /// </summary>
    [Fact]
    public void Le_pret_se_lit_dans_le_paquetage_meme_quand_le_sac_est_vide()
    {
        string weapon = FirstWeapon;
        var camp = new CampStock(Settings with { Storage = [new ItemRequirement(weapon, 1)] });
        var loadout = new CampLoadout(camp, DemoMap.Items);

        var mission = new MissionRun();
        Assert.True(loadout.Give(mission.Alice.Id, weapon));
        loadout.HandOver(mission.Alice);

        mission.Alice.TakeDamage(mission.Alice.Settings.MaxHealth);
        mission.Tracker.Evaluate(1d);

        var report = MissionReport.Of(mission.Tracker, mission.Band, loadout);

        Assert.Equal([weapon], report.Loaned);
        Assert.Equal([weapon], report.Lost);
        Assert.Contains(report.Lines(), line => line == $"Pret non rendu : {weapon}");
    }

    /// <summary>
    /// Le pret se compte au groupe : une arme ramassee sur un camarade tombe et rapportee au van est
    /// rendue. Compter au survivant l'aurait declaree perdue alors qu'elle est sur l'etagere.
    /// </summary>
    [Fact]
    public void Une_arme_rapportee_par_un_autre_que_celui_qui_l_avait_est_rendue()
    {
        var report = Blank("fusil") with { Returned = ["fusil", "conserve", "conserve"] };

        Assert.Empty(report.Lost);
        Assert.Equal(["conserve", "conserve"], report.Brought);
        Assert.Contains(report.Lines(), line => line == "Rapporte au camp : conserve x2");
        Assert.DoesNotContain(report.Lines(), line => line.StartsWith("Pret", StringComparison.Ordinal));
    }

    /// <summary>
    /// Ce que l'entrepot plein a refuse a sa propre ligne, qui dit pourquoi — et ne se compte pas une
    /// seconde fois comme pret perdu.
    /// </summary>
    [Fact]
    public void Un_objet_refuse_faute_de_place_n_est_compte_qu_une_fois()
    {
        var report = Blank("fusil", "bandage") with { Left = ["fusil"] };

        Assert.Equal(["bandage"], report.Lost);
        Assert.Contains(report.Lines(), line => line == "Laisse faute de place a l'entrepot : fusil");
    }

    [Fact]
    public void La_difference_retire_un_exemplaire_par_exemplaire_retire()
    {
        Assert.Equal(["a", "b"], MissionReport.Without(["a", "a", "b", "c"], ["a", "c", "d"]));
    }

    // --- Les consignes --------------------------------------------------------------------------

    /// <summary>
    /// Le portillon 4 tourne sur chaque plan produit sous consigne, et le bilan en rend le compte.
    /// C'est la seule trace, a l'ecran de camp, qu'une consigne du joueur a pese sur la sortie.
    /// </summary>
    [Fact]
    public void Les_plans_verifies_sous_consigne_sont_comptes_au_bilan()
    {
        var run = new MissionRun(instructions: ["forbid(BreakWindow)"]);

        Assert.True(run.RunUntil(() => run.Agent.PlansVerified > 0, 600), run.Trace());

        var report = MissionReport.Of(run.Tracker, run.Band);

        Assert.Equal(1, report.Constraints);
        Assert.Equal(run.Agent.PlansVerified, report.PlansVerified);
        Assert.Equal(0, report.PlansViolated);
        Assert.Contains(report.Lines(), line => line.StartsWith("Consignes : 1 active(s)", StringComparison.Ordinal)
                                             && line.EndsWith("aucun ecart.", StringComparison.Ordinal));
    }

    [Fact]
    public void Une_sortie_sans_consigne_n_en_dit_rien()
    {
        var run = new MissionRun();
        run.RunUntil(() => run.Plans.Count > 0, 300);

        var report = MissionReport.Of(run.Tracker, run.Band);

        Assert.Equal(0, run.Agent.PlansVerified);
        Assert.DoesNotContain(report.Lines(), line => line.StartsWith("Consignes", StringComparison.Ordinal));
    }

    /// <summary>
    /// Une violation est un defaut du compilateur, pas une desobeissance : la ligne le dit, pour que
    /// le joueur ne reproche pas au survivant ce que la traduction a fait.
    /// </summary>
    [Fact]
    public void Un_plan_non_conforme_est_impute_au_compilateur()
    {
        var report = Blank() with { Constraints = 2, PlansVerified = 9, PlansViolated = 1 };

        Assert.Contains(report.Lines(), line => line.Contains("1 non conforme(s) — defaut du compilateur", StringComparison.Ordinal));
    }

    // --- La duree et la journee -----------------------------------------------------------------

    [Fact]
    public void La_duree_se_lit_contre_le_temps_imparti_et_signale_le_rappel()
    {
        var report = Blank() with { TimeLimit = 300d, Recalled = true };

        Assert.Contains("Duree : 3:42 sur 5:00, rappel du van.", report.Lines());
    }

    /// <summary>
    /// L'autonomie du camp suit la ration de <see cref="CampStock.ResolveDay"/> : les paliers disent
    /// qu'une reserve est basse, ce nombre dit quand elle sera vide.
    /// </summary>
    [Fact]
    public void Le_bilan_dit_combien_de_jours_le_camp_tient_encore()
    {
        var camp = new CampStock(Settings);
        var day = camp.ResolveDay([MissionRun.Lone("alice", default), MissionRun.Lone("bob", default)]);

        var report = Blank().Closing(day, camp);

        int food = (int)Math.Floor(camp.Food / (Settings.FoodPerSurvivorPerDay * 2));
        int water = (int)Math.Floor(camp.Water / (Settings.WaterPerSurvivorPerDay * 2));

        Assert.Equal(food, report.FoodDays);
        Assert.Equal(water, report.WaterDays);
        Assert.Contains(report.Lines(), line => line.StartsWith("Reserves : ", StringComparison.Ordinal));
    }

    [Fact]
    public void Un_camp_sans_bouche_a_nourrir_n_annonce_aucune_autonomie()
    {
        var camp = new CampStock(Settings);

        Assert.Null(camp.DaysOf("food", 0));
        Assert.Null(camp.DaysOf("fuel", 3));
    }
}
