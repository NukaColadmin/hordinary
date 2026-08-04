using System.Reflection;
using Hordinary.Agents;
using Hordinary.Config;
using Hordinary.World;

namespace Hordinary.Tests;

/// <summary>
/// Tests exiges par C-tests.md, section « Connaissances » : un agent ne connait pas une cellule non
/// observee ; une observation directe met a jour une croyance ; la confiance d'une position de
/// zombie diminue ; une information recente remplace une information ancienne ; le partage de
/// connaissances n'accede pas au <see cref="WorldState"/>.
/// </summary>
public class KnowledgeTests
{
    private static AgentKnowledge Empty(int width = 5, int height = 5, KnowledgeSettings? settings = null)
        => new("alice", width, height, default, settings);

    // --- Un agent ne connait pas une cellule non observee ----------------------------------

    [Fact]
    public void Une_cellule_jamais_observee_reste_inconnue()
    {
        var knowledge = Empty();
        var cell = new GridPosition(3, 3);

        Assert.False(knowledge.HasObserved(cell));
        Assert.Null(knowledge.GetCell(cell));

        // La carte mentale la decrit comme inconnue, et non comme un sol libre.
        Assert.Equal(CellType.Unknown, knowledge.MentalMap.GetCell(cell));
        Assert.False(knowledge.MentalMap.GetCell(cell).IsKnownWalkable());
    }

    [Fact]
    public void Une_case_vide_observee_se_distingue_d_une_case_non_observee()
    {
        var knowledge = Empty();
        var observed = new GridPosition(1, 1);
        var never = new GridPosition(4, 4);

        knowledge.ObserveCell(observed, CellType.Floor, indoor: false, time: 0d);

        // Les deux cases sont « sans obstacle », mais une seule est connue.
        Assert.True(knowledge.HasObserved(observed));
        Assert.False(knowledge.HasObserved(never));
        Assert.Equal(CellType.Floor, knowledge.MentalMap.GetCell(observed));
        Assert.Equal(CellType.Unknown, knowledge.MentalMap.GetCell(never));
    }

    [Fact]
    public void Une_observation_hors_du_terrain_est_ignoree()
    {
        var knowledge = Empty();

        Assert.False(knowledge.ObserveCell(new GridPosition(99, 99), CellType.Floor, false, 0d));
        Assert.Empty(knowledge.KnownCells);
    }

    // --- Une observation directe met a jour une croyance -----------------------------------

    [Fact]
    public void Une_observation_directe_enregistre_type_source_et_horodatage()
    {
        var knowledge = Empty();
        var cell = new GridPosition(2, 1);

        Assert.True(knowledge.ObserveCell(cell, CellType.Wall, indoor: true, time: 12.5d));

        var belief = knowledge.GetCell(cell);
        Assert.NotNull(belief);
        Assert.Equal(CellType.Wall, belief.Value);
        Assert.True(belief.Indoor);
        Assert.Equal(1f, belief.Confidence, 3);
        Assert.Equal(12.5d, belief.LastObservedAt, 3);
        Assert.Equal(KnowledgeSource.DirectObservation, belief.Source);

        // La carte mentale suit la croyance : c'est elle que parcourra A*.
        Assert.Equal(CellType.Wall, knowledge.MentalMap.GetCell(cell));
        Assert.False(knowledge.MentalMap.IsWalkable(cell));
    }

    [Fact]
    public void Une_observation_directe_corrige_une_croyance_devenue_fausse()
    {
        var knowledge = Empty();
        var door = new GridPosition(2, 2);

        knowledge.ObserveCell(door, CellType.DoorClosed, indoor: false, time: 0d);
        knowledge.ObserveCell(door, CellType.DoorOpen, indoor: false, time: 10d);

        Assert.Equal(CellType.DoorOpen, knowledge.GetCell(door)!.Value);
        Assert.Equal(CellType.DoorOpen, knowledge.MentalMap.GetCell(door));
    }

    // --- La confiance d'une position de zombie diminue -------------------------------------

    [Fact]
    public void La_confiance_dans_la_position_d_un_zombie_diminue_avec_le_temps()
    {
        var settings = new KnowledgeSettings { ZombieConfidenceDecay = 0.08f };
        var knowledge = Empty(settings: settings);

        knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(2, 2), time: 0d);
        var belief = knowledge.GetEntity("zombie-1")!;

