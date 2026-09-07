using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hordinary.World;

namespace Hordinary.Config;

/// <summary>
/// Ce qu'une tuile fait a la carte. C'est un vocabulaire <b>structurel</b>, pas un catalogue : il
/// nomme les terrains que la simulation distingue, jamais les objets qu'on ramasse.
///
/// Les cinq variantes de sol du livrable 11 n'apparaissent donc pas ici — un carrelage et une
/// chaussee sont l'un et l'autre un <see cref="FloorIndoor"/> ou un <see cref="FloorOutdoor"/>, et
/// c'est exactement ce que la specification demande : « une tuile de sol ne change rien au jeu ».
/// De meme, les quinze tuiles d'objets et d'armes n'ont pas de valeur ici : elles portent un
/// identifiant du catalogue, qui est une donnee.
///
/// Il n'existe volontairement pas de tuile « porte verrouillee » : le verrouillage est un etat
/// d'execution pose par le jeu (tirage aleatoire au demarrage, puis crochetage ou effraction), pas
/// une donnee de la carte. Seules les deux apparences existent, fermee et ouverte. La barricade obeit
/// a la meme regle : elle verrouille une porte, elle n'en peint pas une autre.
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

    /// <summary>Sol de la zone d'objectif. Le domaine de planification connait ce lieu par son nom.</summary>
    FloorPharmacy,

    /// <summary>
    /// La chaussee. Comme les deux suivantes, c'est un <see cref="FloorOutdoor"/> qui porte un nom :
    /// meme <see cref="CellType.Floor"/>, meme marqueur exterieur, meme absence d'effet sur le jeu.
    /// Ces noms n'existent que pour que <c>MapPainter</c> puisse <b>demander</b> une image plutot que
    /// de prendre la premiere venue — sans eux, une seule des quatre variantes de sol exterieur etait
    /// jamais peinte, et tout un quartier se rendait en bitume.
    /// </summary>
    FloorRoad,

    /// <summary>Le trottoir : la bordure d'une voie, la ou le quartier passe de la rue au terrain.</summary>
    FloorSidewalk,

    /// <summary>La friche : l'exterieur qu'aucune voie ne traverse, rendu a l'herbe.</summary>
    FloorGrass,

    /// <summary>
    /// Fenetre a barreaux du poste de police (livrable 11). Un mur pour la navigation et la vue, mais
    /// pas seulement : c'est la seule tuile qui <b>retire</b> une action du repertoire. Sur elle,
    /// <c>BreakWindow</c> n'est pas propose, et il ne reste que l'effraction — bruyante, au milieu
    /// d'un batiment plein. C'est toute la difficulte de la zone, et elle est declaree ici plutot que
    /// deduite du dessin.
    /// </summary>
    WindowBarred,

    /// <summary>
    /// Rideau metallique a moitie baisse de la quincaillerie (livrable 11). Une porte fermee qu'on
    /// ouvre mais qu'on ne force jamais : entree unique et etroite, donc facile a tenir au retour.
    /// </summary>
    ShutterHalf,

    /// <summary>
    /// La ou le quartier s'arrete (livrable 13). Une seule famille de tuiles la dessine — le
    /// brouillard — et elle remplace tout ce qui se trouve au-dela : il n'y a ni bande de decor a
    /// rendre, ni fondu a calculer, parce que le brouillard <b>est</b> le fondu.
    ///
    /// Plusieurs tuiles peuvent declarer ce terrain : le brouillard ordinaire et le brouillard
    /// radioactif sont deux images d'une meme regle, et c'est <c>config/mapgen.json</c> qui dit
    /// laquelle un quartier emploie.
    /// </summary>
    Border,
}

/// <summary>
/// Ce qu'une entree de <c>config/tile_legend.json</c> declare : un terrain, et facultativement un
/// objet du catalogue pose dessus.
///
/// Deux ecritures, pour que le fichier reste lisible :
/// <code>
/// "0:5,0": "Wall"                                  // un terrain, rien de plus
/// "0:0,3": { "kind": "Ignored", "item": "canned_food" }   // un objet pose sur ce qu'il y a dessous
/// "0:2,0": { "kind": "FloorIndoor", "item": "medikit" }   // les deux a la fois
/// </code>
/// </summary>
[JsonConverter(typeof(TileDefinitionConverter))]
public sealed record TileDefinition(TileKind Kind, string? Item = null)
{
    public bool CarriesItem => !string.IsNullOrWhiteSpace(Item);

