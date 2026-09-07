using Hordinary.AI.Navigation;
using Hordinary.Agents;
using Hordinary.Config;
using Hordinary.World;

namespace Hordinary.Tests;

/// <summary>
/// Verifie que la carte de demonstration livree respecte la specification (section 7) et le
/// critere de validation du livrable 1 : « un survivant peut atteindre une destination connue
/// en evitant les obstacles ».
/// </summary>
public class DemoMapTests
{
    private static readonly GridMap Map = DemoMap.Load();

    private static AStar Search(NavigationGrid? grid = null)
        => new(grid ?? new NavigationGrid(Map, GameConfig.FromJson(DemoMap.ReadConfig("game_config.json")).Navigation));

    private static GridPosition Spawn(ZoneKind kind)
    {
        var zone = Map.GetZone(kind);
        Assert.NotNull(zone);

        var cell = Map.FindWalkableIn(zone);
        Assert.True(cell.HasValue, $"La zone '{zone.Name}' ne contient aucune case praticable.");
        return cell.Value;
    }

    /// <summary>
    /// Un lieu designe par son nom. La carte compte deux batiments : le type ne suffit plus des que
    /// l'on veut viser celui du sud plutot que celui de l'ouest.
    /// </summary>
    private static GridPosition Spawn(string name)
    {
        var zone = Map.Zones.FirstOrDefault(candidate => candidate.Name == name);
        Assert.True(zone is not null, $"Aucune zone nommee '{name}'.");

        var cell = Map.FindWalkableIn(zone!);
        Assert.True(cell.HasValue, $"La zone '{name}' ne contient aucune case praticable.");
        return cell.Value;
    }

    // --- Section 7.1 : dimensions ---------------------------------------------------------

    [Fact]
    public void Les_dimensions_respectent_la_specification()
    {
        Assert.InRange(Map.Width, 20, 40);
        Assert.InRange(Map.Height, 20, 40);
    }

    // --- Section 7.2 et 7.3 : contenu -----------------------------------------------------

    [Fact]
    public void La_carte_contient_les_types_de_cellules_attendus()
    {
        Assert.True(Map.CountCells(CellType.Floor) > 0, "Aucun sol.");
        Assert.True(Map.CountCells(CellType.Wall) > 0, "Aucun mur.");
        Assert.True(Map.CountCells(CellType.Window) > 0, "Aucune fenetre.");
        Assert.True(Map.CountCells(CellType.DoorClosed) > 0, "Aucune porte fermee.");
        Assert.True(Map.CountCells(CellType.Pharmacy) > 0, "La pharmacie n'est pas marquee.");
        Assert.True(Map.CountCells(CellType.Extraction) > 0, "La zone d'extraction n'est pas marquee.");

        // La carte reelle est entierement definie : une cellule inconnue signalerait une tuile
        // peinte dans la scene mais absente de config/tile_legend.json.
        Assert.Equal(0, Map.CountCells(CellType.Unknown));
    }

    [Fact]
    public void Aucune_porte_n_est_verrouillee_dans_les_donnees_de_la_carte()
    {
        // Le verrouillage est un etat d'execution, pas une tuile : le TileSet ne peint que
        // « fermee » (source 1) et « ouverte » (source 2). Pour le MVP la porte de la pharmacie
        // est donc simplement fermee, ce qui garde la pharmacie atteignable au livrable 1.
        Assert.Equal(0, Map.CountCells(CellType.DoorLocked));

        var pharmacyDoor = new GridPosition(6, -2);
        Assert.Equal(CellType.DoorClosed, Map.GetCell(pharmacyDoor));
        Assert.True(Map.IsWalkable(pharmacyDoor));
    }

    [Fact]
    public void La_carte_contient_tous_les_lieux_principaux()
    {
        Assert.NotNull(Map.GetZone(ZoneKind.Start));
        Assert.NotNull(Map.GetZone(ZoneKind.Pharmacy));
        Assert.NotNull(Map.GetZone(ZoneKind.Extraction));
        Assert.NotNull(Map.GetZone(ZoneKind.Street));
        Assert.NotNull(Map.GetZone(ZoneKind.Alley));

        int buildings = Map.Zones.Count(z => z.Kind == ZoneKind.Building);
        Assert.True(buildings >= 2, $"La specification exige deux batiments secondaires, {buildings} declare(s).");
    }

    [Fact]
    public void Chaque_zone_declaree_est_praticable()
    {
        foreach (var zone in Map.Zones)
        {
            Assert.True(
                Map.FindWalkableIn(zone).HasValue,
                $"La zone '{zone.Name}' {zone.Min}-{zone.Max} ne contient aucune case praticable.");
        }
    }

    // --- Critere de validation du livrable 1 ----------------------------------------------

    [Fact]
    public void Un_survivant_atteint_la_pharmacie_depuis_la_zone_de_depart()
    {
        var result = Search().FindPath(Spawn(ZoneKind.Start), Spawn(ZoneKind.Pharmacy));

        Assert.True(result.Found, $"Pharmacie inatteignable : {result}");
        Assert.All(result.Cells, cell => Assert.True(Map.IsWalkable(cell), $"{cell} n'est pas praticable."));
        Assert.Equal(CellType.Pharmacy, Map.GetCell(result.Cells[^1]));
    }

