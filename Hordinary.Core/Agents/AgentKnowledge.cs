using System.Numerics;
using Hordinary.Config;
using Hordinary.World;

namespace Hordinary.Agents;

/// <summary>
/// Carte mentale d'un agent (specification section 9.6) : uniquement ce qu'il sait ou croit savoir.
///
/// Cette classe ne connait volontairement pas <see cref="WorldState"/> : elle n'expose aucun moyen
/// d'aller lire la verite du monde. Seul <see cref="PerceptionSystem"/> ecrit dedans, a partir de ce
/// que l'agent a reellement percu. C'est ce qui garantit le critere du livrable 2 — un survivant ne
/// peut pas utiliser les informations d'une zone qu'il n'a jamais observee.
/// </summary>
public sealed class AgentKnowledge
{
    private readonly Dictionary<GridPosition, CellBelief> _cells = [];
    private readonly Dictionary<string, EntityBelief> _entities = [];
    private readonly Dictionary<string, ItemBelief> _items = [];
    private readonly Dictionary<string, LocationBelief> _locations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, NoiseBelief> _noises = [];
    private readonly Dictionary<string, SearchBelief> _searches = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IntentBelief> _intents = [];

    public string AgentId { get; }
    public KnowledgeSettings Settings { get; }

    /// <summary>
    /// Grille telle que l'agent se la represente : entierement <see cref="CellType.Unknown"/> au
    /// depart, remplie au fil des observations. C'est elle que parcourt A* depuis le livrable 2.
    /// </summary>
    public GridMap MentalMap { get; }

    public IReadOnlyDictionary<GridPosition, CellBelief> KnownCells => _cells;
    public IReadOnlyDictionary<string, EntityBelief> KnownEntities => _entities;
    public IReadOnlyDictionary<string, ItemBelief> KnownItems => _items;
    public IReadOnlyDictionary<string, LocationBelief> KnownLocations => _locations;

    /// <summary>Bruits entendus et pas encore oublies, indexes par identifiant d'evenement.</summary>
    public IReadOnlyDictionary<string, NoiseBelief> KnownNoises => _noises;

    /// <summary>Ce que les allies ont annonce vouloir faire, indexe par identifiant de survivant.</summary>
    public IReadOnlyDictionary<string, IntentBelief> KnownIntents => _intents;

    /// <summary>
    /// Seules les dimensions du terrain sont fournies, jamais son contenu : l'agent sait ou s'arrete
    /// le quartier, pas ce qu'il contient.
    /// </summary>
    public AgentKnowledge(
        string agentId,
        int width,
        int height,
        GridPosition origin = default,
        KnowledgeSettings? settings = null)
    {
        AgentId = agentId;
        Settings = settings ?? new KnowledgeSettings();
        MentalMap = new GridMap(width, height, origin);
        Danger = new DangerMemory(MentalMap, Settings);
    }

    /// <summary>
    /// Ou l'agent se souvient d'avoir eu peur. C'est une carte de chaleur qui s'efface : elle
    /// survit a la vue d'un zombie sans survivre a l'oubli (specification section 9.4, « niveau de
    /// danger d'une zone »).
    /// </summary>
    public DangerMemory Danger { get; }

    /// <summary>Nombre de cellules observees au moins une fois, sur le total du terrain.</summary>
    public float ExploredRatio => MentalMap.CellCount == 0 ? 0f : (float)_cells.Count / MentalMap.CellCount;

    // --- Cellules -------------------------------------------------------------------------

    /// <summary>
    /// Vrai si la cellule a deja ete observee. Repond a la distinction exigee par la specification
    /// (section 9.6) : « la piece est vide » n'est pas « la piece n'a pas ete observee ».
    /// </summary>
    public bool HasObserved(GridPosition position) => _cells.ContainsKey(position);

    public CellBelief? GetCell(GridPosition position)
        => _cells.TryGetValue(position, out var belief) ? belief : null;

