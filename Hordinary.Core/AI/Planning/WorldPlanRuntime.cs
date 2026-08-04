using Hordinary.AI.Navigation;
using Hordinary.Agents;
using Hordinary.World;

namespace Hordinary.AI.Planning;

/// <summary>
/// Realise dans le monde ce que les verbes primitifs demandent (specification, livrable 5 :
/// « Executer les actions du plan »).
///
/// Il n'y a volontairement pas une methode par action : <c>OpenDoor</c>, <c>ForceDoor</c>,
/// <c>SearchRoom</c>, <c>HealSelf</c> et <c>ShareKnowledge</c> passent tous par <see cref="SetFlag"/>,
/// avec un fait different. Ajouter une action au jeu reste donc une modification de
/// <c>config/planner_domain.json</c>.
///
/// La classe vit dans le coeur, sans dependance au moteur : la couche Godot ne fait que brancher le
/// journal et afficher le resultat. C'est ce qui permet de derouler une mission complete en test.
///
/// Une action peut echouer ici, et c'est voulu : forcer une porte qui n'est plus verrouillee, fouiller
/// une pharmacie vide, ramasser un objet absent. Chaque <c>false</c> renvoye devient un echec d'action,
/// donc une replanification (livrable 6).
/// </summary>
public sealed class WorldPlanRuntime : IPlanRuntime
{
    private readonly Survivor _survivor;
    private readonly WorldState _world;
    private readonly PathFollower _follower;
    private readonly AStar _astar;
    private readonly SurvivorSettings _settings;
    private readonly IReadOnlyList<Survivor> _band;
    private readonly KnowledgeSharing _sharing;
    private readonly IHorde? _horde;

    private bool _blocked;

    /// <param name="band">
    /// Les autres survivants, pour les actions qui portent sur quelqu'un — soigner, rassurer,
    /// partager. C'est la seule facon d'agir sur un allie : sans lui, <c>HealAlly</c> echoue au lieu
    /// de soigner dans le vide.
    /// </param>
    /// <param name="horde">
    /// La horde, pour les actions de combat (livrable 8). Sans elle, frapper echoue au lieu de
    /// frapper dans le vide — c'est le meme parti que <paramref name="band"/> pour le soin.
    /// </param>
    public WorldPlanRuntime(
        Survivor survivor,
        WorldState world,
        PathFollower follower,
        AStar astar,
        SurvivorSettings? settings = null,
        IReadOnlyList<Survivor>? band = null,
        KnowledgeSharing? sharing = null,
        IHorde? horde = null)
    {
        _survivor = survivor ?? throw new ArgumentNullException(nameof(survivor));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _follower = follower ?? throw new ArgumentNullException(nameof(follower));
        _astar = astar ?? throw new ArgumentNullException(nameof(astar));
        _settings = settings ?? SurvivorSettings.Default;
        _band = band ?? [];
        _sharing = sharing ?? new KnowledgeSharing();
        _horde = horde;
    }

    /// <summary>Cellule reellement occupee par le corps du survivant, pas celle que le plan supposait.</summary>
    public GridPosition AgentCell => _follower.CurrentCell;

    /// <summary>Derniere recherche A* lancee par le plan. Alimente l'overlay de navigation.</summary>
    public PathResult? LastPath { get; private set; }

    public GridPosition? LastDestination { get; private set; }

    /// <summary>Emis quand une action a change le monde : la perception doit repasser.</summary>
    public event Action<string>? WorldChanged;

    /// <summary>Journal des decisions de l'IA, cote execution.</summary>
    public event Action<string>? Logged;

    /// <summary>
    /// Signale que le suiveur de chemin a bute sur une cellule devenue infranchissable — c'est
    /// <see cref="PathFollower.Blocked"/> qui le sait. L'executeur le decouvrira au prochain
    /// <see cref="PollMove"/> et fera echouer l'action de deplacement.
    /// </summary>
    public void NotifyBlocked() => _blocked = true;

    // --- Deplacement ----------------------------------------------------------------------------

