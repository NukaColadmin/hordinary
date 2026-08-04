using Hordinary.World;

namespace Hordinary.Tests;

/// <summary>Construction de petites cartes lisibles directement dans les tests.</summary>
internal static class TestMaps
{
    /// <summary>
    /// Chaque caractere decrit une cellule :
    /// <c>.</c> sol, <c>#</c> mur, <c>?</c> inconnue, <c>D</c> porte fermee, <c>L</c> porte verrouillee,
    /// <c>o</c> porte ouverte, <c>W</c> fenetre, <c>O</c> obstacle, <c>P</c> pharmacie, <c>E</c> extraction.
    /// </summary>
    public static GridMap Parse(params string[] rows)
    {
        if (rows.Length == 0) throw new ArgumentException("Carte vide.", nameof(rows));

        int width = rows[0].Length;
        if (rows.Any(r => r.Length != width))
            throw new ArgumentException("Toutes les lignes doivent avoir la meme longueur.", nameof(rows));

        var map = new GridMap(width, rows.Length);

        for (int y = 0; y < rows.Length; y++)
        {
            for (int x = 0; x < width; x++)
            {
                map.SetCell(new GridPosition(x, y), rows[y][x] switch
                {
                    '.' => CellType.Floor,
                    '#' => CellType.Wall,
                    '?' => CellType.Unknown,
                    'D' => CellType.DoorClosed,
                    'L' => CellType.DoorLocked,
                    'o' => CellType.DoorOpen,
                    'W' => CellType.Window,
                    'O' => CellType.Obstacle,
                    'P' => CellType.Pharmacy,
                    'E' => CellType.Extraction,
                    var c => throw new ArgumentException($"Caractere de carte inconnu : '{c}'.", nameof(rows)),
                });
            }
        }

        return map;
    }
}