        Assert.Equal(1f, knowledge.ConfidenceIn(belief, 0d), 3);
        Assert.Equal(0.6f, knowledge.ConfidenceIn(belief, 5d), 3);
        Assert.Equal(0.2f, knowledge.ConfidenceIn(belief, 10d), 3);

        // La confiance ne devient jamais negative : max(0, ...).
        Assert.Equal(0f, knowledge.ConfidenceIn(belief, 60d), 3);
    }

    [Fact]
    public void Un_zombie_oublie_n_est_plus_pris_en_compte_par_une_decision()
    {
        var knowledge = Empty(settings: new KnowledgeSettings { ZombieConfidenceDecay = 0.5f });
        knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(1, 1), time: 0d);

        Assert.Single(knowledge.BelievedEntities(0d));
        Assert.Empty(knowledge.BelievedEntities(10d));
    }

    [Fact]
    public void Un_allie_est_oublie_moins_vite_qu_un_zombie()
    {
        var settings = new KnowledgeSettings { ZombieConfidenceDecay = 0.08f, AllyConfidenceDecay = 0.05f };
        var knowledge = Empty(settings: settings);

        knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(1, 1), 0d);
        knowledge.RememberEntity("bob", EntityKind.Survivor, new GridPosition(2, 2), 0d);

        float zombie = knowledge.ConfidenceIn(knowledge.GetEntity("zombie-1")!, 5d);
        float ally = knowledge.ConfidenceIn(knowledge.GetEntity("bob")!, 5d);

        Assert.True(ally > zombie);
    }

    [Fact]
    public void Une_information_statique_ne_perd_pas_en_fiabilite()
    {
        var knowledge = Empty();
        var wall = new GridPosition(1, 0);

        knowledge.ObserveCell(wall, CellType.Wall, indoor: false, time: 0d);
        knowledge.RememberLocation("Pharmacie", ZoneKind.Pharmacy, new GridPosition(4, 4), 0d);

        Assert.Equal(1f, knowledge.ConfidenceIn(knowledge.GetCell(wall)!, 10_000d), 3);
        Assert.Equal(0f, knowledge.DecayRateFor(knowledge.GetCell(wall)!), 3);
    }

    [Fact]
    public void L_etat_d_une_porte_perd_en_fiabilite_contrairement_a_un_mur()
    {
        var settings = new KnowledgeSettings { DoorStateConfidenceDecay = 0.02f };
        var knowledge = Empty(settings: settings);

        var wall = new GridPosition(0, 0);
        var door = new GridPosition(1, 0);

        knowledge.ObserveCell(wall, CellType.Wall, false, 0d);
        knowledge.ObserveCell(door, CellType.DoorClosed, false, 0d);

        Assert.Equal(1f, knowledge.ConfidenceIn(knowledge.GetCell(wall)!, 20d), 3);
        Assert.Equal(0.6f, knowledge.ConfidenceIn(knowledge.GetCell(door)!, 20d), 3);
    }

    // --- Une information recente remplace une information ancienne ------------------------

    [Fact]
    public void Une_information_recente_remplace_une_information_ancienne()
    {
        var knowledge = Empty();

        knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(1, 1), time: 0d);
        Assert.True(knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(3, 3), time: 5d));

        var belief = knowledge.GetEntity("zombie-1")!;
        Assert.Equal(new GridPosition(3, 3), belief.Value);
        Assert.Equal(5d, belief.LastObservedAt, 3);
    }

    [Fact]
    public void Une_information_perimee_ne_remplace_pas_une_information_recente()
    {
        var knowledge = Empty();

        knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(3, 3), time: 5d);

        // Un allie rapporte une observation plus vieille : elle doit etre rejetee.
        Assert.False(knowledge.RememberEntity(
            "zombie-1", EntityKind.Zombie, new GridPosition(1, 1), time: 1d, KnowledgeSource.AllyCommunication));

        var belief = knowledge.GetEntity("zombie-1")!;
        Assert.Equal(new GridPosition(3, 3), belief.Value);
        Assert.Equal(KnowledgeSource.DirectObservation, belief.Source);
    }

    [Fact]
    public void Une_cellule_perimee_ne_remplace_pas_une_observation_recente()
    {
        var knowledge = Empty();
        var door = new GridPosition(2, 2);

        knowledge.ObserveCell(door, CellType.DoorOpen, false, time: 10d);

        Assert.False(knowledge.ObserveCell(door, CellType.DoorClosed, false, time: 2d));
        Assert.Equal(CellType.DoorOpen, knowledge.MentalMap.GetCell(door));
    }

    // --- Memoire de poursuite : « il me suivait », pas seulement « il etait la » -----------

    [Fact]
    public void Deux_observations_successives_donnent_un_cap()
    {
        var knowledge = Empty(20, 20);

        knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(6, 4), 0d);
        Assert.False(knowledge.GetEntity("zombie-1")!.IsMoving);

        // Une seule observation ne dit rien du mouvement ; deux, si.
        knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(5, 4), 1d);

        var belief = knowledge.GetEntity("zombie-1")!;
        Assert.True(belief.IsMoving);
        Assert.Equal(-1f, belief.Heading.X, 3);
        Assert.Equal(0f, belief.Heading.Y, 3);
    }

    [Fact]
    public void La_position_supposee_prolonge_le_dernier_cap_connu()
    {
        var knowledge = Empty(20, 20);

        knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(6, 4), 0d);
        knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(5, 4), 1d);

        var belief = knowledge.GetEntity("zombie-1")!;

        // Deux secondes plus tard, l'agent ne le croit plus la ou il l'a vu : il le croit deux
        // cases plus loin, dans la direction ou il allait.
        Assert.Equal(new GridPosition(5, 4), knowledge.PredictedPositionOf(belief, 1d));
        Assert.Equal(new GridPosition(3, 4), knowledge.PredictedPositionOf(belief, 3d));
    }

    [Fact]
    public void L_extrapolation_s_arrete_a_l_horizon_de_prediction()
    {
        var knowledge = Empty(20, 20, new KnowledgeSettings { PredictionHorizon = 3f });

        knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(10, 4), 0d);
        knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(9, 4), 1d);

        var belief = knowledge.GetEntity("zombie-1")!;

        // Au-dela de l'horizon, la supposition se fige : deviner une trajectoire une minute apres
        // l'avoir perdu de vue serait une invention, pas une croyance.
        Assert.Equal(new GridPosition(6, 4), knowledge.PredictedPositionOf(belief, 4d));
        Assert.Equal(new GridPosition(6, 4), knowledge.PredictedPositionOf(belief, 60d));
    }

    [Fact]
    public void Un_mur_connu_arrete_la_supposition()
    {
        var knowledge = Empty(20, 20);

        knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(6, 4), 0d);
        knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(5, 4), 1d);

        var belief = knowledge.GetEntity("zombie-1")!;
        Assert.Equal(new GridPosition(3, 4), knowledge.PredictedPositionOf(belief, 3d));

        // L'agent a vu un mur la : il ne prete a personne le pouvoir de le traverser.
        knowledge.ObserveCell(new GridPosition(3, 4), CellType.Wall, indoor: false, time: 2d);
        Assert.Equal(new GridPosition(5, 4), knowledge.PredictedPositionOf(belief, 3d));
    }

    [Fact]
    public void Une_cellule_jamais_observee_n_arrete_pas_la_supposition()
    {
        var knowledge = Empty(20, 20);

        knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(6, 4), 0d);
        knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(5, 4), 1d);

        // (3,4) n'a jamais ete observee : l'agent ignore ce qu'elle contient, il ne peut donc pas
        // s'en servir pour ecarter l'hypothese.
        Assert.False(knowledge.HasObserved(new GridPosition(3, 4)));
        Assert.Equal(new GridPosition(3, 4), knowledge.PredictedPositionOf(knowledge.GetEntity("zombie-1")!, 3d));
    }

    [Fact]
    public void Une_entite_revue_immobile_perd_progressivement_son_cap()
    {
        var knowledge = Empty(20, 20);

        knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(6, 4), 0d);
        knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(5, 4), 1d);
        float running = knowledge.GetEntity("zombie-1")!.Heading.Length();

        knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(5, 4), 2d);
        float stopping = knowledge.GetEntity("zombie-1")!.Heading.Length();

        knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(5, 4), 3d);
        float stopped = knowledge.GetEntity("zombie-1")!.Heading.Length();

        Assert.True(stopping < running);
        Assert.True(stopped < stopping);
    }

    [Fact]
    public void Deux_apercus_trop_espaces_ne_font_pas_une_trajectoire()
    {
        var knowledge = Empty(20, 20, new KnowledgeSettings { HeadingMemory = 2f });

        knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(10, 4), 0d);
        knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(4, 4), 30d);

        // Six cases d'ecart en trente secondes ne decrivent aucun mouvement : l'agent s'abstient.
        Assert.False(knowledge.GetEntity("zombie-1")!.IsMoving);
    }

    [Fact]
    public void Un_cap_credite_ne_depasse_pas_la_vitesse_maximale_suivie()
    {
        var knowledge = Empty(40, 40, new KnowledgeSettings { MaximumTrackedSpeed = 3f });

        // Un saut de position — teleportation apparente, information d'allie mal datee — ne doit
        // pas se transformer en zombie supersonique dans la carte mentale.
        knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(30, 4), 0d);
        knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(10, 4), 1d);

        Assert.Equal(3f, knowledge.GetEntity("zombie-1")!.Heading.Length(), 3);
    }

    [Fact]
    public void Un_dementi_efface_aussi_le_cap()
    {
        var knowledge = Empty(20, 20);

        knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(6, 4), 0d);
        knowledge.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(5, 4), 1d);

        knowledge.ForgetEntityAt("zombie-1", 2d);

        var belief = knowledge.GetEntity("zombie-1")!;
        Assert.Equal(0f, belief.Confidence, 3);
        Assert.False(belief.IsMoving);
    }

    // --- Le partage de connaissances n'accede pas au WorldState ---------------------------

    [Fact]
    public void AgentKnowledge_n_expose_aucun_acces_au_monde_reel()
    {
        var type = typeof(AgentKnowledge);

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
    public void Le_partage_ne_transmet_que_ce_que_l_allie_savait()
    {
        var alice = new AgentKnowledge("alice", 5, 5);
        var bob = new AgentKnowledge("bob", 5, 5);

        // Bob a vu un mur ; personne n'a observe (4,4), ou se trouve pourtant un obstacle reel.
        bob.ObserveCell(new GridPosition(1, 1), CellType.Wall, false, 3d);

        int learned = alice.LearnFrom(bob);

        Assert.Equal(1, learned);
        Assert.True(alice.HasObserved(new GridPosition(1, 1)));
        Assert.Equal(CellType.Wall, alice.MentalMap.GetCell(new GridPosition(1, 1)));

        // Rien d'autre n'a fuite : la case jamais observee par Bob reste inconnue pour Alice.
        Assert.False(alice.HasObserved(new GridPosition(4, 4)));
    }

    [Fact]
    public void Une_information_rapportee_par_un_allie_vaut_moins_qu_une_observation()
    {
        var settings = new KnowledgeSettings { SharedConfidencePenalty = 0.2f };
        var alice = new AgentKnowledge("alice", 5, 5, default, settings);
        var bob = new AgentKnowledge("bob", 5, 5, default, settings);

        bob.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(2, 2), 4d);
        alice.LearnFrom(bob);

        var belief = alice.GetEntity("zombie-1")!;
        Assert.Equal(KnowledgeSource.AllyCommunication, belief.Source);
        Assert.Equal(0.8f, belief.Confidence, 3);
        Assert.Equal(4d, belief.LastObservedAt, 3);
    }

    [Fact]
    public void Un_allie_transmet_le_cap_qu_il_a_observe()
    {
        var alice = new AgentKnowledge("alice", 20, 20);
        var bob = new AgentKnowledge("bob", 20, 20);

        bob.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(6, 4), 0d);
        bob.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(5, 4), 1d);

        alice.LearnFrom(bob);

        // « Il descend la rue vers l'ouest » est une information a part entiere : Alice la recoit
        // telle quelle, au lieu de la recalculer a partir d'une position isolee.
        var belief = alice.GetEntity("zombie-1")!;
        Assert.Equal(-1f, belief.Heading.X, 3);
        Assert.Equal(new GridPosition(3, 4), alice.PredictedPositionOf(belief, 3d));
    }

    [Fact]
    public void Le_partage_ne_regresse_pas_une_observation_plus_recente()
    {
        var alice = new AgentKnowledge("alice", 5, 5);
        var bob = new AgentKnowledge("bob", 5, 5);

        alice.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(3, 3), 10d);
        bob.RememberEntity("zombie-1", EntityKind.Zombie, new GridPosition(1, 1), 2d);

        alice.LearnFrom(bob);

        var belief = alice.GetEntity("zombie-1")!;
        Assert.Equal(new GridPosition(3, 3), belief.Value);
        Assert.Equal(KnowledgeSource.DirectObservation, belief.Source);
    }
}
