namespace Hordinary.AI.Navigation;

/// <summary>Niveau de danger attache a une cellule, alimente par la perception (livrables 2 et 3).</summary>
public enum DangerLevel
{
    None = 0,

    /// <summary>Danger suspecte : bruit entendu, zone signalee par un allie...</summary>
    Suspected = 1,

    /// <summary>Zombie observe a proximite immediate.</summary>
    ZombieNearby = 2,
}
