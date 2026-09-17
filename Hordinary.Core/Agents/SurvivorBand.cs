using Hordinary.AI.Planning.Pddl;
using Hordinary.AI.Instructions;
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
        _source ??= domain;

        var agent = new SurvivorAgent(
            profile, spawn, _world, _config, goals, Repertoire(domain)!, Perception, _survivors, Sharing,
            new DeferredHorde(() => Horde), _catalogue);

        // Un survivant enrole apres une bascule du mode debug doit naitre dans le meme regime que
        // les autres, sinon le groupe panique a moitie.
        agent.Survivor.PanicEnabled = _panicEnabled;
        agent.Instructions = Instructions;

        _agents.Add(agent);
        _survivors.Add(agent.Survivor);
        return agent;
    }

    /// <summary>
    /// Les contraintes actives du joueur (livrable 15). Elles sont <b>collectives</b> : une
    /// instruction s'applique aux trois survivants, parce qu'un domaine compile par agent
    /// demanderait trois compilations, trois portillons 3 et trois journaux — possible, hors
    /// perimetre, et documente comme tel.
    ///
    /// <para>
    /// Les poser ne dit rien a personne : elles ont deja reecrit le domaine. C'est la difference
    /// entre une instruction tactique et un ordre du joueur — l'ordre arrive a un survivant qui
    /// decide quoi en faire, la contrainte change ce qu'il est possible de decider.
    /// </para>
    /// </summary>
    public TacticalInstructions? Instructions { get; private set; }

    /// <summary>
    /// Donne au groupe les contraintes de la mission, et leur domaine avec. Rappelee a chaque fois
    /// que le joueur en ajoute ou en retire une : le domaine reparti de la source, les survivants en
    /// heritent, et chacun replanifie — sans quoi ils continueraient de jouer le plan que la consigne
    /// vient d'invalider.
    /// </summary>
    public void Instruct(TacticalInstructions? instructions, string reason = "consigne tactique")
    {
        Instructions = instructions;

        // Tout retirer rend le repertoire d'origine, et non le dernier domaine reecrit : c'est la
        // promesse du livrable — sans instruction, le jeu est bit a bit celui d'avant.
        var domain = Repertoire(_source);

        foreach (var agent in _agents)
        {
            agent.Instructions = instructions;
            if (domain is not null) agent.Rebase(domain, reason);
        }
    }

    /// <summary>Le repertoire tel que les contraintes actives le laissent, ou celui d'origine.</summary>
    private PlanningDomain? Repertoire(PlanningDomain? domain)
        => Instructions is { Active.Count: > 0 } instructions ? instructions.Compiled.ToPlanningDomain() : domain;

    /// <summary>
    /// Le repertoire d'origine, celui du fichier livre. Il est retenu au premier enrolement parce que
    /// c'est le seul endroit ou le groupe le voit passer — et parce que retirer une contrainte doit
    /// pouvoir y revenir.
    /// </summary>
    private PlanningDomain? _source;

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

    /// <summary>
    /// Le rappel : « rentrez, le van part. » C'est ce que le temps imparti d'une mission declenche
    /// quand il est ecoule (<see cref="Game.MissionSettings.TimeLimit"/>).
    ///
    /// Le rappel emprunte le chemin d'un ordre du joueur et pas un autre : un <see cref="MissionOrder"/>
    /// que la Utility AI met en concurrence avec le reste, avec une priorite plus forte. Il n'y a donc
    /// aucun pilotage ici — un survivant poursuivi commence par echapper a ce qui le poursuit, puis
    /// rentre. Faire du rappel un teleportage, ou une trajectoire imposee, retirerait au groupe la
    /// seule chose que le projet cherche a montrer.
    ///
    /// Le lieu est cherche dans la memoire de <b>chacun</b>, comme tout ordre : on ne renvoie pas
    /// quelqu'un a un endroit dont il ignore l'existence. En pratique tout le monde connait le van —
    /// c'est de la qu'on part, et le briefing le nomme — mais la regle ne souffre pas d'exception.
    /// </summary>
    /// <param name="where">Le type de zone ou rentrer : le camp de la mission.</param>
    /// <param name="label">Ce que l'ordre dit, tel que le journal et le HUD l'afficheront.</param>
    /// <param name="priority">Poids de l'ordre dans les scores d'utilite.</param>
    /// <returns>Ceux qui ont recu le rappel.</returns>
    public IReadOnlyList<SurvivorAgent> Recall(ZoneKind where, string label, float priority)
    {
        var called = new List<SurvivorAgent>();

        foreach (var agent in Standing)
        {
            if (agent.Survivor.Knowledge.GetLocation(where) is not { } home) continue;

            agent.Survivor.Mission = new MissionOrder(label, home.Value, priority);

            // Reconsiderer, et pas seulement demander a reflechir : le rappel doit pouvoir deloger
            // un objectif installe, ce qu'une simple demande ne fait pas — elle laisse au titulaire
            // le benefice de l'hysteresis. Voir UtilitySelector.Reconsider.
            agent.Utility.Reconsider(label);
            called.Add(agent);
        }

        return called;
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

    /// <summary>
    /// Une passe de decision pour tout le monde — chacun a son tour, et chacun annonce avant que le
    /// suivant ne decide.
    ///
    /// <para>
    /// L'annonce intercalee est ce qui repartit le groupe. Sans elle, les trois decident depuis le
    /// meme instantane d'intentions — celui qu'a laisse la derniere passe de partage — et, n'ayant
    /// aucune autre raison de differer, choisissent la meme frontiere. La penalite de duplication de
    /// <c>FrontierExplorer</c> ne peut rien y faire : elle s'applique identiquement aux trois, donc
    /// les repousse ensemble au lieu de les separer. Elle n'a d'effet separateur que si le deuxieme
    /// a decider sait ce que le premier vient de choisir.
    /// </para>
    ///
    /// <para>
    /// Jusqu'au livrable 15, la dissymetrie venait d'ailleurs : chaque survivant avait sa
    /// personnalite, donc ses propres couts A* (<c>NavigationProfile.FromCaution</c>) et ses propres
    /// scores d'utilite. L'epuration l'a retiree — une personnalite qui varie la tactique appartient
    /// au joueur — et a decouvert que la repartition en dependait, alors que
    /// <c>config/utility_goals.json</c> l'attribue depuis toujours a la seule penalite de
    /// duplication. C'est desormais le cas.
    /// </para>
    /// </summary>
    public void Think(float delta)
    {
        var standing = Standing.ToList();
        var survivors = standing.Select(agent => agent.Survivor).ToList();

        foreach (var agent in standing)
        {
            agent.Think(delta);
            Sharing.Announce(agent.Survivor, survivors);
        }
    }

    /// <summary>
    /// La conversation du groupe, cadencee a une fois par seconde. Elle vient <b>apres</b> la
    /// perception et <b>avant</b> la decision : un survivant doit pouvoir agir dans la seconde sur ce
    /// qu'on vient de lui apprendre, sinon le partage n'aurait d'effet visible qu'au tour suivant.
    /// </summary>
    public int ShareKnowledge(double now) => Sharing.Advance(OnField(), now);

    /// <summary>Les corps encore dehors. Rentre au camp, on ne renseigne plus personne.</summary>
    private List<Survivor> OnField() => Standing.Select(agent => agent.Survivor).ToList();

    /// <summary>
    /// Une passe complete, pour les boucles qui n'ont pas besoin de cadences separees.
    /// </summary>
    /// <param name="rng">
    /// Porte l'erreur de localisation des bruits. Sans lui, <c>NoiseSystem.Localize</c> retombe sur
    /// <see cref="Random.Shared"/> et deux parcours de la meme partie divergent des qu'un bruit est
    /// entendu de loin. En passer un de graine connue rend la partie rejouable a l'identique — ce
    /// dont a besoin tout ce qui capture ou compare des deroules.
    /// </param>
    public void Advance(float delta, Random? rng = null)
    {
        Move(delta);
        Perceive(delta, rng);
        ShareKnowledge(_world.Time);
        Think(delta);
    }
}
