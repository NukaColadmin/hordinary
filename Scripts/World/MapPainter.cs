using Godot;
using Hordinary.Config;
using Hordinary.World.MapGeneration;

// Godot expose un noeud 3D nomme GridMap : on desambigue en faveur de notre grille de jeu.
using GridMap = Hordinary.World.GridMap;

namespace Hordinary.World;

/// <summary>
/// Peint une <see cref="GridMap"/> generee dans les <see cref="TileMapLayer"/> de la scene.
///
/// C'est l'exact inverse de <see cref="MapLoader"/>, et le second point de contact entre le moteur et
/// le coeur de simulation. Il n'existe que depuis le livrable 13 : une carte dessinee n'avait qu'a
/// etre lue, un quartier genere doit etre montre. Le generateur, lui, continue d'ignorer que Godot
/// existe — c'est ici, et nulle part ailleurs, qu'un <see cref="CellType"/> redevient une image.
///
/// La correspondance passe par <c>config/tile_legend.json</c> dans les deux sens : repeindre l'atlas
/// ne demande toujours que de modifier ce fichier.
/// </summary>
public sealed class MapPainter(TileLegend legend)
{
    private readonly TileLegend _legend = legend ?? throw new ArgumentNullException(nameof(legend));

    /// <summary>Ordre attendu des couches, du fond vers le premier plan.</summary>
    public const int Ground = 0;
    public const int WallDoor = 1;
    public const int Window = 2;
    public const int Objects = 3;

    /// <summary>
    /// Efface les couches et repeint le quartier. Les couches manquantes sont ignorees : une scene
    /// reduite doit produire une carte incomplete, pas une exception au demarrage.
    /// </summary>
    /// <returns>Les tuiles qu'aucune entree de legende ne sait dessiner, sans doublon.</returns>
    public IReadOnlyList<string> Paint(
        IReadOnlyList<TileMapLayer?> layers,
        GeneratedDistrict district,
        IEnumerable<WorldItem>? items = null)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(district);

        var missing = new List<string>();
        var seen = new HashSet<TileKind>();

        foreach (var layer in layers)
        {
            layer?.Clear();
        }

        // La ceinture a sa propre tuile, nommee par le profil de quartier plutot que deduite du type
        // de cellule : le brouillard ordinaire et le brouillard radioactif sont deux images d'une
        // meme regle, et seule la configuration sait laquelle ce quartier emploie.
        TileLegend.TryParseKey(district.Border.Tile, out var border);

        var map = district.Map;

        // Les meubles poussables ont leur propre image (voir HeavyNode) : sous eux on peint le sol,
        // pas la tuile d'obstacle. La legende ne connait qu'une tuile par terrain, et cette
        // tuile-la est une voiture — sans cette exception, un rayonnage et une benne s'affichaient
        // l'un et l'autre en epave miniature.
        var furnished = district.Heavy.SelectMany(heavy => heavy.Cells).ToHashSet();

        // Les voies sont deja sur la carte, sous forme de zones : la passe 5 du generateur enregistre
        // chaque rue et chaque ruelle. Le peintre n'a donc rien a recalculer ni a se faire passer —
        // il relit ce que le quartier declare, et une carte relue depuis un export se peint pareil.
        var roads = map.Zones
            .Where(zone => zone.Kind is ZoneKind.Street or ZoneKind.Alley)
            .SelectMany(zone => zone.Positions())
            .ToHashSet();

        foreach (var cell in map.AllPositions())
        {
            var type = map.GetCell(cell);
            if (type == CellType.Unknown) continue;

            if (type == CellType.Border)
            {
                Put(layers, Ground, cell, border);
                continue;
            }

            // Le sol d'abord, et pour toutes les cases : les murs, portes et fenetres se posent
            // par-dessus, et une porte ouverte laisse voir la chaussee en dessous d'elle.
            var floor = FloorAt(map, cell, type, roads);

            if (floor is null && seen.Add(TileKind.FloorOutdoor))
                missing.Add("Aucune tuile ne dessine de sol dans config/tile_legend.json.");

            Put(layers, Ground, cell, floor);

            // Les meubles poussables ont leur propre image (voir HeavyNode) : sous eux on s'arrete au
            // sol. Un sol de plus, et le decor pose une epave miniature sous chaque rayonnage.
            if (furnished.Contains(cell)) continue;

            int layer = LayerOf(type);
            if (layer == Ground) continue;

            var kind = KindOf(type);
            var address = _legend.TileFor(kind);

            if (address is null)
            {
                if (seen.Add(kind)) missing.Add($"Aucune tuile ne dessine « {kind} » dans config/tile_legend.json.");
                continue;
            }

            Put(layers, layer, cell, address);
        }