    /// <summary>
    /// Enregistre une cellule. Renvoie <c>false</c> si l'information est plus ancienne que celle
    /// deja memorisee : une information recente remplace une information ancienne, jamais l'inverse.
    /// </summary>
    public bool ObserveCell(
        GridPosition position,
        CellType type,
        bool indoor,
        double time,
        KnowledgeSource source = KnowledgeSource.DirectObservation,
        float confidence = 1f)
    {
        if (!MentalMap.Contains(position)) return false;
        if (_cells.TryGetValue(position, out var known) && known.LastObservedAt > time) return false;

        _cells[position] = new CellBelief(position, type, indoor, Clamp(confidence), time, source);
        MentalMap.SetCell(position, type);
        MentalMap.SetIndoor(position, indoor);
        return true;
    }

    public float ConfidenceIn(CellBelief belief, double now)
        => belief.ConfidenceAt(now, DecayRateFor(belief));

    /// <summary>Taux de decroissance : nul pour une information statique (mur, sol, fenetre).</summary>
    public float DecayRateFor(CellBelief belief)
        => belief.IsDynamic ? Settings.DoorStateConfidenceDecay : 0f;

    // --- Entites --------------------------------------------------------------------------

    public EntityBelief? GetEntity(string id)
        => _entities.TryGetValue(id, out var belief) ? belief : null;

    /// <summary>
    /// Memorise une entite. <paramref name="heading"/> sert au partage entre allies, qui transmet
    /// un cap deja etabli ; laisse a <c>null</c>, le cap est deduit de l'observation precedente.
    ///
    /// <paramref name="health"/> et <paramref name="panic"/> ne s'observent pas : ils n'arrivent que
    /// par la parole (livrable 7). Laisses a <c>null</c>, ce que l'agent savait deja de l'etat de
    /// l'interesse est conserve — apercevoir un allie de loin ne fait pas oublier qu'il vous a dit
    /// dix secondes plus tot qu'il etait blesse.
    /// </summary>
    public bool RememberEntity(
        string id,
        EntityKind kind,
        GridPosition position,
        double time,
        KnowledgeSource source = KnowledgeSource.DirectObservation,
        float confidence = 1f,
        Vector2? heading = null,
        float? health = null,
        float? panic = null)
    {
        _entities.TryGetValue(id, out var known);
        if (known is not null && known.LastObservedAt > time) return false;

        var course = heading ?? (known is null ? default : EstimateHeading(known, position, time));

        _entities[id] = new EntityBelief(
            id, kind, position, Clamp(confidence), time, source, course,
            health ?? known?.HealthRatio,
            panic ?? known?.Panic);

        return true;
    }

    /// <summary>
    /// Cap deduit de deux observations successives, en cellules par seconde. C'est ce qui permet a
    /// un agent de se dire « il me suivait » plutot que « il etait la ».
    ///
    /// Deux apercus trop espaces ne decrivent aucune trajectoire, et une entite revue immobile perd
    /// son cap progressivement : le lissage ramene la direction vers zero au lieu de la figer.
    /// </summary>
    private Vector2 EstimateHeading(EntityBelief previous, GridPosition position, double time)
    {
        float elapsed = (float)(time - previous.LastObservedAt);
        if (elapsed <= 0f) return previous.Heading;
        if (elapsed > Settings.HeadingMemory) return default;

        var observed = new Vector2(position.X - previous.Value.X, position.Y - previous.Value.Y) / elapsed;

        var blended = previous.IsMoving
            ? Vector2.Lerp(observed, previous.Heading, Math.Clamp(Settings.HeadingSmoothing, 0f, 1f))
            : observed;

        float speed = blended.Length();
        return speed > Settings.MaximumTrackedSpeed
            ? blended / speed * Settings.MaximumTrackedSpeed
            : blended;
    }

