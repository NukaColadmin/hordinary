namespace Hordinary.World;

/// <summary>Types de cellules definis par la specification (section 7.2).</summary>
public enum CellType
{
    /// <summary>Jamais observee. Traversable, mais jamais consideree comme surement libre.</summary>
    Unknown = 0,
    Floor,
    Wall,
    DoorOpen,
    DoorClosed,
    DoorLocked,
    Window,
    Obstacle,
    Pharmacy,
    Extraction,
}

public static class CellTypeExtensions
{
    /// <summary>
    /// Vrai si la cellule interdit physiquement le passage.
    /// Une porte fermee n'est PAS bloquante : elle est franchissable au prix d'une ouverture
    /// (voir <see cref="AI.Navigation.NavigationSettings.ClosedDoorCost"/>).
    /// Une cellule inconnue n'est pas bloquante non plus, elle est seulement couteuse.
    /// </summary>
    public static bool BlocksMovement(this CellType cell) => cell switch
    {
        CellType.Wall => true,
        CellType.Window => true,
        CellType.Obstacle => true,
        CellType.DoorLocked => true,
        _ => false,
    };

    /// <summary>Vrai si la cellule bloque la ligne de vue (utilise a partir du livrable 2).</summary>
    public static bool BlocksSight(this CellType cell) => cell switch
    {
        CellType.Wall => true,
        CellType.Obstacle => true,
        CellType.DoorClosed => true,
        CellType.DoorLocked => true,
        _ => false,
    };

    /// <summary>Vrai si la cellule est connue ET surement praticable. <c>Unknown</c> renvoie faux.</summary>
    public static bool IsKnownWalkable(this CellType cell) => cell switch
    {
        CellType.Floor => true,
        CellType.DoorOpen => true,
        CellType.DoorClosed => true,
        CellType.Pharmacy => true,
        CellType.Extraction => true,
        _ => false,
    };

    public static bool IsDoor(this CellType cell)
        => cell is CellType.DoorOpen or CellType.DoorClosed or CellType.DoorLocked;
}
