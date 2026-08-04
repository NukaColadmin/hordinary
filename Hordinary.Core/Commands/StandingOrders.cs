using Hordinary.World;

namespace Hordinary.Commands;

/// <summary>
/// Les consignes permanentes que le joueur laisse a un survivant (specification, livrable 8 :
/// « interdire les actions bruyantes », « autoriser les actions bruyantes », « definir une zone
/// interdite »).
///
/// Elles se distinguent d'un <see cref="Agents.MissionOrder"/> sur deux points. Un ordre de mission
/// dit ou aller et entre en concurrence avec les autres objectifs ; une consigne, elle, ne propose
/// rien — elle retire des possibilites. Et elle ne s'efface pas quand l'objectif change : interdire
/// le bruit vaut jusqu'a ce que le joueur l'autorise de nouveau.
///
/// Le survivant ne « desobeit » donc jamais a une consigne : elle est appliquee en amont de la
/// decision, en amputant le domaine de planification et la carte de navigation. Un plan qui aurait
/// eu besoin d'une vitre brisee n'existe tout simplement pas, et GBFS en trouve un autre — ou
/// echoue, ce qui est une information que le mode debug affiche.
/// </summary>
public sealed class StandingOrders
{
    private readonly List<MapZone> _forbidden = [];

    /// <summary>
    /// Niveau de bruit qu'une action de plan a le droit d'emettre. Sans consigne, rien n'est
    /// interdit : le bareme de la section 18.1 monte a 10 (distraction).
    /// </summary>
    public float MaximumNoise { get; private set; } = float.PositiveInfinity;

    /// <summary>Vrai tant que le joueur n'a pas interdit les actions bruyantes.</summary>
    public bool NoiseAllowed => float.IsPositiveInfinity(MaximumNoise);

    /// <summary>Zones ou le joueur a interdit de mettre les pieds.</summary>
    public IReadOnlyList<MapZone> ForbiddenZones => _forbidden;

    /// <summary>
    /// Interdit les actions au-dela d'un seuil sonore. Le seuil par defaut laisse passer la marche
    /// et l'ouverture d'une porte, et coupe la course, la porte forcee, la vitre et la distraction :
    /// « pas de bruit » veut dire « n'attirez personne », pas « ne bougez plus ».
    /// </summary>
    public void ForbidNoise(float threshold = QuietThreshold) => MaximumNoise = Math.Max(0f, threshold);

    public void AllowNoise() => MaximumNoise = float.PositiveInfinity;

    /// <summary>Seuil retenu par « interdire les actions bruyantes » : marche 1, porte 2, course 3.</summary>
    public const float QuietThreshold = 2f;

    /// <summary>
    /// Ajoute une zone interdite. Deux appels sur la meme zone ne la comptent qu'une fois : le
    /// joueur clique, il ne tient pas de registre.
    /// </summary>
    public bool Forbid(MapZone zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        if (_forbidden.Any(known => ReferenceEquals(known, zone) || known.Name == zone.Name)) return false;

        _forbidden.Add(zone);
        return true;
    }

    public bool Allow(MapZone zone)
        => zone is not null && _forbidden.RemoveAll(known => known.Name == zone.Name) > 0;

    public void ClearForbiddenZones() => _forbidden.Clear();

    /// <summary>Vrai si la cellule tombe dans une zone interdite : A* n'a pas le droit d'y passer.</summary>
    public bool IsForbidden(GridPosition cell)
    {
        foreach (var zone in _forbidden)
        {
            if (zone.Contains(cell)) return true;
        }

        return false;
    }

    /// <summary>Vrai si une action de ce niveau sonore reste autorisee.</summary>
    public bool AllowsNoise(float level) => level <= MaximumNoise;

    public override string ToString()
    {
        string noise = NoiseAllowed ? "bruit autorise" : $"bruit limite a {MaximumNoise:0.#}";
        string zones = _forbidden.Count == 0
            ? "aucune zone interdite"
            : $"{_forbidden.Count} zone(s) interdite(s) : {string.Join(", ", _forbidden.Select(zone => zone.Name))}";

        return $"{noise}, {zones}";
    }
}
