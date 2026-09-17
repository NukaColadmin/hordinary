using System.Numerics;
using Hordinary.Agents;
using Hordinary.Config;
using Hordinary.World;

namespace Hordinary.AI.Utility;

/// <summary>
/// Menace telle que l'agent se la represente : l'endroit ou il la suppose maintenant, le credit
/// qu'il lui accorde, et le cap qu'il lui prete — de quoi distinguer une menace qui passe d'une
/// menace qui vient.
/// </summary>
public readonly record struct Threat(GridPosition Position, float Weight, Vector2 Heading = default)
{
    public bool IsMoving => Heading.LengthSquared() > 0f;
}

/// <summary>
/// Instantane de ce qu'un survivant sait de sa situation, au moment ou il doit choisir un objectif.
/// C'est la couche « capteurs » de la Utility AI : elle transforme des croyances en grandeurs
/// normalisees entre 0 et 1, que les considerations combinent ensuite.
///
/// Comme <c>ZombieStateMachine</c>, cette classe ne connait pas <see cref="WorldState"/> :
/// tout vient de <see cref="AgentKnowledge"/>. Un survivant ne peut donc pas fuir un zombie qu'il
/// n'a ni vu ni entendu, ni courir vers une pharmacie dont il ignore l'existence.
/// </summary>
public sealed class UtilityContext
{
    private readonly List<Threat> _threats = [];
    private readonly List<EntityBelief> _allies = [];

    private readonly FrontierExplorer _explorer;

    /// <summary>
    /// Test d'atteignabilite fourni par l'assemblage, ou <c>null</c> quand personne ne l'a branche.
    /// La Utility AI ne connait pas la navigation : c'est <c>SurvivorAgent</c> qui lui prete la
    /// grille du survivant, et un test qui ne le fait pas retombe sur l'optimisme d'avant.
    /// </summary>
    private readonly Func<GridPosition, bool>? _reachable;

    private GridPosition? _cover;
    private GridPosition? _refuge;
    private Frontier? _frontier;
    private bool _coverSearched;
    private bool _frontierSearched;
    private bool _refugeSearched;

