using Hordinary.AI.Navigation;
using Hordinary.AI.Utility;
using Hordinary.Agents;
using Hordinary.Config;
using Hordinary.Game;
using Hordinary.World;
using Hordinary.World.MapGeneration;

namespace Hordinary.Tests;

/// <summary>
/// Tests du livrable 13 : la generation de quartiers.
///
/// Un generateur ne se teste pas comme une fonction : il n'a pas une bonne reponse, il en a des
/// milliers, et ce qu'on verifie n'est jamais « le quartier attendu » mais « tout quartier rendu
/// tient le contrat ». Ces tests sont donc ecrits en deux familles :
///
/// <list type="bullet">
/// <item>ce qui doit valoir pour <b>chaque</b> profil livre et plusieurs graines — les neuf clauses
/// de jouabilite, la ceinture etanche, la horde a distance ;</item>
/// <item>ce qui doit valoir <b>une fois pour toutes</b> — deux generations sur la meme graine sont
/// le meme quartier, un quartier exporte et relu est le meme, un quartier invalide est jete et non
/// rafistole.</item>
/// </list>
///
/// Aucun profil n'est nomme ici : les tests parcourent <c>config/mapgen.json</c>. Ajouter une
/// ambiance dans le fichier la fait entrer dans la suite sans toucher a une ligne de test — c'est la
/// meme regle que partout ailleurs, et elle vaut aussi pour ce qui verifie.
/// </summary>
public class MapGenerationTests
{
    private static MapGenConfig MapGen { get; } = MapGenConfig.FromJson(DemoMap.ReadConfig("mapgen.json"));

    private static GameConfig Config => MissionRun.Config;

    /// <summary>Quelques graines fixes : la suite doit etre reproductible, pas aleatoire.</summary>
    private static readonly int[] Seeds = [7, 1234, 20260728, 90210];

    /// <summary>
    /// Une mission de test, pour ne pas dependre du contenu de <c>config/missions.json</c> — celui-ci
    /// est du contenu de jeu, il changera, et un test qui casse quand une mission est renommee ne
    /// verifie pas le generateur.
    /// </summary>
    private static MissionDefinition MissionFor(string profile) => new()
    {
        Id = $"test-{profile}",
        Name = "Mission de test",
        Target = new ZoneTarget(ZoneKind.Pharmacy),
        MapProfile = profile,
    };

    private static DistrictRequest Request(MapProfile profile, int seed, int zombies = 12) => new()
    {
        Mission = MissionFor(profile.Id),
        Seed = seed,
        ZombieCount = zombies,
        MinimumSpawnDistance = Config.Zombies.MinimumSpawnDistance,
    };

    private static GeneratedDistrict Generate(MapProfile profile, int seed, int zombies = 12)
        => new DistrictGenerator(MapGen).Generate(Request(profile, seed, zombies));

    // --- Determinisme ---------------------------------------------------------------------------

    /// <summary>
    /// La graine est la seule entree du generateur. Deux generations qui n'en partagent que la valeur
    /// — deux instances, deux tirages, deux quartiers construits de zero — doivent rendre le meme
    /// quartier, octet pour octet apres export.
    ///
    /// C'est ce qui rend une graine partageable, un bug de carte reproductible, et l'ecran de camp
    /// utile. Le hachage d'adresse et le compteur par canal de <see cref="DistrictRandom"/> n'ont
    /// aucune autre raison d'exister.
    /// </summary>
    [Fact]
    public void Une_meme_graine_redonne_exactement_le_meme_quartier()
    {
        foreach (var profile in MapGen.Profiles)
        {
            string first = DistrictExport.ToJson(Generate(profile, 4242));
            string second = DistrictExport.ToJson(Generate(profile, 4242));

            Assert.Equal(first, second);
        }
    }

    /// <summary>Deux graines differentes ne doivent pas rendre le meme quartier.</summary>
    [Fact]
    public void Deux_graines_differentes_donnent_deux_quartiers_differents()
    {
        foreach (var profile in MapGen.Profiles)
        {
            Assert.NotEqual(
                DistrictExport.ToJson(Generate(profile, 4242)),
                DistrictExport.ToJson(Generate(profile, 4243)));
        }
    }

