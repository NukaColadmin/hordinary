using System.Reflection;
using Hordinary.AI.Utility;
using Hordinary.Agents;
using Hordinary.Config;
using Hordinary.World;

namespace Hordinary.Tests;

/// <summary>
/// Tests du livrable 7 : trois survivants distincts, exploration repartie, partage d'informations.
///
/// Le critere de validation — « une information decouverte par Alice peut etre utilisee ensuite par
/// Bob apres communication » — est verifie par
/// <see cref="Ce_qu_Alice_a_vu_devient_utilisable_par_Bob_apres_communication"/>. Les tests exiges par
/// C-tests.md section « Connaissances », dont « le partage de connaissances n'accede pas au
/// WorldState », restent dans <see cref="KnowledgeTests"/> ; celui-ci les prolonge du cote du
/// systeme qui appareille les interlocuteurs.
/// </summary>
public class BandTests
{
    private static SurvivorRoster Roster => MissionRun.Roster;

    private static GameConfig Config => MissionRun.Config;

    private static SurvivorProfile Profile(string id)
        => Roster.Find(id) ?? throw new InvalidOperationException($"Profil « {id} » absent de config/survivors.json.");

    /// <summary>Un survivant nu, sur une carte vide, pour les tests qui n'ont pas besoin du monde.</summary>
    private static Survivor Lone(string id, GridPosition at, int size = 20)
    {
        var profile = Profile(id);
        var knowledge = new AgentKnowledge(id, size, size, default, Config.Knowledge);
        var survivor = Survivor.FromProfile(profile, knowledge, Config.Perception, Config.Survivor);
        survivor.Position = at;
        return survivor;
    }

    // --- Les trois profils ------------------------------------------------------------------

    [Fact]
    public void Les_trois_survivants_de_la_specification_sont_declares_en_configuration()
    {
        Assert.Equal(
            ["alice", "bob", "claire"],
            Roster.Survivors.Select(profile => profile.Id).ToArray());
    }

    /// <summary>
    /// Les trois survivants sont interchangeables sur le papier : meme sante, meme vue, meme vitesse.
    /// Un profil ne declare qu'un identifiant, un nom et une image.
    ///
    /// C'est un test de ce qui a ete <b>retire</b>, et il a sa raison d'etre : tant qu'un profil
    /// portait une fiche de personnage, deux explications se disputaient chaque comportement observe
    /// — « Bob est passe par la porte parce qu'il est bricoleur » et « parce que le joueur a interdit
    /// le bruit ». Une seule des deux est le sujet du jeu, et c'est la seconde.
    /// </summary>
    [Fact]
    public void Les_trois_survivants_partent_avec_les_memes_caracteristiques()
    {
        var survivors = new[] { "alice", "bob", "claire" }
            .Select(id => Survivor.FromProfile(
                Profile(id), new AgentKnowledge(id, 5, 5), Config.Perception, Config.Survivor))
            .ToList();

        foreach (var survivor in survivors.Skip(1))
        {
            Assert.Equal(survivors[0].Settings.MaxHealth, survivor.Settings.MaxHealth, 3);
            Assert.Equal(survivors[0].Settings.WalkSpeed, survivor.Settings.WalkSpeed, 3);
            Assert.Equal(survivors[0].Settings.RunSpeed, survivor.Settings.RunSpeed, 3);
            Assert.Equal(survivors[0].Settings.HealAmount, survivor.Settings.HealAmount, 3);
            Assert.Equal(survivors[0].VisionRange, survivor.VisionRange);
        }

        // Et ce sont bien celles du fichier commun, pas les valeurs par defaut du code.
        Assert.Equal(Config.Survivor.MaxHealth, survivors[0].Settings.MaxHealth, 3);
    }

