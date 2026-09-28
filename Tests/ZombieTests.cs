using System.Reflection;
using Hordinary.AI.Zombies;
using Hordinary.Agents;
using Hordinary.World;

namespace Hordinary.Tests;

/// <summary>
/// Zombies, machine a etats et deplacement collectif (specification section 17), et critere de
/// validation du livrable 3 : « un zombie se dirige vers un bruit sans connaitre automatiquement la
/// position exacte du survivant ».
/// </summary>
public class ZombieTests
{
    /// <summary>Vision panoramique : ce sont les murs, pas l'orientation, qu'on veut tester.</summary>
    private static ZombieSettings Settings(int visionRange = 6) => new()
    {
        VisionRange = visionRange,
        FieldOfViewDegrees = 360f,
        UpdateInterval = 0.3f,
        IdleChance = 0f,
    };

    private static Zombie Lone(GridMap map, GridPosition cell, ZombieSettings? settings = null)
    {
        var zombies = settings ?? Settings();
        var knowledge = new AgentKnowledge("zombie-1", map.Width, map.Height, map.Origin);
        var zombie = new Zombie("zombie-1", knowledge, zombies, seed: 1);
        zombie.PlaceAt(cell);
        return zombie;
    }

    // --- Critere de validation du livrable 3 ------------------------------------------------

    [Fact]
    public void Un_zombie_se_dirige_vers_un_bruit_sans_connaitre_la_position_du_survivant()
    {
        // Alice est cachee derriere un mur, a l'oppose du bruit qu'elle vient de lancer.
        var map = TestMaps.Parse(
            "..............",
            "######........",
            "..............");

        var world = new WorldState(map);
        world.SetEntity("alice", EntityKind.Survivor, new GridPosition(0, 0));

        var manager = new EntityManager(world, Settings());
        var zombie = manager.Spawn("zombie-1", new GridPosition(2, 2));

        var source = new GridPosition(10, 2);
        world.Noises.Emit(NoiseAction.Distraction, source, 0d, "alice");

        manager.Think(0d);

        Assert.Equal(ZombieState.InvestigateNoise, zombie.State);

        // Il ne sait rien d'Alice : ni ou elle est, ni meme qu'elle existe.
        Assert.Null(zombie.Knowledge.GetEntity("alice"));
        Assert.DoesNotContain(zombie.Knowledge.KnownEntities.Values, b => b.Kind == EntityKind.Survivor);

        // Ce qu'il vise est une position approximative, pas la source exacte du bruit.
        var believed = Assert.Single(zombie.Knowledge.KnownNoises.Values);
        Assert.Equal(KnowledgeSource.Sound, believed.Source);

        float toNoise = Distance(zombie.Position, source);
        float toAlice = Distance(zombie.Position, new GridPosition(0, 0));

        for (int step = 0; step < 120; step++)
        {
            world.Advance(0.1d);
            manager.Advance(0.1f);
            manager.Think(world.Time);
        }

        Assert.True(
            Distance(zombie.Position, source) < toNoise,
            $"Le zombie ne s'est pas rapproche du bruit ({zombie.Position}).");
        Assert.True(
            Distance(zombie.Position, new GridPosition(0, 0)) > toAlice,
            $"Le zombie s'est rapproche d'Alice alors qu'il ne l'a jamais percue ({zombie.Position}).");

        // Alice n'a jamais ete vue : le monde le sait, le zombie non.
        Assert.Equal(new GridPosition(0, 0), world.GetEntity("alice")!.Position);
        Assert.Null(zombie.Knowledge.GetEntity("alice"));
    }

    [Fact]
    public void La_machine_a_etats_n_a_aucun_acces_au_monde_reel()
    {
        // Meme garde-fou que pour AgentKnowledge : ce qui n'est pas referencable n'est pas lisible.
        foreach (var type in new[] { typeof(ZombieStateMachine), typeof(Zombie) })
        {
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
            Assert.DoesNotContain(typeof(NoiseSystem), referenced);
            Assert.DoesNotContain(typeof(NoiseEvent), referenced);
            Assert.DoesNotContain(typeof(PerceptionSystem), referenced);
        }
    }

