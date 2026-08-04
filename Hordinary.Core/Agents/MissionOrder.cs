using Hordinary.World;

namespace Hordinary.Agents;

/// <summary>
/// Ordre donne par le joueur (specification section 20.1). Le joueur ne pilote pas le survivant :
/// il pose un objectif general, que la Utility AI met en concurrence avec les autres.
///
/// <see cref="Priority"/> est l'entree <c>player_priority</c> des scores d'objectif : c'est le poids
/// de la consigne, pas une obligation. Un ordre reste donc negociable face a un danger immediat.
/// </summary>
public sealed record MissionOrder(string Label, GridPosition? Destination = null, float Priority = 0.7f)
{
    public override string ToString() => $"{Label} (priorite {Priority:0.00})";
}
