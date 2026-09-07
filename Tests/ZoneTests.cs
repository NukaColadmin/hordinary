using Hordinary.Config;
using Hordinary.Game;
using Hordinary.World;

namespace Hordinary.Tests;

/// <summary>
/// Le catalogue de zones, les quatre agencements et ce qu'ils rendent indisponible (livrable 11).
///
/// Une zone est une donnee. Le code ne nomme ni « supermarche » ni « poste de police » : il sait
/// seulement poser quatre agencements sur une empreinte. Ce qui est verifie ici, c'est que le fichier
/// livre decrit des zones posables, que chaque agencement produit bien la geometrie qu'il annonce, et
/// qu'une mission puisse viser un profil aussi bien qu'un <see cref="ZoneKind"/>.
/// </summary>
public class ZoneTests
{
    private static ZoneCatalogue Delivered => ZoneCatalogue.FromJson(DemoMap.ReadConfig("zones.json"));

    private static TileLegend Legend { get; } = TileLegend.FromJson(DemoMap.ReadConfig("tile_legend.json"));

    // --- Le fichier livre --------------------------------------------------------------------------

    [Fact]
    public void Les_cinq_zones_sont_livrees()
    {
        var zones = Delivered;

        Assert.Equal(5, zones.Zones.Count);
        Assert.All(zones.Zones, zone => Assert.False(string.IsNullOrWhiteSpace(zone.Name)));
    }

    /// <summary>
    /// « Chaque profil reference un <c>layout</c> implemente et des <c>id</c> d'objets existants. » La
    /// premiere moitie est verifiee au chargement ; la seconde demande le catalogue d'objets, et c'est
    /// pour cela qu'elle est une passe a part.
    /// </summary>
    [Fact]
    public void Chaque_profil_reference_un_agencement_implemente_et_des_objets_existants()
    {
        var zones = Delivered;

        foreach (var zone in zones.Zones)
        {
            Assert.True(Enum.IsDefined(zone.Layout), $"« {zone.Id} » demande un agencement inconnu.");
        }

        // Ne leve pas : c'est le test.
        zones.ValidateLoot(DemoMap.Items);
    }

    /// <summary>
    /// Cinq zones, quatre agencements : le rendement est la, et pas dans les tables de butin. Un
    /// agencement declare mais jamais employe serait du code que rien n'exerce.
    /// </summary>
    [Fact]
    public void Les_quatre_agencements_servent_tous()
    {
        var employes = Delivered.Zones.Select(zone => zone.Layout).Distinct().ToList();

        Assert.Equal(Enum.GetValues<ZoneLayout>().Length, employes.Count);
    }

    [Fact]
    public void Un_catalogue_fautif_se_signale_au_chargement()
    {
        var doublon = new ZoneCatalogue
        {
            Zones =
            [
                new ZoneProfile { Id = "x", Name = "X" },
                new ZoneProfile { Id = "x", Name = "X bis" },
            ],
        };

        Assert.Throws<InvalidOperationException>(doublon.Validate);

        var inconnu = new ZoneCatalogue
        {
            Zones = [new ZoneProfile { Id = "y", Name = "Y", Loot = { ["objet_qui_n_existe_pas"] = new RollRange(1f, 1f) } }],
        };

        Assert.Throws<InvalidOperationException>(() => inconnu.ValidateLoot(DemoMap.Items));
    }

    // --- Les quatre agencements ---------------------------------------------------------------------

    /// <summary>Chaque zone livree se pose sur une carte vide sans lever, et laisse ou fouiller.</summary>
    [Fact]
    public void Chaque_zone_livree_se_pose_et_laisse_ou_fouiller()
    {
        foreach (var profile in Delivered.Zones)
        {
            var map = Blank();
            var stamp = ZoneLayouts.Apply(profile, map, new GridPosition(1, 1), new GridPosition(20, 14), seed: 7);

            Assert.Equal(profile.Id, stamp.Zone.Profile);
            Assert.Equal(profile.Kind, stamp.Zone.Kind);
            Assert.NotEmpty(stamp.SearchSpots);
        }
    }

