using Hordinary.AI.Navigation;
using Hordinary.World;

namespace Hordinary.Tests;

/// <summary>
/// Tests exiges par C-tests.md, section « Navigation » :
/// A* trouve un chemin valide ; A* evite les cellules bloquees ; les cellules dangereuses
/// augmentent le cout ; une cellule inconnue n'est pas traitee comme une cellule libre certaine.
/// </summary>
public class NavigationTests
{
    private static (NavigationGrid Grid, AStar Search) Build(GridMap map, NavigationSettings? settings = null)
    {
        var grid = new NavigationGrid(map, settings);
        return (grid, new AStar(grid));
    }

    // --- A* trouve un chemin valide -------------------------------------------------------

    [Fact]
    public void AStar_trouve_un_chemin_valide()
    {
        var map = TestMaps.Parse(
            ".....",
            ".....",
            ".....",
            ".....",
            ".....");
        var (grid, search) = Build(map);

        var result = search.FindPath(new GridPosition(0, 0), new GridPosition(4, 4));

        Assert.True(result.Found);
        Assert.Equal(new GridPosition(0, 0), result.Cells[0]);
        Assert.Equal(new GridPosition(4, 4), result.Cells[^1]);

        // Chemin continu : chaque case touche la precedente (diagonales comprises) et est praticable.
        for (int i = 1; i < result.Cells.Count; i++)
        {
            Assert.Equal(1, result.Cells[i - 1].ChebyshevDistanceTo(result.Cells[i]));
            Assert.True(grid.IsTraversable(result.Cells[i]));
        }

        // Diagonales activees : sur une carte vide le chemin optimal est la distance de Chebyshev.
        Assert.Equal(4, result.StepCount);
        Assert.Equal(4f * MathF.Sqrt(2f), result.Cost, 3);
    }

    [Fact]
    public void AStar_renvoie_un_chemin_d_une_case_quand_depart_egale_arrivee()
    {
        var (_, search) = Build(TestMaps.Parse("...", "...", "..."));

        var result = search.FindPath(new GridPosition(1, 1), new GridPosition(1, 1));

        Assert.True(result.Found);
        Assert.Single(result.Cells);
        Assert.Equal(0, result.StepCount);
    }

    // --- A* evite les cellules bloquees ---------------------------------------------------

    [Fact]
    public void AStar_evite_les_cellules_bloquees()
    {
        var map = TestMaps.Parse(
            ".....",
            "..#..",
            "..#..",
            "..#..",
            ".....");
        var (_, search) = Build(map);

        var result = search.FindPath(new GridPosition(0, 2), new GridPosition(4, 2));

        Assert.True(result.Found);
        Assert.All(result.Cells, cell => Assert.NotEqual(CellType.Wall, map.GetCell(cell)));

        // Il faut contourner le mur par le haut ou par le bas : 2 pas de plus que la ligne droite.
        // Le contournement demande deux diagonales, les quatre autres pas sont orthogonaux.
        Assert.Equal(6, result.StepCount);
        Assert.Equal(4f + 2f * MathF.Sqrt(2f), result.Cost, 3);
    }

    [Theory]
    [InlineData(CellType.Wall)]
    [InlineData(CellType.Obstacle)]
    [InlineData(CellType.Window)]
    [InlineData(CellType.DoorLocked)]
    public void Les_cellules_bloquantes_ont_un_cout_infini(CellType blocking)
    {
        var map = TestMaps.Parse("...", "...", "...");
        var blocked = new GridPosition(1, 1);
        map.SetCell(blocked, blocking);

        var (grid, _) = Build(map);

        Assert.False(grid.IsTraversable(blocked));
        Assert.True(float.IsPositiveInfinity(grid.GetEnterCost(blocked)));
    }

    [Fact]
    public void AStar_echoue_quand_aucun_chemin_n_existe()
    {
        var map = TestMaps.Parse(
            "..#..",
            "..#..",
            "..#..",
            "..#..",
            "..#..");
        var (_, search) = Build(map);

        var result = search.FindPath(new GridPosition(0, 2), new GridPosition(4, 2));

        Assert.False(result.Found);
        Assert.Equal(PathFailureReason.NoRouteFound, result.FailureReason);
        Assert.Empty(result.Cells);
    }

    [Fact]
    public void AStar_refuse_une_destination_bloquee()
    {
        var map = TestMaps.Parse("...", ".#.", "...");
        var (_, search) = Build(map);

        var result = search.FindPath(new GridPosition(0, 0), new GridPosition(1, 1));

        Assert.False(result.Found);
        Assert.Equal(PathFailureReason.GoalBlocked, result.FailureReason);
    }