    // --- Perception (section 17.2) ----------------------------------------------------------

    [Fact]
    public void Un_zombie_voit_un_survivant_et_le_poursuit()
    {
        var map = TestMaps.Parse("..........", "..........");
        var world = new WorldState(map);
        world.SetEntity("alice", EntityKind.Survivor, new GridPosition(5, 0));

        var manager = new EntityManager(world, Settings());
        var zombie = manager.Spawn("zombie-1", new GridPosition(0, 0));

        manager.Think(0d);

        Assert.Equal(ZombieState.Chase, zombie.State);
        Assert.Equal(new GridPosition(5, 0), zombie.Destination);
        Assert.Equal("alice", zombie.TargetId);
    }

    [Fact]
    public void Un_survivant_cache_derriere_un_mur_reste_inconnu()
    {
        var map = TestMaps.Parse(
            "..........",
            "##########",
            "..........");

        var world = new WorldState(map);
        world.SetEntity("alice", EntityKind.Survivor, new GridPosition(5, 2));

        var manager = new EntityManager(world, Settings());
        var zombie = manager.Spawn("zombie-1", new GridPosition(5, 0));

        manager.Think(0d);

        Assert.Null(zombie.Knowledge.GetEntity("alice"));
        Assert.NotEqual(ZombieState.Chase, zombie.State);
    }

    [Fact]
    public void Un_zombie_au_contact_passe_en_attaque()
    {
        var map = TestMaps.Parse("....", "....");
        var world = new WorldState(map);
        world.SetEntity("alice", EntityKind.Survivor, new GridPosition(1, 0));

        var manager = new EntityManager(world, Settings());
        var zombie = manager.Spawn("zombie-1", new GridPosition(0, 0));

        int blows = 0;
        manager.Attacked += (_, target) =>
        {
            Assert.Equal("alice", target);
            blows++;
        };

        manager.Think(0d);
        Assert.Equal(ZombieState.Attack, zombie.State);
        Assert.Equal(1, blows);

        // Les coups suivent l'intervalle configure, pas la cadence de reflexion.
        manager.Think(0.3d);
        Assert.Equal(1, blows);

        manager.Think(2d);
        Assert.Equal(2, blows);
    }

    [Fact]
    public void Un_survivant_au_van_ne_peut_pas_etre_mordu()
    {
        // Meme situation qu'au-dessus, a une chose pres : Alice est dans la zone d'extraction.
        var map = TestMaps.Parse("....", "....");
        map.AddZone(new MapZone("Van", ZoneKind.Extraction, new GridPosition(1, 0), new GridPosition(3, 1)));

        var world = new WorldState(map);
        world.SetEntity("alice", EntityKind.Survivor, new GridPosition(1, 0));

        var manager = new EntityManager(world, Settings());
        var zombie = manager.Spawn("zombie-1", new GridPosition(0, 0));

        int blows = 0;
        manager.Attacked += (_, _) => blows++;

        manager.Think(0d);
        manager.Think(2d);
        manager.Think(4d);

        // Le zombie ne sait pas ou est le van, et on ne le lui apprend pas : il ne voit simplement
        // personne a l'interieur. Pas de coup, et pas de siege — il ne reste pas a cogner contre la
        // tole en barrant l'entree a ceux qui rentrent apres.
        Assert.NotEqual(ZombieState.Attack, zombie.State);
        Assert.Null(zombie.Knowledge.GetEntity("alice"));
        Assert.Equal(0, blows);
        Assert.True(manager.Sheltered("alice"));

        // Un pas hors du vehicule, et la protection tombe avec lui.
        world.SetEntity("alice", EntityKind.Survivor, new GridPosition(0, 1));
        Assert.False(manager.Sheltered("alice"));

        manager.Think(6d);
        Assert.Equal(1, blows);
    }