    /// <summary>
    /// Un agencement en allees coupe la vue en travers et la laisse passer en enfilade. C'est tout ce
    /// que « la vue porte le long d'une allee, jamais a travers » veut dire, et c'est verifiable sans
    /// jouer : le meme <see cref="LineOfSight"/> repond oui dans un sens et non dans l'autre.
    /// </summary>
    [Fact]
    public void Des_allees_coupent_la_vue_en_travers_et_la_laissent_passer_en_enfilade()
    {
        var profile = Delivered.Find("supermarche")!;
        var map = Blank(32, 24);

        var stamp = ZoneLayouts.Apply(profile, map, new GridPosition(2, 2), new GridPosition(22, 16), seed: 3);

        Assert.NotEmpty(stamp.SearchSpots);

        // Une allee au moins doit etre bordee de rayonnages sur toute sa longueur.
        int obstacles = map.AllPositions().Count(cell => map.GetCell(cell) == CellType.Obstacle);
        Assert.True(obstacles > 0, "Un agencement en allees n'a pose aucun rayonnage.");

        // Deux cases separees par un rayonnage ne se voient pas ; deux cases de la meme allee, si.
        var blocked = map.AllPositions().First(cell => map.GetCell(cell) == CellType.Obstacle);

        var left = new GridPosition(blocked.X - 1, blocked.Y);
        var right = new GridPosition(blocked.X + 1, blocked.Y);

        Assert.False(LineOfSight.IsVisible(map, left, right), "La vue traverse un rayonnage.");
    }

    /// <summary>
    /// Un volume libre ne pose rien du tout : l'absence est exactement ce qui le definit, et c'est ce
    /// qui produit les longues lignes de vue du gymnase.
    /// </summary>
    [Fact]
    public void Un_volume_libre_ne_pose_aucun_obstacle_interieur()
    {
        var profile = new ZoneProfile { Id = "vide", Name = "Vide", Layout = ZoneLayout.Open };
        var map = Blank();

        ZoneLayouts.Apply(profile, map, new GridPosition(2, 2), new GridPosition(14, 10));

        for (int y = 3; y < 10; y++)
        {
            for (int x = 3; x < 14; x++)
            {
                Assert.NotEqual(CellType.Obstacle, map.GetCell(new GridPosition(x, y)));
            }
        }
    }

    /// <summary>
    /// Une esplanade n'a pas d'enceinte : c'est ce qui la rend indefendable, et c'est le seul endroit
    /// ou tirer revient a se signaler a tout le monde a la fois.
    /// </summary>
    [Fact]
    public void Une_esplanade_n_a_pas_de_mur_d_enceinte()
    {
        var profile = Delivered.Find("station_service")!;
        var map = Blank(28, 20);

        var stamp = ZoneLayouts.Apply(profile, map, new GridPosition(2, 2), new GridPosition(20, 14), seed: 11);

        Assert.Empty(stamp.Doors);
        Assert.NotNull(stamp.InnerRoom);

        // Le bord nord de l'empreinte reste du sol : on entre par ou l'on veut.
        int murs = Enumerable.Range(3, 16).Count(x => map.GetCell(new GridPosition(x, 2)) == CellType.Wall);
        Assert.Equal(0, murs);
    }

    /// <summary>
    /// Un batiment ferme a une porte, et la piece du fond la sienne. Le poste de police les verrouille
    /// toutes les deux : c'est le seul endroit du jeu ou le joueur choisit deliberement de faire du
    /// bruit, et ou il le sait a l'avance.
    /// </summary>
    [Fact]
    public void Un_batiment_verrouille_n_offre_qu_un_chemin_bruyant()
    {
        var profile = Delivered.Find("poste_police")!;
        var map = Blank(28, 20);

        var stamp = ZoneLayouts.Apply(profile, map, new GridPosition(2, 2), new GridPosition(20, 14), seed: 5);

        Assert.NotEmpty(stamp.Doors);
        Assert.All(stamp.Doors, door => Assert.Equal(CellType.DoorLocked, map.GetCell(door)));
    }

    /// <summary>La meme graine sur la meme empreinte redonne exactement le meme batiment.</summary>
    [Fact]
    public void La_pose_est_deterministe_a_graine_donnee()
    {
        var profile = Delivered.Find("quincaillerie")!;

        Assert.Equal(Render(profile, seed: 42), Render(profile, seed: 42));
        Assert.NotEqual(Render(profile, seed: 42), Render(Delivered.Find("poste_police")!, seed: 42));

        static string Render(ZoneProfile profile, int seed)
        {
            var map = Blank(28, 20);
            ZoneLayouts.Apply(profile, map, new GridPosition(2, 2), new GridPosition(20, 14), seed);

            return string.Concat(map.AllPositions().Select(cell => (char)('0' + (int)map.GetCell(cell))));
        }
    }

