using Hordinary.Agents;

namespace Hordinary.World;

/// <summary>Entite reelle du monde : sa position est la verite, pas une croyance.</summary>
public sealed record WorldEntity(string Id, EntityKind Kind, GridPosition Position);

/// <summary>Objet reellement pose sur la carte.</summary>
public sealed record WorldItem(string Id, ItemKind Kind, GridPosition Position);

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

    public void SetItem(string id, ItemKind kind, GridPosition position)
        => _items[id] = new WorldItem(id, kind, position);

    public void RemoveItem(string id) => _items.Remove(id);

    public IEnumerable<WorldItem> ItemsAt(GridPosition position)
        => _items.Values.Where(item => item.Position == position);
}