    [Fact]
    public void AStar_refuse_une_destination_hors_carte()
    {
        var (_, search) = Build(TestMaps.Parse("...", "...", "..."));

        var result = search.FindPath(new GridPosition(0, 0), new GridPosition(9, 9));

        Assert.False(result.Found);
        Assert.Equal(PathFailureReason.GoalOutsideMap, result.FailureReason);
    }

    // --- Les cellules dangereuses augmentent le cout --------------------------------------

    [Fact]
    public void Une_cellule_dangereuse_coute_plus_cher_qu_une_cellule_libre()
    {
        var map = TestMaps.Parse("...", "...", "...");
        var (grid, _) = Build(map);

        var safe = new GridPosition(0, 0);
        var suspected = new GridPosition(1, 0);
        var zombie = new GridPosition(2, 0);

        grid.SetDanger(suspected, DangerLevel.Suspected);
        grid.SetDanger(zombie, DangerLevel.ZombieNearby);

        Assert.Equal(grid.Settings.KnownCellCost, grid.GetEnterCost(safe), 3);
        Assert.Equal(grid.Settings.SuspectedDangerCellCost, grid.GetEnterCost(suspected), 3);
        Assert.Equal(grid.Settings.DangerousCellCost, grid.GetEnterCost(zombie), 3);

        Assert.True(grid.GetEnterCost(suspected) > grid.GetEnterCost(safe));
        Assert.True(grid.GetEnterCost(zombie) > grid.GetEnterCost(suspected));
    }

    [Fact]
    public void AStar_contourne_les_cellules_dangereuses()
    {
        var map = TestMaps.Parse(
            ".....",
            ".....",
            ".....");
        var (grid, search) = Build(map);

        var start = new GridPosition(0, 1);
        var goal = new GridPosition(4, 1);

        var direct = search.FindPath(start, goal);
        Assert.True(direct.Found);
        Assert.Equal(4, direct.StepCount);

        // Un zombie est apercu au milieu de la ligne droite.
        var dangerous = new[] { new GridPosition(1, 1), new GridPosition(2, 1), new GridPosition(3, 1) };
        foreach (var cell in dangerous) grid.SetDanger(cell, DangerLevel.ZombieNearby);

        var avoided = search.FindPath(start, goal);

        Assert.True(avoided.Found);
        Assert.All(dangerous, cell => Assert.DoesNotContain(cell, avoided.Cells));

        // Le detour compte ici autant de pas que la ligne droite (il passe par la rangee du dessus
        // en diagonale) : c'est bien le cout, pas la longueur, qui traduit l'evitement du danger.
        Assert.True(avoided.Cost > direct.Cost);
    }

    // --- Une cellule inconnue n'est pas une cellule libre certaine -------------------------

    [Fact]
    public void Une_cellule_inconnue_n_est_pas_declaree_surement_praticable()
    {
        Assert.False(CellType.Unknown.IsKnownWalkable());
        Assert.True(CellType.Floor.IsKnownWalkable());

        // Elle reste franchissable : l'absence d'information n'est pas un mur.
        Assert.False(CellType.Unknown.BlocksMovement());
    }

    [Fact]
    public void Une_cellule_inconnue_coute_plus_cher_qu_une_cellule_connue()
    {
        var map = TestMaps.Parse(".?.");
        var (grid, _) = Build(map);

        float known = grid.GetEnterCost(new GridPosition(0, 0));
        float unknown = grid.GetEnterCost(new GridPosition(1, 0));

        Assert.Equal(grid.Settings.KnownCellCost, known, 3);
        Assert.Equal(grid.Settings.UnknownCellCost, unknown, 3);
        Assert.True(unknown > known);
    }

    [Fact]
    public void AStar_prefere_un_detour_connu_a_un_raccourci_inconnu()
    {
        // Raccourci direct de 4 pas mais passant par une case jamais observee (cout 4 a lui seul),
        // contre un detour connu par la rangee du dessous (cout 2 + 2 diagonales).
        var map = TestMaps.Parse(
            "..?..",
            ".....");
        var (_, search) = Build(map);

        var result = search.FindPath(new GridPosition(0, 0), new GridPosition(4, 0));

        Assert.True(result.Found);
        Assert.DoesNotContain(new GridPosition(2, 0), result.Cells);
        Assert.Equal(4, result.StepCount);
        Assert.Equal(2f + 2f * MathF.Sqrt(2f), result.Cost, 3);
    }

