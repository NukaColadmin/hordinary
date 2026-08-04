using System.Text.Json;
using Hordinary.World;

namespace Hordinary.Config;

/// <summary>
/// Signification d'une tuile du TileSet. Une tuile decrit plus que son type de cellule
/// (un sol interieur et un sol exterieur sont tous deux <see cref="CellType.Floor"/>).
///
/// Il n'existe volontairement pas de tuile « porte verrouillee » : le verrouillage est un etat
/// d'execution pose par le jeu (tirage aleatoire au demarrage, puis crochetage ou effraction),
/// pas une donnee de la carte. Seules les deux apparences existent, fermee et ouverte.
/// </summary>
public enum TileKind
{
    Ignored = 0,
    FloorIndoor,
    FloorOutdoor,
    Wall,
    Obstacle,
    Window,
    WindowBroken,
    DoorOpen,
    DoorClosed,
    Medicine,
}

/// <summary>
/// Table de correspondance « tuile du TileSet » -> <see cref="TileKind"/>, chargee depuis
/// <c>config/tile_legend.json</c>. C'est le seul endroit qui sait que la tuile <c>0:5,0</c> est un
/// mur : repeindre l'atlas ne demande que de modifier ce fichier.
/// </summary>
public sealed class TileLegend
{
    /// <summary>Taille d'une tuile en pixels (TileSet Godot).</summary>
    public int TileSize { get; init; } = 16;

    /// <summary>
    /// Cles au format <c>"source:x,y"</c> : identifiant de source du TileSet, puis coordonnees dans
    /// l'atlas de cette source. Le TileSet en compte plusieurs — la source 0 est la planche
    /// <c>atlas.png</c>, les portes vivent dans leur propre source a la coordonnee <c>0,0</c>.
    /// </summary>
    public Dictionary<string, TileKind> Tiles { get; init; } = [];

    public static TileLegend FromJson(string json)
        => JsonSerializer.Deserialize<TileLegend>(json, GameConfig.JsonOptions) ?? new TileLegend();

    public static string Key(int sourceId, int atlasX, int atlasY) => $"{sourceId}:{atlasX},{atlasY}";

    public bool TryGetKind(int sourceId, int atlasX, int atlasY, out TileKind kind)
        => Tiles.TryGetValue(Key(sourceId, atlasX, atlasY), out kind);

    /// <summary>Type de cellule correspondant. <c>null</c> = la tuile ne change pas le type de cellule.</summary>
    public static CellType? ToCellType(TileKind kind) => kind switch
    {
        TileKind.FloorIndoor => CellType.Floor,
        TileKind.FloorOutdoor => CellType.Floor,
        TileKind.Medicine => CellType.Floor,
        // Une fenetre brisee est une ouverture praticable : elle cesse d'etre un obstacle.
        TileKind.WindowBroken => CellType.Floor,
        TileKind.Wall => CellType.Wall,
        TileKind.Obstacle => CellType.Obstacle,
        TileKind.Window => CellType.Window,
        TileKind.DoorOpen => CellType.DoorOpen,
        TileKind.DoorClosed => CellType.DoorClosed,
        _ => null,
    };

    /// <summary><c>true</c> interieur, <c>false</c> exterieur, <c>null</c> : ne change pas le marqueur.</summary>
    public static bool? ToIndoorFlag(TileKind kind) => kind switch
    {
        TileKind.FloorIndoor => true,
        TileKind.Medicine => true,
        TileKind.FloorOutdoor => false,
        _ => null,
    };
}
