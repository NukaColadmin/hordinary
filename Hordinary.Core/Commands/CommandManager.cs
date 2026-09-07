using Hordinary.AI.Utility;
using Hordinary.Agents;
using Hordinary.World;

namespace Hordinary.Commands;

/// <summary>Ce qu'une commande a produit, pour le journal et le HUD.</summary>
/// <param name="Command">La commande telle que le joueur l'a donnee.</param>
/// <param name="Accepted">Survivants qui l'ont recue.</param>
/// <param name="Refused">Survivants qui n'ont pas pu la recevoir, et pourquoi.</param>
public readonly record struct CommandResult(
    PlayerCommand Command,
    IReadOnlyList<string> Accepted,
    IReadOnlyList<string> Refused)
{
    public bool Delivered => Accepted.Count > 0;

    public override string ToString()
    {
        string accepted = Accepted.Count == 0 ? "personne" : string.Join(", ", Accepted);
        string refused = Refused.Count == 0 ? string.Empty : $" — {string.Join(" ; ", Refused)}";

        return $"{Command} → {accepted}{refused}";
    }
}

/// <summary>
/// Traduit les commandes du joueur en consignes pour les survivants (specification section A :
/// <c>Commands/CommandManager</c>).
///
/// C'est le seul endroit qui sait qu'un ordre s'adresse a quelqu'un. Trois choses en decoulent, et
/// les trois comptent pour le livrable :
///
/// <list type="bullet">
/// <item>Un ordre est resolu dans la memoire de <b>chacun</b>, pas sur la carte reelle. Demander la
/// pharmacie a quelqu'un qui ignore ou elle est ne produit rien — et c'est ce refus, journalise, qui
/// rend la perception partielle lisible depuis l'interface.</item>
/// <item>Un ordre n'est jamais un pilotage : il pose un <see cref="MissionOrder"/> que la Utility AI
/// met en concurrence avec ses propres objectifs. Un survivant qui fuit une horde ne s'y rendra pas.</item>
/// <item>Une consigne permanente (<see cref="StandingOrders"/>), elle, ne se negocie pas : elle
/// ampute le domaine de planification et la carte de navigation en amont de la decision.</item>
/// </list>
///
/// La classe vit dans le coeur et ne connait pas le moteur : la couche Godot ne fait que fabriquer
/// des <see cref="PlayerCommand"/> depuis le clavier et la souris, et afficher ce qu'on lui rend.
/// </summary>
public sealed class CommandManager
{
    private readonly SurvivorBand _band;
    private readonly GridMap _map;
    private readonly UtilityAiSettings _settings;

    private int _selected;

    public CommandManager(SurvivorBand band, GridMap map, UtilityAiSettings? settings = null)
    {
        _band = band ?? throw new ArgumentNullException(nameof(band));
        _map = map ?? throw new ArgumentNullException(nameof(map));
        _settings = settings ?? UtilityAiSettings.Default;
    }

    /// <summary>Index du survivant sous les yeux du joueur. N'a de sens que si <see cref="HasSelection"/>.</summary>
    public int SelectedIndex => _selected;

    /// <summary>
    /// Vrai tant que le joueur regarde quelqu'un. Faux apres un <see cref="Deselect"/> : le panneau
    /// lateral se ferme, l'anneau de selection s'eteint, et plus aucun ordre n'a de destinataire.
    ///
    /// Ne pas regarder personne est un etat, et non l'absence d'un etat : la partie commence sur le
    /// premier survivant du groupe, et le joueur peut choisir de le lacher.
    /// </summary>
    public bool HasSelection { get; private set; } = true;

    /// <summary>Le survivant selectionne, <c>null</c> si le groupe est vide ou si rien n'est selectionne.</summary>
    public SurvivorAgent? Selected
        => !HasSelection || _band.Agents.Count == 0 ? null : _band.Agents[Wrap(_selected)];

    /// <summary>Vrai quand le joueur s'adresse au groupe par defaut, sans avoir a le repeter.</summary>
    public bool GroupSelected { get; private set; }

    /// <summary>Commandes recues, de la plus ancienne a la plus recente. Alimente le HUD.</summary>
    public List<CommandResult> History { get; } = [];

    /// <summary>Emis a chaque commande traitee, refus compris.</summary>
    public event Action<CommandResult>? Executed;

    /// <summary>Emis quand le point de vue change : la couche moteur rebranche ses overlays.</summary>
    public event Action<SurvivorAgent>? SelectionChanged;

    /// <summary>Emis quand le joueur lache celui qu'il regardait. Le panneau lateral s'y ferme.</summary>
    public event Action? SelectionCleared;

