using System.Numerics;
using Hordinary.World;

namespace Hordinary.Agents;

/// <summary>
/// Ce dont <see cref="PerceptionSystem"/> a besoin pour faire percevoir un agent : ou il est, ou il
/// regarde, jusqu'ou il voit, et ou ranger ce qu'il apprend.
///
/// Survivants et zombies l'implementent tous les deux : il n'existe qu'un seul chemin entre le monde
/// et une memoire, ce qui vaut la meme garantie aux deux camps — un zombie ne sait d'un survivant
/// que ce qu'il a vu ou entendu.
/// </summary>
public interface IPerceivingAgent
{
    string Id { get; }

    GridPosition Position { get; }

    /// <summary>Axe du champ de vision.</summary>
    Vector2 Facing { get; }

    int VisionRange { get; }

    float FieldOfViewDegrees { get; }

    AgentKnowledge Knowledge { get; }
}