    // --- Le contrat de jouabilite ---------------------------------------------------------------

    /// <summary>
    /// Les neuf clauses, sur chaque profil et chaque graine. Le quartier est reconstruit a la graine
    /// <b>acceptee</b> puis relu : cela verifie du meme coup que la reconstruction redonne le meme
    /// brouillon, sans quoi le rapport porterait sur une autre carte que celle qui a ete rendue.
    /// </summary>
    [Fact]
    public void Chaque_quartier_rendu_tient_le_contrat_de_jouabilite()
    {
        var generator = new DistrictGenerator(MapGen);

        foreach (var profile in MapGen.Profiles)
        {
            foreach (int seed in Seeds)
            {
                var request = Request(profile, seed);
                var district = generator.Generate(request);

                var draft = generator.Build(profile, request, district.Seed);
                var report = ValidationPass.Apply(draft);

                Assert.True(report.Accepted, $"{profile.Id} graine {district.Seed} : {report}");
            }
        }
    }

    /// <summary>
    /// La premiere clause, isolee : l'objectif est joignable depuis la depose <b>au prix d'un
    /// effort</b> — une porte fermee s'ouvre, une porte verrouillee se force, une fenetre se brise.
    ///
    /// Le test verifie aussi qu'il y avait bien quelque chose a forcer. Un quartier sans une seule
    /// porte verrouillee satisferait la clause sans rien prouver, et c'est exactement le genre de
    /// succes vide qu'un generateur produit quand un reglage passe silencieusement a zero.
    /// </summary>
    [Fact]
    public void La_depose_et_l_objectif_restent_relies_a_travers_les_portes_verrouillees()
    {
        var generator = new DistrictGenerator(MapGen);
        int lockedSeen = 0;

        foreach (var profile in MapGen.Profiles)
        {
            foreach (int seed in Seeds)
            {
                var request = Request(profile, seed);
                var district = generator.Generate(request);
                var draft = generator.Build(profile, request, district.Seed);

                var reachable = ValidationPass.Reach(draft);
                var objective = district.Objective;

                Assert.NotNull(objective);
                Assert.Contains(objective.Positions(), reachable.Contains);

                lockedSeen += district.Map.AllPositions().Count(
                    cell => district.Map.GetCell(cell) == CellType.DoorLocked);
            }
        }

        Assert.True(lockedSeen > 0, "Aucun quartier ne pose de porte verrouillee : la clause ne prouve rien.");
    }

    // --- La ceinture ----------------------------------------------------------------------------

    /// <summary>
    /// Rien de praticable ne sort du quartier jouable, et tout ce qui en sort est de la ceinture.
    ///
    /// C'est la clause que le brouillard simplifie : il n'y a plus de terminaison de rue a raccorder,
    /// plus de decor a rendre au-dela, plus de fondu. Une rue qui atteint le bord entre dans le
    /// brouillard, et le brouillard ne se traverse pas.
    /// </summary>
    [Fact]
    public void Rien_de_praticable_ne_sort_du_quartier_jouable()
    {
        foreach (var profile in MapGen.Profiles)
        {
            var district = Generate(profile, 8080);
            var map = district.Map;

            foreach (var cell in map.AllPositions())
            {
                if (district.Playable.Contains(cell)) continue;

                Assert.Equal(CellType.Border, map.GetCell(cell));
                Assert.False(map.IsWalkable(cell), $"{cell} est praticable hors du quartier jouable.");
            }
        }
    }

    /// <summary>Ni zombie, ni objet au sol, ni meuble dans la ceinture.</summary>
    [Fact]
    public void Aucune_entite_n_est_posee_dans_la_ceinture()
    {
        foreach (var profile in MapGen.Profiles)
        {
            foreach (int seed in Seeds)
            {
                var district = Generate(profile, seed);

                var placed = district.ZombieSpawns
                    .Concat(district.Items.Select(item => item.Position))
                    .Concat(district.Heavy.SelectMany(heavy => heavy.Cells));

                foreach (var cell in placed)
                    Assert.True(district.Playable.Contains(cell), $"{cell} est dans la ceinture.");
            }
        }
    }

