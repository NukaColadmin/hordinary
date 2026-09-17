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

    // --- On ne laisse personne derriere ---------------------------------------------------------

    /// <summary>
    /// Le retour d'un seul ne conclut rien, meme la caisse a la main : le van part avec tout le
    /// monde. Sans cette regle, la mission se terminait a la seconde ou le plus rapide franchissait
    /// le seuil, et le sort des deux autres n'avait aucune consequence.
    /// </summary>
    [Fact]
    public void Le_retour_d_un_seul_survivant_ne_termine_pas_la_mission()
    {
        var mission = new MissionRun(team: true);
        var tracker = mission.Tracker;

        var camp = mission.Definition.CampIn(mission.Map)!;
        var van = mission.Map.FindWalkableIn(camp)!.Value;
        var dehors = mission.Spawn(ZoneKind.Pharmacy);

        // Le groupe part.
        foreach (var survivor in mission.Band.Survivors) survivor.Position = dehors;
        Assert.Equal(MissionOutcome.Running, tracker.Evaluate(1d).Outcome);

        // Alice rentre, la caisse a la main. Les deux autres sont encore dehors : rien n'est fini.
        mission.Alice.AddToInventory(Survivor.DefaultObjectiveItem);
        mission.Alice.Position = van;

        Assert.Equal(MissionOutcome.Running, tracker.Evaluate(2d).Outcome);
        Assert.Null(tracker.Courier);
        Assert.Equal([mission.Alice], tracker.Home);

        // Les deux autres rentrent : la mission est remplie, et c'est Alice qui rapporte la caisse.
        foreach (var survivor in mission.Band.Survivors) survivor.Position = van;

        var verdict = tracker.Evaluate(3d);

        Assert.Equal(MissionOutcome.Victory, verdict.Outcome);
        Assert.Equal(mission.Alice, tracker.Courier);
        Assert.Equal(3, tracker.Home.Count());
    }

    /// <summary>
    /// Etre rentre se perd. Un survivant qui traverse le camp au debut de l'expedition — sur la
    /// carte livree, le camp jouxte la ruelle et les trois y passent — comptait sinon comme rentre
    /// jusqu'a la fin de la partie, et la victoire se declarait a la seconde ou la caisse etait
    /// ramassee, tout le groupe etant encore dans la pharmacie.
    ///
    /// La tolerance reste : un pas de cote autour du vehicule ne fait pas repartir en mission.
    /// </summary>
    [Fact]
    public void Un_survivant_reparti_du_van_ne_compte_plus_comme_rentre()
    {
        var mission = new MissionRun(settings: new MissionSettings { TimeLimit = 0d });
        var tracker = mission.Tracker;

        var camp = mission.Definition.CampIn(mission.Map)!;
        var van = mission.Map.FindWalkableIn(camp)!.Value;

        // Alice sort, revient : elle est rentree.
        mission.Alice.Position = mission.Spawn(ZoneKind.Pharmacy);
        tracker.Evaluate(1d);

        mission.Alice.Position = van;
        tracker.Evaluate(2d);
        Assert.True(tracker.IsHome(mission.Alice));

        // Elle tourne autour du vehicule — juste au bord de la tolerance : toujours rentree.
        mission.Alice.Position = new GridPosition(van.X, camp.Max.Y + tracker.Settings.HomeRadius);
        tracker.Evaluate(3d);
        Assert.True(
            tracker.IsHome(mission.Alice),
            "Un pas de cote autour du van ne doit pas compter comme un depart.");

        // Elle repart pour de bon : elle ne l'est plus.
        mission.Alice.Position = mission.Spawn(ZoneKind.Pharmacy);
        tracker.Evaluate(4d);

        Assert.False(tracker.IsHome(mission.Alice));
        Assert.Empty(tracker.Home);
    }

    /// <summary>
    /// On n'attend pas indefiniment celui qui est reste sur le pave : « tout le monde », ce sont les
    /// survivants encore capables d'agir. Sans cela, une seule perte empecherait a jamais la mission
    /// de se conclure.
    /// </summary>
    [Fact]
    public void Un_survivant_hors_de_combat_n_empeche_pas_le_groupe_de_rentrer()
    {
        var mission = new MissionRun(team: true);
        var tracker = mission.Tracker;

        var van = mission.Map.FindWalkableIn(mission.Definition.CampIn(mission.Map)!)!.Value;
        var dehors = mission.Spawn(ZoneKind.Pharmacy);

        foreach (var survivor in mission.Band.Survivors) survivor.Position = dehors;
        tracker.Evaluate(1d);

        var perdu = mission.Band.Survivors[^1];
        perdu.TakeDamage(perdu.Settings.MaxHealth);

        // Les deux qui tiennent encore debout rentrent, la caisse avec eux.
        mission.Alice.AddToInventory(Survivor.DefaultObjectiveItem);
        foreach (var agent in mission.Band.Standing) agent.Survivor.Position = van;

        Assert.Equal(MissionOutcome.Victory, tracker.Evaluate(2d).Outcome);
    }

    // --- Le temps imparti -----------------------------------------------------------------------

    /// <summary>
    /// Le temps ecoule, le van annonce le depart : chacun recoit l'ordre de rentrer. C'est un ordre
    /// et pas un pilotage — il entre dans les scores de la Utility AI, comme celui du joueur —, mais
    /// il pese plus lourd, parce que le depart ne se negocie plus avec la mission.
    /// </summary>
    [Fact]
    public void Le_temps_ecoule_renvoie_tout_le_monde_au_van()
    {
        // Vingt secondes : le temps de sortir du camp, pas celui d'atteindre la pharmacie.
        //
        // Le plafond de planification est desserre parce que c'est une horloge (« planner.max_time_ms »
        // vaut 50 ms dans le fichier livre) : une recherche qui aboutit seule expire quand la machine
        // est chargee, et ce test-ci est le plus expose de la suite — il attend que les trois soient
        // rentres, depuis trois endroits differents, en une seule traite. Il passait par chance tant
        // que les trois exploraient ensemble et n'avaient donc jamais loin a revenir. Le plafond de
        // noeuds, lui, ne bouge pas : c'est celui qui dit quelque chose du domaine.
        var mission = new MissionRun(
            team: true,
            settings: new MissionSettings { TimeLimit = 20d },
            planner: MissionRun.Config.Planner with { MaxTimeMs = 60_000 });
        var tracker = mission.Tracker;

        var rappeles = new List<string>();
        tracker.Recalling += names => rappeles.AddRange(names);

        Assert.True(
            mission.RunUntil(() => tracker.Recalled),
            $"Le van n'a jamais annonce le depart. {mission.Trace()}");

        Assert.Equal(3, rappeles.Count);

        Assert.All(mission.Band.Standing, agent => Assert.Equal(
            MissionTracker.RecallOrder, agent.Survivor.Mission?.Label));

        Assert.True(
            mission.Band.Standing.All(
                agent => agent.Survivor.Mission!.Priority > MissionRun.Config.UtilityAi.PlayerOrderPriority),
            "Le rappel ne pese pas plus lourd qu'un ordre du joueur.");

        // Et la boucle les ramene : personne n'a besoin de conclure la mission a leur place.
        Assert.True(
            mission.RunUntil(() => tracker.IsOver),
            $"Le groupe n'est jamais rentre au van. {mission.Trace()}");

        Assert.Equal(3, tracker.Home.Count());
    }

    /// <summary>
    /// Rentrer bredouille n'est une defaite qu'une fois le temps ecoule : avant le rappel, un groupe
    /// au complet au van peut encore repartir, et rien ne dit qu'il a renonce.
    /// </summary>
    [Fact]
    public void Rentrer_les_mains_vides_apres_le_temps_ecoule_est_une_defaite()
    {
        var mission = new MissionRun(settings: new MissionSettings { TimeLimit = 60d });
        var tracker = mission.Tracker;

        var van = mission.Map.FindWalkableIn(mission.Definition.CampIn(mission.Map)!)!.Value;

        // Alice sort, puis revient bredouille : le van attend encore, la mission continue.
        mission.Alice.Position = mission.Spawn(ZoneKind.Pharmacy);
        tracker.Evaluate(1d);

        mission.Alice.Position = van;
        Assert.Equal(MissionOutcome.Running, tracker.Evaluate(2d).Outcome);

        // Le temps s'ecoule : le van part, et il part sans les medicaments.
        var verdict = tracker.Evaluate(61d);

        Assert.True(tracker.Recalled);
        Assert.Equal(MissionOutcome.Defeat, verdict.Outcome);
        Assert.Contains("temps est ecoule", verdict.Reason, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Zero desamorce le chronometre : le van attend indefiniment. Ce n'est pas un reglage de
    /// confort — c'est ce qui rend observables les tests qui mesurent autre chose que l'horloge.
    /// </summary>
    [Fact]
    public void Une_mission_sans_temps_imparti_ne_rappelle_personne()
    {
        var mission = new MissionRun(settings: new MissionSettings { TimeLimit = 0d });
        var tracker = mission.Tracker;

        Assert.False(tracker.Timed);
        Assert.Null(tracker.TimeLeft(0d));

        mission.Alice.Position = mission.Spawn(ZoneKind.Pharmacy);
        tracker.Evaluate(10_000d);

        Assert.False(tracker.Recalled);
        Assert.Null(mission.Alice.Mission);
        Assert.Equal(MissionOutcome.Running, tracker.Outcome);
    }

    /// <summary>
    /// Une mission ne redit que ce qui la distingue : son temps imparti l'emporte sur celui de la
    /// configuration, comme sa horde.
    /// </summary>
    [Fact]
    public void Le_temps_imparti_d_une_mission_prime_sur_celui_de_la_configuration()
    {
        var definition = MissionRun.Catalogue.Default! with { TimeLimit = 42d };

        var mission = new MissionRun(
            definition: definition, settings: new MissionSettings { TimeLimit = 300d });

        Assert.Equal(42d, mission.Tracker.TimeLimit, 3);
        Assert.Equal(12d, mission.Tracker.TimeLeft(30d)!.Value, 3);
        Assert.Equal(0d, mission.Tracker.TimeLeft(120d)!.Value, 3);
    }

    /// <summary>Le fichier livre donne cinq minutes par mission. Le chiffre est un choix de jeu.</summary>
    [Fact]
    public void Le_fichier_livre_accorde_cinq_minutes_par_mission()
    {
        Assert.Equal(300d, MissionRun.Config.Mission.TimeLimit, 3);
        Assert.True(
            MissionRun.Config.Mission.RecallPriority > MissionRun.Config.UtilityAi.PlayerOrderPriority,
            "Le rappel du van doit peser plus lourd qu'un ordre du joueur.");
    }
}