    [Fact]
    public void Sans_zone_d_extraction_personne_n_est_a_l_abri()
    {
        // L'abri se declare dans la carte ; une carte qui n'en pose pas n'en invente pas.
        var map = TestMaps.Parse("....", "....");
        var world = new WorldState(map);
        world.SetEntity("alice", EntityKind.Survivor, new GridPosition(1, 0));

        var manager = new EntityManager(world, Settings());
        manager.Spawn("zombie-1", new GridPosition(0, 0));

        int blows = 0;
        manager.Attacked += (_, _) => blows++;

        manager.Think(0d);

        Assert.False(manager.Sheltered("alice"));
        Assert.Equal(1, blows);
    }

    // --- Ordre de priorite (section 17.3) ---------------------------------------------------

    [Fact]
    public void Un_survivant_visible_prime_sur_un_bruit()
    {
        var map = TestMaps.Parse("..........", "..........");
        var world = new WorldState(map);
        world.SetEntity("alice", EntityKind.Survivor, new GridPosition(2, 0));
        world.Noises.Emit(NoiseAction.Distraction, new GridPosition(9, 1), 0d, "bob");

        var manager = new EntityManager(world, Settings());
        var zombie = manager.Spawn("zombie-1", new GridPosition(0, 0));

        manager.Think(0d);

        Assert.Equal(ZombieState.Chase, zombie.State);
        Assert.Equal(new GridPosition(2, 0), zombie.Destination);

        // Le bruit a bien ete entendu : il est simplement moins prioritaire.
        Assert.Single(zombie.Knowledge.KnownNoises);
    }

    [Fact]
    public void Un_bruit_prime_sur_une_errance()
    {
        var map = TestMaps.Parse("..........", "..........");
        var world = new WorldState(map);

        var manager = new EntityManager(world, Settings());
        var zombie = manager.Spawn("zombie-1", new GridPosition(0, 0));

        manager.Think(0d);
        Assert.True(zombie.State is ZombieState.Wander or ZombieState.Idle);

        world.Noises.Emit(NoiseAction.Distraction, new GridPosition(6, 1), 1d, "alice");
        manager.Think(1d);

        Assert.Equal(ZombieState.InvestigateNoise, zombie.State);
    }

    [Fact]
    public void Un_vieux_bruit_ne_detourne_pas_d_une_piste_plus_fraiche()
    {
        var map = TestMaps.Parse("..........", "..........");
        var world = new WorldState(map);

        var manager = new EntityManager(world, Settings());
        var zombie = manager.Spawn("zombie-1", new GridPosition(0, 0));

        // Un bruit lointain d'abord...
        world.Noises.Emit(NoiseAction.Distraction, new GridPosition(9, 1), 0d, "alice");
        manager.Think(0d);
        Assert.Equal(ZombieState.InvestigateNoise, zombie.State);

        // ... puis Alice apparait et disparait aussitot : cette information est plus recente.
        world.SetEntity("alice", EntityKind.Survivor, new GridPosition(3, 0));
        manager.Think(1d);
        Assert.Equal(ZombieState.Chase, zombie.State);

        world.RemoveEntity("alice");
        manager.Think(2d);

        Assert.Equal(ZombieState.SearchLastKnownPosition, zombie.State);
    }