    /// <summary>
    /// Un meuble ferme toutes les cases qu'il couvre, et pas seulement celle de son ancre.
    ///
    /// C'est ce qui separe une voiture d'un dessin de voiture : une epave fait quatre cases, on la
    /// contourne sur toute son emprise, et la navigation le sait avant le premier pas. La taille
    /// vient du profil de quartier — le test ne dit pas quatre, il dit « ce que le fichier a
    /// declare ».
    /// </summary>
    [Fact]
    public void Un_meuble_ferme_toutes_les_cases_qu_il_couvre()
    {
        foreach (var profile in MapGen.Profiles)
        {
            var sizes = profile.Props.HeavyKinds.ToDictionary(
                kind => kind.Kind,
                kind => Math.Max(1, kind.Length) * Math.Max(1, kind.Breadth),
                StringComparer.OrdinalIgnoreCase);

            foreach (int seed in Seeds)
            {
                var district = Generate(profile, seed);

                foreach (var heavy in district.Heavy)
                {
                    Assert.Equal(sizes[heavy.Kind], heavy.Cells.Count());

                    foreach (var cell in heavy.Cells)
                    {
                        Assert.Equal(CellType.Obstacle, district.Map.GetCell(cell));
                        Assert.False(
                            district.Map.IsWalkable(cell),
                            $"{heavy} laisse passer en {cell}.");
                    }
                }
            }
        }
    }

    /// <summary>
    /// La ceinture est connue du groupe des la premiere seconde, et cesse d'offrir des frontieres.
    ///
    /// Le test se joue en deux temps parce que la seconde moitie ne veut rien dire sans la premiere :
    /// un survivant qui a vu tout le quartier jouable trouve encore des frontieres — le bord du monde
    /// touche des cases jamais observees — et c'est precisement le millier de frontieres
    /// infranchissables que <see cref="SurvivorBand.KnowBorder"/> supprime. Sans lui, le groupe
    /// passerait la mission a longer un brouillard qu'il redecouvre.
    /// </summary>
    [Fact]
    public void La_ceinture_connue_au_depart_n_offre_plus_aucune_frontiere()
    {
        var profile = MapGen.Profiles.OrderBy(candidate => candidate.Size).First();
        var district = Generate(profile, 2718, zombies: 0);

        var world = new WorldState(district.Map, Config.Noise);
        var band = new SurvivorBand(world, Config);

        var agent = band.Enlist(
            MissionRun.Roster.Survivors[0], district.Drop, MissionRun.Goals, MissionRun.Domain);

        var knowledge = agent.Survivor.Knowledge;

        // Le survivant a fait le tour du quartier jouable, et lui seul : la ceinture reste inconnue.
        foreach (var cell in district.Playable.Positions())
        {
            knowledge.ObserveCell(cell, district.Map.GetCell(cell), district.Map.IsIndoor(cell), 0d);
        }

        Assert.True(Frontiers(knowledge) > 0, "Sans mise au courant, le bord du monde n'offre aucune frontiere.");

        int marked = band.KnowBorder();

        Assert.Equal(district.BorderCells.Count(), marked);
        Assert.All(district.BorderCells, cell => Assert.True(knowledge.HasObserved(cell)));
        Assert.Equal(0, Frontiers(knowledge));
    }

    private static int Frontiers(AgentKnowledge knowledge)
        => knowledge.KnownCells.Values.Count(belief => FrontierExplorer.IsFrontier(knowledge, belief));

    // --- Le rejet -------------------------------------------------------------------------------

