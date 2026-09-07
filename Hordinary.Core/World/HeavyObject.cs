namespace Hordinary.World;

/// <summary>
/// Un meuble qu'on pousse : rayonnage, benne. Il bloque le passage et la vue, et se deplace au prix
/// d'un effort proportionnel a sa masse (specification, livrable 11).
///
/// Il n'existe pas de classe par meuble : <see cref="Kind"/> est une chaine venue de la
/// configuration, et c'est <see cref="Mass"/> qui decide de tout le reste. La regle des deux
/// survivants au-dela de trois n'est pas ecrite ici mais dans <see cref="HeavyObjects"/>, parce
/// qu'elle porte sur la scene, pas sur le meuble.
/// </summary>
public sealed class HeavyObject(string id, string kind, GridPosition position, float mass)
{
    public string Id { get; } = id;

    /// <summary>Categorie lisible : <c>shelf</c>, <c>dumpster</c>. Elle nomme la tuile, rien de plus.</summary>
    public string Kind { get; } = kind;

    /// <summary>Coin haut-gauche de l'empreinte. Pour un meuble d'une case, c'est le meuble.</summary>
    public GridPosition Position { get; private set; } = position;

    /// <summary>
    /// Largeur et hauteur, en cases. Un rayonnage tient sur une case ; une epave de voiture en
    /// occupe quatre, et c'est bien la seule facon d'en dessiner une a l'echelle du reste du decor.
    ///
    /// L'empreinte est une propriete du meuble et non de son image : la navigation ferme toutes ces
    /// cases, la vue s'y arrete, et pousser l'engin les deplace ensemble. Une voiture qu'on
    /// contournerait par le capot ne serait qu'un grand dessin.
    /// </summary>
    public int Width { get; init; } = 1;

    public int Height { get; init; } = 1;

    /// <summary>Les cases que le meuble occupe reellement, de gauche a droite puis de haut en bas.</summary>
    public IEnumerable<GridPosition> Cells
    {
        get
        {
            for (int dy = 0; dy < Math.Max(1, Height); dy++)
            {
                for (int dx = 0; dx < Math.Max(1, Width); dx++)
                {
                    yield return new GridPosition(Position.X + dx, Position.Y + dy);
                }
            }
        }
    }

    /// <summary>Vrai si le meuble couvre cette case.</summary>
    public bool Covers(GridPosition cell)
        => cell.X >= Position.X && cell.X < Position.X + Math.Max(1, Width)
        && cell.Y >= Position.Y && cell.Y < Position.Y + Math.Max(1, Height);

    /// <summary>Case la plus proche de <paramref name="from"/> parmi celles que le meuble occupe.</summary>
    public GridPosition ClosestCellTo(GridPosition from)
        => new(
            Math.Clamp(from.X, Position.X, Position.X + Math.Max(1, Width) - 1),
            Math.Clamp(from.Y, Position.Y, Position.Y + Math.Max(1, Height) - 1));

    /// <summary>De 1 a 5. Au-dela de <see cref="HeavyObjects.SoloMassLimit"/>, il faut etre deux.</summary>
    public float Mass { get; } = mass;

    public bool BlocksMovement { get; init; } = true;

    public bool BlocksSight { get; init; } = true;

    /// <summary>Bruit d'une case poussee, avant multiplication par la masse.</summary>
    public float PushNoise { get; init; } = 2f;

    /// <summary>Secondes par case poussee, avant multiplication par la masse.</summary>
    public float PushDurationPerCell { get; init; } = 0.6f;

    /// <summary>Ce qu'une case de poussee coute reellement : l'effort suit la masse.</summary>
    public float NoiseOfPush => PushNoise * Mass;

    public float DurationOfPush => PushDurationPerCell * Mass;

    internal void MoveTo(GridPosition destination) => Position = destination;

    public override string ToString()
    {
        string footprint = Width > 1 || Height > 1 ? $", {Width}x{Height}" : string.Empty;
        return $"{Kind} « {Id} » en {Position} (masse {Mass:0.#}{footprint})";
    }
}

/// <summary>
/// Les meubles poussables d'une carte, et la regle qui dit si une poussee est possible.
///
/// La regle vit ici plutot que sur l'objet parce qu'elle porte sur la scene entiere : la case d'ou
/// l'on pousse, celle ou l'on pousse, ce que le pousseur croit savoir du terrain, et le nombre de
/// bras disponibles. Un meuble ne sait rien de tout cela.
/// </summary>
public sealed class HeavyObjects
{
    /// <summary>
    /// Masse au-dela de laquelle un survivant seul n'y arrive pas. C'est ce seuil qui donne au
    /// regroupement une raison mecanique plutot qu'une consigne : a deux, la benne bouge.
    /// </summary>
    public const float SoloMassLimit = 3f;

