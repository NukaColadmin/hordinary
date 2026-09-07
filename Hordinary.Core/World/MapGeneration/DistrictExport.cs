using System.Text;
using System.Text.Json;
using Hordinary.Config;

namespace Hordinary.World.MapGeneration;

/// <summary>
/// Un quartier genere, ecrit dans un fichier et relu a l'identique.
///
/// C'est ce qui permet de rejouer, d'inspecter ou de <b>corriger a la main</b> un quartier interessant
/// — ou fautif. Sans lui, un bug de carte se raconte ; avec lui, il se joint a un rapport.
///
/// Le format est celui de la carte de demonstration, aussi litteralement que possible : la grille est
/// un damier de caracteres, et les lieux sont ecrits comme <c>config/map_locations.json</c> les ecrit,
/// avec les memes <c>kind</c>, les memes <c>min</c> et <c>max</c> inclusifs. Ce qu'un quartier a en
/// plus — la ceinture, la horde, les meubles — s'ajoute a cote sans rien changer a ce qui existait.
/// </summary>
public static class DistrictExport
{
    /// <summary>
    /// Le damier. Chaque caractere est un <see cref="CellType"/>, et <c>~</c> est la ceinture. Ce sont
    /// les memes lettres que celles des cartes ecrites dans les tests : une carte se lit a l'oeil.
    /// </summary>
    private static readonly (char Glyph, CellType Cell)[] Glyphs =
    [
        ('.', CellType.Floor),
        ('#', CellType.Wall),
        ('D', CellType.DoorClosed),
        ('L', CellType.DoorLocked),
        ('o', CellType.DoorOpen),
        ('W', CellType.Window),
        ('O', CellType.Obstacle),
        ('P', CellType.Pharmacy),
        ('E', CellType.Extraction),
        ('~', CellType.Border),
        ('?', CellType.Unknown),
    ];

    public static char GlyphOf(CellType type)
    {
        foreach (var (glyph, cell) in Glyphs)
        {
            if (cell == type) return glyph;
        }

        return '?';
    }

    public static CellType CellOf(char glyph)
    {
        foreach (var (candidate, cell) in Glyphs)
        {
            if (candidate == glyph) return cell;
        }

        throw new InvalidOperationException($"Caractere de carte inconnu : « {glyph} ».");
    }

    public static string ToJson(GeneratedDistrict district)
    {
        ArgumentNullException.ThrowIfNull(district);

        return JsonSerializer.Serialize(
            new DistrictFile
            {
                Seed = district.Seed,
                Profile = district.ProfileId,
                Border = district.Border.Id,
                BeltThickness = district.BeltThickness,
                Attempts = district.Attempts,
                Origin = [district.Map.Origin.X, district.Map.Origin.Y],
                Drop = [district.Drop.X, district.Drop.Y],
                Van = [district.Van.Min.X, district.Van.Min.Y, district.Van.Max.X, district.Van.Max.Y],
                Playable =
                [
                    district.Playable.Min.X, district.Playable.Min.Y,
                    district.Playable.Max.X, district.Playable.Max.Y,
                ],
                Rows = [.. Rows(district.Map)],
                Indoor = [.. Indoor(district.Map)],
                Unbreachable = [.. district.Map.Unbreachable.Select(cell => new[] { cell.X, cell.Y })],
                Zones = [.. district.Map.Zones.Select(Describe)],
                Items = [.. district.Items.Select(item => new ItemLine
                {
                    Item = item.ItemId,
                    At = [item.Position.X, item.Position.Y],
                })],
                Heavy = [.. district.Heavy.Select(heavy => new HeavyLine
                {
                    Id = heavy.Id,
                    Kind = heavy.Kind,
                    Mass = heavy.Mass,
                    At = [heavy.Position.X, heavy.Position.Y],
                    Size = [heavy.Width, heavy.Height],
                })],
                Zombies = [.. district.ZombieSpawns.Select(cell => new[] { cell.X, cell.Y })],
                Breach = [.. district.Breach.Select(cell => new[] { cell.X, cell.Y })],
            },
            new JsonSerializerOptions(GameConfig.JsonOptions) { WriteIndented = true });
    }

    /// <summary>
    /// Relit un quartier exporte. Ce qui revient est identique a ce qui est sorti — c'est la seule
    /// propriete qui compte, et le test associe la verifie case par case.
    /// </summary>
    public static GeneratedDistrict FromJson(string json, MapGenConfig? config = null)
    {
        var file = JsonSerializer.Deserialize<DistrictFile>(json, GameConfig.JsonOptions)
            ?? throw new InvalidOperationException("Fichier de quartier vide.");

        if (file.Rows.Count == 0) throw new InvalidOperationException("Le quartier exporte n'a aucune ligne.");

        int width = file.Rows[0].Length;
        if (file.Rows.Any(row => row.Length != width))
            throw new InvalidOperationException("Toutes les lignes d'un quartier ont la meme longueur.");

        var origin = new GridPosition(file.Origin[0], file.Origin[1]);
        var map = new GridMap(width, file.Rows.Count, origin);

        for (int y = 0; y < file.Rows.Count; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var cell = new GridPosition(origin.X + x, origin.Y + y);
                map.SetCell(cell, CellOf(file.Rows[y][x]));
                map.SetIndoor(cell, y < file.Indoor.Count && x < file.Indoor[y].Length && file.Indoor[y][x] == 'i');
            }
        }