    public override string ToString() => CarriesItem ? $"{Kind} + {Item}" : Kind.ToString();
}

/// <summary>Lit les deux ecritures d'une entree de legende, la courte et la longue.</summary>
public sealed class TileDefinitionConverter : JsonConverter<TileDefinition>
{
    public override TileDefinition Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new TileDefinition(ParseKind(reader.GetString()));
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Une entree de legende est une chaine ou un objet { kind, item }.");
        }

        var kind = TileKind.Ignored;
        string? item = null;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) continue;

            string property = reader.GetString() ?? string.Empty;
            reader.Read();

            switch (property.ToLowerInvariant())
            {
                case "kind": kind = ParseKind(reader.GetString()); break;
                case "item": item = reader.GetString(); break;
                default: reader.Skip(); break;
            }
        }

        return new TileDefinition(kind, item);
    }

    public override void Write(Utf8JsonWriter writer, TileDefinition value, JsonSerializerOptions options)
    {
        if (!value.CarriesItem)
        {
            writer.WriteStringValue(value.Kind.ToString());
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("kind", value.Kind.ToString());
        writer.WriteString("item", value.Item);
        writer.WriteEndObject();
    }

    private static TileKind ParseKind(string? name)
    {
        if (Enum.TryParse<TileKind>(name, ignoreCase: true, out var kind)) return kind;

        throw new JsonException(
            $"Terrain de tuile inconnu : « {name} ». Terrains connus : "
          + $"{string.Join(", ", Enum.GetNames<TileKind>().Order())}.");
    }
}

/// <summary>
/// Lit la table des tuiles en sautant les cles de commentaire. Les autres fichiers du projet
/// commentent avec des proprietes <c>"//quelque chose"</c> que le deserialiseur ignore parce
/// qu'elles ne correspondent a aucun champ ; dans un dictionnaire, toutes les cles comptent. Ce
/// convertisseur leur rend le meme privilege, pour que la legende reste commentee comme le reste.
/// </summary>
public sealed class TileTableConverter : JsonConverter<Dictionary<string, TileDefinition>>
{
    public override Dictionary<string, TileDefinition> Read(
        ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("La table des tuiles est un objet.");
        }

        var tiles = new Dictionary<string, TileDefinition>(StringComparer.OrdinalIgnoreCase);

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) continue;

            string key = reader.GetString() ?? string.Empty;
            reader.Read();

            if (key.StartsWith("//", StringComparison.Ordinal))
            {
                reader.Skip();
                continue;
            }

            tiles[key] = JsonSerializer.Deserialize<TileDefinition>(ref reader, options)
                ?? throw new JsonException($"La tuile « {key} » n'a pas de definition.");
        }

        return tiles;
    }

    public override void Write(
        Utf8JsonWriter writer, Dictionary<string, TileDefinition> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        foreach (var (key, tile) in value)
        {
            writer.WritePropertyName(key);
            JsonSerializer.Serialize(writer, tile, options);
        }

        writer.WriteEndObject();
    }
}

/// <summary>
/// Ou vit une tuile dans le TileSet : sa source, puis sa cellule dans l'atlas de cette source.
/// C'est la cle de legende <c>"source:x,y"</c>, relue en trois nombres — ce que demande quiconque
/// veut decouper l'image plutot que seulement savoir ce qu'elle signifie.
/// </summary>
public readonly record struct TileAddress(int Source, int X, int Y)
{
    public override string ToString() => TileLegend.Key(Source, X, Y);
}

/// <summary>
/// Table de correspondance « tuile du TileSet » -> <see cref="TileDefinition"/>, chargee depuis
/// <c>config/tile_legend.json</c>. C'est le seul endroit qui sait que la tuile <c>0:5,0</c> est un
/// mur : repeindre l'atlas ne demande que de modifier ce fichier.
/// </summary>
public sealed class TileLegend
{
    /// <summary>Taille d'une tuile en pixels (TileSet Godot).</summary>
    public int TileSize { get; init; } = 32;

