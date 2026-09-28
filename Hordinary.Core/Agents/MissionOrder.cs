using Hordinary.World;

namespace Hordinary.Agents;

/// <summary>
/// Ordre donne par le joueur (specification section 20.1). Le joueur ne pilote pas le survivant :
/// il pose un objectif general, que la Utility AI met en concurrence avec les autres.
///
/// <see cref="Priority"/> est l'entree <c>player_priority</c> des scores d'objectif : c'est le poids
/// de la consigne, pas une obligation. Un ordre reste donc negociable face a un danger immediat.
///
/// <para>
/// Un ordre peut aussi venir d'un camarade — c'est l'action <c>OrderAlly</c> du domaine. Il emprunte
/// alors le meme chemin, et deux champs de plus le distinguent : <see cref="Issuer"/>, qui dit de qui
/// il vient, et <see cref="Superseded"/>, l'ordre qu'il a mis de cote. Un ordre de camarade est
/// <b>temporaire</b> : rempli ou reconnu impossible, il s'efface et rend la place a celui d'avant
/// (<see cref="Survivor.Settle"/>). Un ordre du joueur, lui, reste pose jusqu'au suivant.
/// </para>
/// </summary>
public sealed record MissionOrder(string Label, GridPosition? Destination = null, float Priority = 0.7f)
{
    /// <summary>
    /// Identifiant du survivant qui a donne l'ordre, <c>null</c> pour un ordre du joueur ou le rappel
    /// du van. C'est l'entree <c>obeying</c> des scores : tant qu'il est renseigne, le survivant ne
    /// poursuit plus ses propres objectifs, seulement ses reflexes.
    /// </summary>
    public string? Issuer { get; init; }

    /// <summary>
    /// L'ordre qu'un ordre de camarade a mis de cote, et qui revient quand celui-ci est resolu. Sans
    /// lui, un camarade qui envoie Alice au van effacerait en passant ce que le joueur lui avait dit.
    /// </summary>
    public MissionOrder? Superseded { get; init; }

    /// <summary>Vrai pour un ordre venu d'un camarade, donc temporaire.</summary>
    public bool FromAlly => Issuer is not null;

    /// <summary>
    /// Vrai pour un ordre de camarade qui <b>se tient</b> une fois rempli : le rappel de tout le
    /// groupe au van (<c>OrderBand(extraction)</c>). Il ne s'efface alors que s'il est impossible.
    ///
    /// Un ordre simple s'efface a l'arrivee, et c'est voulu — on renvoie un camarade quelque part,
    /// pas en faction. Un rappel general au van dit autre chose : « rentrez tous », c'est-a-dire que
    /// la mission se conclut, et la conclusion demande tout le monde au van en meme temps. Mesure dans
    /// le moteur sur <c>rappeler_tout_le_monde</c> : Bob, arrive le premier, voyait l'ordre s'effacer,
    /// repartait fouiller deux secondes avant le retour d'Alice, et la prise ne se concluait qu'au
    /// rappel du temps imparti. Le rappel du van (<c>SurvivorBand.Recall</c>) tient deja de cette
    /// facon ; celui-ci l'imite, au poids d'un ordre de camarade.
    /// </summary>
    public bool Holds { get; init; }

    /// <summary>
    /// « L'ordre est rempli » : a une case de la destination. C'est la regle de l'entree
    /// <c>mission_reached</c>, et celle qui efface un ordre de camarade — les deux ne doivent pas
    /// diverger, sans quoi un survivant arrive attendrait un effacement que rien ne declenche.
    /// </summary>
    public bool ReachedFrom(GridPosition position)
        => Destination is { } destination && position.ChebyshevDistanceTo(destination) <= 1;

    public override string ToString() => $"{Label} (priorite {Priority:0.00})";
}