    /// <summary>
    /// Ou l'agent croit trouver l'entite maintenant : sa derniere position vue, prolongee par le cap
    /// qu'il lui prete. C'est ce qui fait la difference entre fuir l'endroit ou etait un zombie et
    /// fuir le zombie.
    ///
    /// La supposition s'arrete a ce que l'agent sait du terrain : il ne prete a personne le pouvoir
    /// de traverser un mur qu'il a vu. Une cellule jamais observee, en revanche, ne bloque rien —
    /// il ignore ce qu'elle contient, il ne peut pas s'en servir pour ecarter une hypothese.
    /// </summary>
    public GridPosition PredictedPositionOf(EntityBelief belief, double now)
    {
        var predicted = belief.PredictedPositionAt(now, Settings.PredictionHorizon);

        if (predicted == belief.Value) return belief.Value;
        if (!MentalMap.Contains(predicted)) return belief.Value;
        if (HasObserved(predicted) && !MentalMap.IsWalkable(predicted)) return belief.Value;

        return predicted;
    }

    /// <summary>
    /// L'agent regarde la ou l'entite devrait se trouver et ne l'y trouve pas : la croyance tombe a
    /// zero, cap compris. Il sait alors que la case est vide, ce qui n'est pas la meme chose que ne
    /// pas savoir.
    ///
    /// Ce dementi porte sur la position <b>supposee</b>, pas sur celle d'origine : voir vide
    /// l'endroit ou un poursuivant se tenait il y a trois secondes ne prouve rien, sinon qu'il a
    /// bouge — ce que l'agent croyait deja.
    /// </summary>
    public void ForgetEntityAt(string id, double time)
    {
        if (!_entities.TryGetValue(id, out var known)) return;
        if (known.LastObservedAt > time) return;

        _entities[id] = known with
        {
            Confidence = 0f,
            LastObservedAt = time,
            Source = KnowledgeSource.DirectObservation,
            Heading = default,
        };
    }

    public float ConfidenceIn(EntityBelief belief, double now)
        => belief.ConfidenceAt(now, DecayRateFor(belief));

    /// <summary>
    /// Une position d'entite est toujours dynamique. Les zombies s'oublient plus vite que les
    /// allies : ils se deplacent sans prevenir (specification section 9.5).
    /// </summary>
    public float DecayRateFor(EntityBelief belief) => belief.Kind switch
    {
        EntityKind.Zombie => Settings.ZombieConfidenceDecay,
        _ => Settings.AllyConfidenceDecay,
    };

    /// <summary>Entites encore assez credibles pour etre prises en compte par une decision.</summary>
    public IEnumerable<EntityBelief> BelievedEntities(double now, float? minimumConfidence = null)
    {
        float floor = minimumConfidence ?? Settings.MinimumConfidence;
        return _entities.Values.Where(belief => ConfidenceIn(belief, now) > floor);
    }

    // --- Objets et lieux ------------------------------------------------------------------

    public ItemBelief? GetItem(string id) => _items.TryGetValue(id, out var belief) ? belief : null;

    public bool RememberItem(
        string id,
        string itemId,
        GridPosition position,
        double time,
        KnowledgeSource source = KnowledgeSource.DirectObservation,
        float confidence = 1f)
    {
        if (_items.TryGetValue(id, out var known) && known.LastObservedAt > time) return false;

        _items[id] = new ItemBelief(id, itemId, position, Clamp(confidence), time, source);
        return true;
    }

    /// <summary>
    /// L'agent revient sur la case ou il croyait laisser un objet et ne l'y trouve plus : quelqu'un
    /// est passe avant lui. La croyance tombe a zero au lieu d'etre effacee — « je sais que ce n'est
    /// plus la » n'est pas « je n'en ai jamais entendu parler ».
    ///
    /// L'invalidation est <b>differee</b>, et c'est le point : rien dans le monde ne previent qu'un
    /// objet a change de main. Tant que l'agent n'est pas retourne voir, il continue de planifier
    /// avec un objet qui n'existe plus — et c'est la replanification qui rattrapera l'erreur.
    /// </summary>
    public bool ForgetItemAt(string id, double time)
    {
        if (!_items.TryGetValue(id, out var known)) return false;
        if (known.LastObservedAt > time || known.Confidence <= 0f) return false;

        _items[id] = known with
        {
            Confidence = 0f,
            LastObservedAt = time,
            Source = KnowledgeSource.DirectObservation,
        };

        return true;
    }

    // --- Meubles poussables (livrable 11) ---------------------------------------------------