    /// <summary>
    /// Un quartier qui ne tient pas le contrat est <b>jete</b>, et une nouvelle carte est generee a
    /// <c>seed + 1</c>. Il n'est pas retouche : retoucher reviendrait a produire des quartiers qui
    /// satisfont le contrat sans le meriter, et a ne jamais savoir lesquels.
    ///
    /// Le profil impossible est fabrique ici plutot que livre : un profil qu'aucune carte ne peut
    /// satisfaire n'a rien a faire dans <c>config/mapgen.json</c>, ou il ne servirait qu'a produire
    /// des missions injouables.
    /// </summary>
    [Fact]
    public void Un_profil_impossible_epuise_ses_essais_et_dit_pourquoi()
    {
        var config = Impossible(attempts: 3);
        var profile = config.Profiles[0];
        var generator = new DistrictGenerator(config);

        var failure = Assert.Throws<InvalidOperationException>(() => generator.Generate(Request(profile, 55)));

        Assert.Equal(3, generator.Rejected.Count);
        Assert.Contains("praticabilite", failure.Message, StringComparison.Ordinal);

        // Les trois essais portent trois graines consecutives : la regeneration part de seed + 1, elle
        // ne rejoue pas la meme carte en esperant un autre resultat.
        Assert.Equal([55, 56, 57], generator.Rejected.Select(rejected => rejected.Seed));
    }

    /// <summary>
    /// L'autre moitie de la meme regle : quand une graine produit un quartier refuse, la suivante est
    /// essayee, et c'est un quartier valide qui sort — portant la graine acceptee, pas celle demandee.
    /// </summary>
    [Fact]
    public void Un_quartier_refuse_est_regenere_a_la_graine_suivante()
    {
        var generator = new DistrictGenerator(MapGen);
        var profile = MapGen.Profiles.OrderBy(candidate => candidate.Size).First();

        // On cherche une graine que la relecture refuse du premier coup. Il y en a : le contrat n'est
        // pas satisfait par construction, c'est tout son interet.
        for (int seed = 1; seed < 200; seed++)
        {
            var request = Request(profile, seed);
            if (ValidationPass.Apply(generator.Build(profile, request, seed)).Accepted) continue;

            var district = generator.Generate(request);

            Assert.True(district.Attempts > 1);
            Assert.True(district.Seed > seed);
            Assert.Equal(district.Attempts - 1, generator.Rejected.Count);
            return;
        }

        Assert.Fail($"Aucune graine refusee pour « {profile.Id} » en 200 essais : le contrat ne filtre rien.");
    }

    /// <summary>Un profil dont l'intervalle de praticabilite ne peut etre atteint par aucune carte.</summary>
    private static MapGenConfig Impossible(int attempts)
    {
        var profile = MapGen.Profiles.OrderBy(candidate => candidate.Size).First();

        return MapGen with
        {
            MaxGenerationAttempts = attempts,
            Profiles = [profile with { WalkableRatio = new RollRange(0.999f, 1f) }],
        };
    }

    // --- La navigation --------------------------------------------------------------------------

    /// <summary>
    /// A* traverse le plus grand quartier livre sans depasser <c>navigation.max_explored_nodes</c>.
    ///
    /// La destination n'est pas choisie au hasard : c'est la case navigable la plus eloignee de la
    /// depose, celle qui coute le plus cher a atteindre. Elle est trouvee par un parcours en largeur
    /// qui emploie le <b>meme</b> voisinage que A* — sans quoi le test pourrait viser une case que la
    /// navigation refuse d'atteindre et confondre « trop grand » avec « pas de chemin ».
    /// </summary>
    [Fact]
    public void A_star_traverse_le_plus_grand_quartier_sous_la_limite_de_noeuds()
    {
        var profile = MapGen.Profiles.OrderByDescending(candidate => candidate.Size).First();
        var district = Generate(profile, 31415);

        var grid = new NavigationGrid(district.Map, Config.Navigation);
        var farthest = Farthest(grid, district.Drop);

        var path = new AStar(grid).FindPath(district.Drop, farthest);

        Assert.True(path.Found, $"Aucun chemin de {district.Drop} a {farthest} ({path.FailureReason}).");
        Assert.True(
            path.ExploredNodeCount <= Config.Navigation.MaxExploredNodes,
            $"{path.ExploredNodeCount} noeuds explores pour {profile.Size} cases de cote, "
          + $"limite {Config.Navigation.MaxExploredNodes}.");
    }