    [Fact]
    public void Un_survivant_rentre_au_camp_depuis_la_pharmacie()
    {
        var result = Search().FindPath(Spawn(ZoneKind.Pharmacy), Spawn(ZoneKind.Extraction));

        Assert.True(result.Found, $"Camp inatteignable : {result}");
        Assert.Equal(CellType.Extraction, Map.GetCell(result.Cells[^1]));
    }

    /// <summary>
    /// Une mission commence et finit au meme endroit : le camp est a la fois la zone de depart et la
    /// zone de retour. Les deux zones sont declarees separement dans
    /// <c>config/map_locations.json</c> — le domaine de planification connait la sortie sous le nom
    /// « extraction » et le briefing sous le nom « start » — mais elles couvrent le meme rectangle.
    /// </summary>
    [Fact]
    public void Le_camp_est_a_la_fois_le_depart_et_le_retour()
    {
        var start = Map.GetZone(ZoneKind.Start);
        var back = Map.GetZone(ZoneKind.Extraction);

        Assert.NotNull(start);
        Assert.NotNull(back);

        Assert.Equal(start.Min, back.Min);
        Assert.Equal(start.Max, back.Max);
    }

    [Fact]
    public void Le_chemin_vers_la_pharmacie_traverse_la_porte_et_jamais_un_mur()
    {
        var result = Search().FindPath(Spawn(ZoneKind.Start), Spawn(ZoneKind.Pharmacy));

        Assert.True(result.Found);
        Assert.DoesNotContain(result.Cells, cell => Map.GetCell(cell) == CellType.Wall);
        Assert.DoesNotContain(result.Cells, cell => Map.GetCell(cell) == CellType.Window);
        Assert.DoesNotContain(result.Cells, cell => Map.GetCell(cell) == CellType.DoorLocked);

        // La seule entree de la pharmacie est une porte fermee : elle doit apparaitre dans le chemin.
        Assert.Contains(result.Cells, cell => Map.GetCell(cell) == CellType.DoorClosed);
    }

    // --- Section 7.3 : au moins deux chemins possibles ------------------------------------

    /// <summary>
    /// Le camp etant desormais le depart <b>et</b> le retour, l'itineraire qui compte n'est plus
    /// « depart vers extraction » — ce serait un chemin de longueur nulle — mais celui qui traverse
    /// la carte : du camp, au nord, jusqu'au hangar sud.
    /// </summary>
    [Fact]
    public void Il_existe_au_moins_deux_itineraires_entre_le_camp_et_le_hangar_sud()
    {
        var start = Spawn(ZoneKind.Start);
        var goal = Spawn("Hangar sud");

        var first = Search().FindPath(start, goal);
        Assert.True(first.Found, $"Aucun itineraire initial : {first}");

        // On coupe la ruelle centrale : un second itineraire doit contourner les batiments.
        var alley = Map.GetZone(ZoneKind.Alley);
        Assert.NotNull(alley);

        var detourMap = DemoMap.Load();
        foreach (var cell in alley.Positions()) detourMap.SetCell(cell, CellType.Obstacle);

        var second = new AStar(new NavigationGrid(detourMap)).FindPath(start, goal);

        Assert.True(second.Found, "La carte n'offre qu'un seul itineraire vers le sud.");
        Assert.NotEqual(first.Cells, second.Cells);
    }

    // --- Livrable 2 : le survivant ne sait rien de ce qu'il n'a pas vu --------------------

    [Fact]
    public void Au_depart_le_survivant_ignore_l_interieur_de_la_pharmacie()
    {
        var world = new WorldState(Map);
        world.SetItem("objective-1", Survivor.DefaultObjectiveItem, new GridPosition(11, 4));

        var knowledge = new AgentKnowledge("alice", Map.Width, Map.Height, Map.Origin);
        var alice = new Survivor("alice", "Alice", knowledge, new PerceptionSettings { FieldOfViewDegrees = 360f })
        {
            Position = Spawn(ZoneKind.Start),
        };

        new PerceptionSystem().Observe(alice, world, 0d);

        Assert.InRange(knowledge.ExploredRatio, 0.001f, 0.35f);

        // L'interieur de la pharmacie et les medicaments sont hors de portee et hors de vue.
        Assert.False(knowledge.HasObserved(new GridPosition(9, 0)));
        Assert.Empty(knowledge.KnownItems);

        // A* planifie quand meme : les cases inconnues sont franchissables, seulement couteuses.
        var route = new AStar(new NavigationGrid(knowledge.MentalMap))
            .FindPath(alice.Position, Spawn(ZoneKind.Pharmacy));

        Assert.True(route.Found);
        Assert.Contains(route.Cells, cell => !knowledge.HasObserved(cell));
    }

    // --- Le danger detourne le survivant sur la vraie carte -------------------------------

    [Fact]
    public void Un_danger_signale_modifie_l_itineraire_reel()
    {
        var start = Spawn(ZoneKind.Start);
        var goal = Spawn("Hangar sud");

        var grid = new NavigationGrid(Map);
        var search = new AStar(grid);

        var safe = search.FindPath(start, goal);
        Assert.True(safe.Found);

        // Une horde est signalee au milieu de l'itineraire.
        var threatened = safe.Cells.Skip(safe.Cells.Count / 3).Take(6).ToList();
        foreach (var cell in threatened) grid.SetDanger(cell, DangerLevel.ZombieNearby);

        var avoided = search.FindPath(start, goal);
        grid.ClearDanger();

        Assert.True(avoided.Found);
        Assert.NotEqual(safe.Cells, avoided.Cells);
        Assert.True(avoided.Cells.Count(threatened.Contains) < threatened.Count);
    }
}