    public bool BeginMove(GridPosition destination)
    {
        _blocked = false;
        LastDestination = destination;

        // A* travaille sur la carte mentale : un plan ne peut emprunter qu'un itineraire que le
        // survivant croit praticable.
        var path = _astar.FindPath(AgentCell, destination);
        LastPath = path;

        if (!path.Found)
        {
            Log($"aucun itineraire vers {destination} : {path}");
            return false;
        }

        _follower.SetPath(path.Cells);
        return true;
    }

    /// <summary>
    /// Arrete le survivant la ou il est. Un plan abandonne en pleine rue ne doit pas laisser derriere
    /// lui un corps qui finit tranquillement l'itineraire d'un plan qui n'existe plus.
    /// </summary>
    public void CancelMove()
    {
        _blocked = false;
        _follower.Clear();
    }

    public MoveState PollMove(GridPosition destination)
    {
        if (_blocked) return MoveState.Blocked;
        if (AgentCell == destination) return MoveState.Arrived;
        if (!_follower.IsFinished) return MoveState.Moving;

        // Le chemin s'acheve ailleurs qu'a destination : elle n'etait pas ou le plan la croyait.
        return MoveState.Blocked;
    }

    // --- Objets ---------------------------------------------------------------------------------

    public bool Take(string item)
    {
        if (!TryParseItem(item, out var kind)) return false;

        var found = _world.Items.Values
            .Where(candidate => candidate.Kind == kind && IsWithinReach(candidate.Position))
            .OrderBy(candidate => candidate.Position.ChebyshevDistanceTo(AgentCell))
            .FirstOrDefault();

        if (found is null)
        {
            Log($"{_survivor.Name} cherche {item} en {AgentCell} et n'y trouve rien");
            return false;
        }

        _world.RemoveItem(found.Id);
        _survivor.AddToInventory(kind);
        Log($"{_survivor.Name} ramasse {found.Id}");

        WorldChanged?.Invoke($"{found.Id} ramasse");
        return true;
    }

    public bool Put(string item, GridPosition where)
    {
        if (!TryParseItem(item, out var kind)) return false;
        if (!_survivor.RemoveFromInventory(kind)) return false;

        string id = $"{kind}-{_survivor.Id}-{_world.Time:0.###}".ToLowerInvariant();
        _world.SetItem(id, kind, where);
        Log($"{_survivor.Name} depose {item} en {where}");

        WorldChanged?.Invoke($"{item} depose en {where}");
        return true;
    }

    // --- Effets sur le monde ---------------------------------------------------------------------

    /// <summary>
    /// Traduit un effet symbolique en changement du monde. Le predicat suffit a savoir quoi faire :
    /// une nouvelle action de configuration peut donc reutiliser un effet existant sans code.
    /// </summary>
    public bool SetFlag(string fact, bool value, GridPosition? where) => Fact.PredicateOf(fact) switch
    {
        "door_open" => SetDoor(where, value),
        "window_broken" => BreakWindow(where),
        "medicine_found" => SearchForMedicine(),
        "healed" => Heal(Subject(fact)),
        "calmed" => Calm(Subject(fact)),
        "shared_with" => ShareWith(Subject(fact)),
        "pushed_back" => PushBack(Subject(fact)),
        // Fouiller, observer : rien a changer dans le monde, la perception fait le reste.
        _ => true,
    };

    /// <summary>
    /// Sur qui porte un fait de la forme <c>healed(claire)</c>. Un predicat nu ou sans argument
    /// designe l'agent lui-meme : <c>HealSelf</c> pose <c>healed(agent)</c>.
    /// </summary>
    private static string Subject(string fact)
        => Fact.ArgumentsOf(fact) is [var subject, ..] ? subject : PlanningProblem.Agent;

