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
    private readonly ItemCatalogue _catalogue;

    private bool _blocked;

    /// <param name="band">
    /// Les autres survivants, pour les actions qui portent sur quelqu'un — leur parler, depuis que
    /// l'entraide autonome a ete retiree (livrable 15). C'est la seule facon d'agir sur un allie :
    /// sans lui, <c>ShareKnowledge</c> echoue au lieu de parler dans le vide.
    /// </param>
    /// <param name="horde">
    /// La horde, pour les actions de combat (livrable 8). Sans elle, frapper echoue au lieu de
    /// frapper dans le vide — c'est le meme parti que <paramref name="band"/> pour la parole.
    /// </param>
    /// <param name="catalogue">
    /// Les objets livres (livrable 11). C'est lui qui dit ce qu'un objet fait : sans catalogue, une
    /// trousse de soin n'est qu'un identifiant, et soigner echoue au lieu de soigner dans le vide.
    /// </param>
    public WorldPlanRuntime(
        Survivor survivor,
        WorldState world,
        PathFollower follower,
        AStar astar,
        SurvivorSettings? settings = null,
        IReadOnlyList<Survivor>? band = null,
        KnowledgeSharing? sharing = null,
        IHorde? horde = null,
        ItemCatalogue? catalogue = null)
    {
        _survivor = survivor ?? throw new ArgumentNullException(nameof(survivor));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _follower = follower ?? throw new ArgumentNullException(nameof(follower));
        _astar = astar ?? throw new ArgumentNullException(nameof(astar));
        _settings = settings ?? SurvivorSettings.Default;
        _band = band ?? [];
        _sharing = sharing ?? new KnowledgeSharing();
        _horde = horde;
        _catalogue = catalogue ?? ItemCatalogue.Empty;
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
    /// Un coup porte a un zombie, et son resultat. Separe de <see cref="Logged"/> parce que ce n'est
    /// pas un detail d'execution : c'est un fait du monde, de la meme famille que la morsure que le
    /// zombie rend. Les deux meritent le meme canal de journal, sinon le joueur voit ce qui le mord
    /// sans jamais voir ce qu'il abat.
    /// </summary>
    public event Action<string>? Fought;

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

    /// <summary>
    /// L'exemplaire le plus proche que l'agent peut atteindre depuis la piece ou il se tient, ou
    /// <c>null</c> s'il n'y en a aucun. C'est ce que <see cref="Take"/> prendra une fois le survivant
    /// arrive dessus — et c'est donc aussi ou l'executeur doit le mener.
    /// </summary>
    public GridPosition? WhereIs(string item) => Nearest(Resolve(item))?.Position;

    private WorldItem? Nearest(string itemId)
        => _world.Items.Values
            .Where(candidate => candidate.ItemId.Equals(itemId, StringComparison.OrdinalIgnoreCase))
            .Where(candidate => IsWithinReach(candidate.Position))
            .OrderBy(candidate => candidate.Position.ChebyshevDistanceTo(AgentCell))
            .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
            .FirstOrDefault();

    public bool Take(string item)
    {
        string itemId = Resolve(item);

        var found = _world.Items.Values
            .Where(candidate => candidate.ItemId.Equals(itemId, StringComparison.OrdinalIgnoreCase))
            .Where(candidate => IsWithinArmsLength(candidate.Position))
            .OrderBy(candidate => candidate.Position.ChebyshevDistanceTo(AgentCell))
            .FirstOrDefault();

        if (found is null)
        {
            Log($"{_survivor.Name} cherche {item} en {AgentCell} et n'y trouve rien a portee de main");
            return false;
        }

        // Un sac plein refuse un ramassage de plus. L'echec devient une replanification, ce qui est
        // exactement le bon comportement : le plan avait ete bati sans compter les emplacements.
        if (!_survivor.AddToInventory(itemId))
        {
            Log($"{_survivor.Name} ne peut pas prendre {found.Id} : {_survivor.Inventory}");
            return false;
        }

        _world.RemoveItem(found.Id);
        Log($"{_survivor.Name} ramasse {found.Id}");

        WorldChanged?.Invoke($"{found.Id} ramasse");
        return true;
    }

    public bool Put(string item, GridPosition where)
    {
        string itemId = Resolve(item);
        if (!_survivor.RemoveFromInventory(itemId)) return false;

        string id = $"{itemId}-{_survivor.Id}-{_world.Time:0.###}".ToLowerInvariant();
        _world.SetItem(id, itemId, where);
        Log($"{_survivor.Name} depose {item} en {where}");

        WorldChanged?.Invoke($"{item} depose en {where}");
        return true;
    }

    /// <summary>
    /// Traduit le nom d'objet employe par le plan en identifiant du catalogue.
    ///
    /// Le domaine parle de deux objets symboliques et d'aucun objet reel. <c>objective</c> est « ce
    /// qu'on est venu chercher » : c'est la mission qui decide lequel c'est. <c>remedy</c> est « de quoi
    /// soigner » : c'est le catalogue qui decide, en cherchant dans le sac le premier objet qui declare
    /// un effet de soin. Tout autre nom est deja un identifiant de <c>config/items.json</c>, celui
    /// qu'une action <c>PickUpItem</c> a lie.
    /// </summary>
    private string Resolve(string item)
    {
        if (item.Equals(ObjectiveObject, StringComparison.OrdinalIgnoreCase)) return _survivor.ObjectiveItem;
        if (item.Equals(RemedyObject, StringComparison.OrdinalIgnoreCase)) return HealingItem()?.Id ?? item;

        return item;
    }

    /// <summary>Nom, dans le domaine, de l'objet que la mission envoie chercher.</summary>
    public const string ObjectiveObject = "objective";

    /// <summary>Nom, dans le domaine, de ce qui sait soigner — quel que soit l'objet reel.</summary>
    public const string RemedyObject = "remedy";

    // --- Effets sur le monde ---------------------------------------------------------------------

    /// <summary>
    /// Traduit un effet symbolique en changement du monde. Le predicat suffit a savoir quoi faire :
    /// une nouvelle action de configuration peut donc reutiliser un effet existant sans code.
    /// </summary>
    public bool SetFlag(string fact, bool value, GridPosition? where) => Fact.PredicateOf(fact) switch
    {
        "door_open" => SetDoor(where, value),
        "door_barricaded" => Barricade(where),
        "window_broken" => BreakWindow(where),
        "equipped" => Equip(Subject(fact), value),
        "used" => UseItem(Subject(fact)),
        "objective_found" => SearchForObjective(),
        "healed" => Heal(Subject(fact)),
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
        // Un montant nul veut dire « ce que je tiens » : c'est ainsi qu'AttackWithWeapon lit ses
        // degats dans config/weapons.json au lieu de les redire dans le domaine. AttackZombie, elle,
        // declare les siens et frappe a mains nues comme au MVP.
        var weapon = amount > 0f ? null : _survivor.Inventory.EquippedWeapon;

        if (weapon is not null)
        {
            if (!_survivor.Inventory.CanAttackWith(weapon))
            {
                Log($"{_survivor.Name} veut frapper avec {weapon}, mais elle est hors d'usage");
                return false;
            }

            var strike = _survivor.Inventory.StrikeWith(weapon);
            amount = strike.Damage;

            if (Reach(target, _survivor.Inventory.RangeOf(weapon)) is not { } quarry) return false;

            EmitNoise(strike.Noise, quarry.Position);
            _survivor.Inventory.RegisterStrike(weapon);

            return Strike(quarry, amount);
        }

        if (Reach(target) is not { } zombie) return false;

        return Strike(zombie, amount);
    }

    /// <summary>
    /// Le coup lui-meme, une fois su qui l'on frappe et avec quelle force.
    ///
    /// Il ne rend vrai que s'il met le zombie a terre, et c'est ce qui permet de tuer. L'action
    /// declare <c>threat_down(?threat)</c> comme effet : tant que le zombie tient debout, cet effet
    /// n'a pas eu lieu, l'action a donc echoue au sens de l'executeur, et l'echec devient une
    /// replanification — qui reconduit au meme coup, jusqu'a ce qu'il porte.
    ///
    /// L'alternative etait de faire boucler l'executeur sur l'action tant que la cible tient. Elle a
    /// ete ecartee : elle aurait donne au combat une boucle a lui, hors du cycle decision-plan-echec
    /// que tout le reste du jeu emprunte, et un survivant acharne sur un zombie aurait cesse de
    /// reconsiderer la fuite entre deux coups.
    ///
    /// Le bruit, la munition et la durabilite sont deja consommes quand on arrive ici : le coup a
    /// bien ete porte, c'est son resultat qui n'est pas celui annonce.
    /// </summary>
    private bool Strike(Zombie zombie, float amount)
    {
        bool downed = zombie.TakeDamage(amount);

        Fought?.Invoke(downed
            ? $"{_survivor.Name} met {zombie.Id} a terre"
            : $"{_survivor.Name} frappe {zombie.Id} : {zombie.Health:0}/{zombie.MaxHealth:0} points de vie");

        if (!downed) return false;

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
            Fought?.Invoke($"{_survivor.Name} pousse {zombie.Id} : il est acule, rien ne bouge");
            return false;
        }

        zombie.PlaceAt(landing);
        zombie.LoseTrack();
        _world.SetEntity(zombie.Id, EntityKind.Zombie, landing);

        Fought?.Invoke($"{_survivor.Name} repousse {zombie.Id} vers {landing}");
        return true;
    }

    /// <summary>
    /// Le zombie designe, s'il est reellement a portee de bras. La croyance a servi a batir le plan ;
    /// c'est la position reelle qui decide si le coup porte.
    /// </summary>
    private Zombie? Reach(string target, int? range = null)
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

        if (zombie.Position.ChebyshevDistanceTo(AgentCell) > (range ?? _settings.MeleeRange))
        {
            Log($"{_survivor.Name} vise {target} en {zombie.Position} : hors de portee depuis {AgentCell}");
            return null;
        }

        return zombie;
    }

    // --- Meubles et diversions (livrable 11) ------------------------------------------------------

    /// <summary>
    /// Pousse un meuble d'une case. L'action echoue quand la regle s'y oppose — case de depart
    /// mauvaise, destination occupee, ou masse trop forte pour un seul — et la raison part au journal
    /// plutot qu'un simple echec : une replanification qui sait pourquoi vaut mieux qu'une qui
    /// reessaie a l'identique.
    /// </summary>
    public bool Push(string target)
    {
        if (_world.Heavy.Find(target) is not { } heavy)
        {
            Log($"{_survivor.Name} pousse {target}, mais il n'y a rien de tel ici");
            return false;
        }

        var helpers = _band
            .Where(other => other.Alive && !ReferenceEquals(other, _survivor))
            .Select(other => other.Position);

        var origin = heavy.Position;

        // L'empreinte d'avant, relevee avant que le meuble ne bouge : une epave de voiture en occupe
        // huit, et les huit doivent etre rendues au sol.
        var vacated = heavy.Cells.ToList();

        if (_world.Heavy.WhyCannotPush(heavy, AgentCell, _world.Grid, helpers) is { } refusal)
        {
            Log($"{_survivor.Name} ne peut pas pousser {heavy.Id} : {refusal}");
            return false;
        }

        if (_world.Heavy.Push(heavy, AgentCell, _world.Grid, helpers) is not { } landing) return false;

        // Le meuble est un obstacle de la grille : le deplacer, c'est deplacer l'obstacle. La
        // navigation et les lignes de vue n'ont donc rien de nouveau a apprendre. L'ordre compte —
        // on libere d'abord, on ferme ensuite, sinon un meuble qui n'avance que d'une case
        // s'effacerait lui-meme.
        foreach (var cell in vacated) _world.Grid.SetCell(cell, CellType.Floor);

        if (heavy.BlocksMovement || heavy.BlocksSight)
        {
            foreach (var cell in heavy.Cells) _world.Grid.SetCell(cell, CellType.Obstacle);
        }

        EmitNoise(heavy.NoiseOfPush, landing);
        Log($"{_survivor.Name} pousse {heavy.Id} de {origin} vers {landing}");

        TellWitnesses(origin, landing);

        WorldChanged?.Invoke($"{heavy.Id} pousse en {landing}");
        return true;
    }

    /// <summary>
    /// Met a jour la carte mentale des seuls survivants qui ont vu la scene. La contrainte de
    /// perception partielle du livrable 2 vaut aussi pour le decor : un survivant qui revient sur une
    /// case qu'il croit degagee peut se heurter a un meuble qu'un allie a pousse.
    ///
    /// Le pousseur en fait partie de droit — il a les mains dessus.
    /// </summary>
    private void TellWitnesses(params GridPosition[] cells)
    {
        var everyone = _band.Contains(_survivor) ? _band : [.. _band, _survivor];

        foreach (var witness in everyone)
        {
            if (!witness.Alive) continue;

            foreach (var cell in cells)
            {
                bool sees = ReferenceEquals(witness, _survivor)
                    || (cell.ChebyshevDistanceTo(witness.Position) <= witness.VisionRange
                        && LineOfSight.IsVisible(_world.Grid, witness.Position, cell));

                if (!sees) continue;

                witness.Knowledge.ObserveCell(
                    cell, _world.Grid.GetCell(cell), _world.Grid.IsIndoor(cell), _world.Time);
            }
        }
    }

    /// <summary>
    /// Lance quelque chose de bruyant a une case visee, sans s'y trouver. C'est ce qui separe la
    /// diversion de celui qui la provoque : la diversion sur place faisait du bruit sous ses propres
    /// pieds, et attirait donc la horde sur soi.
    ///
    /// Aucun plan ne demande plus cela : <c>ThrowFirecracker</c> a ete retiree du domaine au livrable
    /// 15, en meme temps que l'objectif <c>CreateDistraction</c>. Se sacrifier pour degager un allie
    /// n'est pas un reflexe, c'est une strategie — elle revient au joueur, et le moteur reste pret.
    /// </summary>
    public bool ThrowNoise(string target, float amount)
    {
        if (_survivor.Knowledge.GetLocation(target)?.Value is not { } where
            && !TryParseCell(target, out where))
        {
            Log($"{_survivor.Name} vise « {target} » : il ne sait pas ou c'est");
            return false;
        }

        EmitNoise(amount, where, nameof(NoiseAction.Distraction));
        Log($"{_survivor.Name} cree une diversion en {where} depuis {AgentCell}");

        WorldChanged?.Invoke($"diversion en {where}");
        return true;
    }

    /// <summary>Lit une cible ecrite en coordonnees, <c>"12,7"</c>, quand ce n'est pas un lieu connu.</summary>
    private static bool TryParseCell(string target, out GridPosition cell)
    {
        cell = default;

        var parts = target.Split(',');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], out int x) || !int.TryParse(parts[1], out int y)) return false;

        cell = new GridPosition(x, y);
        return true;
    }

    /// <summary>
    /// Emet le bruit d'une action du plan. <paramref name="label"/> vient de la configuration du
    /// domaine : c'est ce qui fait qu'une porte forcee s'inscrit au journal comme telle, au lieu du
    /// libelle generique qu'un niveau sonore nu ne peut pas nommer.
    /// </summary>
    public void EmitNoise(float level, GridPosition where, string? label = null)
    {
        bool named = Enum.TryParse(label, ignoreCase: true, out NoiseAction action);

        // Le bareme de la section 18.1 vaut pour tout le monde : une vitre brisee fait le bruit d'une
        // vitre brisee, quel que soit celui qui la brise. Ce qui rend une action plus discrete est ce
        // qu'on a en main (config/items.json), jamais qui on est.
        float heard = level;

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

    /// <summary>
    /// Barricader une porte. Il n'y a rien de nouveau a dessiner ni a modeliser : une porte
    /// barricadee est une porte verrouillee, et le verrouillage est deja un etat d'execution que le
    /// jeu sait poser depuis le MVP. Elle ne se rouvre donc plus qu'a l'effraction — pour le groupe
    /// comme pour ce qui le suit, ce qui est tout l'interet de l'operation.
    /// </summary>
    private bool Barricade(GridPosition? where)
    {
        if (where is not { } cell || _world.Grid.GetCell(cell) != CellType.DoorClosed) return false;

        _world.Grid.SetCell(cell, CellType.DoorLocked);
        _survivor.Knowledge.ObserveCell(cell, CellType.DoorLocked, _world.Grid.IsIndoor(cell), _world.Time);

        Log($"{_survivor.Name} barricade la porte en {cell}");
        WorldChanged?.Invoke($"porte barricadee en {cell}");
        return true;
    }

    /// <summary>
    /// Prendre en main, ou reposer. Une arme occupe l'unique emplacement d'arme, un outil s'ajoute
    /// aux autres : c'est l'inventaire qui connait la difference, pas le domaine.
    /// </summary>
    private bool Equip(string itemId, bool value)
    {
        if (!value) return _survivor.Inventory.Unequip(itemId);

        var inventory = _survivor.Inventory;
        return inventory.EquipWeapon(itemId) || inventory.EquipTool(itemId);
    }

    /// <summary>
    /// Se servir d'un objet porte. Ce qu'il fait n'est pas ecrit ici : le catalogue le declare en
    /// verbes primitifs, et cette methode ne fait que les executer dans l'ordre.
    /// </summary>
    private bool UseItem(string itemId)
    {
        if (_catalogue.Find(itemId) is not { } item) return false;
        if (!_survivor.Carries(item.Id)) return false;

        bool consumed = false;

        foreach (var effect in item.Effects)
        {
            switch (effect.Verb)
            {
                case EffectVerb.Heal when effect.Target is "self":
                    _survivor.Heal(effect.Amount > 0f ? effect.Amount : _survivor.Settings.HealAmount);
                    break;

                case EffectVerb.EmitNoise:
                    EmitNoise(effect.Amount, AgentCell, nameof(NoiseAction.Distraction));
                    break;

                case EffectVerb.Consume:
                    consumed = true;
                    break;
            }
        }

        if (consumed) _survivor.RemoveFromInventory(item.Id);

        Log($"{_survivor.Name} utilise {item.Name}{(consumed ? " (consomme)" : string.Empty)}");
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
    /// Fouiller une piece, c'est y trouver — ou non — ce qu'on esperait. L'action echoue quand l'objet
    /// de la mission n'est pas la : c'est le declencheur « un objet attendu est absent » du livrable 6.
    ///
    /// Le resultat entre dans la memoire du survivant, y compris — surtout — quand il est negatif.
    /// Sans cela, la replanification qui suit l'echec reconstruit un plan depuis une carte mentale
    /// qui a oublie la deception, et redemande la meme fouille indefiniment.
    /// </summary>
    private bool SearchForObjective()
    {
        var found = _world.Items.Values
            .Where(item => item.ItemId.Equals(_survivor.ObjectiveItem, StringComparison.OrdinalIgnoreCase))
            .Where(item => IsWithinReach(item.Position))
            .OrderBy(item => item.Position.ChebyshevDistanceTo(AgentCell))
            .FirstOrDefault();

        string? place = PlaceHere();
        if (place is not null) _survivor.Knowledge.RememberSearch(place, found is not null, _world.Time);

        if (found is null)
        {
            Log($"{_survivor.Name} fouille {place ?? AgentCell.ToString()} : rien de ce qu'il cherche");
            return false;
        }

        _survivor.Knowledge.RememberItem(found.Id, found.ItemId, found.Position, _world.Time);
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
            .Select(zone => SymbolicStateBuilder.PlaceNameOf(zone, _survivor))
            .FirstOrDefault(name => name is not null);

    /// <summary>
    /// Ce qu'on peut <b>prendre</b> depuis la ou l'on se tient : ce qui est a portee de bras, et
    /// rien d'autre. Une caisse au fond de la pharmacie demande d'aller la chercher, meme quand on
    /// est deja dans la pharmacie — c'est la difference entre reperer et ramasser.
    ///
    /// Le plan sait s'en accommoder : la fouille a memorise l'emplacement, la replanification en
    /// fait un lieu (<c>objective_spot</c>), et le survivant s'y rend avant de tendre la main.
    /// </summary>
    private bool IsWithinArmsLength(GridPosition cell)
        => cell.ChebyshevDistanceTo(AgentCell) <= _settings.PickUpRadius;

    /// <summary>
    /// Ce qu'on peut <b>fouiller</b> depuis la ou l'on est : ce qui est a portee immediate, et —
    /// quand on est a l'interieur — ce qui se trouve n'importe ou dans le meme local.
    ///
    /// C'est la contrepartie de la granularite du plan : <c>at(agent, pharmacy)</c> ne designe pas une
    /// cellule mais un lieu, et une pharmacie fait dix cases de long. Fouiller une piece, c'est en
    /// faire le tour et regarder ce qu'il y a sur les etageres, pas tendre le bras depuis la porte. En
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
    /// Soigner. Le sujet du fait dit qui : <c>HealSelf</c> pose <c>healed(agent)</c>, et c'est le seul
    /// soin que le domaine sache produire depuis le livrable 15. La methode reste ecrite sur un sujet
    /// quelconque, parce que c'est le fait qui designe le patient et non une branche de code : une
    /// action de soin rendue au joueur par une instruction tactique n'aurait rien a recompiler.
    ///
    /// Le montant vient de la fiche du soignant — c'est ce qui fait de Claire une secouriste : la meme
    /// boite de medicaments rend davantage entre ses mains.
    /// </summary>
    private bool Heal(string subject)
    {
        if (ResolveSurvivor(subject) is not { } patient) return false;
        if (!patient.Alive) return false;

        // Ce qui soigne n'est pas un objet nomme dans le code : c'est le premier objet porte qui
        // declare un effet « heal ». Une trousse, un bandage, ou ce qu'un futur fichier ajoutera.
        if (HealingItem() is not { } remedy) return false;
        if (!_survivor.RemoveFromInventory(remedy.Id)) return false;

        // Un remede a montant nul s'en remet a la fiche du soignant : c'est ce qui fait de Claire
        // une secouriste, la meme trousse rendant davantage entre ses mains. Un bandage, lui, porte
        // son propre montant et vaut la meme chose pour tout le monde.
        float amount = remedy.AmountOf(EffectVerb.Heal) is > 0f and var declared
            ? declared
            : _survivor.Settings.HealAmount;

        patient.Heal(amount);

        Log(ReferenceEquals(patient, _survivor)
            ? $"{_survivor.Name} se soigne avec {remedy.Name} : sante {patient.Health:0}"
            : $"{_survivor.Name} soigne {patient.Name} avec {remedy.Name} : sante {patient.Health:0}");

        return true;
    }

    /// <summary>
    /// Le premier objet porte qui sait soigner — et l'objet de la mission en dernier recours, pas en
    /// premier.
    ///
    /// L'ordre a change et il compte. Tant que toute mission ramenait des medicaments, soigner avec
    /// la caisse qu'on etait venu chercher revenait a perdre la mission pour un bobo ; le survivant
    /// consomme donc d'abord un bandage, et n'entame ce qu'il rapporte que s'il n'a rien d'autre.
    /// </summary>
    private ItemDefinition? HealingItem()
    {
        var carried = _survivor.Inventory.ItemIds
            .Select(_catalogue.Find)
            .Where(item => item is not null && item.Effects.Any(effect => effect.Verb == EffectVerb.Heal))
            .ToList();

        return carried.FirstOrDefault(item =>
                   !item!.Id.Equals(_survivor.ObjectiveItem, StringComparison.OrdinalIgnoreCase))
            ?? carried.FirstOrDefault();
    }

    /// <summary>
    /// L'action <c>ShareKnowledge</c> du domaine : dire a un allie ce qu'on a vu. Elle echoue s'il
    /// n'est plus a portee de voix — le plan avait ete construit sur une position qui a bouge, et
    /// c'est a la replanification de rattraper.
    /// </summary>
    private bool ShareWith(string subject)
    {
        if (ResolveSurvivor(subject) is not { } listener) return false;
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
    private Survivor? ResolveSurvivor(string subject)
    {
        if (subject.Equals(PlanningProblem.Agent, StringComparison.OrdinalIgnoreCase)) return _survivor;
        if (subject.Equals(_survivor.Id, StringComparison.OrdinalIgnoreCase)) return _survivor;

        var found = _band.FirstOrDefault(other => other.Id.Equals(subject, StringComparison.OrdinalIgnoreCase));
        if (found is null) Log($"{_survivor.Name} ne trouve aucun allie nomme « {subject} »");

        return found;
    }

    private void Log(string message) => Logged?.Invoke(message);

}
