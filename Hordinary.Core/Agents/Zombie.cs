using System.Numerics;
using Hordinary.AI.Navigation;
using Hordinary.AI.Zombies;
using Hordinary.World;

namespace Hordinary.Agents;

/// <summary>
/// Zombie (specification section 17). Comme un survivant, il ne connait du monde que sa
/// <see cref="AgentKnowledge"/> : il n'a aucun acces a la position reelle d'une cible.
///
/// Son corps, lui, est bien dans le monde — <see cref="Follower"/> le fait glisser d'une cellule a
/// l'autre — et se cogne aux vrais murs. Savoir et se deplacer sont deux choses distinctes : la
/// premiere passe par la memoire, la seconde par la geometrie.
/// </summary>
public sealed class Zombie : IPerceivingAgent
{
    public string Id { get; }

    public AgentKnowledge Knowledge { get; }

    /// <summary>Corps du zombie : position continue, orientation et pas courant.</summary>
    public PathFollower Follower { get; } = new();

    /// <summary>Hasard propre a ce zombie : errance et erreur de localisation des bruits.</summary>
    public Random Rng { get; }

    public GridPosition Position => Follower.CurrentCell;

    public Vector2 Facing => Follower.Facing;

    public int VisionRange { get; set; }

    public float FieldOfViewDegrees { get; set; }

    // --- Etat de la machine a etats -------------------------------------------------------

    public ZombieState State { get; set; } = ZombieState.Idle;

    /// <summary>Motif de la decision courante, pour le journal et l'affichage debug.</summary>
    public string Reason { get; set; } = "apparition";

    /// <summary>Cellule visee par la decision courante. Nulle quand le zombie n'a nulle part ou aller.</summary>
    public GridPosition? Destination { get; set; }

    /// <summary>
    /// But d'errance ou de fouille en cours. Distinct de <see cref="Destination"/> : une poursuite
    /// ecrase la destination, mais ne doit pas faire oublier ou le zombie flanait.
    /// </summary>
    public GridPosition? RoamTarget { get; set; }

    /// <summary>Identifiant de la cible suivie, si le zombie en a une.</summary>
    public string? TargetId { get; set; }

    /// <summary>Derniere position ou la cible a ete vue. Sert d'ancre de fouille quand elle disparait.</summary>
    public GridPosition? LastKnownTargetPosition { get; set; }

    /// <summary>Centre de la fouille en cours (specification section 18.3, etape 4).</summary>
    public GridPosition? SearchAnchor { get; set; }

    public double SearchUntil { get; set; }

    public double IdleUntil { get; set; }

    /// <summary>Date a laquelle une destination d'errance ou de fouille sera retiree au sort.</summary>
    public double RepickAt { get; set; }

    public double LastAttackAt { get; set; } = double.NegativeInfinity;

    // --- Combat (livrable 8) ----------------------------------------------------------------

    /// <summary>
    /// Points de vie. Un zombie n'a ni panique ni moral : le seul etat qu'un survivant peut lui
    /// infliger est celui-ci — et le recul d'une poussee, qui ne l'entame pas.
    /// </summary>
    public float Health { get; private set; }

    public float MaxHealth { get; }

    public bool Alive => Health > 0f;

    /// <summary>Encaisse un coup. Renvoie vrai s'il vient d'etre mis a terre.</summary>
    public bool TakeDamage(float amount)
    {
        if (amount <= 0f || !Alive) return false;

        Health = Math.Max(0f, Health - amount);
        return !Alive;
    }

    public Zombie(string id, AgentKnowledge knowledge, ZombieSettings? settings = null, int seed = 0)
    {
        var zombies = settings ?? ZombieSettings.Default;

        Id = id;
        Knowledge = knowledge ?? throw new ArgumentNullException(nameof(knowledge));
        Rng = new Random(seed);
        VisionRange = zombies.VisionRange;
        FieldOfViewDegrees = zombies.FieldOfViewDegrees;
        MaxHealth = zombies.MaxHealth;
        Health = zombies.MaxHealth;
    }

    public void PlaceAt(GridPosition cell) => Follower.Teleport(cell);

    /// <summary>Le zombie abandonne toute piste : plus de cible, plus de fouille.</summary>
    public void LoseTrack()
    {
        TargetId = null;
        LastKnownTargetPosition = null;
        SearchAnchor = null;
        SearchUntil = 0d;
    }

    public override string ToString() => $"{Id} {Position} {State} ({Reason})";
}