    /// <summary>
    /// « Effectuer une attaque simple » (livrable 8). Le combat du MVP tient en trois lignes, et
    /// c'est voulu : ce prototype demontre une architecture de decision, pas un systeme de duel.
    ///
    /// L'action echoue si la menace n'est plus la ou le survivant la croyait. C'est le cas le plus
    /// interessant : le plan a ete construit sur une croyance, le monde a bouge, et l'echec devient
    /// une replanification (livrable 6) — exactement comme une porte trouvee verrouillee.
    /// </summary>
    public bool Damage(string target, float amount)
    {
        if (Reach(target) is not { } zombie) return false;

        bool downed = zombie.TakeDamage(amount);

        Log(downed
            ? $"{_survivor.Name} met {zombie.Id} a terre"
            : $"{_survivor.Name} frappe {zombie.Id} : {zombie.Health:0}/{zombie.MaxHealth:0} points de vie");

        if (!downed) return true;

        _horde?.Remove(zombie.Id);

        // Il l'a vu tomber : ce n'est pas un oubli, c'est un dementi. Sans cela il continuerait a
        // contourner une menace qu'il vient lui-meme de mettre a terre.
        _survivor.Knowledge.ForgetEntityAt(zombie.Id, _world.Time);

        WorldChanged?.Invoke($"{zombie.Id} mis a terre");
        return true;
    }

    /// <summary>
    /// « Repousser un zombie » (livrable 8) : le contraire d'une attaque. Cela ne l'entame pas, cela
    /// le fait reculer de quelques cases — le temps de partir. C'est la reponse d'un survivant qui
    /// veut se degager, pas gagner.
    ///
    /// Le recul suit la direction qui l'eloigne du survivant, en s'arretant au premier obstacle : on
    /// ne pousse pas quelqu'un a travers un mur.
    /// </summary>
    private bool PushBack(string target)
    {
        if (Reach(target) is not { } zombie) return false;

        var landing = zombie.Position;
        int dx = Math.Sign(zombie.Position.X - AgentCell.X);
        int dy = Math.Sign(zombie.Position.Y - AgentCell.Y);

        // Un zombie exactement sur la case du survivant n'a pas de direction de fuite : on le pousse
        // vers la premiere case libre venue plutot que de ne rien faire.
        if (dx == 0 && dy == 0) (dx, dy) = (1, 0);

        for (int step = 0; step < _settings.PushBackDistance; step++)
        {
            var next = new GridPosition(landing.X + dx, landing.Y + dy);
            if (!_world.Grid.IsWalkable(next)) break;

            landing = next;
        }

        if (landing == zombie.Position)
        {
            Log($"{_survivor.Name} pousse {zombie.Id} : il est acule, rien ne bouge");
            return false;
        }

        zombie.PlaceAt(landing);
        zombie.LoseTrack();
        _world.SetEntity(zombie.Id, EntityKind.Zombie, landing);

        Log($"{_survivor.Name} repousse {zombie.Id} vers {landing}");
        return true;
    }

    /// <summary>
    /// Le zombie designe, s'il est reellement a portee de bras. La croyance a servi a batir le plan ;
    /// c'est la position reelle qui decide si le coup porte.
    /// </summary>
    private Zombie? Reach(string target)
    {
        if (_horde is null)
        {
            Log($"{_survivor.Name} frappe {target}, mais aucune horde n'est en jeu");
            return null;
        }

        if (_horde.Find(target) is not { Alive: true } zombie)
        {
            Log($"{_survivor.Name} vise {target} : il n'y a plus rien a frapper");
            return null;
        }

        if (zombie.Position.ChebyshevDistanceTo(AgentCell) > _settings.MeleeRange)
        {
            Log($"{_survivor.Name} vise {target} en {zombie.Position} : hors de portee depuis {AgentCell}");
            return null;
        }

        return zombie;
    }

    /// <summary>
    /// Emet le bruit d'une action du plan. <paramref name="label"/> vient de la configuration du
    /// domaine : c'est ce qui fait qu'une porte forcee s'inscrit au journal comme telle, au lieu du
    /// libelle generique qu'un niveau sonore nu ne peut pas nommer.
    /// </summary>
    public void EmitNoise(float level, GridPosition where, string? label = null)
    {
        bool named = Enum.TryParse(label, ignoreCase: true, out NoiseAction action);

        // « Peu bruyante » decrit une facon de se deplacer, pas un pouvoir sur la matiere : la
        // discretion du profil ne s'applique qu'aux pas. Une vitre brisee par Alice fait le bruit
        // d'une vitre brisee, et le bareme de la section 18.1 reste celui de l'action. Ce qu'un
        // survivant fait mieux ou moins bruyamment qu'un autre sur une action precise se dit dans
        // ses competences (config/survivors.json), pas ici.
        float heard = named && action is NoiseAction.Walk or NoiseAction.Run
            ? _survivor.Profile.NoiseOf(level)
            : level;

        if (heard <= 0f) return;

        var noise = named
            ? _world.Noises.EmitRaw(where, heard, _world.Time, _survivor.Id, action)
            : _world.Noises.EmitRaw(where, heard, _world.Time, _survivor.Id);

        if (noise is not null) Log($"bruit : {noise}");
    }

