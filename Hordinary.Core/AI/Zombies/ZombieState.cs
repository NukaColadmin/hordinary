namespace Hordinary.AI.Zombies;

/// <summary>Etats minimums exiges par la specification (section 17.1).</summary>
public enum ZombieState
{
    /// <summary>Immobile. Sert de respiration entre deux errances.</summary>
    Idle = 0,

    /// <summary>Deplacement sans but vers une case au hasard des environs.</summary>
    Wander,

    /// <summary>Se dirige vers la position <b>approximative</b> d'un bruit entendu.</summary>
    InvestigateNoise,

    /// <summary>Poursuit une cible actuellement visible.</summary>
    Chase,

    /// <summary>Au contact d'une cible visible.</summary>
    Attack,

    /// <summary>La cible a disparu : il fouille autour de l'endroit ou il l'a vue pour la derniere fois.</summary>
    SearchLastKnownPosition,
}