    /// <summary>Cibles que <see cref="Locate"/> sait resoudre. Sert aussi a valider la configuration.</summary>
    public static IReadOnlySet<string> TargetNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "self", "danger", "away_from_danger", "cover", "frontier", "mission",
        "ally", "group", "objective", "objective_zone", "pharmacy", "extraction", "start",
    };

    public Survivor Self { get; }
    public double Now { get; }
    public UtilityAiSettings Settings { get; }

    /// <summary>Menaces credibles : zombies memorises, plus les bruits recents, moins credites.</summary>
    public IReadOnlyList<Threat> Threats => _threats;

    /// <summary>Allies dont le survivant croit connaitre la position.</summary>
    public IReadOnlyList<EntityBelief> Allies => _allies;

    /// <summary>
    /// Les meubles poussables que le survivant a vus et qu'il croit encore en place (livrable 11).
    /// C'est sa memoire qui repond, pas le monde : un rayonnage qu'un allie a deplace hors de sa vue
    /// est ici a l'endroit ou il l'a laisse.
    /// </summary>
    public IEnumerable<HeavyBelief> KnownHeavyObjects
        => Self.Knowledge.KnownHeavy.Values
            .Where(heavy => heavy.Confidence > Self.Knowledge.Settings.MinimumConfidence);

    /// <summary>Danger ressenti la ou se trouve le survivant, entre 0 et 1.</summary>
    public float PerceivedDanger { get; }

    /// <summary>Danger pesant sur l'allie le plus menace, entre 0 et 1.</summary>
    public float AllyDanger { get; }

    /// <summary>
    /// Sante de l'allie le plus mal en point, entre 0 et 1, telle qu'on la lui connait. Vaut 1 quand
    /// personne n'a rien dit : la sante d'un allie ne s'observe pas, elle se raconte (livrable 7).
    /// Un survivant qui n'a parle a personne ne part donc pas soigner un blesse qu'il devine.
    /// </summary>
    public float AllyHealth { get; } = 1f;

    /// <summary>Panique de l'allie le plus affole, entre 0 et 1. Rapportee elle aussi, jamais devinee.</summary>
    public float AllyPanic { get; }

    /// <summary>
    /// Pression ressentie : ce qu'elle voit maintenant, ou ce qu'elle vient de vivre — le plus fort
    /// des deux. Un zombie sorti du champ de vision cesse d'etre un danger percu, mais pas une
    /// raison de courir : sans cette memoire, les objectifs oscilleraient au rythme des coups d'oeil.
    /// </summary>
    public float ThreatPressure => Math.Max(PerceivedDanger, Self.Panic);

    /// <summary>
    /// A quel point quelque chose la suit : une menace credible, proche, dont le cap pointe vers
    /// elle. Nulle si les menaces connues sont immobiles ou s'eloignent. C'est ce qui distingue
    /// « il y a un zombie dans la rue » de « il me court apres ».
    /// </summary>
    public float PursuitPressure { get; }

    /// <summary>Intensite, ramenee a <see cref="UtilityAiSettings.LoudNoiseIntensity"/>, du dernier bruit credible.</summary>
    public float LoudestNoise { get; }

    /// <summary>Vrai si le survivant a repere quelque part ce que la mission lui demande.</summary>
    public bool KnowsObjective { get; }

    /// <summary>
    /// Vrai lorsqu'au moins un exemplaire connu de l'objet de mission est relie a la position de
    /// l'agent par sa carte mentale. Voir un objet derriere une zone coupee ne suffit pas a en faire
    /// une destination : sinon <c>PickUpObjective</c> reconstruit sans fin le meme trajet qu'A* vient
    /// de refuser. La valeur redevient vraie naturellement des qu'une ouverture relie les deux
    /// composantes.
    /// </summary>
    public bool CanReachObjective => NearestObjective() is not null;

    /// <summary>
    /// Nom symbolique du lieu que la mission designe comme objectif. Il vaut pour la Utility AI, pour
    /// le planificateur et pour la memoire des fouilles : c'est parce que les trois emploient le meme
    /// mot qu'une fouille infructueuse peut fermer un objectif au lieu de le laisser se reproposer.
    /// </summary>
    public const string ObjectiveZoneTarget = "objective_zone";

    /// <summary>
    /// Vrai quand le survivant a deja fouille le lieu de la mission et n'y a rien trouve.
    ///
    /// C'est ce qui empeche d'aller chercher indefiniment un objet qui n'existe plus — quelqu'un l'a
    /// pris avant, ou il n'y en a jamais eu. Sans cette memoire, l'objectif de recherche se rescore
    /// haut a chaque decision et le survivant repart vers une caisse imaginaire.
    /// </summary>
    public bool SearchedObjectiveZoneInVain =>
        Self.Knowledge.GetSearch(ObjectiveZoneTarget) is { Found: false };

    /// <summary>
    /// Menace que la carte de chaleur retient pour la case ou se tient le survivant. C'est un
    /// souvenir, pas une observation : il survit a la disparition du zombie, et s'efface tout seul.
    /// </summary>
    public float RememberedDanger { get; }

    public UtilityContext(
        Survivor self,
        double now,
        UtilityAiSettings? settings = null,
        ExplorationSettings? exploration = null,
        Func<GridPosition, bool>? reachable = null)
    {
        Self = self ?? throw new ArgumentNullException(nameof(self));
        Now = now;
        Settings = settings ?? UtilityAiSettings.Default;
        _reachable = reachable;
        _explorer = new FrontierExplorer(exploration);

        var knowledge = self.Knowledge;
        float minimumConfidence = knowledge.Settings.MinimumConfidence;

        // Une croyance ne vaut pas pour l'endroit ou l'entite etait, mais pour celui ou elle doit
        // etre maintenant : c'est la position supposee qui entre dans la decision.
        foreach (var belief in knowledge.BelievedEntities(now))
        {
            float confidence = knowledge.ConfidenceIn(belief, now);
            var supposed = knowledge.PredictedPositionOf(belief, now);

            if (belief.Kind == EntityKind.Zombie) _threats.Add(new Threat(supposed, confidence, belief.Heading));
            else if (belief.Id != self.Id) _allies.Add(belief with { Value = supposed });
        }

        foreach (var noise in knowledge.KnownNoises.Values)
        {
            float confidence = knowledge.ConfidenceIn(noise, now);
            if (confidence <= minimumConfidence) continue;

            float loudness = Math.Clamp(noise.PerceivedIntensity / Settings.LoudNoiseIntensity, 0f, 1f);
            LoudestNoise = Math.Max(LoudestNoise, loudness);

            // Un bruit ne trahit qu'un endroit approximatif : il pese moins qu'un zombie sous les yeux.
            _threats.Add(new Threat(noise.Value, confidence * loudness * Settings.NoiseDangerWeight));
        }

        KnowsObjective = knowledge.KnownItems.Values
            .Any(item => item.ItemId.Equals(self.ObjectiveItem, StringComparison.OrdinalIgnoreCase) && item.Confidence > minimumConfidence);

        PerceivedDanger = DangerAt(self.Position);
        AllyDanger = _allies.Count == 0 ? 0f : _allies.Max(ally => DangerAt(ally.Value));

        // « Ce qu'aucun allie ne m'a dit ne me concerne pas » : les allies muets sur leur etat sont
        // ecartes du calcul plutot que comptes en pleine forme, ce qui reviendrait au meme ici mais
        // ferait mentir la moyenne des qu'un seul se confie.
        var reported = _allies.Where(ally => ally.HealthRatio is not null).ToList();
        if (reported.Count > 0) AllyHealth = reported.Min(ally => ally.HealthRatio!.Value);

        var confided = _allies.Where(ally => ally.Panic is not null).ToList();
        if (confided.Count > 0) AllyPanic = confided.Max(ally => ally.Panic!.Value);

        PursuitPressure = ComputePursuitPressure();
        RememberedDanger = knowledge.Danger.ValueAt(self.Position, now);
    }

    /// <summary>
    /// La plus inquietante des menaces en mouvement : proche, credible, et venant droit sur elle.
    /// Un bruit n'y contribue jamais — il n'a pas de cap.
    /// </summary>
    private float ComputePursuitPressure()
    {
        float worst = 0f;

        foreach (var threat in _threats)
        {
            if (!threat.IsMoving) continue;

            float proximity = 1f - Distance(Self.Position, threat.Position) / Settings.DangerRadius;
            if (proximity <= 0f) continue;

            // Au contact, la question du cap ne se pose plus.
            var toSelf = new Vector2(Self.Position.X - threat.Position.X, Self.Position.Y - threat.Position.Y);
            float alignment = toSelf.LengthSquared() <= 0f
                ? 1f
                : Vector2.Dot(Vector2.Normalize(threat.Heading), Vector2.Normalize(toSelf));

            // Un cap qui s'ecarte n'est pas une poursuite : elle n'en tire aucune inquietude.
            if (alignment <= 0f) continue;

            worst = Math.Max(worst, alignment * proximity * threat.Weight);
        }

        return Math.Clamp(worst, 0f, 1f);
    }

    // --- Danger -----------------------------------------------------------------------------

    /// <summary>
    /// Danger ressenti sur une cellule : chaque menace y contribue d'autant plus qu'elle est proche
    /// et credible. Les contributions s'accumulent en probabilite (<c>1 - produit des complements</c>)
    /// plutot qu'en maximum : trois zombies autour de soi inquietent plus qu'un seul (section 19.3).
    /// </summary>
    public float DangerAt(GridPosition cell)
    {
        float safe = 1f;

        foreach (var threat in _threats)
        {
            float proximity = 1f - Distance(cell, threat.Position) / Settings.DangerRadius;
            if (proximity <= 0f) continue;

            safe *= 1f - Math.Clamp(threat.Weight * proximity, 0f, 1f);
        }

        return Math.Clamp(1f - safe, 0f, 1f);
    }

    /// <summary>Distance a la menace credible la plus proche, ou <see cref="float.PositiveInfinity"/>.</summary>
    public float DistanceToNearestThreat(GridPosition cell)
    {
        float best = float.PositiveInfinity;
        foreach (var threat in _threats) best = Math.Min(best, Distance(cell, threat.Position));
        return best;
    }

    // --- Distances --------------------------------------------------------------------------

    /// <summary>
    /// Distance a une cible, ramenee entre 0 (sur place) et 1 (a <see cref="UtilityAiSettings.DistanceScale"/>
    /// cellules ou plus). Une cible inconnue vaut 1 : la consideration <c>inverse</c> qui la lit
    /// tombe alors a 0 et annule l'objectif, ce qui est exactement le comportement voulu.
    /// </summary>
    public float NormalizedDistanceTo(string target)
    {
        if (Locate(target) is not { } cell) return 1f;
        return Math.Clamp(Distance(Self.Position, cell) / Settings.DistanceScale, 0f, 1f);
    }

    /// <summary>
    /// Traduit un nom de cible en cellule, uniquement a partir de la memoire du survivant.
    /// C'est le vocabulaire partage par <c>distance_to(...)</c> et par <see cref="UtilityGoal.Target"/>.
    /// </summary>
    public GridPosition? Locate(string? target) => target?.ToLowerInvariant() switch
    {
        null or "" => null,
        "self" => Self.Position,
        "mission" => Self.Mission?.Destination,
        "danger" => NearestThreat(),
        "away_from_danger" => Refuge(),
        "cover" => Cover(),
        "frontier" => BestFrontier?.Cell,
        "ally" => NearestAlly()?.Value,
        "group" => GroupCentre(),
        "objective" => NearestObjective(),
        "objective_zone" => ObjectiveZoneCell(),
        "pharmacy" => LocationOf(ZoneKind.Pharmacy),
        "extraction" => LocationOf(ZoneKind.Extraction),
        "start" => LocationOf(ZoneKind.Start),
        _ => throw new InvalidOperationException(
            $"Cible inconnue « {target} ». Cibles connues : {string.Join(", ", TargetNames.Order())}."),
    };

    public EntityBelief? NearestAlly()
        => _allies.OrderBy(ally => Distance(Self.Position, ally.Value)).FirstOrDefault();

    private GridPosition? NearestThreat()
    {
        if (_threats.Count == 0) return null;
        return _threats.OrderBy(threat => Distance(Self.Position, threat.Position)).First().Position;
    }

    private GridPosition? GroupCentre()
    {
        if (_allies.Count == 0) return null;

        int x = 0, y = 0;
        foreach (var ally in _allies)
        {
            x += ally.Value.X;
            y += ally.Value.Y;
        }

        return new GridPosition(x / _allies.Count, y / _allies.Count);
    }

    private GridPosition? NearestObjective()
    {
        float minimumConfidence = Self.Knowledge.Settings.MinimumConfidence;

        return Self.Knowledge.KnownItems.Values
            .Where(item => item.ItemId.Equals(Self.ObjectiveItem, StringComparison.OrdinalIgnoreCase) && item.Confidence > minimumConfidence)
            .Where(item => CanReach(item.Value))
            .OrderBy(item => Distance(Self.Position, item.Value))
            .Select(item => (GridPosition?)item.Value)
            .FirstOrDefault();
    }

    private GridPosition? LocationOf(ZoneKind kind) => Self.Knowledge.GetLocation(kind)?.Value;

    /// <summary>
    /// Le lieu de la mission, cherche d'abord par son nom. La categorie ne sert que de repli : deux
    /// batiments partagent la meme, et envoyer fouiller « un batiment » ne veut rien dire.
    /// </summary>
    private GridPosition? ObjectiveZoneCell()
    {
        if (Self.ObjectiveZone is not { } site) return null;

        return (Self.Knowledge.GetLocation(site.Name) ?? Self.Knowledge.GetLocation(site.Kind))?.Value;
    }

    // --- Cellules choisies dans la carte mentale ---------------------------------------------

    /// <summary>Cellule connue, praticable et a l'interieur la plus proche : de quoi se mettre a l'abri.</summary>
    private GridPosition? Cover()
    {
        if (_coverSearched) return _cover;
        _coverSearched = true;

        _cover = NearestKnownCell(belief => belief.Indoor && belief.Value.IsKnownWalkable());
        return _cover;
    }

    /// <summary>
    /// Ou aller voir : la frontiere la mieux notee par <see cref="FrontierExplorer"/> (livrable 7),
    /// gain d'information et repartition entre allies compris. Le calcul est fait une seule fois par
    /// contexte — <c>Locate("frontier")</c> et le planificateur doivent parler de la meme case.
    /// </summary>
    public Frontier? BestFrontier
    {
        get
        {
            if (_frontierSearched) return _frontier;

            _frontierSearched = true;
            return _frontier = _explorer.Choose(this);
        }
    }

    /// <summary>Les frontieres candidates et le detail de leur note, pour le mode debug.</summary>
    public IReadOnlyList<Frontier> RankFrontiers() => _explorer.Rank(this);

    /// <summary>
    /// Vrai si un itineraire mene la ou l'on veut aller. Sans test branche, tout est repute
    /// atteignable — c'est le comportement d'avant, conserve pour les tests qui construisent un
    /// contexte a la main.
    /// </summary>
    public bool CanReach(GridPosition cell) => _reachable is null || _reachable(cell);

    /// <summary>
    /// Ou fuir : parmi les cellules connues a portee, la plus sure, moins le prix du trajet. La
    /// surete est celle de <see cref="DangerAt"/> — donc toutes les menaces, pas seulement la plus
    /// proche : il ne sert a rien de s'eloigner d'un zombie pour tomber sur trois autres. Sans
    /// menace memorisee, il n'y a nulle part ou fuir.
    /// </summary>
    private GridPosition? Refuge()
    {
        if (_refugeSearched) return _refuge;
        _refugeSearched = true;

        if (_threats.Count == 0) return _refuge = null;

        float bestScore = float.NegativeInfinity;
        GridPosition? best = null;

        foreach (var belief in Self.Knowledge.KnownCells.Values)
        {
            if (!belief.Value.IsKnownWalkable()) continue;

            float travel = Distance(Self.Position, belief.Position);
            if (travel > Settings.DistanceScale) continue;

            // Les deux termes sont dans [0, 1] : la surete de la cellule contre le prix du trajet.
            float score = 1f - DangerAt(belief.Position)
                - Settings.FleeTravelPenalty * travel / Settings.DistanceScale;

            if (score <= bestScore) continue;

            bestScore = score;
            best = belief.Position;
        }

        return _refuge = best;
    }

    private GridPosition? NearestKnownCell(Func<CellBelief, bool> predicate)
    {
        float bestDistance = float.PositiveInfinity;
        GridPosition? best = null;

        foreach (var belief in Self.Knowledge.KnownCells.Values)
        {
            if (!predicate(belief)) continue;

            float distance = Distance(Self.Position, belief.Position);
            if (distance >= bestDistance) continue;

            bestDistance = distance;
            best = belief.Position;
        }

        return best;
    }

    private static float Distance(GridPosition a, GridPosition b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}