    private readonly Dictionary<string, HeavyObject> _objects = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<HeavyObject> All => _objects.Values;

    public HeavyObject? Find(string? id)
        => id is not null && _objects.TryGetValue(id, out var found) ? found : null;

    /// <summary>Le meuble qui occupe cette case, empreinte comprise. C'est l'ancre pour un 1 x 1.</summary>
    public HeavyObject? At(GridPosition position)
        => _objects.Values.FirstOrDefault(heavy => heavy.Covers(position));

    public HeavyObject Add(HeavyObject heavy)
    {
        _objects[heavy.Id] = heavy;
        return heavy;
    }

    public bool Remove(string id) => _objects.Remove(id);

    public bool BlocksSightAt(GridPosition position) => At(position) is { BlocksSight: true };

    public bool BlocksMovementAt(GridPosition position) => At(position) is { BlocksMovement: true };

    /// <summary>
    /// Ce qui empeche une poussee, ou <c>null</c> si elle est possible. Renvoyer la raison plutot
    /// qu'un booleen a un interet precis : c'est elle qui part au journal quand l'action echoue, et
    /// une replanification qui sait pourquoi vaut mieux qu'une qui reessaie.
    /// </summary>
    /// <param name="heavy">Le meuble vise.</param>
    /// <param name="pusher">La case ou se tient celui qui pousse.</param>
    /// <param name="grid">Le terrain reel, pour savoir si la destination existe et est libre.</param>
    /// <param name="helpers">
    /// Les cases occupees par les allies. Au-dela de <see cref="SoloMassLimit"/>, il en faut un
    /// adjacent au meuble : c'est la seule regle du jeu qui exige d'etre a deux au meme endroit.
    /// </param>
    public string? WhyCannotPush(
        HeavyObject heavy,
        GridPosition pusher,
        GridMap grid,
        IEnumerable<GridPosition>? helpers = null)
    {
        ArgumentNullException.ThrowIfNull(heavy);
        ArgumentNullException.ThrowIfNull(grid);

        // On pousse depuis la case opposee a la direction voulue, et seulement en ligne droite : une
        // poussee en diagonale demanderait de decider laquelle des deux composantes l'emporte.
        //
        // Sur un meuble a plusieurs cases — une epave de voiture — la direction se lit depuis le
        // bord contre lequel on s'appuie, et non depuis l'ancre : pousser un capot ou un coffre,
        // c'est le meme geste.
        var contact = heavy.ClosestCellTo(pusher);

        int dx = contact.X - pusher.X;
        int dy = contact.Y - pusher.Y;

        if (Math.Abs(dx) + Math.Abs(dy) != 1)
        {
            return $"il faut se tenir sur une case adjacente a {heavy.Id}, pas en {pusher}";
        }

        var step = new GridPosition(dx, dy);

        // Toute l'empreinte se deplace : il suffit qu'une seule case d'arrivee soit prise pour que
        // rien ne bouge. Les cases que le meuble se libere a lui-meme ne comptent pas.
        foreach (var cell in heavy.Cells)
        {
            var destination = cell + step;
            if (heavy.Covers(destination)) continue;

            if (!grid.Contains(destination)) return $"{heavy.Id} bute sur le bord de la carte";
            if (!grid.IsWalkable(destination)) return $"la case {destination} n'est pas libre";
            if (At(destination) is { } other && !ReferenceEquals(other, heavy))
                return $"un autre meuble occupe deja {destination}";
        }

        if (heavy.Mass <= SoloMassLimit) return null;

        bool helped = (helpers ?? []).Any(ally =>
            ally != pusher && heavy.Cells.Any(cell => ally.ChebyshevDistanceTo(cell) <= 1));

        return helped
            ? null
            : $"{heavy.Id} pese {heavy.Mass:0.#} : il faut etre deux, et personne n'est a portee";
    }

    /// <summary>
    /// Pousse le meuble d'une case et renvoie sa nouvelle position, ou <c>null</c> si la regle s'y
    /// oppose. La verification et le mouvement sont volontairement le meme appel : rien ne peut
    /// bouger sans etre passe par la regle.
    /// </summary>
    public GridPosition? Push(
        HeavyObject heavy,
        GridPosition pusher,
        GridMap grid,
        IEnumerable<GridPosition>? helpers = null)
    {
        if (WhyCannotPush(heavy, pusher, grid, helpers) is not null) return null;

        var contact = heavy.ClosestCellTo(pusher);
        var destination = new GridPosition(
            heavy.Position.X + (contact.X - pusher.X),
            heavy.Position.Y + (contact.Y - pusher.Y));

        heavy.MoveTo(destination);
        return destination;
    }
}