    // --- Details ---------------------------------------------------------------------------------

    private bool SetDoor(GridPosition? where, bool open)
    {
        if (where is not { } cell || !_world.Grid.GetCell(cell).IsDoor()) return false;

        var before = _world.Grid.GetCell(cell);
        var after = open ? CellType.DoorOpen : CellType.DoorClosed;
        if (before == after) return true;

        _world.Grid.SetCell(cell, after);
        _survivor.Knowledge.ObserveCell(cell, after, _world.Grid.IsIndoor(cell), _world.Time);

        Log($"porte en {cell} : {before} -> {after}");
        WorldChanged?.Invoke($"porte {cell} {after}");
        return true;
    }

    /// <summary>Une vitre brisee laisse un passage : la cellule devient du sol.</summary>
    private bool BreakWindow(GridPosition? where)
    {
        if (where is not { } cell || _world.Grid.GetCell(cell) != CellType.Window) return false;

        _world.Grid.SetCell(cell, CellType.Floor);
        _survivor.Knowledge.ObserveCell(cell, CellType.Floor, _world.Grid.IsIndoor(cell), _world.Time);

        Log($"vitre brisee en {cell}");
        WorldChanged?.Invoke($"vitre brisee en {cell}");
        return true;
    }

    /// <summary>
    /// Fouiller une piece, c'est y trouver — ou non — ce qu'on esperait. L'action echoue quand les
    /// medicaments ne sont pas la : c'est le declencheur « un objet attendu est absent » du livrable 6.
    ///
    /// Le resultat entre dans la memoire du survivant, y compris — surtout — quand il est negatif.
    /// Sans cela, la replanification qui suit l'echec reconstruit un plan depuis une carte mentale
    /// qui a oublie la deception, et redemande la meme fouille indefiniment.
    /// </summary>
    private bool SearchForMedicine()
    {
        var found = _world.Items.Values
            .Where(item => item.Kind == ItemKind.Medicine)
            .Where(item => IsWithinReach(item.Position))
            .OrderBy(item => item.Position.ChebyshevDistanceTo(AgentCell))
            .FirstOrDefault();

        string? place = PlaceHere();
        if (place is not null) _survivor.Knowledge.RememberSearch(place, found is not null, _world.Time);

        if (found is null)
        {
            Log($"{_survivor.Name} fouille {place ?? AgentCell.ToString()} : aucun medicament");
            return false;
        }

        _survivor.Knowledge.RememberItem(found.Id, found.Kind, found.Position, _world.Time);
        Log($"{_survivor.Name} repere {found.Id} en {found.Position}");
        return true;
    }

    /// <summary>
    /// Nom symbolique du lieu ou se tient le survivant, s'il en est un. C'est le meme vocabulaire
    /// que celui du planificateur — <c>pharmacy</c>, <c>extraction</c> — pour qu'une fouille
    /// memorisee ici redevienne un fait la-bas.
    /// </summary>
    private string? PlaceHere()
        => _world.Grid.ZonesAt(AgentCell)
            .Select(zone => SymbolicStateBuilder.PlaceNameOf(zone.Kind))
            .FirstOrDefault(name => name is not null);