    /// <summary>La case navigable la plus loin de <paramref name="from"/>, en nombre de pas.</summary>
    private static GridPosition Farthest(NavigationGrid grid, GridPosition from)
    {
        var seen = new HashSet<GridPosition> { from };
        var pending = new Queue<GridPosition>();
        pending.Enqueue(from);

        var last = from;

        while (pending.Count > 0)
        {
            last = pending.Dequeue();

            foreach (var (neighbour, _) in grid.GetNeighbours(last))
            {
                if (seen.Add(neighbour)) pending.Enqueue(neighbour);
            }
        }

        return last;
    }

    // --- L'export -------------------------------------------------------------------------------

    /// <summary>
    /// Un quartier exporte au format de la carte de demonstration, relu, puis reexporte est le meme.
    ///
    /// C'est la propriete qui rend un quartier interessant conservable : il devient un fichier, comme
    /// la carte dessinee, et cesse de dependre de la version du generateur qui l'a produit.
    /// </summary>
    [Fact]
    public void Un_quartier_exporte_puis_relu_est_le_meme()
    {
        foreach (var profile in MapGen.Profiles)
        {
            var district = Generate(profile, 161803);

            string written = DistrictExport.ToJson(district);
            var reread = DistrictExport.FromJson(written, MapGen);

            Assert.Equal(written, DistrictExport.ToJson(reread));

            Assert.Equal(district.Seed, reread.Seed);
            Assert.Equal(district.Drop, reread.Drop);
            Assert.Equal(district.Map.Width, reread.Map.Width);
            Assert.Equal(district.Map.Height, reread.Map.Height);
            Assert.Equal(district.Map.Zones.Count, reread.Map.Zones.Count);

            foreach (var cell in district.Map.AllPositions())
            {
                Assert.Equal(district.Map.GetCell(cell), reread.Map.GetCell(cell));
                Assert.Equal(district.Map.IsIndoor(cell), reread.Map.IsIndoor(cell));
            }
        }
    }

    // --- Les frontieres du livrable --------------------------------------------------------------

    /// <summary>
    /// Le generateur vit dans le coeur et n'y fait entrer aucun type du moteur. C'est ce qui permet a
    /// tous les tests de ce fichier d'exister : un quartier se genere, se valide et se rejoue sans
    /// lancer Godot. Le jour ou quelqu'un aura besoin d'un <c>Vector2I</c> « juste ici », cette
    /// assertion le lui dira avant la revue.
    /// </summary>
    [Fact]
    public void Le_generateur_n_a_besoin_d_aucun_type_du_moteur()
    {
        var assembly = typeof(DistrictGenerator).Assembly;

        Assert.DoesNotContain(
            assembly.GetReferencedAssemblies(),
            reference => reference.Name?.Contains("Godot", StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <summary>
    /// Chaque mission livree qui demande un quartier nomme un profil qui existe. Une faute de frappe
    /// ici ne se verrait autrement qu'au depart de la mission, sous la forme d'une carte de
    /// demonstration servie a la place de celle qu'on attendait.
    /// </summary>
    [Fact]
    public void Chaque_mission_generee_nomme_un_profil_existant()
    {
        foreach (var mission in MissionRun.Catalogue.Missions.Where(mission => mission.IsGenerated))
        {
            Assert.True(
                MapGen.Find(mission.MapProfile) is not null,
                $"La mission « {mission.Id} » demande le profil « {mission.MapProfile} », "
              + "absent de config/mapgen.json.");
        }
    }

    /// <summary>
    /// Une mission sans <c>map_profile</c> continue de se jouer sur la carte dessinee. C'est la
    /// garantie que le livrable 13 ajoute une possibilite sans en retirer une : la carte de
    /// demonstration reste livree, jouable et testee.
    /// </summary>
    [Fact]
    public void Une_mission_sans_profil_ne_genere_rien()
    {
        var mission = new MissionDefinition { Id = "dessinee", Name = "Carte dessinee" };

        Assert.False(mission.IsGenerated);
        Assert.Null(mission.MapProfile);
    }
}
