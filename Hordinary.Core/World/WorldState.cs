using Hordinary.Agents;

namespace Hordinary.World;

/// <summary>Entite reelle du monde : sa position est la verite, pas une croyance.</summary>
public sealed record WorldEntity(string Id, EntityKind Kind, GridPosition Position);

/// <summary>
/// Objet reellement pose sur la carte. <c>ItemId</c> est un identifiant du catalogue
/// (<c>config/items.json</c>, <c>config/weapons.json</c>) : le monde ne connait pas plus les objets
/// que les agents, il sait seulement lequel se trouve la.
/// </summary>
public sealed record WorldItem(string Id, string ItemId, GridPosition Position);

/// <summary>
/// Verite complete du jeu (specification section 9.1) : la grille reelle, les entites et les objets.
///
/// Aucun systeme de decision ne doit lire cet objet. Seul <see cref="PerceptionSystem"/> y accede,
/// pour en deriver ce que chaque agent peut percevoir ; les IA raisonnent ensuite exclusivement sur
/// leur <see cref="AgentKnowledge"/>.
/// </summary>
public sealed class WorldState(GridMap grid, NoiseSettings? noise = null)
{
    private readonly Dictionary<string, WorldEntity> _entities = [];
    private readonly Dictionary<string, WorldItem> _items = [];

    public GridMap Grid { get; } = grid ?? throw new ArgumentNullException(nameof(grid));

    /// <summary>
    /// Evenements sonores en cours (specification section A : <c>WorldState.noises</c>). Comme le
    /// reste du monde, ils ne sont lisibles que par les systemes de perception.
    /// </summary>
    public NoiseSystem Noises { get; } = new(noise);

    /// <summary>Temps de jeu ecoule, en secondes. Horodate toutes les observations.</summary>
    public double Time { get; private set; }

    /// <summary>
    /// Les meubles poussables (livrable 11). Ils occupent une cellule <see cref="CellType.Obstacle"/>
    /// de la grille : la navigation et les lignes de vue n'ont donc rien de nouveau a apprendre, et
    /// pousser un rayonnage revient a deplacer un obstacle d'une case.
    /// </summary>
    public HeavyObjects Heavy { get; } = new();

    public IReadOnlyDictionary<string, WorldEntity> Entities => _entities;
    public IReadOnlyDictionary<string, WorldItem> Items => _items;

    public void Advance(double deltaSeconds)
    {
        if (deltaSeconds <= 0d) return;

        Time += deltaSeconds;
        Noises.Expire(Time);
    }

    public void SetEntity(string id, EntityKind kind, GridPosition position)
        => _entities[id] = new WorldEntity(id, kind, position);

    public void RemoveEntity(string id) => _entities.Remove(id);

    public WorldEntity? GetEntity(string id)
        => _entities.TryGetValue(id, out var entity) ? entity : null;

    public IEnumerable<WorldEntity> EntitiesAt(GridPosition position)
        => _entities.Values.Where(entity => entity.Position == position);

    /// <summary>
    /// Emis quand un objet parait ou disparait du sol. Le monde n'a pas d'affichage et n'en veut
    /// pas ; mais un objet pose que le decor ne montre pas, ou ramasse que le decor montre encore,
    /// fait mentir la carte au joueur — d'ou ces deux annonces, que la couche moteur ecoute pour
    /// tenir la couche « Objets » a jour.
    /// </summary>
    public event Action<WorldItem>? ItemPlaced;

    public event Action<WorldItem>? ItemTaken;

    public void SetItem(string id, string itemId, GridPosition position)
    {
        // Un objet deplace quitte d'abord sa case : sans cela le decor garderait les deux.
        if (_items.TryGetValue(id, out var previous)) ItemTaken?.Invoke(previous);

        var item = new WorldItem(id, itemId, position);
        _items[id] = item;
        ItemPlaced?.Invoke(item);
    }

    public void RemoveItem(string id)
    {
        if (!_items.Remove(id, out var removed)) return;

        ItemTaken?.Invoke(removed);
    }

    public IEnumerable<WorldItem> ItemsAt(GridPosition position)
        => _items.Values.Where(item => item.Position == position);
}