    /// <summary>
    /// Cles au format <c>"source:x,y"</c> : identifiant de source du TileSet, puis coordonnees dans
    /// l'atlas de cette source. Le TileSet en compte plusieurs — la source 0 est la planche
    /// <c>atlas.png</c>, les portes vivent dans leur propre source a la coordonnee <c>0,0</c>.
    /// </summary>
    [JsonConverter(typeof(TileTableConverter))]
    public Dictionary<string, TileDefinition> Tiles { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public static TileLegend FromJson(string json)
        => JsonSerializer.Deserialize<TileLegend>(json, GameConfig.JsonOptions) ?? new TileLegend();

    public static string Key(int sourceId, int atlasX, int atlasY) => $"{sourceId}:{atlasX},{atlasY}";

    public bool TryGetTile(int sourceId, int atlasX, int atlasY, out TileDefinition tile)
        => Tiles.TryGetValue(Key(sourceId, atlasX, atlasY), out tile!);

    public bool TryGetKind(int sourceId, int atlasX, int atlasY, out TileKind kind)
    {
        if (TryGetTile(sourceId, atlasX, atlasY, out var tile))
        {
            kind = tile.Kind;
            return true;
        }

        kind = TileKind.Ignored;
        return false;
    }

    /// <summary>
    /// Relit une cle de legende. C'est l'inverse exact de <see cref="Key"/>, et le seul endroit qui
    /// sache defaire le format : une cle mal formee ne doit pas se decouvrir a trois endroits.
    /// </summary>
    public static bool TryParseKey(string? key, out TileAddress address)
    {
        address = default;
        if (key is null) return false;

        int colon = key.IndexOf(':');
        int comma = key.IndexOf(',');

        if (colon <= 0 || comma <= colon + 1 || comma == key.Length - 1) return false;

        if (!int.TryParse(key.AsSpan(0, colon), CultureInfo.InvariantCulture, out int source)) return false;
        if (!int.TryParse(key.AsSpan(colon + 1, comma - colon - 1), CultureInfo.InvariantCulture, out int x)) return false;
        if (!int.TryParse(key.AsSpan(comma + 1), CultureInfo.InvariantCulture, out int y)) return false;

        address = new TileAddress(source, x, y);
        return true;
    }

    private Dictionary<string, TileAddress>? _byItem;

    /// <summary>
    /// La tuile qui represente un objet du catalogue, adressee dans le TileSet.
    ///
    /// C'est ce qui fait qu'un objet n'a <b>qu'une</b> image : l'icone d'inventaire est la meme tuile
    /// de 32 px que celle posee au sol, simplement agrandie dans un emplacement. Une seconde serie de
    /// dessins en trois quarts serait plus jolie, mais elle ferait exister deux verites pour le meme
    /// objet — et la premiere fois qu'on repeindrait l'une sans l'autre, une conserve n'aurait plus la
    /// meme tete selon qu'on la regarde par terre ou dans un sac.
    /// </summary>
    public TileAddress? TileOf(string? itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId)) return null;