    private readonly Dictionary<string, HeavyBelief> _heavy = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Les meubles que l'agent a vus. Un meuble qu'un allie a pousse pendant qu'on regardait
    /// ailleurs reste ou on l'a laisse dans cette carte-la : c'est la perception partielle du
    /// livrable 2, appliquee au decor.
    /// </summary>
    public IReadOnlyDictionary<string, HeavyBelief> KnownHeavy => _heavy;

    public HeavyBelief? GetHeavy(string id) => _heavy.TryGetValue(id, out var belief) ? belief : null;

    public bool RememberHeavy(
        string id,
        string kind,
        GridPosition position,
        float mass,
        double time,
        KnowledgeSource source = KnowledgeSource.DirectObservation,
        float confidence = 1f)
    {
        if (_heavy.TryGetValue(id, out var known) && known.LastObservedAt > time) return false;

        _heavy[id] = new HeavyBelief(id, kind, position, mass, Clamp(confidence), time, source);
        return true;
    }

    /// <summary>L'agent repasse et ne trouve plus le meuble ou il croyait : quelqu'un l'a pousse.</summary>
    public bool ForgetHeavyAt(string id, double time)
    {
        if (!_heavy.TryGetValue(id, out var known)) return false;
        if (known.LastObservedAt > time || known.Confidence <= 0f) return false;

        _heavy[id] = known with
        {
            Confidence = 0f,
            LastObservedAt = time,
            Source = KnowledgeSource.DirectObservation,
        };

        return true;
    }

    // --- Fouilles -------------------------------------------------------------------------

    /// <summary>Lieux fouilles, et ce que la fouille a donne.</summary>
    public IReadOnlyDictionary<string, SearchBelief> SearchedPlaces => _searches;

    public SearchBelief? GetSearch(string place)
        => _searches.TryGetValue(place, out var belief) ? belief : null;

    /// <summary>
    /// Retient ce qu'une fouille a donne. Comme toute croyance, la plus recente l'emporte : une
    /// piece ou l'on a pris quelque chose peut tres bien etre vide la fois suivante.
    /// </summary>
    public bool RememberSearch(
        string place,
        bool found,
        double time,
        KnowledgeSource source = KnowledgeSource.DirectObservation,
        float confidence = 1f)
    {
        if (string.IsNullOrWhiteSpace(place)) return false;
        if (_searches.TryGetValue(place, out var known) && known.LastObservedAt > time) return false;

        _searches[place] = new SearchBelief(place, found, Clamp(confidence), time, source);
        return true;
    }

    public LocationBelief? GetLocation(string name)
        => _locations.TryGetValue(name, out var belief) ? belief : null;

    public LocationBelief? GetLocation(ZoneKind kind)
        => _locations.Values.FirstOrDefault(belief => belief.Kind == kind);

    public bool RememberLocation(
        string name,
        ZoneKind kind,
        GridPosition position,
        double time,
        KnowledgeSource source = KnowledgeSource.DirectObservation,
        float confidence = 1f)
    {
        if (_locations.TryGetValue(name, out var known) && known.LastObservedAt > time) return false;

        _locations[name] = new LocationBelief(name, kind, position, Clamp(confidence), time, source);
        return true;
    }

    // --- Intentions -----------------------------------------------------------------------

    public IntentBelief? GetIntent(string id) => _intents.TryGetValue(id, out var belief) ? belief : null;

    /// <summary>
    /// Retient qu'un survivant — souvent soi-meme — s'est annonce vers un endroit. Declaree en son
    /// nom propre, l'intention n'a aucun effet ici ; elle n'en prend un qu'une fois transmise a un
    /// allie, ou elle devient la penalite de duplication de <c>FrontierExplorer</c>.
    /// </summary>
    public bool DeclareIntent(
        string agentId,
        string goal,
        GridPosition target,
        double time,
        KnowledgeSource source = KnowledgeSource.DirectObservation,
        float confidence = 1f)
    {
        if (string.IsNullOrWhiteSpace(agentId) || string.IsNullOrWhiteSpace(goal)) return false;

        var incoming = new IntentBelief(agentId, goal, target, Clamp(confidence), time, source);
        if (!BeliefMerge.Accepts(GetIntent(agentId), incoming)) return false;

        _intents[agentId] = incoming;
        return true;
    }