    [Fact]
    public void Un_zombie_qui_perd_sa_cible_fouille_puis_renonce()
    {
        var map = TestMaps.Parse(
            "..........",
            "..........",
            "..........");

        var world = new WorldState(map);
        world.SetEntity("alice", EntityKind.Survivor, new GridPosition(4, 1));

        var settings = new ZombieSettings
        {
            VisionRange = 6,
            FieldOfViewDegrees = 360f,
            IdleChance = 0f,
            SearchDuration = 4f,
            SightMemory = 0.5f,
        };

        var manager = new EntityManager(world, settings);
        var zombie = manager.Spawn("zombie-1", new GridPosition(0, 1));

        manager.Think(0d);
        Assert.Equal(ZombieState.Chase, zombie.State);

        // Alice s'evapore hors de vue : le zombie garde la derniere position connue.
        world.SetEntity("alice", EntityKind.Survivor, new GridPosition(9, 1));
        manager.Think(1d);

        Assert.Equal(ZombieState.SearchLastKnownPosition, zombie.State);
        Assert.Equal(new GridPosition(4, 1), zombie.LastKnownTargetPosition);

        // Il fouille les alentours un moment...
        manager.Think(3d);
        Assert.Equal(ZombieState.SearchLastKnownPosition, zombie.State);

        // ... puis abandonne et se remet a errer.
        manager.Think(30d);
        Assert.True(
            zombie.State is ZombieState.Wander or ZombieState.Idle,
            $"Le zombie devrait avoir renonce, il est en {zombie.State}.");
        Assert.Null(zombie.TargetId);
    }

    // --- Errance (section 17.1) -------------------------------------------------------------

    [Fact]
    public void Un_zombie_sans_rien_a_faire_erre_dans_ce_qu_il_connait()
    {
        var map = new GridMap(21, 21, new GridPosition(-10, -10), CellType.Floor);
        var world = new WorldState(map);

        var manager = new EntityManager(world, Settings());
        var zombie = manager.Spawn("zombie-1", GridPosition.Zero);

        var start = zombie.Position;

        for (int step = 0; step < 200; step++)
        {
            world.Advance(0.1d);
            manager.Advance(0.1f);
            manager.Think(world.Time);
        }

        Assert.NotEqual(start, zombie.Position);
        Assert.True(zombie.State is ZombieState.Wander or ZombieState.Idle);

        // Il n'erre que la ou il a deja regarde : sa carte mentale reste tres partielle.
        Assert.True(zombie.Knowledge.ExploredRatio < 1f);
    }

    [Fact]
    public void Une_errance_ne_vise_jamais_une_case_jamais_observee()
    {
        var map = new GridMap(21, 21, new GridPosition(-10, -10), CellType.Floor);
        var world = new WorldState(map);

        var manager = new EntityManager(world, Settings(visionRange: 3));
        var zombie = manager.Spawn("zombie-1", GridPosition.Zero);

        for (int step = 0; step < 40; step++)
        {
            manager.Think(step * 0.3d);

            if (zombie.State != ZombieState.Wander || zombie.Destination is not { } target) continue;

            Assert.True(
                zombie.Knowledge.HasObserved(target),
                $"Le zombie erre vers {target}, une case qu'il n'a jamais vue.");
        }
    }

    // --- Deplacement collectif (section 17.4) -----------------------------------------------

    [Fact]
    public void L_attraction_rapproche_de_la_destination()
    {
        var map = TestMaps.Parse("..........", "..........");
        var zombie = Lone(map, new GridPosition(0, 0));

        var step = new HordeBehaviour().ChooseStep(zombie, new GridPosition(9, 0), [zombie], map);

        Assert.NotNull(step);
        Assert.True(step.Value.X > 0, $"Le zombie devrait avancer vers la droite, il vise {step}.");
    }

    [Fact]
    public void La_separation_empeche_deux_zombies_d_occuper_la_meme_case()
    {
        var map = TestMaps.Parse("...", "...", "...");

        var mover = Lone(map, new GridPosition(0, 1));
        var blocker = new Zombie("zombie-2", new AgentKnowledge("zombie-2", map.Width, map.Height), Settings(), 2);
        blocker.PlaceAt(new GridPosition(1, 1));

        var horde = new List<Zombie> { mover, blocker };
        var step = new HordeBehaviour().ChooseStep(mover, new GridPosition(2, 1), horde, map);

        Assert.NotEqual(new GridPosition(1, 1), step);
    }

