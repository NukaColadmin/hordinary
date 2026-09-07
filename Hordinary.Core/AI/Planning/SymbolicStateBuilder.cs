using System.Globalization;
using Hordinary.AI.Utility;
using Hordinary.Agents;
using Hordinary.Config;
using Hordinary.World;

namespace Hordinary.AI.Planning;

/// <summary>Etat symbolique et objets derives d'une memoire d'agent, a un instant donne.</summary>
public sealed record SymbolicSnapshot(
    SymbolicState State,
    IReadOnlyList<PlanningObject> Objects,
    IReadOnlyDictionary<string, float> Danger,
    GridPosition AgentPosition);

/// <summary>
/// Traduit ce qu'un survivant sait en probleme de planification (specification, livrable 5 :
/// « Representation d'un etat »).
///
/// C'est le seul endroit ou la memoire devient des faits. La signature n'accepte qu'un
/// <see cref="UtilityContext"/>, lui-meme construit a partir de la seule <c>AgentKnowledge</c> :
/// le planificateur ne peut donc structurellement pas raisonner sur une porte, un lieu ou un zombie
/// que le survivant n'a jamais percu. Un plan qui echoue parce que le monde reel n'etait pas celui
/// qu'il croyait est le comportement attendu, pas un defaut — c'est ce que rattrape le livrable 6.
///
/// Le vocabulaire des lieux est exactement celui du livrable 4
/// (<see cref="UtilityContext.TargetNames"/>) : <c>objective_zone</c>, <c>extraction</c>, <c>cover</c>,
/// <c>frontier</c>, <c>away_from_danger</c>… Un objectif Utility AI et un but symbolique parlent ainsi
/// de la meme carte mentale.
/// </summary>
public static class SymbolicStateBuilder
{
    /// <summary>Cible qui designe l'agent lui-meme : elle ne devient pas un lieu ou se rendre.</summary>
    private const string SelfTarget = "self";