        _byItem ??= BuildItemIndex();
        return _byItem.TryGetValue(itemId, out var address) ? address : null;
    }

    /// <summary>
    /// Un objet peut apparaitre sur plusieurs tuiles — <c>medikit</c> en a deux, l'heritee du MVP qui
    /// peint aussi un sol, et la sienne propre. L'icone prend celle qui ne pose que l'objet : une
    /// vignette d'inventaire montre l'objet, pas le carrelage sur lequel il trainait. A egalite, la
    /// plus petite cle gagne, pour que l'ordre d'un dictionnaire ne decide de rien.
    /// </summary>
    private Dictionary<string, TileAddress> BuildItemIndex()
    {
        var index = new Dictionary<string, TileAddress>(StringComparer.OrdinalIgnoreCase);

        var candidates = Tiles
            .Where(pair => pair.Value.CarriesItem)
            .OrderBy(pair => pair.Value.Kind == TileKind.Ignored ? 0 : 1)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal);

        foreach (var (key, tile) in candidates)
        {
            if (TryParseKey(key, out var address)) index.TryAdd(tile.Item!, address);
        }

        return index;
    }

    /// <summary>Les identifiants d'objets que la legende pose sur la carte, sans doublon.</summary>
    public IEnumerable<string> DeclaredItems
        => Tiles.Values.Where(tile => tile.CarriesItem).Select(tile => tile.Item!).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>Vrai si au moins une tuile declare ce terrain. Un <c>Obstacle</c> sans tuile serait invisible.</summary>
    public bool Declares(TileKind kind) => Tiles.Values.Any(tile => tile.Kind == kind);

    /// <summary>
    /// Une tuile qui dessine ce terrain, adressee dans le TileSet. C'est le chemin inverse de
    /// <see cref="ToCellType"/>, et il n'existe que depuis le livrable 13 : une carte generee doit
    /// etre <b>peinte</b>, alors qu'une carte dessinee n'avait qu'a etre lue.
    ///
    /// Plusieurs tuiles declarent souvent le meme terrain ; la plus petite cle gagne, pour que
    /// l'ordre d'un dictionnaire ne decide de rien. Une tuile qui pose aussi un objet est ecartee :
    /// peindre un mur ne doit pas semer une conserve.
    /// </summary>
    public TileAddress? TileFor(TileKind kind)
    {
        foreach (var address in TilesFor(kind)) return address;

        return null;
    }

    /// <summary>
    /// Toutes les tuiles qui dessinent ce terrain, dans l'ordre de leur cle. Plusieurs images pour un
    /// meme terrain, c'est le cas ordinaire des sols : trois carrelages differents restent trois
    /// carrelages, et rien dans le jeu ne les distingue.
    ///
    /// <see cref="TileFor"/> en prend une, toujours la meme ; qui veut varier les prend toutes et
    /// choisit — ce que fait <c>MapPainter</c> pour ne pas donner le meme sol a tous les batiments
    /// d'un quartier.
    /// </summary>
    public IReadOnlyList<TileAddress> TilesFor(TileKind kind)
    {
        var found = new List<TileAddress>();

        foreach (var (key, tile) in Tiles.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (tile.Kind != kind || tile.CarriesItem) continue;
            if (TryParseKey(key, out var address)) found.Add(address);
        }

        return found;
    }

    /// <summary>Type de cellule correspondant. <c>null</c> = la tuile ne change pas le type de cellule.</summary>
    public static CellType? ToCellType(TileKind kind) => kind switch
    {
        TileKind.FloorIndoor => CellType.Floor,
        TileKind.FloorOutdoor => CellType.Floor,
        TileKind.FloorRoad => CellType.Floor,
        TileKind.FloorSidewalk => CellType.Floor,
        TileKind.FloorGrass => CellType.Floor,
        // Une fenetre brisee est une ouverture praticable : elle cesse d'etre un obstacle.
        TileKind.WindowBroken => CellType.Floor,
        TileKind.FloorPharmacy => CellType.Pharmacy,
        TileKind.Wall => CellType.Wall,
        TileKind.Obstacle => CellType.Obstacle,
        TileKind.Window => CellType.Window,
        // Une fenetre a barreaux est un mur pour tout le monde ; ce qu'elle a de particulier n'est
        // pas son type de cellule mais l'action qu'elle interdit, voir ForbidsBreakWindow.
        TileKind.WindowBarred => CellType.Wall,
        TileKind.DoorOpen => CellType.DoorOpen,
        TileKind.DoorClosed => CellType.DoorClosed,
        TileKind.ShutterHalf => CellType.DoorClosed,
        TileKind.Border => CellType.Border,
        _ => null,
    };

    /// <summary><c>true</c> interieur, <c>false</c> exterieur, <c>null</c> : ne change pas le marqueur.</summary>
    public static bool? ToIndoorFlag(TileKind kind) => kind switch
    {
        TileKind.FloorIndoor => true,
        TileKind.FloorPharmacy => true,
        TileKind.ShutterHalf => true,
        TileKind.FloorOutdoor => false,
        TileKind.FloorRoad => false,
        TileKind.FloorSidewalk => false,
        TileKind.FloorGrass => false,
        TileKind.Border => false,
        _ => null,
    };

    /// <summary>
    /// Les fenetres a barreaux ne se brisent pas. C'est la seule facon dont une tuile retire une
    /// action au planificateur, et elle est declaree plutot que devinee.
    /// </summary>
    public static bool ForbidsBreakWindow(TileKind kind) => kind == TileKind.WindowBarred;

    /// <summary>Un rideau metallique s'ouvre, il ne se force pas.</summary>
    public static bool ForbidsForceDoor(TileKind kind) => kind == TileKind.ShutterHalf;
}