    [Fact]
    public void Un_zombie_ne_traverse_ni_les_murs_ni_les_coins()
    {
        var map = TestMaps.Parse(
            ".#.",
            "##.",
            "...");

        var zombie = Lone(map, new GridPosition(0, 0));

        // La destination est de l'autre cote ; toutes les issues sont murees, y compris la diagonale.
        var step = new HordeBehaviour().ChooseStep(zombie, new GridPosition(2, 2), [zombie], map);

        Assert.Null(step);
    }

    [Fact]
    public void La_cohesion_rassemble_les_zombies_proches()
    {
        var map = new GridMap(21, 21, new GridPosition(-10, -10), CellType.Floor);

        var settings = new ZombieSettings
        {
            AttractionWeight = 0f,
            SeparationWeight = 0f,
            CohesionWeight = 1f,
            CohesionRadius = 8,
        };

        var lonely = Lone(map, new GridPosition(0, 0), settings);
        var group = new List<Zombie> { lonely };

        for (int i = 0; i < 3; i++)
        {
            var mate = new Zombie($"mate-{i}", new AgentKnowledge($"mate-{i}", map.Width, map.Height, map.Origin), settings, i);
            mate.PlaceAt(new GridPosition(5, i));
            group.Add(mate);
        }

        // Sans attraction ni separation, seule la cohesion parle : il rejoint le groupe.
        var step = new HordeBehaviour(settings).ChooseStep(lonely, new GridPosition(0, 0), group, map);

        Assert.NotNull(step);
        Assert.True(step.Value.X > 0, $"Le zombie devrait se rapprocher du groupe, il vise {step}.");
    }

    // --- Population -------------------------------------------------------------------------

    /// <summary>
    /// La horde livree compte cinq zombies, et c'est un ecart assume a la specification (section 4 :
    /// « 20 a 30 zombies »).
    ///
    /// L'interet de ce prototype est de <b>voir</b> une decision : un survivant qui repere une
    /// menace, replanifie, communique et contourne. A vingt-quatre zombies sur une grille de 26 x 29,
    /// les trois survivants passent la partie en fuite et l'on n'observe plus jamais un plan aller a
    /// son terme — le mode debug n'a plus rien a montrer. Cinq zombies laissent la place aux
    /// rencontres sans les rendre permanentes.
    ///
    /// Le nombre reste une valeur de configuration : remonter <c>zombies.count</c> a 24 dans
    /// <c>config/game_config.json</c> suffit a retrouver la densite de la specification, sans
    /// recompilation. C'est ce test-ci qu'il faudra alors ajuster, et lui seul.
    /// </summary>
    [Fact]
    public void La_horde_livree_compte_cinq_zombies()
    {
        var config = Hordinary.Config.GameConfig.FromJson(DemoMap.ReadConfig("game_config.json"));
        Assert.Equal(5, config.Zombies.Count);
    }

    [Fact]
    public void La_horde_apparait_sur_des_cases_libres_et_distinctes_loin_du_depart()
    {
        var map = DemoMap.Load();
        var world = new WorldState(map);
        var config = Hordinary.Config.GameConfig.FromJson(DemoMap.ReadConfig("game_config.json"));

        var start = map.FindWalkableIn(map.GetZone(ZoneKind.Start)!)!.Value;

        var manager = new EntityManager(world, config.Zombies);
        int spawned = manager.SpawnHorde(start);

        Assert.Equal(config.Zombies.Count, spawned);
        Assert.Equal(spawned, manager.Zombies.Select(z => z.Position).Distinct().Count());

        foreach (var zombie in manager.Zombies)
        {
            Assert.True(map.IsWalkable(zombie.Position), $"{zombie.Id} est apparu dans un mur.");
            Assert.True(
                Distance(zombie.Position, start) >= config.Zombies.MinimumSpawnDistance,
                $"{zombie.Id} est apparu trop pres du depart ({zombie.Position}).");

            // Aucun zombie ne nait en sachant quoi que ce soit.
            Assert.Empty(zombie.Knowledge.KnownEntities);
            Assert.Equal(0f, zombie.Knowledge.ExploredRatio, 3);
        }
    }

