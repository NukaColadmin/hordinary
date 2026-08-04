using Godot;
using Hordinary.Config;

namespace Hordinary.World;

/// <summary>Objet repere sur la carte (medicaments...), pose par une tuile.</summary>
public readonly record struct MapItem(TileKind Kind, GridPosition Position);

public sealed class MapBuildResult(GridMap map, IReadOnlyList<MapItem> items, IReadOnlyList<string> warnings)
{
    public GridMap Map { get; } = map;
    public IReadOnlyList<MapItem> Items { get; } = items;
    public IReadOnlyList<string> Warnings { get; } = warnings;
}

/// <summary>
/// Convertit les <see cref="TileMapLayer"/> peints dans l'editeur en <see cref="GridMap"/>.
/// C'est le seul point de contact entre le moteur et le coeur de simulation : tout le reste
/// de l'IA travaille sur la <see cref="GridMap"/> et ignore l'existence de Godot.
/// </summary>
public sealed class MapLoader(TileLegend legend)
{
    private readonly TileLegend _legend = legend ?? throw new ArgumentNullException(nameof(legend));

    /// <summary>
    /// Les couches sont appliquees dans l'ordre : la derniere qui peint une case gagne.
    /// L'ordre attendu est donc sol, puis murs et portes, puis fenetres, puis objets.
    /// </summary>
    public MapBuildResult Load(IReadOnlyList<TileMapLayer> layers)
    {
        var warnings = new List<string>();
        var items = new List<MapItem>();

        var bounds = ComputeBounds(layers);
        if (bounds is null)
        {
            warnings.Add("Aucune tuile trouvee : la carte est vide.");
            return new MapBuildResult(new GridMap(1, 1), items, warnings);
        }

        var rect = bounds.Value;
        var map = new GridMap(rect.Size.X, rect.Size.Y, new GridPosition(rect.Position.X, rect.Position.Y));
        var unknownTiles = new HashSet<string>();

        foreach (var layer in layers)
        {
            if (layer is null) continue;

            foreach (var cell in layer.GetUsedCells())
            {
                // Le TileSet compte plusieurs sources (planche d'atlas, porte fermee, porte
                // ouverte) : la coordonnee d'atlas seule ne suffit plus a identifier une tuile.
                int sourceId = layer.GetCellSourceId(cell);
                var atlas = layer.GetCellAtlasCoords(cell);
                var position = new GridPosition(cell.X, cell.Y);

                if (!_legend.TryGetKind(sourceId, atlas.X, atlas.Y, out var kind))
                {
                    var key = TileLegend.Key(sourceId, atlas.X, atlas.Y);
                    if (unknownTiles.Add(key))
                    {
                        warnings.Add($"Tuile {key} (couche '{layer.Name}') absente de config/tile_legend.json.");
                    }
                    continue;
                }

                var cellType = TileLegend.ToCellType(kind);
                if (cellType is not null) map.SetCell(position, cellType.Value);

                var indoor = TileLegend.ToIndoorFlag(kind);
                if (indoor is not null) map.SetIndoor(position, indoor.Value);

                if (kind == TileKind.Medicine) items.Add(new MapItem(kind, position));
            }
        }

        return new MapBuildResult(map, items, warnings);
    }

    /// <summary>Rectangle englobant toutes les couches utilisees.</summary>
    private static Rect2I? ComputeBounds(IReadOnlyList<TileMapLayer> layers)
    {
        Rect2I? bounds = null;

        foreach (var layer in layers)
        {
            if (layer is null) continue;

            var used = layer.GetUsedRect();
            if (used.Size.X <= 0 || used.Size.Y <= 0) continue;

            bounds = bounds is null ? used : bounds.Value.Merge(used);
        }

        return bounds;
    }
}