    /// <summary>
    /// Ce qui change le prix d'une action, c'est ce qu'on a en main — et rien d'autre.
    ///
    /// GBFS minimise le cout : un survivant equipe d'une caisse a outils trouve donc spontanement le
    /// plan qui passe par la porte, la ou le meme survivant les mains vides preferera le detour. La
    /// difference de comportement sort de la recherche, elle n'est ecrite nulle part — et elle tient
    /// a une situation, pas a une identite.
    /// </summary>
    [Fact]
    public void L_equipement_change_le_prix_d_une_action_sans_changer_son_sens()
    {
        var commun = MissionRun.Domain;
        var outille = commun.Specialize(DemoMap.Items.Find("toolbox")!.ActionModifiers);

        var forceeAMains = commun.FindAction("ForceDoor")!;
        var forceeOutillee = outille.FindAction("ForceDoor")!;

        Assert.True(forceeOutillee.Cost < forceeAMains.Cost);

        // Les preconditions et les effets, eux, ne bougent pas : une porte forcee est une porte
        // forcee, avec ou sans outils.
        Assert.Equal(forceeAMains.Preconditions, forceeOutillee.Preconditions);
        Assert.Equal(forceeAMains.Effects, forceeOutillee.Effects);

        // Le domaine commun n'a pas bouge : la specialisation rend une copie.
        Assert.Equal(3f, commun.FindAction("ForceDoor")!.Cost, 3);
    }

    // --- merge_belief -----------------------------------------------------------------------

    [Fact]
    public void Merge_belief_suit_les_trois_lignes_de_la_specification()
    {
        var cell = new GridPosition(2, 2);

        var known = new CellBelief(cell, CellType.Floor, false, 1f, 10d, KnowledgeSource.AllyCommunication);

        // 1. Une observation directe l'emporte toujours, meme sur une croyance plus recente.
        var seen = new CellBelief(cell, CellType.Wall, false, 1f, 4d, KnowledgeSource.DirectObservation);
        Assert.Same(seen, BeliefMerge.Merge(known, seen));

        // 2. Entre deux temoignages, le plus recent l'emporte.
        var fresher = new CellBelief(cell, CellType.Wall, false, 1f, 12d, KnowledgeSource.AllyCommunication);
        Assert.Same(fresher, BeliefMerge.Merge(known, fresher));

        // 3. Sinon on garde ce qu'on avait — y compris a date egale, sans quoi deux allies en
        // conversation se renverraient la meme information en la degradant a chaque passage.
        var stale = new CellBelief(cell, CellType.Wall, false, 1f, 3d, KnowledgeSource.AllyCommunication);
        var tie = new CellBelief(cell, CellType.Wall, false, 1f, 10d, KnowledgeSource.AllyCommunication);
        Assert.Same(known, BeliefMerge.Merge(known, stale));
        Assert.Same(known, BeliefMerge.Merge(known, tie));

        // Ne rien savoir, c'est accepter n'importe quel renseignement.
        Assert.Same(stale, BeliefMerge.Merge<CellBelief>(null, stale));
    }

    [Fact]
    public void Un_aller_retour_de_conversation_ne_degrade_pas_indefiniment_une_croyance()
    {
        var alice = new AgentKnowledge("alice", 10, 10, default, Config.Knowledge);
        var bob = new AgentKnowledge("bob", 10, 10, default, Config.Knowledge);

        alice.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(4, 4), 5d);

        for (int round = 0; round < 5; round++)
        {
            bob.LearnFrom(alice);
            alice.LearnFrom(bob);
        }