    [Fact]
    public void La_horde_est_reproductible_a_graine_egale()
    {
        var config = Hordinary.Config.GameConfig.FromJson(DemoMap.ReadConfig("game_config.json"));
        var start = DemoMap.Load().FindWalkableIn(DemoMap.Load().GetZone(ZoneKind.Start)!)!.Value;

        var first = Positions(config.Zombies, start);
        var second = Positions(config.Zombies, start);

        Assert.Equal(first, second);

        static List<GridPosition> Positions(ZombieSettings settings, GridPosition start)
        {
            var manager = new EntityManager(new WorldState(DemoMap.Load()), settings);
            manager.SpawnHorde(start);
            return manager.Zombies.Select(z => z.Position).ToList();
        }
    }

    [Fact]
    public void La_horde_erre_sur_la_carte_livree_sans_se_coincer_dans_les_murs()
    {
        var map = DemoMap.Load();
        var world = new WorldState(map);
        var config = Hordinary.Config.GameConfig.FromJson(DemoMap.ReadConfig("game_config.json"));

        var start = map.FindWalkableIn(map.GetZone(ZoneKind.Start)!)!.Value;

        var manager = new EntityManager(world, config.Zombies, config.Knowledge);
        manager.SpawnHorde(start);

        var origins = manager.Zombies.Select(z => z.Position).ToList();

        for (int step = 0; step < 300; step++)
        {
            world.Advance(0.1d);
            manager.Advance(0.1f);
            manager.Think(world.Time);
        }

        foreach (var zombie in manager.Zombies)
        {
            Assert.True(map.IsWalkable(zombie.Position), $"{zombie.Id} a fini dans un mur ({zombie.Position}).");
        }

        // Ils n'ont vu personne : le monde connait Alice, eux non.
        Assert.All(manager.Zombies, z => Assert.DoesNotContain(
            z.Knowledge.KnownEntities.Values, b => b.Kind == EntityKind.Survivor));

        int moved = manager.Zombies.Zip(origins, (zombie, origin) => zombie.Position != origin).Count(m => m);
        Assert.True(moved > manager.Zombies.Count / 2, $"Seuls {moved} zombies ont bouge en 30 secondes.");
    }

    // --- Une distraction attire plusieurs zombies (scenario de la section 3) ----------------

    [Fact]
    public void Une_distraction_attire_plusieurs_zombies()
    {
        var map = new GridMap(31, 31, new GridPosition(-15, -15), CellType.Floor);
        var world = new WorldState(map);

        var manager = new EntityManager(world, Settings());

        var spawns = new[]
        {
            new GridPosition(-6, 0), new GridPosition(6, 0),
            new GridPosition(0, -6), new GridPosition(0, 6),
        };

        for (int i = 0; i < spawns.Length; i++) manager.Spawn($"zombie-{i}", spawns[i]);

        world.Noises.Emit(NoiseAction.Distraction, GridPosition.Zero, 0d, "alice");
        manager.Think(0d);

        Assert.Equal(4, manager.Zombies.Count(z => z.State == ZombieState.InvestigateNoise));

        var before = manager.Zombies.Select(z => Distance(z.Position, GridPosition.Zero)).ToList();

        for (int step = 0; step < 30; step++)
        {
            world.Advance(0.1d);
            manager.Advance(0.1f);
            manager.Think(world.Time);
        }

        var after = manager.Zombies.Select(z => Distance(z.Position, GridPosition.Zero)).ToList();

        for (int i = 0; i < after.Count; i++)
        {
            Assert.True(
                after[i] < before[i],
                $"{manager.Zombies[i].Id} ne s'est pas rapproche de la distraction ({before[i]} -> {after[i]}).");
        }
    }

    private static float Distance(GridPosition a, GridPosition b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}