    /// <summary>
    /// Cibles du livrable 4 dont le nom entrerait en collision avec un objet d'un autre type.
    /// « objective » designe l'objet ramassable — ce que la mission envoie chercher, quel qu'il soit ;
    /// l'endroit ou le survivant croit le voir s'appelle « objective_spot ». Sans cette distinction,
    /// <c>has(agent, objective)</c> et <c>at(agent, objective)</c> parleraient du meme objet.
    /// </summary>
    private static readonly Dictionary<string, string> LocationAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["objective"] = "objective_spot",
    };

    /// <summary>
    /// Lieux que le terrain lui-meme designe (section 7.2 : la pharmacie et l'extraction ont leur
    /// propre type de cellule). Se tenir sur une de ces cellules, c'est etre dans le lieu — pas
    /// seulement a cote de la cellule que la memoire retient comme representative. C'est ce qui donne
    /// a <c>at(agent, pharmacy)</c> la granularite d'une piece plutot que d'une case.
    /// </summary>
    private static readonly Dictionary<CellType, string> LocationCellTypes = new()
    {
        [CellType.Pharmacy] = "pharmacy",
        [CellType.Extraction] = "extraction",
    };

    /// <summary>
    /// Lieux nommes de la carte, ceux qui viennent d'une zone (<see cref="ZoneKind"/>). Les autres
    /// cibles du livrable 4 — <c>cover</c>, <c>frontier</c>, <c>away_from_danger</c>, <c>group</c>… —
    /// sont des reperes recalcules a chaque decision, pas des endroits : une porte ne garde pas une
    /// frontiere d'exploration, et il n'y aurait aucun sens a declarer un tel repere inaccessible.
    /// </summary>
    private static readonly HashSet<string> NamedPlaces = new(StringComparer.OrdinalIgnoreCase)
    {
        "pharmacy", "extraction", "start", UtilityContext.ObjectiveZoneTarget,
    };

    /// <summary>
    /// Meme vocabulaire que <see cref="NamedPlaces"/>, mais depuis le terrain : c'est ce qui permet
    /// a un systeme qui raisonne en zones — la fouille d'une piece, par exemple — de nommer un lieu
    /// comme le planificateur le nomme, sans dupliquer la correspondance.
    /// </summary>
    public static string? PlaceNameOf(ZoneKind kind) => kind switch
    {
        ZoneKind.Pharmacy => "pharmacy",
        ZoneKind.Extraction => "extraction",
        ZoneKind.Start => "start",
        _ => null,
    };

    /// <summary>
    /// Le meme nom, mais du point de vue d'un survivant en mission : le lieu qu'on l'a envoye
    /// fouiller s'appelle <c>objective_zone</c>, quelle que soit sa categorie.
    ///
    /// Cette surcharge existe pour une raison qui n'a rien de cosmetique. Une fouille infructueuse
    /// n'entre dans la memoire que sous un nom que le planificateur sait relire ; tant que seules la
    /// pharmacie et l'extraction en avaient un, une mission qui envoyait fouiller un entrepot ne
    /// gardait aucune trace de sa deception — et le survivant y retournait indefiniment.
    /// </summary>
    public static string? PlaceNameOf(MapZone zone, Survivor survivor)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(survivor);

        return survivor.ObjectiveZone is { } site && site.Matches(zone)
            ? UtilityContext.ObjectiveZoneTarget
            : PlaceNameOf(zone.Kind);
    }

    /// <summary>
    /// Predicats que <see cref="Observe"/> sait produire, donc ceux sur lesquels une nouvelle
    /// observation a le dernier mot. Tout le reste — <c>searched</c>, <c>explored</c>, <c>hidden</c>,
    /// <c>waited</c>… — decrit l'avancement d'un plan et non le monde : aucun regard ne peut le
    /// confirmer ni le dementir.
    ///
    /// C'est <see cref="SymbolicState.Reconcile"/> qui s'en sert, au livrable 6, pour distinguer
    /// « ce qui a change » de « ce qui a ete fait ». La liste est ici, a cote du code qui pose ces
    /// faits, pour qu'ajouter un predicat observable et oublier de le declarer reste difficile.
    /// </summary>
    public static IReadOnlySet<string> ObservablePredicates { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "known", "accessible", "at", "extraction_known", "zombie_near", "zombie_adjacent",
        "door_open", "door_closed", "door_locked", "door_of", "window_intact", "window_of",
        "has", "item_at", "objective_found", "objective_collected", "ally_in_danger",
        "ally_wounded", "ally_panicking", "searched_in_vain",

        // Livrable 11. « equipped », « usable » et « inventory_full » decrivent le sac, que le
        // survivant a sous les yeux en permanence ; « heavy_at », « mass_ok » et « zombie_in_range »
        // decrivent la scene, et se dementent donc comme le reste du decor.
        //
        // « door_barricaded » n'y figure pas, et c'est voulu : barricader verrouille la porte, ce que
        // « door_locked » dit deja et qu'un regard peut confirmer. Le fait de barricade n'est qu'un
        // marqueur d'avancement de plan, comme « searched ».
        "equipped", "usable", "inventory_full", "heavy_at", "mass_ok", "zombie_in_range",
    };

    /// <param name="carried">
    /// Acquis du plan precedent a reconduire : ce qu'un survivant a fait et qui reste vrai, mais
    /// qu'aucune observation ne peut confirmer — la piece deja fouillee, la vitre deja brisee. Les
    /// marqueurs de but en sont exclus par <see cref="Carry"/>.
    /// </param>
    public static PlanningProblem Build(
        UtilityContext context,
        PlanningDomain domain,
        PlanningGoal goal,
        PlannerSettings? settings = null,
        IEnumerable<string>? carried = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(goal);

        var tuning = settings ?? new PlannerSettings();
        var snapshot = Observe(context, tuning);

        var initial = carried is null
            ? snapshot.State
            : new SymbolicState(snapshot.State.Facts.Concat(carried));

        // Les consignes permanentes du joueur (livrable 8) amputent le domaine avant la recherche,
        // et non les scores apres coup. Interdire les actions bruyantes ne rend pas la vitre brisee
        // couteuse : elle disparait du repertoire, et GBFS trouve un autre plan — ou n'en trouve
        // aucun, ce que le mode debug affiche au lieu de le masquer par un contournement silencieux.
        var available = domain.Ground(snapshot.Objects)
            .Where(action => context.Self.Orders.AllowsNoise(action.NoiseLevel))
            .ToList();

        var actions = ActionRelevance.Filter(available, goal.ParsedConditions);

        return new PlanningProblem(
            goal.Name,
            initial,
            goal.ParsedConditions,
            snapshot.Objects,
            actions,
            snapshot.AgentPosition,
            snapshot.Danger);
    }

    /// <summary>
    /// Trie, dans l'etat laisse par un plan, ce qui merite de survivre au suivant.
    ///
    /// Deux exclusions, et les deux comptent. Ce que l'observation sait produire est ecarte : le
    /// monde a le dernier mot sur les portes, les lieux et les objets, et reconduire une croyance
    /// perimee serait exactement l'erreur que la replanification cherche a corriger. Ce dont un
    /// objectif fait sa reussite est ecarte aussi : voir <see cref="PlanningDomain.GoalPredicates"/>.
    ///
    /// Reste l'acquis : la piece fouillee, la vitre brisee, l'information partagee. Sans ce tri, un
    /// survivant qui replanifie refouille indefiniment la meme piece.
    /// </summary>
    public static IEnumerable<string> Carry(SymbolicState state, PlanningDomain domain)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(domain);

        return state.Facts.Where(fact =>
        {
            string predicate = Fact.PredicateOf(fact);
            return !ObservablePredicates.Contains(predicate) && !domain.GoalPredicates.Contains(predicate);
        });
    }

    /// <summary>Photographie la memoire du survivant : objets connus, faits vrais, danger par lieu.</summary>
    public static SymbolicSnapshot Observe(UtilityContext context, PlannerSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        var tuning = settings ?? new PlannerSettings();
        var self = context.Self;
        var knowledge = self.Knowledge;

        var objects = new List<PlanningObject>();
        var facts = new List<string>();
        var danger = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        var locations = CollectLocations(context, objects, facts, danger, tuning);
        var places = locations.Where(location => NamedPlaces.Contains(location.Name)).ToList();

        var doorsteps = CollectOpenings(knowledge, locations, places, objects, facts, tuning, self.Position);

        // Un lieu qu'on ne peut plus traverser se ramene a son seuil : voir plus bas.
        Relocate(objects, locations, places, doorsteps);

        CollectPositions(context, locations, facts, tuning);
        CollectItems(context, locations, objects, facts);
        CollectSearches(knowledge, facts);
        CollectAllies(context, objects, facts, danger, tuning);
        CollectThreats(context, objects, facts);
        CollectHeavy(context, objects, facts);

        return new SymbolicSnapshot(new SymbolicState(facts), objects, danger, self.Position);
    }

    // --- Lieux ----------------------------------------------------------------------------------

    /// <summary>
    /// Un lieu par cible resolvable de la carte mentale. Une cible que le survivant ne sait pas
    /// situer ne produit aucun objet : il ne peut donc pas planifier de s'y rendre, ce qui est la
    /// traduction symbolique de « je ne sais pas ou c'est ».
    /// </summary>
    private static List<PlanningObject> CollectLocations(
        UtilityContext context,
        List<PlanningObject> objects,
        List<string> facts,
        Dictionary<string, float> danger,
        PlannerSettings settings)
    {
        var locations = new List<PlanningObject>();

        // Le lieu de la mission, s'il en a un. Il porte deja un nom du terrain — « pharmacy » sur la
        // carte livree — et le laisser en produire un second au meme endroit serait une erreur
        // silencieuse : la porte ne se rattache qu'a UN lieu, et le jumeau qui ne l'obtient pas passe
        // pour ouvert. Un seul objet par endroit, donc, et c'est « objective_zone » qui l'emporte —
        // le domaine ne connait plus que ce nom-la.
        var objectiveCell = context.Locate(UtilityContext.ObjectiveZoneTarget);

        foreach (var target in UtilityContext.TargetNames.Order(StringComparer.Ordinal))
        {
            if (target.Equals(SelfTarget, StringComparison.OrdinalIgnoreCase)) continue;
            if (context.Locate(target) is not { } cell) continue;

            if (IsMergedIntoObjective(target, cell, objectiveCell)) continue;

            string name = LocationAliases.GetValueOrDefault(target, target);

            var location = new PlanningObject(name, PlanningObjectTypes.Location, cell);
            locations.Add(location);
            objects.Add(location);

            facts.Add($"known({name})");

            float threat = context.DangerAt(cell);
            danger[name] = threat;
            if (threat >= settings.DangerThreshold) facts.Add($"zombie_near({name})");
        }

        // La specification liste « extraction_known » parmi les faits de l'etat : c'est la condition
        // qui autorise a planifier une sortie.
        if (locations.Any(location => location.Name.Equals("extraction", StringComparison.OrdinalIgnoreCase)))
            facts.Add("extraction_known");

        return locations;
    }

    /// <summary>
    /// Vrai si ce lieu nomme est en fait celui de la mission, deja declare sous le nom
    /// <c>objective_zone</c>. Seuls les lieux du terrain sont concernes : « frontier » ou
    /// « away_from_danger » sont des reperes recalcules, et deux reperes au meme endroit ne genent
    /// personne — c'est le rattachement d'une porte a UN lieu qui ne supporte pas les jumeaux.
    /// </summary>
    private static bool IsMergedIntoObjective(string target, GridPosition cell, GridPosition? objectiveCell)
        => objectiveCell is { } objective
        && cell == objective
        && !target.Equals(UtilityContext.ObjectiveZoneTarget, StringComparison.OrdinalIgnoreCase)
        && NamedPlaces.Contains(target);

    /// <summary>
    /// Ou se tient l'agent, une fois les lieux places pour de bon. La question se pose apres
    /// <see cref="Relocate"/> et non pendant <see cref="CollectLocations"/> : etre « a la pharmacie »
    /// ne veut pas dire la meme chose selon qu'on peut y entrer ou non.
    /// </summary>
    private static void CollectPositions(
        UtilityContext context,
        List<PlanningObject> locations,
        List<string> facts,
        PlannerSettings settings)
    {
        foreach (var location in locations)
        {
            if (location.Position is { } cell && IsAgentAt(context, location.Name, cell, settings))
                facts.Add($"at({PlanningProblem.Agent}, {location.Name})");
        }
    }

    /// <summary>
    /// Vrai si l'agent est « sur place » : a portee de la cellule que sa memoire retient pour ce lieu,
    /// ou debout sur une cellule que le terrain rattache a ce lieu. Le second cas est ce qui permet a un
    /// survivant plante au fond de la pharmacie de savoir qu'il y est.
    /// </summary>
    private static bool IsAgentAt(UtilityContext context, string name, GridPosition cell, PlannerSettings settings)
    {
        var self = context.Self;
        if (self.Position.ChebyshevDistanceTo(cell) <= settings.ArrivalRadius) return true;

        // La question se pose depuis la carte mentale : une cellule jamais observee ne renseigne pas.
        if (self.Knowledge.GetCell(self.Position) is not { } here) return false;
        if (!LocationCellTypes.TryGetValue(here.Value, out string? designated)) return false;

        // Le sol de la pharmacie dit « pharmacy » ; quand la pharmacie EST la mission, le lieu ne
        // porte plus ce nom mais « objective_zone », et l'equivalence doit suivre — sans quoi un
        // survivant plante au fond de la piece ne saurait plus qu'il y est.
        if (context.Locate(UtilityContext.ObjectiveZoneTarget) is { } objective
            && context.Locate(designated) == objective)
        {
            designated = UtilityContext.ObjectiveZoneTarget;
        }

        return designated.Equals(name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ramene chaque lieu devenu impenetrable a son seuil — la case franchissable d'ou l'on peut
    /// agir sur l'ouverture qui le ferme.
    ///
    /// Sans cela, un plan parfaitement correct devient inexecutable : la memoire retient la
    /// pharmacie par une case de son interieur, <c>ForceDoor</c> demande d'etre « a la pharmacie »,
    /// et A* ne peut pas y mener puisque la porte est justement verrouillee. Le survivant
    /// replanifierait indefiniment le bon plan sans jamais pouvoir en jouer la premiere action.
    ///
    /// Un lieu ferme <b>est</b> sa porte, tant qu'il est ferme. Une fois l'entree ouverte, il
    /// redevient son interieur — et la replanification qui suit fait entrer le survivant.
    /// </summary>
    private static void Relocate(
        List<PlanningObject> objects,
        List<PlanningObject> locations,
        List<PlanningObject> places,
        IReadOnlyDictionary<string, GridPosition> doorsteps)
    {
        if (doorsteps.Count == 0) return;

        foreach (var list in new[] { objects, locations, places })
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (!doorsteps.TryGetValue(list[i].Name, out var doorstep)) continue;
                if (list[i].Type != PlanningObjectTypes.Location) continue;

                list[i] = list[i] with { Position = doorstep };
            }
        }
    }

    // --- Portes et fenetres ---------------------------------------------------------------------

    /// <summary>
    /// Chaque porte et chaque fenetre memorisee devient un objet, rattachee au lieu nomme qu'elle
    /// garde — le plus proche, dans la limite de <see cref="PlannerSettings.DoorLinkRadius"/>.
    /// Rattacher une porte a tous les lieux alentour ferait passer une rue pour une piece verrouillee.
    ///
    /// Un lieu reste <c>accessible</c> tant qu'une de ses ouvertures connues n'est pas verrouillee.
    /// C'est cette precondition qui oblige le planificateur a intercaler une action adaptee —
    /// <c>ForceDoor</c> ou <c>BreakWindow</c> — avant de pouvoir fouiller une piece fermee a clef.
    ///
    /// Renvoie, pour chaque lieu devenu impenetrable, le seuil depuis lequel on peut agir sur son
    /// ouverture (voir <see cref="Relocate"/>).
    /// </summary>
    private static Dictionary<string, GridPosition> CollectOpenings(
        AgentKnowledge knowledge,
        List<PlanningObject> locations,
        List<PlanningObject> places,
        List<PlanningObject> objects,
        List<string> facts,
        PlannerSettings settings,
        GridPosition from)
    {
        var locked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var passable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bordered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var openings = new Dictionary<string, List<GridPosition>>(StringComparer.OrdinalIgnoreCase);

        foreach (var belief in knowledge.KnownCells.Values.OrderBy(c => c.Position.Y).ThenBy(c => c.Position.X))
        {
            bool isDoor = belief.Value.IsDoor();
            bool isWindow = belief.Value == CellType.Window;
            if (!isDoor && !isWindow) continue;

            string name = Token(isDoor ? "door" : "window", belief.Position);
            objects.Add(new PlanningObject(
                name,
                isDoor ? PlanningObjectTypes.Door : PlanningObjectTypes.Window,
                belief.Position));

            if (isWindow) facts.Add($"window_intact({name})");
            else if (belief.Value == CellType.DoorOpen) facts.Add($"door_open({name})");
            else if (belief.Value == CellType.DoorClosed) facts.Add($"door_closed({name})");
            else facts.Add($"door_locked({name})");

            if (NearestLocation(places, belief.Position, settings.DoorLinkRadius) is not { } guarded) continue;

            facts.Add(isDoor ? $"door_of({name}, {guarded.Name})" : $"window_of({name}, {guarded.Name})");
            bordered.Add(guarded.Name);

            if (!openings.TryGetValue(guarded.Name, out var cells)) openings[guarded.Name] = cells = [];
            cells.Add(belief.Position);

            if (!isDoor) continue;
            if (belief.Value == CellType.DoorLocked) locked.Add(guarded.Name);
            else passable.Add(guarded.Name);
        }

        var doorsteps = new Dictionary<string, GridPosition>(StringComparer.OrdinalIgnoreCase);

        foreach (var location in locations)
        {
            bool sealedOff = bordered.Contains(location.Name)
                && locked.Contains(location.Name)
                && !passable.Contains(location.Name);

            if (!sealedOff)
            {
                facts.Add($"accessible({location.Name})");
                continue;
            }

            if (Doorstep(knowledge, openings.GetValueOrDefault(location.Name), from) is { } threshold)
                doorsteps[location.Name] = threshold;
        }

        return doorsteps;
    }

    /// <summary>
    /// Le pas de porte d'un lieu ferme : la case franchissable, voisine d'une de ses ouvertures, la
    /// plus proche de <paramref name="from"/>. Prendre la plus proche revient a prendre celle du
    /// cote ou l'on se trouve — on force une porte depuis la rue, pas depuis la piece.
    ///
    /// Les candidates sont cherchees dans la carte mentale : un seuil jamais observe ne serait pas
    /// un endroit ou le survivant sait aller.
    /// </summary>
    private static GridPosition? Doorstep(AgentKnowledge knowledge, List<GridPosition>? openings, GridPosition from)
    {
        if (openings is null) return null;

        GridPosition? best = null;
        int shortest = int.MaxValue;

        foreach (var opening in openings)
        {
            foreach (var offset in GridPosition.OrthogonalOffsets)
            {
                var neighbour = opening + offset;

                if (!knowledge.HasObserved(neighbour)) continue;
                if (!knowledge.MentalMap.IsWalkable(neighbour)) continue;

                int distance = neighbour.ChebyshevDistanceTo(from);
                if (distance >= shortest) continue;

                shortest = distance;
                best = neighbour;
            }
        }

        return best;
    }

    private static PlanningObject? NearestLocation(List<PlanningObject> locations, GridPosition cell, int radius)
    {
        PlanningObject? nearest = null;
        int best = int.MaxValue;

        foreach (var location in locations)
        {
            if (location.Position is not { } position) continue;

            int distance = position.ChebyshevDistanceTo(cell);
            if (distance > radius || distance >= best) continue;

            best = distance;
            nearest = location;
        }

        return nearest;
    }

    // --- Objets et allies -------------------------------------------------------------------------

    private static void CollectItems(
        UtilityContext context,
        List<PlanningObject> locations,
        List<PlanningObject> objects,
        List<string> facts)
    {
        // « objective » est ce que la mission envoie chercher, quel que soit l'objet reel : le
        // survivant sait qu'il existe avant meme de le voir. C'est son emplacement qu'il ignore, pas
        // son existence — d'ou une position tant qu'il ne l'a pas repere.
        var spot = locations.FirstOrDefault(l => l.Name.Equals("objective_spot", StringComparison.OrdinalIgnoreCase));
        objects.Add(new PlanningObject("objective", PlanningObjectTypes.Item, spot?.Position));

        if (context.KnowsObjective) facts.Add("objective_found");

        if (spot is not null) facts.Add("item_at(objective, objective_spot)");

        CollectInventory(context, objects, facts);
        CollectSpottedItems(context, objects, facts);

        if (!context.Self.CarriesObjective) return;

        facts.Add($"has({PlanningProblem.Agent}, objective)");
        facts.Add("objective_collected");
    }

    /// <summary>
    /// Ce que le survivant porte (livrable 11). Le sac est la seule partie du monde qu'il n'a jamais
    /// besoin d'observer : il sait ce qu'il transporte, ce qu'il a en main, et s'il lui reste de la
    /// place. Ces trois faits suffisent aux actions d'objet.
    /// </summary>
    private static void CollectInventory(UtilityContext context, List<PlanningObject> objects, List<string> facts)
    {
        var inventory = context.Self.Inventory;
        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "objective", "remedy" };

        // « remedy » est a l'objet qui soigne ce que « objective » est a celui de la mission : un nom
        // symbolique que le domaine peut employer sans jamais nommer une trousse. Tant que toute
        // mission ramenait des medicaments, soigner et rapporter parlaient du meme objet ; des qu'une
        // mission va chercher un jerrican, les confondre rend le soin impossible.
        if (inventory.HasHealingItem)
        {
            objects.Add(new PlanningObject("remedy", PlanningObjectTypes.Item, context.Self.Position));
            facts.Add($"has({PlanningProblem.Agent}, remedy)");
            facts.Add("usable(remedy)");
        }

        foreach (string itemId in inventory.ItemIds.Order(StringComparer.Ordinal))
        {
            if (!declared.Add(itemId)) continue;

            objects.Add(new PlanningObject(itemId, PlanningObjectTypes.Item, context.Self.Position));
            facts.Add($"has({PlanningProblem.Agent}, {itemId})");

            // Une arme cassee reste dans le sac mais ne sert plus a rien : ne pas la declarer
            // utilisable est ce qui empeche GBFS de batir un plan autour d'une machette ruinee.
            if (inventory.CanAttackWith(itemId)) facts.Add($"usable({itemId})");

            if (inventory.IsEquipped(itemId)) facts.Add($"equipped({itemId})");
        }

        if (inventory.IsFull) facts.Add("inventory_full");
    }

    /// <summary>
    /// Les objets apercus et pas encore ramasses. Un objet qu'on n'a jamais vu n'existe pas pour le
    /// planificateur : c'est la meme regle que pour une porte, et c'est ce qui interdit de planifier
    /// le ramassage de ce qu'on ne fait que supposer.
    /// </summary>
    private static void CollectSpottedItems(UtilityContext context, List<PlanningObject> objects, List<string> facts)
    {
        var knowledge = context.Self.Knowledge;
        float floor = knowledge.Settings.MinimumConfidence;
        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var belief in knowledge.KnownItems.Values
                     .Where(item => item.Confidence > floor)
                     .OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            // L'objet de la mission a deja son objet symbolique « objective » : le declarer deux fois
            // donnerait deux facons de planifier le meme ramassage.
            if (belief.ItemId.Equals(context.Self.ObjectiveItem, StringComparison.OrdinalIgnoreCase)) continue;
            if (context.Self.Inventory.Carries(belief.ItemId)) continue;
            if (!declared.Add(belief.ItemId)) continue;

            string spot = Token("spot", belief.Value);

            objects.Add(new PlanningObject(belief.ItemId, PlanningObjectTypes.Item, belief.Value));
            objects.Add(new PlanningObject(spot, PlanningObjectTypes.Location, belief.Value));

            facts.Add($"known({spot})");
            facts.Add($"item_at({belief.ItemId}, {spot})");
        }
    }

    /// <summary>
    /// Les meubles poussables que le survivant a vus (livrable 11). <c>mass_ok</c> est le seul fait
    /// qui depend d'autre chose que du meuble : il dit qu'il y a assez de bras, ce qui donne au
    /// regroupement une raison mecanique plutot qu'une consigne.
    /// </summary>
    private static void CollectHeavy(UtilityContext context, List<PlanningObject> objects, List<string> facts)
    {
        foreach (var heavy in context.KnownHeavyObjects.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            string place = Token("heavy", heavy.Value);

            objects.Add(new PlanningObject(heavy.Id, PlanningObjectTypes.Heavy, heavy.Value));
            objects.Add(new PlanningObject(place, PlanningObjectTypes.Location, heavy.Value));

            facts.Add($"known({place})");
            facts.Add($"heavy_at({heavy.Id}, {place})");

            bool alone = heavy.Mass > HeavyObjects.SoloMassLimit
                && !context.Allies.Any(ally => ally.Value.ChebyshevDistanceTo(heavy.Value) <= 1);

            if (!alone) facts.Add($"mass_ok({heavy.Id})");
        }
    }

    /// <summary>
    /// Ce que les fouilles passees ont appris. Seule la deception devient un fait : trouver quelque
    /// chose se dit deja par <c>objective_found</c>, mais ne rien trouver n'avait jusqu'ici aucune
    /// traduction symbolique — et c'est pourtant l'information qui empeche de refouiller.
    /// </summary>
    private static void CollectSearches(AgentKnowledge knowledge, List<string> facts)
    {
        foreach (var belief in knowledge.SearchedPlaces.Values.OrderBy(b => b.Place, StringComparer.Ordinal))
        {
            if (!belief.Found) facts.Add($"searched_in_vain({belief.Place})");
        }
    }

    /// <summary>
    /// Les allies dont le survivant connait la position, et ce qu'il sait de leur etat.
    ///
    /// <c>ally_in_danger</c> se deduit de la carte mentale : voir des zombies pres de quelqu'un
    /// suffit a s'inquieter pour lui. <c>ally_wounded</c> et <c>ally_panicking</c>, non — une
    /// blessure ne se lit pas a trente metres. Ces deux faits n'apparaissent que si l'interesse l'a
    /// <b>dit</b>, c'est-a-dire apres un partage de connaissances (livrable 7). C'est ce qui donne a
    /// <c>HealAlly</c> une precondition que seule la rencontre peut rendre vraie, au lieu d'un plan
    /// de soin fonde sur une intuition.
    /// </summary>
    private static void CollectAllies(
        UtilityContext context,
        List<PlanningObject> objects,
        List<string> facts,
        Dictionary<string, float> danger,
        PlannerSettings settings)
    {
        foreach (var ally in context.Allies.OrderBy(a => a.Id, StringComparer.Ordinal))
        {
            objects.Add(new PlanningObject(ally.Id, PlanningObjectTypes.Ally, ally.Value));

            float threat = context.DangerAt(ally.Value);
            danger[ally.Id] = threat;

            if (threat >= settings.DangerThreshold) facts.Add($"ally_in_danger({ally.Id})");
            if (ally.HealthRatio <= settings.AllyWoundedThreshold) facts.Add($"ally_wounded({ally.Id})");
            if (ally.Panic >= settings.AllyPanicThreshold) facts.Add($"ally_panicking({ally.Id})");
        }
    }

    /// <summary>
    /// Les menaces memorisees deviennent des objets adressables, et celles que le survivant croit a
    /// portee de bras deviennent en plus un fait — c'est la precondition du combat du livrable 8.
    ///
    /// Le seuil est celui du survivant (<see cref="SurvivorSettings.MeleeRange"/>) et non un rayon
    /// du planificateur : frapper est une capacite de l'agent, pas une regle de la recherche. Aucune
    /// action ne permet en revanche de <b>rejoindre</b> une menace : on ne planifie pas une chasse au
    /// zombie, on repond a celui qui est deja sur soi.
    /// </summary>
    private static void CollectThreats(UtilityContext context, List<PlanningObject> objects, List<string> facts)
    {
        var knowledge = context.Self.Knowledge;
        int reach = context.Self.Settings.MeleeRange;

        foreach (var belief in knowledge.BelievedEntities(context.Now)
                     .Where(belief => belief.Kind == EntityKind.Zombie)
                     .OrderBy(belief => belief.Id, StringComparer.Ordinal))
        {
            var supposed = knowledge.PredictedPositionOf(belief, context.Now);
            objects.Add(new PlanningObject(belief.Id, PlanningObjectTypes.Threat, supposed));

            int distance = supposed.ChebyshevDistanceTo(context.Self.Position);

            if (distance <= reach) facts.Add($"zombie_adjacent({belief.Id})");

            // Une arme a portee change ce qui est atteignable sans bouger : un revolver met six cases
            // sous le feu, une machette une seule. Le fait n'est pas « je peux tirer » mais « il est
            // a portee de ce que je tiens » — GBFS arbitre ensuite par le cout et le bruit.
            if (distance <= WeaponReach(context.Self, reach)) facts.Add($"zombie_in_range({belief.Id})");
        }
    }

    /// <summary>Portee de l'arme en main, jamais inferieure a celle du bras.</summary>
    private static int WeaponReach(Survivor self, int melee)
    {
        if (self.Inventory.EquippedWeapon is not { } weapon) return melee;
        if (!self.Inventory.CanAttackWith(weapon)) return melee;

        return Math.Max(melee, self.Inventory.RangeOf(weapon));
    }

    // --- Nommage ------------------------------------------------------------------------------

    /// <summary>
    /// Nom d'objet derive d'une cellule : <c>door_7_4</c>. Les coordonnees negatives, que l'editeur
    /// Godot autorise, sont prefixees par <c>m</c> — un nom d'objet ne doit contenir ni signe ni
    /// virgule, il se retrouve tel quel dans les faits.
    /// </summary>
    private static string Token(string prefix, GridPosition cell)
        => $"{prefix}_{Coordinate(cell.X)}_{Coordinate(cell.Y)}";

    private static string Coordinate(int value)
        => value < 0 ? $"m{-value}" : value.ToString(CultureInfo.InvariantCulture);
}