    /// <summary>
    /// Ce qu'on peut fouiller ou ramasser depuis la ou l'on est : ce qui est a portee immediate, et —
    /// quand on est a l'interieur — ce qui se trouve n'importe ou dans le meme local.
    ///
    /// C'est la contrepartie de la granularite du plan : <c>at(agent, pharmacy)</c> ne designe pas une
    /// cellule mais un lieu, et une pharmacie fait dix cases de long. Fouiller une piece, c'est en
    /// faire le tour et prendre ce qu'il y a sur les etageres, pas tendre le bras depuis la porte. En
    /// revanche cela ne dit jamais rien de ce qui se trouve dehors.
    /// </summary>
    private bool IsWithinReach(GridPosition cell)
    {
        if (cell.ChebyshevDistanceTo(AgentCell) <= _settings.SearchRadius) return true;

        var grid = _world.Grid;
        if (!grid.IsIndoor(AgentCell) || !grid.IsIndoor(cell)) return false;

        return grid.ZonesAt(AgentCell).Any(zone => zone.Contains(cell));
    }

    /// <summary>
    /// Soigner, soi-meme ou quelqu'un d'autre. Une seule methode pour <c>HealSelf</c> et
    /// <c>HealAlly</c> : c'est le sujet du fait qui les distingue, pas deux branches de code.
    ///
    /// Le montant vient de la fiche du soignant, pas de celle du blesse — c'est ce qui fait de Claire
    /// une secouriste : la meme boite de medicaments rend davantage entre ses mains.
    /// </summary>
    private bool Heal(string subject)
    {
        if (Resolve(subject) is not { } patient) return false;
        if (!patient.Alive) return false;
        if (!_survivor.RemoveFromInventory(ItemKind.Medicine)) return false;

        patient.Heal(_survivor.Settings.HealAmount);

        Log(ReferenceEquals(patient, _survivor)
            ? $"{_survivor.Name} se soigne : sante {patient.Health:0}"
            : $"{_survivor.Name} soigne {patient.Name} : sante {patient.Health:0}");

        return true;
    }

    /// <summary>
    /// Rassurer un allie : l'action speciale de Claire. Elle ne coute aucun objet et ne change rien
    /// au monde — seulement l'etat d'esprit de quelqu'un, ce qui suffit a lui faire lacher la fuite
    /// pour reprendre la mission.
    /// </summary>
    private bool Calm(string subject)
    {
        if (Resolve(subject) is not { } listener) return false;
        if (!listener.Alive || ReferenceEquals(listener, _survivor)) return false;
        if (!_sharing.CanTalk(_survivor, listener)) return false;

        listener.Calm(_survivor.Settings.CalmAmount);
        Log($"{_survivor.Name} rassure {listener.Name} : panique {listener.Panic:P0}");
        return true;
    }

    /// <summary>
    /// L'action <c>ShareKnowledge</c> du domaine : dire a un allie ce qu'on a vu. Elle echoue s'il
    /// n'est plus a portee de voix — le plan avait ete construit sur une position qui a bouge, et
    /// c'est a la replanification de rattraper.
    /// </summary>
    private bool ShareWith(string subject)
    {
        if (Resolve(subject) is not { } listener) return false;
        if (!_sharing.CanTalk(_survivor, listener)) return false;

        int learned = _sharing.Talk(_survivor, listener, _world.Time);
        Log($"{_survivor.Name} informe {listener.Name} : {learned} croyance(s) transmise(s)");
        return true;
    }

    /// <summary>
    /// Qui designe un sujet de fait : <c>agent</c>, c'est soi-meme ; un identifiant, c'est un allie
    /// du groupe. Un allie absent du groupe ne renvoie rien, et l'action echoue plutot que de reussir
    /// dans le vide.
    /// </summary>
    private Survivor? Resolve(string subject)
    {
        if (subject.Equals(PlanningProblem.Agent, StringComparison.OrdinalIgnoreCase)) return _survivor;
        if (subject.Equals(_survivor.Id, StringComparison.OrdinalIgnoreCase)) return _survivor;

        var found = _band.FirstOrDefault(other => other.Id.Equals(subject, StringComparison.OrdinalIgnoreCase));
        if (found is null) Log($"{_survivor.Name} ne trouve aucun allie nomme « {subject} »");

        return found;
    }

    private void Log(string message) => Logged?.Invoke(message);

    private static bool TryParseItem(string name, out ItemKind kind)
        => Enum.TryParse(name, ignoreCase: true, out kind);
}