    // --- Selection --------------------------------------------------------------------------

    /// <summary>Change de point de vue par position dans le groupe (c'est la touche Tab).</summary>
    public SurvivorAgent? SelectAt(int index)
    {
        if (_band.Agents.Count == 0) return null;

        _selected = Wrap(index);
        GroupSelected = false;
        HasSelection = true;

        var agent = _band.Agents[_selected];
        SelectionChanged?.Invoke(agent);
        return agent;
    }

    public SurvivorAgent? SelectNext() => SelectAt(_selected + 1);

    /// <summary>
    /// Le joueur ne regarde plus personne — il a clique a cote.
    ///
    /// Le rang reste memorise : Tab reprend la ou l'on en etait plutot que de repartir du premier.
    /// La selection du groupe, elle, tombe : lacher quelqu'un et continuer a commander tout le monde
    /// serait le contraire de ce que le geste veut dire.
    /// </summary>
    /// <returns>Faux si personne n'etait selectionne — rien a annoncer.</returns>
    public bool Deselect()
    {
        if (!HasSelection && !GroupSelected) return false;

        HasSelection = false;
        GroupSelected = false;

        SelectionCleared?.Invoke();
        return true;
    }

    // --- Execution --------------------------------------------------------------------------

    public CommandResult Execute(PlayerCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var accepted = new List<string>();
        var refused = new List<string>();

        switch (command.Kind)
        {
            case PlayerCommandKind.SelectSurvivor:
                SelectSurvivor(command.SurvivorId, accepted, refused);
                break;

            case PlayerCommandKind.SelectGroup:
                GroupSelected = true;
                foreach (var agent in _band.Standing) accepted.Add(agent.Survivor.Name);
                break;

            default:
                Dispatch(command, accepted, refused);
                break;
        }

        var result = new CommandResult(command, accepted, refused);

        History.Add(result);
        Executed?.Invoke(result);
        return result;
    }

    private void SelectSurvivor(string? id, List<string> accepted, List<string> refused)
    {
        if (_band.Find(id) is not { } agent)
        {
            refused.Add($"aucun survivant « {id} » dans le groupe");
            return;
        }

        SelectAt(_band.Agents.ToList().IndexOf(agent));
        accepted.Add(agent.Survivor.Name);
    }

    /// <summary>
    /// Qui recoit la commande : le groupe si le joueur l'a demande — ou s'il a selectionne le groupe
    /// une fois pour toutes —, le survivant affiche sinon. Les survivants hors de combat sont exclus :
    /// ils n'ont plus d'objectif a mettre en concurrence.
    /// </summary>
    private IEnumerable<SurvivorAgent> Addressed(PlayerCommand command)
    {
        if (command.ToAll || GroupSelected) return _band.Standing;

        return Selected is { Survivor.Alive: true } agent ? [agent] : [];
    }

    private void Dispatch(PlayerCommand command, List<string> accepted, List<string> refused)
    {
        foreach (var agent in Addressed(command).ToList())
        {
            if (Apply(command, agent) is { } why) refused.Add($"{agent.Survivor.Name} : {why}");
            else accepted.Add(agent.Survivor.Name);
        }

        if (accepted.Count == 0 && refused.Count == 0) refused.Add("aucun survivant en etat de recevoir l'ordre");
    }

    /// <summary>Applique une commande a un survivant. Renvoie la raison du refus, ou <c>null</c>.</summary>
    private string? Apply(PlayerCommand command, SurvivorAgent agent) => command.Kind switch
    {
        PlayerCommandKind.ForbidNoise => Quiet(agent, forbid: true),
        PlayerCommandKind.AllowNoise => Quiet(agent, forbid: false),
        PlayerCommandKind.ForbidZone => Fence(agent, command.Zone, forbid: true),
        PlayerCommandKind.AllowZone => Fence(agent, command.Zone, forbid: false),

        PlayerCommandKind.ExploreArea => ExploreArea(agent, command.Zone),
        PlayerCommandKind.FetchObjective => SendToObjective(agent),
        PlayerCommandKind.ReachExtraction => SendToZone(agent, ZoneKind.Extraction, "rejoindre l'extraction"),
        PlayerCommandKind.Regroup => Regroup(agent),

        _ => $"commande {command.Kind} non traitee",
    };

    // --- Objectifs --------------------------------------------------------------------------

    /// <summary>
    /// Pose l'ordre du joueur. Il n'ecrase pas la decision : il entre dans les scores par
    /// <c>player_priority</c>, et donne le droit d'y repenser tout de suite plutot qu'a la prochaine
    /// evaluation periodique.
    /// </summary>
    private string? Give(SurvivorAgent agent, string label, GridPosition destination)
    {
        agent.Survivor.Mission = new MissionOrder(label, destination, _settings.PlayerOrderPriority);
        agent.Utility.Request($"nouvel ordre — {label}");
        return null;
    }