    /// <summary>Une empreinte ou rien ne tient se signale, plutot que de poser une piece sans porte.</summary>
    [Fact]
    public void Une_empreinte_trop_petite_se_signale()
    {
        var profile = Delivered.Find("supermarche")!;
        var map = Blank();

        Assert.Throws<InvalidOperationException>(
            () => ZoneLayouts.Apply(profile, map, new GridPosition(1, 1), new GridPosition(3, 2)));
    }

    // --- Ce que les tuiles rendent indisponible ------------------------------------------------------

    /// <summary>
    /// « <c>BreakWindow</c> n'est pas propose sur une <c>WindowBarred</c>, et <c>ForceDoor</c> ne l'est
    /// pas sur un <c>ShutterHalf</c>. »
    ///
    /// Rien n'est teste a l'execution : une fenetre a barreaux est un <c>Wall</c> pour la carte, et un
    /// rideau a moitie baisse une porte fermee qu'aucune donnee ne verrouille. L'action devient
    /// inapplicable parce que son objet n'existe pas, ce qui est plus solide qu'une condition ecrite
    /// quelque part dans le planificateur.
    /// </summary>
    [Fact]
    public void Une_fenetre_a_barreaux_et_un_rideau_baisse_ferment_leurs_actions()
    {
        Assert.True(TileLegend.ForbidsBreakWindow(TileKind.WindowBarred));
        Assert.False(TileLegend.ForbidsBreakWindow(TileKind.Window));

        Assert.True(TileLegend.ForbidsForceDoor(TileKind.ShutterHalf));
        Assert.False(TileLegend.ForbidsForceDoor(TileKind.DoorClosed));

        // Et la legende livree les declare bien, sans quoi la regle porterait sur du vide.
        Assert.True(Legend.Declares(TileKind.WindowBarred));
        Assert.True(Legend.Declares(TileKind.ShutterHalf));

        // Une fenetre a barreaux n'est pas une Window pour la carte : BreakWindow n'a donc aucun objet
        // sur lequel s'appliquer.
        Assert.Equal(CellType.Wall, TileLegend.ToCellType(TileKind.WindowBarred));
        Assert.Equal(CellType.DoorClosed, TileLegend.ToCellType(TileKind.ShutterHalf));
    }

    /// <summary>
    /// Une case declaree infranchissable le reste : c'est ce qui empeche une passe ulterieure de
    /// verrouiller un rideau et de rouvrir <c>ForceDoor</c> par accident.
    /// </summary>
    [Fact]
    public void Une_case_infranchissable_est_marquee_sur_la_carte()
    {
        var map = Blank();
        var cell = new GridPosition(4, 4);

        Assert.False(map.IsUnbreachable(cell));

        map.SetUnbreachable(cell);

        Assert.True(map.IsUnbreachable(cell));
        Assert.Contains(cell, map.Unbreachable);
    }

    // --- Missions et profils -------------------------------------------------------------------------

    /// <summary>
    /// « Une mission dont l'<c>objective</c> est un profil de zone se resout comme une mission dont
    /// l'<c>objective</c> est un <see cref="ZoneKind"/>. » Les deux trouvent la meme zone sur la meme
    /// carte : elles n'y arrivent simplement pas par le meme chemin.
    /// </summary>
    [Fact]
    public void Une_mission_qui_vise_un_profil_se_resout_comme_une_qui_vise_un_type()
    {
        var map = DemoMap.Load();

        var batiment = map.Zones.First(zone => zone.Kind == ZoneKind.Building);
        map.AddZone(new MapZone(batiment.Name, batiment.Kind, batiment.Min, batiment.Max, "quincaillerie"));

        var byKind = new MissionDefinition
        {
            Id = "type", Name = "Par type", Target = ZoneKind.Building, ObjectiveZone = batiment.Name,
        };

        var byProfile = new MissionDefinition { Id = "profil", Name = "Par profil", Target = new ZoneTarget(ZoneKind.Other, "quincaillerie") };

        Assert.Equal(batiment.Name, byKind.ObjectiveIn(map)!.Name);
        Assert.Equal(batiment.Name, byProfile.ObjectiveIn(map)!.Name);
        Assert.Equal("quincaillerie", byProfile.ObjectiveProfile);
    }

