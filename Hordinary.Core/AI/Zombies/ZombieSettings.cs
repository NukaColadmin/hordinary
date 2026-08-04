namespace Hordinary.AI.Zombies;

/// <summary>
/// Parametres des zombies (specification sections 17 et A), charges depuis
/// <c>config/game_config.json</c>, section <c>zombies</c>.
/// </summary>
public sealed class ZombieSettings
{
    // --- Population -----------------------------------------------------------------------

    /// <summary>Nombre de zombies sur la carte. La specification en demande 20 a 30.</summary>
    public int Count { get; init; } = 24;

    /// <summary>Graine de placement et de decision : une partie rejouee se deroule a l'identique.</summary>
    public int Seed { get; init; } = 20260728;

    /// <summary>Distance minimale, en cellules, entre un zombie et la zone de depart des survivants.</summary>
    public int MinimumSpawnDistance { get; init; } = 9;

    // --- Perception -----------------------------------------------------------------------

    /// <summary>Un zombie voit moins loin qu'un survivant.</summary>
    public int VisionRange { get; init; } = 6;

    public float FieldOfViewDegrees { get; init; } = 140f;

    /// <summary>Periode entre deux decisions, en secondes (2 a 5 fois par seconde, cf. section A).</summary>
    public float UpdateInterval { get; init; } = 0.3f;

    /// <summary>
    /// Anciennete maximale d'une observation pour que la cible compte encore comme « visible ».
    /// Au-dela, le zombie ne poursuit plus : il cherche.
    /// </summary>
    public float SightMemory { get; init; } = 0.5f;

    // --- Deplacement ----------------------------------------------------------------------

    public float WanderSpeed { get; init; } = 1.1f;

    /// <summary>Vitesse en alerte : il a entendu ou perdu quelque chose, il ne traine plus.</summary>
    public float AlertSpeed { get; init; } = 1.8f;

    public float ChaseSpeed { get; init; } = 2.4f;

    /// <summary>Rayon d'errance autour de la position courante, en cellules.</summary>
    public int WanderRadius { get; init; } = 6;

    /// <summary>Probabilite, a chaque fin d'errance, de rester immobile un moment.</summary>
    public float IdleChance { get; init; } = 0.3f;

    public float IdleDuration { get; init; } = 2f;

    /// <summary>Delai au bout duquel un but d'errance inatteignable est abandonne.</summary>
    public float RoamRepickInterval { get; init; } = 5f;

    // --- Fouille --------------------------------------------------------------------------

    /// <summary>Duree de la fouille autour de la derniere position connue (section 18.3, etape 4).</summary>
    public float SearchDuration { get; init; } = 8f;

    public int SearchRadius { get; init; } = 3;

    // --- Combat ---------------------------------------------------------------------------

    public float AttackInterval { get; init; } = 1.2f;

    /// <summary>Degats infliges par coup, sur les 100 points de sante d'un survivant (section 19.2).</summary>
    public float AttackDamage { get; init; } = 8f;

    /// <summary>
    /// Points de vie d'un zombie (livrable 8, « combats simples pour le MVP »). Volontairement bas
    /// devant les 100 points d'un survivant, mais superieur aux degats d'une attaque : un survivant
    /// ne se debarrasse pas d'un zombie d'un seul coup, et deux zombies au contact restent une
    /// raison de fuir plutot que de se battre.
    /// </summary>
    public float MaxHealth { get; init; } = 30f;

    // --- Deplacement collectif (section 17.4) ---------------------------------------------

    /// <summary>Attraction vers la destination.</summary>
    public float AttractionWeight { get; init; } = 1f;

    /// <summary>Separation : penalite par voisin trop proche, pour eviter les superpositions.</summary>
    public float SeparationWeight { get; init; } = 1.5f;

    public int SeparationRadius { get; init; } = 1;

    /// <summary>Cohesion : legere attirance vers le centre des zombies proches.</summary>
    public float CohesionWeight { get; init; } = 0.3f;

    public int CohesionRadius { get; init; } = 5;

    public static ZombieSettings Default { get; } = new();

    /// <summary>Un zombie lance a la poursuite est plus rapide qu'un zombie qui traine.</summary>
    public float SpeedFor(ZombieState state) => state switch
    {
        ZombieState.Chase or ZombieState.Attack => ChaseSpeed,
        ZombieState.InvestigateNoise or ZombieState.SearchLastKnownPosition => AlertSpeed,
        _ => WanderSpeed,
    };
}