        foreach (var cell in file.Unbreachable) map.SetUnbreachable(new GridPosition(cell[0], cell[1]));
        foreach (var zone in file.Zones) map.AddZone(zone.ToZone());

        var border = config?.Border.Find(file.Border)
            ?? new BorderKind { Id = file.Border, Name = file.Border, Tile = "0:0,0" };

        return new GeneratedDistrict
        {
            Map = map,
            Seed = file.Seed,
            ProfileId = file.Profile,
            Border = border,
            BeltThickness = file.BeltThickness,
            Playable = Rect.FromBounds(file.Playable[0], file.Playable[1], file.Playable[2], file.Playable[3]),
            Drop = new GridPosition(file.Drop[0], file.Drop[1]),
            Van = Rect.FromBounds(file.Van[0], file.Van[1], file.Van[2], file.Van[3]),
            Objective = file.Zones
                .Select(zone => zone.ToZone())
                .FirstOrDefault(zone => zone.Kind is not (ZoneKind.Start or ZoneKind.Extraction
                                                       or ZoneKind.Street or ZoneKind.Alley)),
            ZombieSpawns = [.. file.Zombies.Select(cell => new GridPosition(cell[0], cell[1]))],
            Items = [.. file.Items.Select(item => new DistrictItem(item.Item, new GridPosition(item.At[0], item.At[1])))],
            Heavy = [.. file.Heavy.Select(heavy => new HeavyObject(
                heavy.Id, heavy.Kind, new GridPosition(heavy.At[0], heavy.At[1]), heavy.Mass)
            {
                Width = heavy.Size is [var width, _] ? width : 1,
                Height = heavy.Size is [_, var height] ? height : 1,
            })],
            Breach = [.. file.Breach.Select(cell => new GridPosition(cell[0], cell[1]))],
            Attempts = file.Attempts,
        };
    }

    // --- Le damier --------------------------------------------------------------------------------

    private static IEnumerable<string> Rows(GridMap map)
    {
        for (int y = 0; y < map.Height; y++)
        {
            var row = new StringBuilder(map.Width);

            for (int x = 0; x < map.Width; x++)
            {
                row.Append(GlyphOf(map.GetCell(new GridPosition(map.Origin.X + x, map.Origin.Y + y))));
            }

            yield return row.ToString();
        }
    }

    /// <summary>
    /// Le marqueur interieur / exterieur, en damier lui aussi. Il ne se deduit pas du type de cellule
    /// — une chaussee et un carrelage sont l'un et l'autre du <see cref="CellType.Floor"/> — et une
    /// relecture qui le perdrait rendrait tous les batiments exterieurs.
    /// </summary>
    private static IEnumerable<string> Indoor(GridMap map)
    {
        for (int y = 0; y < map.Height; y++)
        {
            var row = new StringBuilder(map.Width);

            for (int x = 0; x < map.Width; x++)
            {
                row.Append(map.IsIndoor(new GridPosition(map.Origin.X + x, map.Origin.Y + y)) ? 'i' : '.');
            }

            yield return row.ToString();
        }
    }

    private static ZoneDefinition Describe(MapZone zone) => new()
    {
        Name = zone.Name,
        Kind = zone.Kind,
        Profile = zone.Profile,
        Min = [zone.Min.X, zone.Min.Y],
        Max = [zone.Max.X, zone.Max.Y],
    };

    // --- Le fichier -------------------------------------------------------------------------------

    private sealed class DistrictFile
    {
        public int Seed { get; init; }
        public string Profile { get; init; } = string.Empty;
        public string Border { get; init; } = string.Empty;
        public int BeltThickness { get; init; }
        public int Attempts { get; init; } = 1;
        public int[] Origin { get; init; } = [0, 0];
        public int[] Drop { get; init; } = [0, 0];
        public int[] Van { get; init; } = [0, 0, 0, 0];
        public int[] Playable { get; init; } = [0, 0, 0, 0];
        public List<string> Rows { get; init; } = [];
        public List<string> Indoor { get; init; } = [];
        public List<int[]> Unbreachable { get; init; } = [];
        public List<ZoneDefinition> Zones { get; init; } = [];
        public List<ItemLine> Items { get; init; } = [];
        public List<HeavyLine> Heavy { get; init; } = [];
        public List<int[]> Zombies { get; init; } = [];
        public List<int[]> Breach { get; init; } = [];
    }

    private sealed class ItemLine
    {
        public string Item { get; init; } = string.Empty;
        public int[] At { get; init; } = [0, 0];
    }

    private sealed class HeavyLine
    {
        public string Id { get; init; } = string.Empty;
        public string Kind { get; init; } = string.Empty;
        public float Mass { get; init; } = 1f;
        public int[] At { get; init; } = [0, 0];

        /// <summary>Largeur et hauteur en cases. Absente d'un fichier ancien : le meuble fait 1 x 1.</summary>
        public int[] Size { get; init; } = [1, 1];
    }
}