    /// <summary>
    /// Intentions encore fraiches des <b>autres</b> survivants. Une annonce vieillit vite : dire ou
    /// l'on allait il y a une minute ne renseigne plus sur ou l'on est.
    /// </summary>
    public IEnumerable<IntentBelief> AllyIntents(double now, float maximumAge)
        => _intents.Values.Where(intent =>
            intent.Id != AgentId && now - intent.LastObservedAt <= maximumAge);

    // --- Bruits ---------------------------------------------------------------------------

    public NoiseBelief? GetNoise(string id) => _noises.TryGetValue(id, out var belief) ? belief : null;

    /// <summary>
    /// Memorise un bruit : ou l'agent croit l'avoir entendu, a quelle intensite, et de quelle nature.
    /// <paramref name="approximatePosition"/> est ce que l'agent croit etre son origine, pas la
    /// position reelle de la source : c'est <see cref="World.NoiseSystem.Localize"/> qui a introduit
    /// l'erreur, et cette classe n'a aucun moyen de la corriger.
    ///
    /// <paramref name="action"/> est le <b>type</b> de bruit — un pas, une porte forcee, une vitre.
    /// Il ne dit rien de qui l'a produit : la signature n'accepte deliberement aucun identifiant de
    /// source, ce qui rend impossible de deduire d'un fracas que c'est un zombie.
    ///
    /// Un bruit deja connu n'est pas rafraichi : sa confiance continue de decroitre depuis la
    /// premiere ecoute, sinon un bruit persistant resterait indefiniment « recent ».
    /// </summary>
    public bool RememberNoise(
        string id,
        GridPosition approximatePosition,
        float perceivedIntensity,
        double time,
        float confidence = 1f,
        NoiseAction action = NoiseAction.Walk)
    {
        if (_noises.ContainsKey(id)) return false;

        _noises[id] = new NoiseBelief(
            id, approximatePosition, perceivedIntensity, Clamp(confidence), time, KnowledgeSource.Sound, action);
        return true;
    }

    public void ForgetNoise(string id) => _noises.Remove(id);

    public float ConfidenceIn(NoiseBelief belief, double now)
        => belief.ConfidenceAt(now, DecayRateFor(belief));

    public float DecayRateFor(NoiseBelief _) => Settings.NoiseConfidenceDecay;

    /// <summary>
    /// Bruit encore credible le plus recent — a intensite egale, le plus fort. C'est le « bruit
    /// recent » de l'ordre de priorite de la specification (section 17.3).
    /// </summary>
    public NoiseBelief? MostRecentNoise(double now, float? minimumConfidence = null)
    {
        float floor = minimumConfidence ?? Settings.MinimumConfidence;

        return _noises.Values
            .Where(belief => ConfidenceIn(belief, now) > floor)
            .OrderByDescending(belief => belief.LastObservedAt)
            .ThenByDescending(belief => belief.PerceivedIntensity)
            .FirstOrDefault();
    }

    /// <summary>Retire les bruits dont la confiance est retombee a zero. Simple menage.</summary>
    public int ForgetFadedNoises(double now)
    {
        var faded = _noises.Values.Where(belief => ConfidenceIn(belief, now) <= 0f).Select(b => b.Id).ToList();
        foreach (var id in faded) _noises.Remove(id);
        return faded.Count;
    }

    // --- Partage (livrable 7) --------------------------------------------------------------

    /// <summary>
    /// Recopie les croyances d'un allie qui l'emportent sur les siennes. La signature ne prend qu'une
    /// autre <see cref="AgentKnowledge"/> : le partage ne peut structurellement pas puiser dans la
    /// verite du monde — c'est le critere du livrable 7 (« le partage de connaissances n'accede pas
    /// au <c>WorldState</c> »), garanti par le type plutot que par la discipline.
    ///
    /// Chaque croyance transmise suit les quatre regles de la specification : sa source devient
    /// <see cref="KnowledgeSource.AllyCommunication"/>, sa confiance baisse d'un cran, et c'est
    /// <see cref="BeliefMerge"/> qui arbitre — le plus recent l'emporte, mais jamais sur une
    /// observation directe deja faite.
    /// </summary>
    public int LearnFrom(AgentKnowledge other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(other, this)) return 0;

