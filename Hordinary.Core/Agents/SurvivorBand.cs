using Hordinary.AI.Planning;
using Hordinary.AI.Utility;
using Hordinary.Config;
using Hordinary.World;

namespace Hordinary.Agents;

/// <summary>
/// Les trois survivants du MVP et ce qui les relie (specification, livrable 7).
///
/// Le groupe n'est pas un esprit collectif : il n'a ni memoire, ni objectif, ni plan. Chaque agent
/// decide seul, avec sa seule carte mentale. Ce que le groupe porte, c'est le canal par lequel ils
/// se parlent — et la cadence a laquelle ils le font, une fois par seconde (document A).
///
/// Cette distinction est le livrable en entier : deux survivants dans le meme groupe mais a vingt
/// cases l'un de l'autre en savent exactement autant que deux inconnus. La coordination est une
/// consequence de la communication, jamais un privilege de l'appartenance.
/// </summary>
public sealed class SurvivorBand
{
    private readonly List<SurvivorAgent> _agents = [];
    private readonly List<Survivor> _survivors = [];
    private readonly WorldState _world;
    private readonly GameConfig _config;
    private readonly ItemCatalogue _catalogue;

    public IReadOnlyList<SurvivorAgent> Agents => _agents;

    /// <summary>Les corps, tels que le partage et les actions d'entraide les manipulent.</summary>
    public IReadOnlyList<Survivor> Survivors => _survivors;

    public KnowledgeSharing Sharing { get; }

    public PerceptionSystem Perception { get; }

    /// <summary>
    /// La horde, pour les actions de combat du livrable 8. Elle se branche apres coup : le groupe
    /// existe avant elle, et un groupe sans horde reste parfaitement jouable — les actions
    /// offensives echouent alors, ce qui est la reponse honnete a « frapper quoi ? ».
    /// </summary>
    public IHorde? Horde { get; set; }

    /// <summary>
    /// Survivants encore sur le terrain, c'est-a-dire capables d'agir. On en sort d'une seule facon :
    /// etre mis hors de combat. L'agent cesse alors de percevoir, de decider et de recevoir des
    /// ordres.
    /// </summary>
    public IEnumerable<SurvivorAgent> Standing
        => _agents.Where(agent => agent.Survivor.Alive);

    /// <summary>
    /// Interrupteur du mode debug (livrable 8), lu dans <c>debug.panic_enabled</c> : a <c>false</c>,
    /// plus personne ne panique et la panique deja accumulee retombe a zero.
    ///
    /// C'est le seul reglage que le groupe impose a tous ses membres. Il n'appartient pas au jeu
    /// mais a son observation, et n'aurait aucun sens survivant par survivant : comparer un
    /// survivant qui panique a un autre qui ne peut pas ne renseigne sur rien.
    /// </summary>
    public bool PanicEnabled
    {
        get => _panicEnabled;
        set
        {
            _panicEnabled = value;
            foreach (var agent in _agents) agent.Survivor.PanicEnabled = value;
        }
    }

    private bool _panicEnabled = true;

    public SurvivorBand(
        WorldState world,
        GameConfig config,
        PerceptionSystem? perception = null,
        ItemCatalogue? catalogue = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _catalogue = catalogue ?? ItemCatalogue.Empty;

        Perception = perception ?? new PerceptionSystem(config.Perception);
        Sharing = new KnowledgeSharing(config.Knowledge);
    }

    /// <summary>
    /// Enrole un survivant depuis sa fiche. La liste des corps est passee <b>par reference</b> a son
    /// moteur d'execution : un survivant enrole apres lui pourra donc quand meme le soigner, sans que
    /// l'ordre du fichier de configuration devienne une regle du jeu.
    /// </summary>
    public SurvivorAgent Enlist(
        SurvivorProfile profile,
        GridPosition spawn,
        UtilityGoalSet goals,
        PlanningDomain domain)
    {
        var agent = new SurvivorAgent(
            profile, spawn, _world, _config, goals, domain, Perception, _survivors, Sharing,
            new DeferredHorde(() => Horde), _catalogue);

        // Un survivant enrole apres une bascule du mode debug doit naitre dans le meme regime que
        // les autres, sinon le groupe panique a moitie.
        agent.Survivor.PanicEnabled = _panicEnabled;

        _agents.Add(agent);
        _survivors.Add(agent.Survivor);
        return agent;
    }