    [Fact]
    public void Un_profil_de_navigation_penalise_les_zones_inconnues_selon_son_multiplicateur()
    {
        var map = TestMaps.Parse("?");
        var cell = new GridPosition(0, 0);

        var neutral = new NavigationGrid(map, NavigationSettings.Default, NavigationProfile.Neutral);
        var cautious = new NavigationGrid(
            map, NavigationSettings.Default, new NavigationProfile { UnknownCostMultiplier = 2f, DangerCostMultiplier = 2f });
        var reckless = new NavigationGrid(
            map, NavigationSettings.Default, new NavigationProfile { UnknownCostMultiplier = 0.5f, DangerCostMultiplier = 0.5f });

        Assert.True(cautious.GetEnterCost(cell) > neutral.GetEnterCost(cell));
        Assert.True(reckless.GetEnterCost(cell) < neutral.GetEnterCost(cell));
    }

    // --- Diagonales -----------------------------------------------------------------------

    [Fact]
    public void Un_pas_diagonal_ne_coupe_pas_le_coin_entre_deux_murs()
    {
        // Les deux cases orthogonales sont des murs : la diagonale traverserait la matiere.
        var map = TestMaps.Parse(
            ".#",
            "#.");
        var (_, search) = Build(map);

        var result = search.FindPath(new GridPosition(0, 0), new GridPosition(1, 1));

        Assert.False(result.Found);
        Assert.Equal(PathFailureReason.NoRouteFound, result.FailureReason);
    }

    [Fact]
    public void Un_pas_diagonal_exige_les_deux_cases_orthogonales_libres()
    {
        // Un seul mur suffit a interdire la diagonale : il faut contourner en deux pas.
        var map = TestMaps.Parse(
            ".#",
            "..");
        var (_, search) = Build(map);

        var result = search.FindPath(new GridPosition(0, 0), new GridPosition(1, 1));

        Assert.True(result.Found);
        Assert.Equal(2, result.StepCount);
        Assert.Equal(new GridPosition(0, 1), result.Cells[1]);
    }

    [Fact]
    public void Desactiver_les_diagonales_ramene_un_chemin_orthogonal()
    {
        var map = TestMaps.Parse("...", "...", "...");
        var (_, search) = Build(map, new NavigationSettings { AllowDiagonal = false });

        var result = search.FindPath(new GridPosition(0, 0), new GridPosition(2, 2));

        Assert.True(result.Found);
        Assert.Equal(4, result.StepCount);
        Assert.All(
            result.Cells.Skip(1).Select((cell, i) => (Previous: result.Cells[i], Current: cell)),
            step => Assert.Equal(1, step.Previous.ManhattanDistanceTo(step.Current)));
    }

    // --- Portes ---------------------------------------------------------------------------

    [Fact]
    public void Une_porte_fermee_est_franchissable_avec_un_surcout()
    {
        var map = TestMaps.Parse("#D#");
        var (grid, _) = Build(map);

        var door = new GridPosition(1, 0);

        Assert.True(grid.IsTraversable(door));
        Assert.Equal(
            grid.Settings.KnownCellCost + grid.Settings.ClosedDoorCost,
            grid.GetEnterCost(door),
            3);
    }

    [Fact]
    public void Une_porte_verrouillee_bloque_le_passage()
    {
        var map = TestMaps.Parse(
            "#L#",
            "...");
        var (_, search) = Build(map);

        var result = search.FindPath(new GridPosition(1, 1), new GridPosition(1, 0));

        Assert.False(result.Found);
        Assert.Equal(PathFailureReason.GoalBlocked, result.FailureReason);
    }

    // --- Garde-fou de recherche -----------------------------------------------------------

    [Fact]
    public void La_recherche_respecte_la_limite_de_noeuds_explores()
    {
        var map = new GridMap(60, 60, fill: CellType.Floor);
        // Destination isolee : A* doit explorer toute la carte avant d'abandonner.
        foreach (var offset in GridPosition.OrthogonalOffsets)
        {
            map.SetCell(new GridPosition(59, 59) + offset, CellType.Wall);
        }

        var (_, search) = Build(map, new NavigationSettings { MaxExploredNodes = 50 });

        var result = search.FindPath(new GridPosition(0, 0), new GridPosition(59, 59));

        Assert.False(result.Found);
        Assert.Equal(PathFailureReason.SearchLimitReached, result.FailureReason);
        Assert.True(result.ExploredNodeCount <= 50);
    }
}