        float trust = 1f - Clamp(Settings.SharedConfidencePenalty);
        int learned = 0;

        // Chaque boucle suit le meme dessin : degrader la croyance en temoignage, demander a
        // merge_belief si elle l'emporte, et seulement alors l'ecrire.
        foreach (var belief in other.KnownCells.Values)
        {
            var incoming = belief with { Confidence = belief.Confidence * trust, Source = Hearsay };
            if (!BeliefMerge.Accepts(GetCell(belief.Position), incoming)) continue;

            if (ObserveCell(
                    incoming.Position, incoming.Value, incoming.Indoor, incoming.LastObservedAt,
                    incoming.Source, incoming.Confidence))
            {
                learned++;
            }
        }

        foreach (var belief in other.KnownEntities.Values)
        {
            // Ce qu'un allie croit savoir de moi ne m'apprend rien : je suis mieux place pour le
            // savoir, et l'importer ferait de ma propre position une croyance de seconde main.
            if (belief.Id == AgentId) continue;

            var incoming = belief with { Confidence = belief.Confidence * trust, Source = Hearsay };
            if (!BeliefMerge.Accepts(GetEntity(belief.Id), incoming)) continue;

            if (RememberEntity(
                    incoming.Id, incoming.Kind, incoming.Value, incoming.LastObservedAt,
                    incoming.Source, incoming.Confidence, incoming.Heading,
                    incoming.HealthRatio, incoming.Panic))
            {
                learned++;
            }
        }

        foreach (var belief in other.KnownItems.Values)
        {
            var incoming = belief with { Confidence = belief.Confidence * trust, Source = Hearsay };
            if (!BeliefMerge.Accepts(GetItem(belief.Id), incoming)) continue;

            if (RememberItem(
                    incoming.Id, incoming.ItemId, incoming.Value, incoming.LastObservedAt,
                    incoming.Source, incoming.Confidence))
            {
                learned++;
            }
        }

        foreach (var belief in other.KnownLocations.Values)
        {
            var incoming = belief with { Confidence = belief.Confidence * trust, Source = Hearsay };
            if (!BeliefMerge.Accepts(GetLocation(belief.Name), incoming)) continue;

            if (RememberLocation(
                    incoming.Name, incoming.Kind, incoming.Value, incoming.LastObservedAt,
                    incoming.Source, incoming.Confidence))
            {
                learned++;
            }
        }

        // « J'ai deja fouille la, il n'y avait rien » evite a l'allie d'y retourner : c'est
        // exactement le genre d'information qu'on se dit, et elle vaut une case decouverte.
        foreach (var belief in other.SearchedPlaces.Values)
        {
            var incoming = belief with { Confidence = belief.Confidence * trust, Source = Hearsay };
            if (!BeliefMerge.Accepts(GetSearch(belief.Place), incoming)) continue;

            if (RememberSearch(
                    incoming.Place, incoming.Found, incoming.LastObservedAt,
                    incoming.Source, incoming.Confidence))
            {
                learned++;
            }
        }

        // « Je m'occupe du nord » : sans cette ligne, la penalite de duplication n'aurait rien a lire
        // et les trois survivants viseraient la meme frontiere. DeclareIntent arbitre lui-meme.
        foreach (var belief in other.KnownIntents.Values)
        {
            if (belief.Id == AgentId) continue;

            if (DeclareIntent(
                    belief.Id, belief.Goal, belief.Value, belief.LastObservedAt,
                    Hearsay, belief.Confidence * trust))
            {
                learned++;
            }
        }

        return learned;
    }

    /// <summary>Ce que devient la source d'une croyance qui a change de bouche.</summary>
    private const KnowledgeSource Hearsay = KnowledgeSource.AllyCommunication;

    private static float Clamp(float value) => Math.Clamp(value, 0f, 1f);
}
