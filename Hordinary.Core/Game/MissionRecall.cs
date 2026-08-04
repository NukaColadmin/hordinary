using Hordinary.Agents;
using Hordinary.Config;
using Hordinary.World;

namespace Hordinary.Game;

/// <summary>
/// Le rappel au camp : ce qui se passe une fois la mission remplie.
///
/// La caisse est revenue au camp, mais les autres ne le savent pas — ils sont a l'autre bout de la
/// carte, occupes a fouiller un entrepot pour rien. Quelqu'un doit aller le leur dire. C'est cela,
/// le rappel : <b>l'agent qui a fini la mission repart chercher les autres</b>, un par un, pour leur
/// annoncer « <c>Mission terminee, rentrez au camp s'il vous plait.</c> »
///
/// Trois choses en decoulent, et les trois tiennent au modele de connaissance du livrable 7 :
///
/// <list type="bullet">
/// <item>Le message se transmet <b>de vive voix</b>, a portee de communication. Il n'existe pas
/// d'annonce globale : un allie qu'on n'a pas rejoint continue sa mission, et c'est correct — il
/// n'a aucun moyen de savoir.</item>
/// <item>Le messager cherche ses allies <b>la ou il croit qu'ils sont</b>. Une position vieille de
/// vingt secondes l'enverra a un endroit qu'ils ont quitte, et il devra recommencer. C'est le prix
/// normal d'un monde ou personne n'a la carte complete.</item>
/// <item>Rentrer n'est pas un pilotage : le rappel pose un <see cref="MissionOrder"/> que la Utility
/// AI met en concurrence avec le reste. Un survivant poursuivi par une horde ne rentre pas tout
/// droit — il fuit d'abord, et rentre apres.</item>
/// </list>
///
/// Un survivant rentre au camp <b>disparait</b> du terrain (<see cref="SurvivorAgent.ComeHome"/>) :
/// il sort du monde reel, cesse de percevoir et de decider. Il n'est pas mort — c'est le meme qui
/// repartira, avec la sante et la motivation qu'il rapporte.
/// </summary>
public sealed class MissionRecall
{
    /// <summary>Le message, mot pour mot. Le journal et le HUD le citent tel quel.</summary>
    public const string Message = "Mission terminee, rentrez au camp s'il vous plait.";

    private readonly SurvivorBand _band;
    private readonly GridMap _map;
    private readonly MissionSettings _settings;
    private readonly HashSet<string> _told = new(StringComparer.OrdinalIgnoreCase);

    public MissionRecall(SurvivorBand band, GridMap map, MissionDefinition mission, MissionSettings? settings = null)
    {
        _band = band ?? throw new ArgumentNullException(nameof(band));
        _map = map ?? throw new ArgumentNullException(nameof(map));
        Mission = mission ?? throw new ArgumentNullException(nameof(mission));
        _settings = settings ?? MissionSettings.Default;

        CampZone = mission.CampIn(_map);
        Camp = CampZone is null ? default : _map.FindWalkableIn(CampZone) ?? CampZone.Center;
    }

    public MissionDefinition Mission { get; }

    /// <summary>Le camp, tel que le monde le connait : une case praticable de la zone de retour.</summary>
    public GridPosition Camp { get; }

    public MapZone? CampZone { get; }

    /// <summary>Vrai une fois la mission remplie et le messager parti prevenir les autres.</summary>
    public bool Started => Messenger is not null;

    /// <summary>Celui qui a rapporte la caisse, et qui va donc porter la nouvelle.</summary>
    public SurvivorAgent? Messenger { get; private set; }

    /// <summary>Identifiants des survivants a qui le message a ete transmis.</summary>
    public IReadOnlyCollection<string> Told => _told;

    /// <summary>Vrai quand plus personne, dehors, n'ignore que la mission est finie.</summary>
    public bool Everyone => _band.Standing.All(agent => agent.Survivor.Recalled);

    /// <summary>Ce que le rappel raconte, ligne a ligne : le journal et le HUD s'y branchent.</summary>
    public event Action<string>? Said;