        foreach (var item in items ?? [])
        {
            Put(layers, Objects, item.Position, _legend.TileOf(item.ItemId));
        }

        return missing;
    }

    /// <summary>
    /// Montre — ou retire — l'objet pose sur une case, sur la couche des objets et nulle part
    /// ailleurs.
    ///
    /// C'est la contrepartie a l'echelle d'une case de <see cref="Paint"/>, et elle existe parce
    /// qu'un decor peint une fois pour toutes ment des le premier ramassage : les caisses de la
    /// mission sont posees apres la generation, un survivant en prend une, un autre laisse tomber
    /// son sac en mourant. Le monde annonce les trois (<see cref="WorldState.ItemPlaced"/>,
    /// <see cref="WorldState.ItemTaken"/>), il ne reste qu'a suivre.
    /// </summary>
    /// <param name="itemId">L'objet a montrer, ou <c>null</c> pour laisser la case nue.</param>
    public void ShowItem(IReadOnlyList<TileMapLayer?> layers, GridPosition cell, string? itemId)
    {
        ArgumentNullException.ThrowIfNull(layers);

        if (Objects >= layers.Count || layers[Objects] is not { } layer) return;

        var coords = new Vector2I(cell.X, cell.Y);
        var address = itemId is null ? null : _legend.TileOf(itemId);

        if (address is not { } tile) layer.EraseCell(coords);
        else layer.SetCell(coords, tile.Source, new Vector2I(tile.X, tile.Y));
    }

    /// <summary>
    /// Repeint une case dont le terrain vient de changer : une porte qu'on ouvre, une vitre qu'on
    /// brise.
    ///
    /// Sans elle, le decor mentait sur le seul changement de terrain que le jeu produise : une porte
    /// forcee restait dessinee fermee jusqu'a la fin de la partie, et un survivant traversait une
    /// porte close. Le monde le disait pourtant — <see cref="GridMap.CellChanged"/> annonce la case —
    /// et il ne manquait qu'un peintre pour l'ecouter.
    ///
    /// Seules les couches hautes sont touchees : le sol a ete peint une fois pour toutes, et c'est
    /// justement lui qu'une porte ouverte doit laisser voir.
    /// </summary>
    public void ShowCell(IReadOnlyList<TileMapLayer?> layers, GridPosition cell, CellType type)
    {
        ArgumentNullException.ThrowIfNull(layers);

        var coords = new Vector2I(cell.X, cell.Y);

        foreach (int rank in (int[])[WallDoor, Window])
        {
            if (rank < layers.Count && layers[rank] is { } layer) layer.EraseCell(coords);
        }

        int destination = LayerOf(type);
        if (destination == Ground) return;

        Put(layers, destination, cell, _legend.TileFor(KindOf(type)));
    }

    /// <summary>
    /// Le sol d'une case, variante comprise.
    ///
    /// Le quartier declarait quatre sols exterieurs et n'en peignait qu'un : <see cref="TileLegend.TileFor"/>
    /// rend toujours la meme tuile pour un terrain donne, et une carte entiere se rendait en bitume —
    /// ni trottoir, ni herbe, ni les trois carrelages d'interieur. Le choix se fait donc ici, et il se
    /// lit sur la carte elle-meme plutot que sur un tirage :
    ///
    /// <list type="bullet">
    /// <item>une case de rue ou de ruelle est une <b>chaussee</b> ;</item>
    /// <item>une case exterieure qui touche une voie est un <b>trottoir</b> ;</item>
    /// <item>tout le reste de l'exterieur est une <b>friche</b>, rendue a l'herbe ;</item>
    /// <item>a l'interieur, le sol est celui du <b>batiment</b> : une variante par zone, la meme d'un
    /// bout a l'autre d'une piece. Un carrelage tire case par case ferait un damier, pas une piece.</item>
    /// </list>
    ///
    /// Rien de tout cela n'entre dans le jeu : les quatre variantes restent du <see cref="CellType.Floor"/>
    /// exterieur, et la navigation ne les distingue pas.
    /// </summary>
    private TileAddress? FloorAt(GridMap map, GridPosition cell, CellType type, IReadOnlySet<GridPosition> roads)
    {
        if (type == CellType.Pharmacy)
        {
            return _legend.TileFor(TileKind.FloorPharmacy) ?? Indoor(map, cell);
        }

        if (map.IsIndoor(cell)) return Indoor(map, cell);

        if (roads.Contains(cell)) return Outdoor(TileKind.FloorRoad);

        return Kerb(cell, roads) ? Outdoor(TileKind.FloorSidewalk) : Outdoor(TileKind.FloorGrass);
    }

    /// <summary>Vrai si la case borde une voie : c'est la definition d'un trottoir.</summary>
    private static bool Kerb(GridPosition cell, IReadOnlySet<GridPosition> roads)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                if (roads.Contains(new GridPosition(cell.X + dx, cell.Y + dy))) return true;
            }
        }

        return false;
    }

    /// <summary>Une variante exterieure, ou le sol exterieur generique si la legende n'en declare pas.</summary>
    private TileAddress? Outdoor(TileKind kind)
        => _legend.TileFor(kind) ?? _legend.TileFor(TileKind.FloorOutdoor);

    /// <summary>
    /// Le sol d'un interieur : une variante par batiment, choisie sur le nom de la zone. Le nom est
    /// stable — c'est la passe 5 qui le donne — donc un quartier se repeint toujours a l'identique,
    /// et deux batiments voisins n'ont pas le meme carrelage.
    /// </summary>
    private TileAddress? Indoor(GridMap map, GridPosition cell)
    {
        var variants = _legend.TilesFor(TileKind.FloorIndoor);
        if (variants.Count == 0) return null;

        string? zone = map.ZonesAt(cell)
            .FirstOrDefault(zone => zone.Kind is ZoneKind.Building or ZoneKind.Pharmacy)?.Name;

        if (zone is null) return variants[0];

        // Somme des caracteres plutot que GetHashCode : celui de string est randomise par processus
        // depuis .NET Core, et le meme quartier n'aurait pas le meme sol d'un lancement a l'autre.
        int sum = zone.Sum(character => character);

        return variants[sum % variants.Count];
    }

    /// <summary>
    /// Le terrain qui dessine ce type de cellule, pour tout ce qui n'est pas un sol — les sols
    /// passent par <see cref="FloorAt"/>, qui sait aussi en choisir la variante.
    /// C'est <see cref="TileLegend.ToCellType"/> a l'envers, et l'inverse n'est pas une bijection.
    /// </summary>
    private static TileKind KindOf(CellType cell) => cell switch
    {
        CellType.Wall => TileKind.Wall,
        CellType.Obstacle => TileKind.Obstacle,
        CellType.Window => TileKind.Window,
        CellType.DoorOpen => TileKind.DoorOpen,
        CellType.DoorClosed => TileKind.DoorClosed,

        // Le verrouillage est un etat d'execution, pas un terrain : une porte verrouillee se dessine
        // comme une porte fermee, et c'est le jeu qui sait qu'elle ne s'ouvre pas.
        CellType.DoorLocked => TileKind.DoorClosed,
        _ => TileKind.Ignored,
    };

    private static int LayerOf(CellType cell) => cell switch
    {
        CellType.Wall or CellType.DoorOpen or CellType.DoorClosed or CellType.DoorLocked => WallDoor,
        CellType.Window => Window,
        CellType.Obstacle => WallDoor,
        _ => Ground,
    };

    private static void Put(IReadOnlyList<TileMapLayer?> layers, int index, GridPosition cell, TileAddress? address)
    {
        if (address is not { } tile) return;
        if (index < 0 || index >= layers.Count || layers[index] is not { } layer) return;

        layer.SetCell(new Vector2I(cell.X, cell.Y), tile.Source, new Vector2I(tile.X, tile.Y));
    }
}
