using System.Text.RegularExpressions;
using Hordinary.Config;
using Hordinary.World;

namespace Hordinary.Tests;

/// <summary>
/// Charge la carte de demonstration reelle (<c>Scenes/Main.tscn</c>) et les vrais fichiers de
/// configuration, sans dependre de Godot.
///
/// Le decodage du <c>PackedByteArray</c> reproduit ce que fait <c>MapLoader</c> via l'API du moteur :
/// c'est volontaire, cela permet de verifier en integration continue que la carte livree satisfait
/// toujours la specification, meme sans editeur installe.
/// </summary>
internal static class DemoMap
{
    /// <summary>En-tete du format : 2 octets, puis 12 octets par cellule.</summary>
    private const int HeaderSize = 2;
    private const int RecordSize = 12;

    /// <summary>Du fond vers le premier plan : la derniere couche qui peint une case gagne.</summary>
    private static readonly string[] LayerOrder = ["Ground", "Wall-Door", "Window", "Objects"];

    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static GridMap Load()
    {
        var legend = TileLegend.FromJson(ReadConfig("tile_legend.json"));
        var layers = ReadTileLayers(Path.Combine(RepositoryRoot, "Scenes", "Main.tscn"));

        var cells = LayerOrder
            .Where(layers.ContainsKey)
            .SelectMany(name => layers[name])
            .ToList();

        if (cells.Count == 0) throw new InvalidOperationException("Aucune tuile decodee depuis Main.tscn.");

        int minX = cells.Min(c => c.X), maxX = cells.Max(c => c.X);
        int minY = cells.Min(c => c.Y), maxY = cells.Max(c => c.Y);

        var map = new GridMap(maxX - minX + 1, maxY - minY + 1, new GridPosition(minX, minY));

        foreach (var cell in cells)
        {
            if (!legend.TryGetKind(cell.SourceId, cell.AtlasX, cell.AtlasY, out var kind)) continue;

            var position = new GridPosition(cell.X, cell.Y);
            if (TileLegend.ToCellType(kind) is { } type) map.SetCell(position, type);
            if (TileLegend.ToIndoorFlag(kind) is { } indoor) map.SetIndoor(position, indoor);
        }

        MapLocationsConfig.FromJson(ReadConfig("map_locations.json")).ApplyTo(map);
        return map;
    }

    public static string ReadConfig(string fileName)
        => File.ReadAllText(Path.Combine(RepositoryRoot, "config", fileName));

    /// <summary>
    /// Le catalogue livre : <c>config/items.json</c> et <c>config/weapons.json</c> reunis. Comme
    /// pour le domaine de planification et les profils, les tests s'appuient sur les vrais fichiers
    /// et non sur une copie de circonstance — un objet retire du catalogue doit faire echouer les
    /// tests qui s'en servent, c'est tout l'interet.
    /// </summary>
    public static ItemCatalogue Items { get; } = ItemCatalogue.Merge(
        ItemCatalogue.FromJson(ReadConfig("items.json")),
        ItemCatalogue.FromJson(ReadConfig("weapons.json")));

    private readonly record struct TileRecord(int X, int Y, int SourceId, int AtlasX, int AtlasY);

    private static Dictionary<string, List<TileRecord>> ReadTileLayers(string scenePath)
    {
        var layers = new Dictionary<string, List<TileRecord>>();
        string? current = null;

        foreach (var line in File.ReadLines(scenePath))
        {
            var node = Regex.Match(line, @"^\[node name=""([^""]+)""");
            if (node.Success)
            {
                current = node.Groups[1].Value;
                continue;
            }

            var data = Regex.Match(line, @"^tile_map_data = PackedByteArray\(""([^""]*)""\)");
            if (!data.Success || current is null) continue;

            layers[current] = Decode(Convert.FromBase64String(data.Groups[1].Value));
        }

        return layers;
    }

    private static List<TileRecord> Decode(byte[] bytes)
    {
        var records = new List<TileRecord>();

        for (int offset = HeaderSize; offset + RecordSize <= bytes.Length; offset += RecordSize)
        {
            records.Add(new TileRecord(
                X: BitConverter.ToInt16(bytes, offset),
                Y: BitConverter.ToInt16(bytes, offset + 2),
                SourceId: BitConverter.ToUInt16(bytes, offset + 4),
                AtlasX: BitConverter.ToUInt16(bytes, offset + 6),
                AtlasY: BitConverter.ToUInt16(bytes, offset + 8)));
            // offset + 10 : tuile alternative / transformation, sans effet sur le type de cellule
        }

        return records;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "project.godot"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Racine du projet introuvable (project.godot).");
    }
}