    /// <summary>Emis une fois par survivant, au moment ou il quitte le terrain.</summary>
    public event Action<Survivor>? CameHome;

    // --- Declenchement ---------------------------------------------------------------------------

    /// <summary>
    /// La mission est remplie : <paramref name="messenger"/> a rapporte la caisse au camp. Il se
    /// rappelle lui-meme — il est deja sur place — puis part chercher les autres.
    /// </summary>
    public bool Begin(SurvivorAgent messenger)
    {
        ArgumentNullException.ThrowIfNull(messenger);

        if (Started) return false;

        Messenger = messenger;
        Tell(messenger, announce: false);

        Said?.Invoke(
            $"{messenger.Survivor.Name} : mission terminee. "
          + "Je vais chercher les autres pour leur dire de rentrer.");

        return true;
    }

    /// <summary>
    /// Un survivant renonce de lui-meme : sa motivation est tombee a zero, ou le joueur le rappelle.
    /// Il rentre sans que personne n'ait eu besoin de le prevenir.
    /// </summary>
    public bool Abandon(SurvivorAgent agent, string reason)
    {
        ArgumentNullException.ThrowIfNull(agent);

        if (!agent.Survivor.Recall(reason)) return false;

        _told.Add(agent.Survivor.Id);
        SendHome(agent, "rentrer au camp");
        Said?.Invoke($"{agent.Survivor.Name} : {reason}");
        return true;
    }

    // --- Boucle ----------------------------------------------------------------------------------

    /// <summary>
    /// Une passe de rappel, a la cadence de la boucle de jeu. Elle fait trois choses, dans cet
    /// ordre : ceux qui craquent renoncent, ceux qui sont arrives rentrent, et le messager continue
    /// sa tournee.
    /// </summary>
    public void Advance(double now)
    {
        if (_settings.AbandonAtZeroSanity) CollectTheExhausted();

        Welcome();

        if (Started) Deliver(now);
    }

    /// <summary>
    /// « Je n'ai plus la tete a ca. » Un survivant sans motivation abandonne la mission de lui-meme
    /// et prend le chemin du camp. C'est ce qui donne un poids a la jauge <c>SANITY</c> : sans cela,
    /// elle ne serait qu'un chiffre de plus sur la fiche.
    /// </summary>
    private void CollectTheExhausted()
    {
        foreach (var agent in _band.Standing.ToList())
        {
            if (agent.Survivor.Motivated || agent.Survivor.Recalled) continue;

            Abandon(agent, "je n'ai plus la tete a ca, je rentre au camp");
        }
    }

    /// <summary>
    /// Tous ceux qui ont recu le message et qui ont atteint le camp le quittent pour de bon.
    ///
    /// Le messager fait exception tant qu'il reste quelqu'un a prevenir : il part <b>du</b> camp, et
    /// sans cette clause il rentrerait a la seconde meme ou il decide d'aller chercher les autres.
    /// </summary>
    private void Welcome()
    {
        bool everyone = Everyone;

        foreach (var agent in _band.Standing.ToList())
        {
            if (!agent.Survivor.Recalled) continue;
            if (!everyone && ReferenceEquals(agent, Messenger)) continue;
            if (!AtCamp(agent.Survivor.Position)) continue;

            if (!agent.ComeHome("rentre au camp")) continue;

            Said?.Invoke($"{agent.Survivor.Name} : je suis rentre au camp.");
            CameHome?.Invoke(agent.Survivor);
        }
    }

    /// <summary>
    /// La tournee du messager : il annonce a tous ceux qui sont a portee de voix, puis se dirige
    /// vers le plus proche de ceux qui restent — ou vers le camp, si tout le monde est prevenu.
    /// </summary>
    private void Deliver(double now)
    {
        if (Messenger is not { Survivor.Returned: false } messenger) return;

        foreach (var agent in _band.Standing.ToList())
        {
            if (agent.Survivor.Recalled) continue;
            if (!_band.Sharing.CanTalk(messenger.Survivor, agent.Survivor)) continue;

            Tell(agent, announce: true);
        }

        Point(messenger, now);
    }