    /// <summary>
    /// « Allez chercher ce qu'on est venu chercher. » L'ordre ne nomme aucun lieu : il lit celui que la
    /// mission a donne au survivant. C'est ce qui fait que la meme touche envoie a la pharmacie, a
    /// l'entrepot ou au hangar selon la mission en cours, sans une ligne de code par lieu.
    /// </summary>
    private string? SendToObjective(SurvivorAgent agent)
    {
        if (agent.Survivor.ObjectiveZone is not { } site) return "n'a recu aucun lieu d'objectif";

        const string Label = "recuperer l'objet de la mission";

        if (agent.Survivor.Knowledge.GetLocation(site.Name) is { } known)
            return Give(agent, $"{Label} ({known.Name})", known.Value);

        return SendToZone(agent, site.Kind, Label);
    }

    /// <summary>
    /// Un lieu de mission est cherche dans la memoire du survivant, jamais sur la carte reelle : on ne
    /// peut pas envoyer quelqu'un a un endroit dont il ignore l'existence.
    /// </summary>
    private string? SendToZone(SurvivorAgent agent, ZoneKind kind, string label)
    {
        if (agent.Survivor.Knowledge.GetLocation(kind) is not { } location)
            return $"ne connait aucun lieu de type {kind}";

        return Give(agent, $"{label} ({location.Name})", location.Value);
    }

    /// <summary>
    /// « Va voir la-bas. » Une zone designee devient une destination ; sans zone, le survivant part
    /// vers la frontiere que son propre calcul d'exploration a retenue — ce qui repartit le groupe au
    /// lieu de l'envoyer au meme endroit (livrable 7).
    /// </summary>
    private string? ExploreArea(SurvivorAgent agent, MapZone? zone)
    {
        if (zone is null)
        {
            if (agent.Context?.BestFrontier is not { } frontier) return "plus rien a decouvrir d'accessible";
            return Give(agent, "explorer la frontiere", frontier.Cell);
        }

        if (_map.FindWalkableIn(zone) is not { } cell) return $"{zone.Name} n'offre aucune case praticable";

        return Give(agent, $"explorer {zone.Name}", cell);
    }

    /// <summary>
    /// « Regroupez-vous. » Le point de ralliement est celui que le survivant se represente, a partir
    /// des allies dont il croit connaitre la position : deux survivants qui ne se sont pas parles
    /// depuis longtemps ne convergent pas vers le meme point, et c'est voulu.
    /// </summary>
    private string? Regroup(SurvivorAgent agent)
    {
        if (agent.Context?.Locate("group") is not { } centre) return "ne sait ou est le groupe";

        return Give(agent, "se regrouper", centre);
    }

    // --- Consignes permanentes ----------------------------------------------------------------

    /// <summary>
    /// Le bruit interdit ne devient pas une penalite de score : les actions au-dela du seuil sont
    /// retirees du domaine avant la recherche. Un plan qui passait par la vitre n'existe plus, et
    /// GBFS en trouve un autre — ou echoue, ce que le mode debug affiche.
    /// </summary>
    private static string? Quiet(SurvivorAgent agent, bool forbid)
    {
        if (forbid) agent.Survivor.Orders.ForbidNoise();
        else agent.Survivor.Orders.AllowNoise();

        agent.Replan(forbid ? "actions bruyantes interdites" : "actions bruyantes autorisees");
        return null;
    }

    /// <summary>
    /// Une zone interdite est retiree de la carte de navigation de ce survivant-la. A* n'y passe
    /// plus du tout : ce n'est pas un detour couteux, c'est un mur — la consigne du joueur est le
    /// seul element du jeu qui ne se negocie pas.
    /// </summary>
    private static string? Fence(SurvivorAgent agent, MapZone? zone, bool forbid)
    {
        if (zone is null) return "aucune zone designee";

        bool changed = forbid ? agent.Survivor.Orders.Forbid(zone) : agent.Survivor.Orders.Allow(zone);
        if (!changed) return forbid ? $"{zone.Name} etait deja interdite" : $"{zone.Name} n'etait pas interdite";

        agent.Replan(forbid ? $"{zone.Name} interdite" : $"{zone.Name} de nouveau autorisee");
        return null;
    }

    private int Wrap(int index)
    {
        int count = _band.Agents.Count;
        return count == 0 ? 0 : ((index % count) + count) % count;
    }
}