        // Alice garde son observation directe intacte ; Bob garde le temoignage a une seule decote.
        Assert.Equal(1f, alice.GetEntity("zombie-1")!.Confidence, 3);
        Assert.Equal(KnowledgeSource.DirectObservation, alice.GetEntity("zombie-1")!.Source);
        Assert.Equal(1f - Config.Knowledge.SharedConfidencePenalty, bob.GetEntity("zombie-1")!.Confidence, 3);
    }

    // --- Portee de communication ------------------------------------------------------------

    [Fact]
    public void KnowledgeSharing_n_expose_aucun_acces_au_monde_reel()
    {
        // Le critere du livrable, garanti par le type : si le partage pouvait lire WorldState, ce
        // test echouerait avant meme qu'un survivant ait triche.
        var type = typeof(KnowledgeSharing);

        var referenced = type
            .GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(field => field.FieldType)
            .Concat(type.GetProperties().Select(property => property.PropertyType))
            .Concat(type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(method => method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType)))
            .Concat(type.GetConstructors().SelectMany(c => c.GetParameters().Select(p => p.ParameterType)))
            .ToList();

        Assert.DoesNotContain(typeof(WorldState), referenced);
        Assert.DoesNotContain(typeof(WorldEntity), referenced);
        Assert.DoesNotContain(typeof(WorldItem), referenced);
        Assert.DoesNotContain(typeof(PerceptionSystem), referenced);
    }

    [Fact]
    public void Au_dela_de_cinq_cellules_les_survivants_ne_se_parlent_pas()
    {
        var sharing = new KnowledgeSharing(Config.Knowledge);

        var alice = Lone("alice", new GridPosition(2, 2));
        var bob = Lone("bob", new GridPosition(2, 2));

        alice.Knowledge.ObserveCell(new GridPosition(9, 9), CellType.Wall, false, 1d);

        // Six cellules : hors de portee de voix.
        bob.Position = new GridPosition(8, 2);
        Assert.False(sharing.CanTalk(alice, bob));
        Assert.Equal(0, sharing.Share([alice, bob], 1d));
        Assert.False(bob.Knowledge.HasObserved(new GridPosition(9, 9)));

        // Cinq : a portee.
        bob.Position = new GridPosition(7, 2);
        Assert.True(sharing.CanTalk(alice, bob));
        Assert.True(sharing.Share([alice, bob], 1d) > 0);
        Assert.True(bob.Knowledge.HasObserved(new GridPosition(9, 9)));
    }

    [Fact]
    public void La_radio_de_groupe_supprime_la_contrainte_de_distance()
    {
        var settings = new KnowledgeSettings { CommunicationRange = 5, RadioEnabled = true };
        var sharing = new KnowledgeSharing(settings);

        var alice = Lone("alice", new GridPosition(0, 0));
        var bob = Lone("bob", new GridPosition(18, 18));

        Assert.True(sharing.CanTalk(alice, bob));
    }

    [Fact]
    public void Le_partage_respecte_la_cadence_d_une_fois_par_seconde()
    {
        var sharing = new KnowledgeSharing(Config.Knowledge);

        var alice = Lone("alice", new GridPosition(2, 2));
        var bob = Lone("bob", new GridPosition(3, 2));

        alice.Knowledge.ObserveCell(new GridPosition(9, 9), CellType.Wall, false, 0.5d);
        Assert.True(sharing.Advance([alice, bob], 1d) > 0);

        // Une nouvelle decouverte, mais la conversation vient d'avoir lieu : elle attendra.
        alice.Knowledge.ObserveCell(new GridPosition(8, 8), CellType.Wall, false, 1.2d);
        Assert.Equal(0, sharing.Advance([alice, bob], 1.5d));
        Assert.False(bob.Knowledge.HasObserved(new GridPosition(8, 8)));

        Assert.True(sharing.Advance([alice, bob], 2.1d) > 0);
        Assert.True(bob.Knowledge.HasObserved(new GridPosition(8, 8)));
    }

    [Fact]
    public void Se_parler_apprend_aussi_dans_quel_etat_est_l_interlocuteur()
    {
        var sharing = new KnowledgeSharing(Config.Knowledge);

        var bob = Lone("bob", new GridPosition(2, 2));
        var claire = Lone("claire", new GridPosition(4, 2));

        bob.TakeDamage(bob.Settings.MaxHealth * 0.7f);

        // Avant la rencontre, Claire ne sait meme pas ou est Bob — a fortiori pas qu'il est blesse.
        Assert.Null(claire.Knowledge.GetEntity("bob"));

        sharing.Share([bob, claire], 3d);

        var belief = claire.Knowledge.GetEntity("bob")!;
        Assert.Equal(bob.HealthRatio, belief.HealthRatio!.Value, 3);
        Assert.True(belief.Panic > 0f);
    }

    // --- Critere de validation du livrable ---------------------------------------------------

    [Fact]
    public void Ce_qu_Alice_a_vu_devient_utilisable_par_Bob_apres_communication()
    {
        var mission = new MissionRun(team: true);

        var alice = mission.AgentOf("alice");
        var bob = mission.AgentOf("bob");

        // Alice part seule reperer les medicaments dans la pharmacie ; Bob reste au depart.
        alice.Follower.Teleport(MissionRun.ObjectiveCell);
        alice.Perceive();

        Assert.Contains(
            alice.Survivor.Knowledge.KnownItems.Values,
            item => item.ItemId == Survivor.DefaultObjectiveItem);

        // Bob n'y est jamais alle : il ignore tout de la piece et de ce qu'elle contient.
        Assert.Empty(bob.Survivor.Knowledge.KnownItems);
        Assert.False(bob.Survivor.Knowledge.HasObserved(MissionRun.ObjectiveCell));

        var beforeSharing = new UtilityContext(
            bob.Survivor, mission.World.Time, MissionRun.Config.UtilityAi, MissionRun.Config.Exploration);

        Assert.False(beforeSharing.KnowsObjective);
        Assert.Null(beforeSharing.Locate("objective"));

        // Elles se retrouvent, et Alice raconte.
        alice.Follower.Teleport(bob.Survivor.Position);
        alice.Perceive();
        Assert.True(mission.Band.Sharing.Share(mission.Band.Survivors, mission.World.Time) > 0);

        // Bob sait maintenant ou sont les medicaments — sans y etre alle, et sans que rien n'ait
        // lu le monde reel pour lui : l'information est passee par la memoire d'Alice.
        var afterSharing = new UtilityContext(
            bob.Survivor, mission.World.Time, MissionRun.Config.UtilityAi, MissionRun.Config.Exploration);

        Assert.True(afterSharing.KnowsObjective);
        Assert.Equal(MissionRun.ObjectiveCell, afterSharing.Locate("objective"));

        var learned = bob.Survivor.Knowledge.KnownItems.Values.Single(item => item.ItemId == Survivor.DefaultObjectiveItem);
        Assert.Equal(KnowledgeSource.AllyCommunication, learned.Source);
        Assert.True(learned.Confidence < 1f);

        // Et il peut s'en servir : le planificateur lui construit un plan pour aller les chercher.
        var plan = bob.Planner.PlanFor("PickUpObjective", afterSharing, mission.World.Time);
        Assert.True(plan.Found, $"Bob n'a pas su planifier avec ce qu'Alice lui a appris : {plan.Reason}");
    }

    [Fact]
    public void Un_survivant_hors_de_portee_reste_reellement_isole()
    {
        var mission = new MissionRun(team: true);

        var alice = mission.AgentOf("alice");
        var claire = mission.AgentOf("claire");

        alice.Follower.Teleport(MissionRun.ObjectiveCell);
        alice.Perceive();

        // Claire est restee au depart, bien au-dela des cinq cellules : la conversation n'a pas lieu.
        Assert.False(mission.Band.Sharing.CanTalk(alice.Survivor, claire.Survivor));
        mission.Band.Sharing.Share(mission.Band.Survivors, mission.World.Time);

        Assert.Empty(claire.Survivor.Knowledge.KnownItems);
    }

    // --- Exploration repartie ---------------------------------------------------------------

    /// <summary>
    /// Un couloir en croix : le survivant se tient au centre d'une zone deja vue, et les quatre
    /// branches partent vers de l'inconnu. Deux frontieres opposees valent donc exactement la meme
    /// chose — jusqu'a ce qu'un allie en annonce une.
    /// </summary>
    private static Survivor AtCrossroads(string id, out GridPosition west, out GridPosition east)
    {
        var survivor = Lone(id, new GridPosition(10, 10), size: 21);
        var knowledge = survivor.Knowledge;

        for (int x = 4; x <= 16; x++) knowledge.ObserveCell(new GridPosition(x, 10), CellType.Floor, false, 0d);

        west = new GridPosition(4, 10);
        east = new GridPosition(16, 10);
        return survivor;
    }

    [Fact]
    public void Une_frontiere_est_une_cellule_connue_au_bord_de_l_inconnu()
    {
        var alice = AtCrossroads("alice", out var west, out var east);
        var knowledge = alice.Knowledge;

        var explorer = new FrontierExplorer(Config.Exploration);

        Assert.True(FrontierExplorer.IsFrontier(knowledge, knowledge.GetCell(west)!));
        Assert.True(FrontierExplorer.IsFrontier(knowledge, knowledge.GetCell(east)!));

        // Le milieu du couloir est connu de tous les cotes utiles... mais borde le nord et le sud
        // jamais observes : c'est donc bien une frontiere lui aussi.
        var context = new UtilityContext(alice, 0d, Config.UtilityAi, Config.Exploration);
        var ranked = explorer.Rank(context);

        Assert.NotEmpty(ranked);
        Assert.All(ranked, frontier => Assert.True(knowledge.HasObserved(frontier.Cell)));
        Assert.All(ranked, frontier => Assert.True(knowledge.MentalMap.IsWalkable(frontier.Cell)));
    }

    [Fact]
    public void Le_gain_d_information_favorise_ce_qui_ouvre_sur_le_plus_d_inconnu()
    {
        var alice = AtCrossroads("alice", out var west, out _);
        var explorer = new FrontierExplorer(Config.Exploration);

        // Le bout du couloir donne sur un large inconnu ; le milieu, deja entoure de connu, non.
        float atEdge = explorer.InformationGain(alice.Knowledge, west);
        float atCentre = explorer.InformationGain(alice.Knowledge, new GridPosition(10, 10));

        Assert.True(atEdge > atCentre, $"bord {atEdge:0.00}, centre {atCentre:0.00}");
    }

    [Fact]
    public void L_annonce_d_un_allie_detourne_l_exploration_vers_l_autre_cote()
    {
        var alice = AtCrossroads("alice", out var west, out var east);
        var explorer = new FrontierExplorer(Config.Exploration);

        // Sans rien savoir des autres, les deux bouts du couloir se valent.
        var alone = new UtilityContext(alice, 0d, Config.UtilityAi, Config.Exploration);
        Assert.Equal(
            explorer.Score(alone, west).Score,
            explorer.Score(alone, east).Score,
            3);

        // Bob annonce qu'il s'occupe de l'ouest. L'information est arrivee par la parole, et rien
        // d'autre n'a change dans la carte mentale d'Alice.
        alice.Knowledge.DeclareIntent(
            "bob", "ExploreUnknownArea", west, 0d, KnowledgeSource.AllyCommunication);

        var informed = new UtilityContext(alice, 0d, Config.UtilityAi, Config.Exploration);
        var chosen = explorer.Choose(informed);

        Assert.NotNull(chosen);
        Assert.True(explorer.Score(informed, east).Score > explorer.Score(informed, west).Score);
        Assert.True(chosen.Value.Cell.X > 10, $"Alice repart vers {chosen.Value.Cell} malgre l'annonce de Bob.");
    }

    /// <summary>
    /// La case sous les pieds borde elle aussi l'inconnu — le nord et le sud du couloir n'ont jamais
    /// ete observes — et elle bat toutes les autres au score : gain d'information intact, trajet
    /// gratuit. La choisir produirait un plan « aller la ou l'on est » qui reussit sans un pas, et
    /// que la decision suivante rebatirait a l'identique. C'est le survivant immobile affichant
    /// <c>ExploreUnknownArea</c>.
    /// </summary>
    [Fact]
    public void La_frontiere_sous_les_pieds_n_est_plus_une_destination()
    {
        var alice = AtCrossroads("alice", out _, out _);
        var here = alice.Position;

        Assert.True(FrontierExplorer.IsFrontier(alice.Knowledge, alice.Knowledge.GetCell(here)!));

        var context = new UtilityContext(alice, 0d, Config.UtilityAi, Config.Exploration);
        var explorer = new FrontierExplorer(Config.Exploration);

        Assert.DoesNotContain(explorer.Rank(context), frontier => frontier.Cell == here);
        Assert.NotEqual(here, explorer.Choose(context)?.Cell);
    }

    /// <summary>
    /// Le bord du connu ne promet aucun itineraire : une cour apercue par-dessus un mur en fait
    /// partie. Sans le test d'atteignabilite, elle est choisie, A* n'y mene pas, l'action echoue
    /// faute de route — et la replanification refait le meme choix, indefiniment.
    /// </summary>
    [Fact]
    public void Une_frontiere_qu_aucun_itineraire_ne_dessert_est_ecartee()
    {
        var alice = AtCrossroads("alice", out var west, out var east);

        // L'ouest est atteignable, l'est ne l'est pas. Le test ne dit pas pourquoi — c'est la
        // navigation qui le sait, et la Utility AI ne fait que la croire.
        var context = new UtilityContext(
            alice, 0d, Config.UtilityAi, Config.Exploration, reachable: cell => cell.X <= 10);

        var ranked = new FrontierExplorer(Config.Exploration).Rank(context);

        Assert.Contains(ranked, frontier => frontier.Cell == west);
        Assert.DoesNotContain(ranked, frontier => frontier.Cell == east);
    }

    [Fact]
    public void Une_annonce_perimee_cesse_de_peser_sur_la_repartition()
    {
        var alice = AtCrossroads("alice", out var west, out _);
        var explorer = new FrontierExplorer(Config.Exploration);

        alice.Knowledge.DeclareIntent(
            "bob", "ExploreUnknownArea", west, 0d, KnowledgeSource.AllyCommunication);

        var fresh = new UtilityContext(alice, 1d, Config.UtilityAi, Config.Exploration);

        // Bien apres intent_memory, « Bob allait a l'ouest » ne dit plus rien de ce que fait Bob.
        double stale = Config.Knowledge.IntentMemory + 10d;
        var forgotten = new UtilityContext(alice, stale, Config.UtilityAi, Config.Exploration);

        Assert.True(explorer.Score(fresh, west).Duplication > 0f);
        Assert.Equal(0f, explorer.Score(forgotten, west).Duplication, 3);
    }

    [Fact]
    public void Un_survivant_annonce_ou_il_va_pour_que_les_autres_aillent_ailleurs()
    {
        var mission = new MissionRun(team: true);
        var alice = mission.AgentOf("alice");
        var bob = mission.AgentOf("bob");

        mission.RunUntil(() => alice.Survivor.Knowledge.GetIntent("alice") is not null, maxTicks: 200);

        var declared = alice.Survivor.Knowledge.GetIntent("alice");
        Assert.NotNull(declared);
        Assert.Equal(alice.Survivor.CurrentGoal, declared.Goal);
        Assert.Equal(KnowledgeSource.DirectObservation, declared.Source);

        // Et l'annonce circule — a la conversation suivante, pas par la seule appartenance au groupe.
        Assert.True(
            mission.RunUntil(() => bob.Survivor.Knowledge.GetIntent("alice") is not null, maxTicks: 200),
            $"Bob n'a jamais appris ou allait Alice. {mission.Trace()}");

        var heard = bob.Survivor.Knowledge.GetIntent("alice")!;
        Assert.Equal(KnowledgeSource.AllyCommunication, heard.Source);
        Assert.True(heard.Confidence < 1f);
    }

    /// <summary>
    /// La dispersion est mesuree <b>pendant</b> la mission et non a un instant fixe : depuis que le
    /// camp est aussi le point de retour, une mission remplie declenche le rappel, et le rappel
    /// ramene tout le monde au meme endroit. Regrouper le groupe a la fin est le comportement voulu ;
    /// ce qui doit etre verifie ici, c'est qu'entre-temps ils ne sont pas partis au meme endroit.
    /// </summary>
    [Fact]
    public void Trois_survivants_partis_du_meme_point_n_explorent_pas_le_meme_endroit()
    {
        var mission = new MissionRun(team: true);

        // Ils demarrent tous les trois sur la meme case, avec la meme carte mentale vide : sans la
        // penalite de duplication, ils calculeraient le meme meilleur score et partiraient ensemble.
        var start = mission.Band.Survivors.Select(survivor => survivor.Position).Distinct().ToList();
        Assert.Single(start);

        bool dispersed = false;

        mission.RunUntil(
            () =>
            {
                dispersed |= Dispersed(mission);
                return mission.Tracker.IsOver;
            },
            maxTicks: 600);

        Assert.True(
            dispersed,
            $"Les trois survivants ne se sont jamais separes. {mission.Trace()}");

        // Chacun a decouvert du terrain. Les cartes mentales, elles, ont partiellement fusionne —
        // c'est le partage qui fait cela, et c'est voulu. Ce qui doit differer, c'est ce que chacun
        // a vu DE SES YEUX : c'est la seule mesure de l'exploration reellement repartie.
        var seen = mission.Band.Agents
            .Select(agent => agent.Survivor.Knowledge.KnownCells.Values
                .Where(belief => belief.Source == KnowledgeSource.DirectObservation)
                .Select(belief => belief.Position)
                .ToHashSet())
            .ToList();

        Assert.All(seen, cells => Assert.True(cells.Count > 20, $"{cells.Count} cellules vues seulement."));

        Assert.True(
            seen[0].Except(seen[1]).Count() > 10,
            $"Alice et Bob ont arpente le meme terrain : {seen[0].Except(seen[1]).Count()} cases d'ecart.");

        // Et ce qu'un seul a vu profite quand meme aux autres : Bob connait des cases ou il n'a
        // jamais mis les pieds, parce qu'on les lui a decrites.
        var bob = mission.AgentOf("bob").Survivor.Knowledge;
        Assert.Contains(
            bob.KnownCells.Values,
            belief => belief.Source == KnowledgeSource.AllyCommunication);
    }

    /// <summary>
    /// Vrai quand les trois sont sur trois cases differentes <b>et</b> qu'au moins deux d'entre eux
    /// sont hors de portee de voix. La seconde condition est la vraie : trois cases voisines ne sont
    /// pas une repartition, ce sont trois personnes qui marchent ensemble.
    /// </summary>
    private static bool Dispersed(MissionRun mission)
    {
        var agents = mission.Band.Agents;

        if (agents.Select(agent => agent.Survivor.Position).Distinct().Count() != agents.Count) return false;

        return (from a in agents
                from b in agents
                where !ReferenceEquals(a, b)
                select mission.Band.Sharing.CanTalk(a.Survivor, b.Survivor))
            .Any(talking => !talking);
    }

    // --- Entraide ---------------------------------------------------------------------------

    [Fact]
    public void Un_blesse_ne_se_devine_pas_il_se_raconte()
    {
        var sharing = new KnowledgeSharing(Config.Knowledge);

        var bob = Lone("bob", new GridPosition(2, 2));
        var claire = Lone("claire", new GridPosition(4, 2));
        claire.AddToInventory(Survivor.DefaultObjectiveItem);

        bob.TakeDamage(bob.Settings.MaxHealth * 0.8f);

        // Claire voit Bob de ses yeux, mais voir n'est pas ausculter : sa sante reste inconnue.
        claire.Knowledge.RememberEntity("bob", EntityKind.Survivor, bob.Position, 1d);

        var blind = new UtilityContext(claire, 1d, Config.UtilityAi, Config.Exploration);
        Assert.Equal(1f, blind.AllyHealth, 3);

        // Ils se parlent : Bob dit dans quel etat il est, et l'objectif de soin devient atteignable.
        sharing.Share([bob, claire], 2d);

        var informed = new UtilityContext(claire, 2d, Config.UtilityAi, Config.Exploration);
        Assert.True(informed.AllyHealth < 0.3f);
        Assert.True(UtilityInputs.Read("ally_wounded", informed) > 0.7f);
    }

    [Fact]
    public void Claire_soigne_reellement_l_allie_qu_elle_rejoint()
    {
        var mission = new MissionRun(team: true);

        var bob = mission.AgentOf("bob").Survivor;
        var claire = mission.AgentOf("claire").Survivor;

        bob.TakeDamage(bob.Settings.MaxHealth * 0.7f);
        claire.AddToInventory(Survivor.DefaultObjectiveItem);

        float before = bob.Health;

        // L'effet symbolique « healed(bob) », tel que l'executeur le posera au bout de l'action.
        Assert.True(mission.AgentOf("claire").Runtime.SetFlag("healed(bob)", true, bob.Position));

        Assert.True(bob.Health > before);
        Assert.False(claire.Carries(Survivor.DefaultObjectiveItem));

        // Et c'est bien la fiche de la soignante qui decide de l'efficacite du soin.
        Assert.Equal(
            Math.Min(bob.Settings.MaxHealth, before + claire.Settings.HealAmount), bob.Health, 3);
    }

    [Fact]
    public void Rassurer_un_allie_fait_redescendre_sa_panique()
    {
        var mission = new MissionRun(team: true);

        var bob = mission.AgentOf("bob").Survivor;
        var claire = mission.AgentOf("claire");

        bob.UpdatePanic(1f, 10f);
        float terrified = bob.Panic;
        Assert.True(terrified > 0.4f);

        Assert.True(claire.Runtime.SetFlag("calmed(bob)", true, bob.Position));
        Assert.True(bob.Panic < terrified);
    }

    [Fact]
    public void Soigner_quelqu_un_qui_n_est_pas_du_groupe_echoue_au_lieu_de_soigner_dans_le_vide()
    {
        var mission = new MissionRun(team: true);
        var claire = mission.AgentOf("claire");

        claire.Survivor.AddToInventory(Survivor.DefaultObjectiveItem);

        Assert.False(claire.Runtime.SetFlag("healed(inconnu)", true, new GridPosition(0, 0)));
        Assert.True(claire.Survivor.Carries(Survivor.DefaultObjectiveItem));
    }
}