    public SurvivorAgent? Find(string? id)
        => id is null
            ? null
            : _agents.FirstOrDefault(agent => agent.Survivor.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Ce que le joueur dit a tout le groupe avant le depart : les lieux de la mission, pas leur
    /// contenu. C'est une connaissance transmise (<see cref="KnowledgeSource.Inference"/>), pas une
    /// observation — le terrain reste entierement a decouvrir.
    /// </summary>
    public void Brief(params ZoneKind[] kinds)
    {
        foreach (var kind in kinds)
        {
            if (_world.Grid.GetZone(kind) is { } zone) Brief(zone);
        }
    }

    /// <summary>
    /// Annonce un lieu precis plutot qu'une categorie.
    ///
    /// La carte livree compte deux batiments, et le generateur peut en poser davantage : « allez
    /// fouiller le batiment » ne designe alors rien. C'est par ici que l'objectif d'une mission est
    /// annonce, parce que lui seul sait lequel des deux on vise.
    /// </summary>
    public void Brief(MapZone zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var target = _world.Grid.FindWalkableIn(zone) ?? zone.Center;

        foreach (var agent in _agents)
        {
            agent.Survivor.Knowledge.RememberLocation(
                zone.Name, zone.Kind, target, 0d, KnowledgeSource.Inference);
        }
    }

    /// <summary>
    /// La ceinture d'un quartier genere (livrable 13) : chacun sait des le depart qu'on ne va pas
    /// par la.
    ///
    /// Ce n'est pas un confort d'affichage, c'est une condition de bon fonctionnement. Un explorateur
    /// de frontieres appelle « frontiere » une case connue qui touche une case inconnue ; sans cette
    /// mise au courant, le bord du monde en offrirait un millier, toutes infranchissables, et le
    /// groupe passerait la mission a longer un brouillard qu'il redecouvre. Un survivant depose la
    /// voit le brouillard : lui dire ou le monde s'arrete ne lui apprend rien qu'un regard ne donne.
    ///
    /// La source est donc <see cref="KnowledgeSource.Inference"/>, comme le briefing : c'est su, pas
    /// observe. La carte dessinee n'a pas de ceinture — l'appel n'y marque alors aucune case.
    /// </summary>
    /// <returns>Le nombre de cases de ceinture portees a la connaissance du groupe.</returns>
    public int KnowBorder()
    {
        int marked = 0;

        foreach (var cell in _world.Grid.AllPositions())
        {
            if (_world.Grid.GetCell(cell) != CellType.Border) continue;
            marked++;

            foreach (var agent in _agents)
            {
                agent.Survivor.Knowledge.ObserveCell(
                    cell, CellType.Border, indoor: false, 0d, KnowledgeSource.Inference);
            }
        }

        return marked;
    }

    // --- Boucle ---------------------------------------------------------------------------------

    /// <summary>Fait glisser les corps. A appeler a chaque frame.</summary>
    public void Move(float delta)
    {
        foreach (var agent in Standing) agent.Move(delta);
    }

    /// <summary>Une passe de perception pour tout le monde.</summary>
    public void Perceive(double elapsed = 0d, Random? rng = null)
    {
        foreach (var agent in Standing) agent.Perceive(elapsed, rng);
    }

    /// <summary>Une passe de decision pour tout le monde.</summary>
    public void Think(float delta)
    {
        foreach (var agent in Standing) agent.Think(delta);
    }

    /// <summary>
    /// La conversation du groupe, cadencee a une fois par seconde. Elle vient <b>apres</b> la
    /// perception et <b>avant</b> la decision : un survivant doit pouvoir agir dans la seconde sur ce
    /// qu'on vient de lui apprendre, sinon le partage n'aurait d'effet visible qu'au tour suivant.
    /// </summary>
    public int ShareKnowledge(double now) => Sharing.Advance(OnField(), now);

    /// <summary>Les corps encore dehors. Rentre au camp, on ne renseigne plus personne.</summary>
    private List<Survivor> OnField() => Standing.Select(agent => agent.Survivor).ToList();

    /// <summary>Une passe complete, pour les boucles qui n'ont pas besoin de cadences separees.</summary>
    public void Advance(float delta)
    {
        Move(delta);
        Perceive(delta);
        ShareKnowledge(_world.Time);
        Think(delta);
    }
}