    /// <summary>
    /// Le catalogue de zones donne sa categorie structurelle a une mission qui vise un profil : elle
    /// redevient alors une mission comme les autres, briefing compris.
    /// </summary>
    [Fact]
    public void Le_catalogue_de_zones_renseigne_la_categorie_d_une_mission_par_profil()
    {
        const string json = """
        { "missions": [ { "id": "essence", "name": "Rapporter de l'essence", "objective": "station_service", "item": "fuel_can" } ] }
        """;

        var brut = MissionCatalogue.FromJson(json);
        Assert.Equal(ZoneKind.Other, brut.Missions[0].Objective);

        var resolu = MissionCatalogue.FromJson(json, Delivered);
        Assert.Equal(Delivered.Find("station_service")!.Kind, resolu.Missions[0].Objective);
        Assert.Contains(resolu.Missions[0].Objective, resolu.Missions[0].BriefedZones());
    }

    /// <summary>Une mission qui vise un profil inexistant se signale au chargement, pas a la partie.</summary>
    [Fact]
    public void Une_mission_qui_vise_un_profil_inconnu_se_signale()
    {
        const string json = """
        { "missions": [ { "id": "nulle_part", "name": "Nulle part", "objective": "chateau_de_versailles" } ] }
        """;

        Assert.Throws<InvalidOperationException>(() => MissionCatalogue.FromJson(json, Delivered));
    }

    /// <summary>
    /// « La carte de demonstration, qui ne declare aucun <c>profile</c>, reste chargeable et jouable. »
    /// Le champ est facultatif par construction, et ce test est ce qui l'atteste.
    /// </summary>
    [Fact]
    public void La_carte_de_demonstration_ne_declare_aucun_profil_et_reste_jouable()
    {
        var map = DemoMap.Load();

        Assert.All(map.Zones, zone => Assert.Null(zone.Profile));

        foreach (var mission in MissionRun.Catalogue.Missions)
        {
            Assert.NotNull(mission.ObjectiveIn(map));
            Assert.NotNull(mission.CampIn(map));
        }
    }

    /// <summary>Une zone ecrite a la main peut declarer un profil, et la carte le retient.</summary>
    [Fact]
    public void Une_zone_ecrite_a_la_main_peut_declarer_un_profil()
    {
        const string json = """
        {
          "zones": [
            { "name": "Quincaillerie", "kind": "Building", "profile": "quincaillerie", "min": [2, 2], "max": [6, 6] }
          ]
        }
        """;

        var map = Blank();
        MapLocationsConfig.FromJson(json).ApplyTo(map);

        Assert.Equal("quincaillerie", map.Zones.Single().Profile);
    }

    /// <summary>
    /// La grille annonce les cases dont le terrain change, et seulement celles-la.
    ///
    /// C'est ce qui manquait pour que le decor cesse de mentir : une porte forcee restait dessinee
    /// fermee jusqu'a la fin de la partie, et un survivant traversait une porte close. Le monde le
    /// savait — il ne le disait a personne. <c>MapPainter.ShowCell</c> ecoute maintenant cet
    /// evenement, et repeint la case annoncee.
    ///
    /// La condition « et seulement celles-la » n'est pas un detail : une generation de quartier pose
    /// des milliers de cases, et un evenement par ecriture plutot que par changement rendrait
    /// l'abonnement impraticable.
    /// </summary>
    [Fact]
    public void La_grille_annonce_les_cases_dont_le_terrain_change()
    {
        var map = Blank();
        var porte = new GridPosition(4, 4);

        map.SetCell(porte, CellType.DoorClosed);

        var annonces = new List<(GridPosition Cell, CellType Type)>();
        map.CellChanged += (cell, type) => annonces.Add((cell, type));

        // Ouvrir la porte : une annonce, avec le terrain d'arrivee.
        map.SetCell(porte, CellType.DoorOpen);
        Assert.Equal([(porte, CellType.DoorOpen)], annonces);

        // La reecrire a l'identique n'annonce rien : rien n'a change, rien n'est a repeindre.
        map.SetCell(porte, CellType.DoorOpen);
        Assert.Single(annonces);

        // Hors carte non plus : il n'y a pas de case a repeindre.
        map.SetCell(new GridPosition(-1, -1), CellType.Wall);
        Assert.Single(annonces);

        // Une vitre brisee est le second changement de terrain que le jeu produit, et il passe par
        // le meme chemin.
        var vitre = new GridPosition(6, 6);
        map.SetCell(vitre, CellType.Window);
        map.SetCell(vitre, CellType.Floor);

        Assert.Equal([(porte, CellType.DoorOpen), (vitre, CellType.Window), (vitre, CellType.Floor)], annonces);
    }

    private static GridMap Blank(int width = 24, int height = 18)
        => new(width, height, fill: CellType.Floor);
}