    /// <summary>Ou le messager doit aller : chercher le plus proche qui ignore encore, ou rentrer.</summary>
    private void Point(SurvivorAgent messenger, double now)
    {
        if (NearestUnaware(messenger, now) is not { } target)
        {
            SendHome(messenger, "rentrer au camp");
            return;
        }

        // Reposer le meme ordre a chaque passe relancerait la decision en boucle : on ne le change
        // que lorsque celui qu'on cherche a bouge d'assez pour valoir un nouveau trajet.
        if (messenger.Survivor.Mission is { Destination: { } current } order
            && order.Label.EndsWith(target.Name, StringComparison.Ordinal)
            && current.ChebyshevDistanceTo(target.Cell) <= _settings.RecallReroute)
        {
            return;
        }

        messenger.Survivor.Mission = new MissionOrder(
            $"prevenir {target.Name}", target.Cell, _settings.RecallPriority);

        messenger.Utility.Request($"rappel — prevenir {target.Name}");
        Said?.Invoke($"{messenger.Survivor.Name} part prevenir {target.Name} vers {target.Cell}.");
    }

    /// <summary>
    /// L'allie encore ignorant le plus proche, <b>tel que le messager se le represente</b>. Un allie
    /// dont il n'a aucune idee de la position ne compte pas : on ne part pas chercher quelqu'un
    /// qu'on ne sait pas ou trouver — le messager rentre alors, et l'autre finira par revenir seul,
    /// faute de motivation ou parce qu'il croisera quelqu'un.
    /// </summary>
    private (string Name, GridPosition Cell)? NearestUnaware(SurvivorAgent messenger, double now)
    {
        (string Name, GridPosition Cell)? best = null;
        int shortest = int.MaxValue;

        foreach (var agent in _band.Standing)
        {
            if (agent.Survivor.Recalled) continue;
            if (ReferenceEquals(agent, messenger)) continue;

            if (messenger.Survivor.Knowledge.GetEntity(agent.Survivor.Id) is not { } belief) continue;

            var cell = messenger.Survivor.Knowledge.PredictedPositionOf(belief, now);
            int distance = messenger.Survivor.Position.ChebyshevDistanceTo(cell);

            if (distance >= shortest) continue;

            shortest = distance;
            best = (agent.Survivor.Name, cell);
        }

        return best;
    }

    // --- Transmission ----------------------------------------------------------------------------

    private void Tell(SurvivorAgent agent, bool announce)
    {
        if (!agent.Survivor.Recall(Message)) return;

        _told.Add(agent.Survivor.Id);
        SendHome(agent, "rentrer au camp");

        if (!announce) return;

        Said?.Invoke($"{Messenger?.Survivor.Name} a {agent.Survivor.Name} : « {Message} »");
    }

    /// <summary>
    /// Pose l'ordre de retour. Comme tout ordre, il entre dans les scores par
    /// <c>player_priority</c> : il ne pilote pas le survivant, il pese lourd.
    ///
    /// La destination est celle que <b>le survivant</b> connait, pas celle que le monde sait. Il a
    /// ete briefe sur le camp au depart et il en vient : le cas ou il l'ignore n'existe pas en
    /// pratique, mais le repli sur la case reelle evite un ordre sans destination si un jour il
    /// arrive.
    /// </summary>
    private void SendHome(SurvivorAgent agent, string label)
    {
        var destination = agent.Survivor.Knowledge.GetLocation(Mission.Camp)?.Value ?? Camp;

        agent.Survivor.Mission = new MissionOrder(label, destination, _settings.RecallPriority);
        agent.Utility.Request($"rappel — {label}");
    }

    private bool AtCamp(GridPosition cell)
        => CampZone?.Contains(cell) ?? cell.ChebyshevDistanceTo(Camp) <= _settings.CampRadius;
}
