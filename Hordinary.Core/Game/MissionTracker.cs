using Hordinary.Agents;
using Hordinary.World;

namespace Hordinary.Game;

/// <summary>Ou en est la partie (specification section 6).</summary>
public enum MissionOutcome
{
    /// <summary>Ni gagnee ni perdue : la partie continue.</summary>
    Running = 0,

    /// <summary>La caisse est revenue au van, et le groupe avec elle.</summary>
    Victory,

    /// <summary>
    /// Plus personne pour agir, plus de medicaments a rapporter, ou le temps ecoule et le groupe
    /// rentre les mains vides.
    /// </summary>
    Defeat,
}

/// <summary>Pourquoi la partie s'est terminee. Le HUD l'affiche tel quel.</summary>
public readonly record struct MissionVerdict(MissionOutcome Outcome, string Reason, double At)
{
    public bool IsOver => Outcome != MissionOutcome.Running;

    public override string ToString() => Outcome switch
    {
        MissionOutcome.Victory => $"Victoire — {Reason}",
        MissionOutcome.Defeat => $"Defaite — {Reason}",
        _ => Reason,
    };
}

/// <summary>
/// Ferme la boucle de jeu (specification section 6, livrable 8) : elle regarde le monde, tient
/// l'horloge de la mission et dit ou en est la partie.
///
/// <b>Victoire</b> : la caisse est revenue au van <b>et tout le groupe avec elle</b>. Le retour d'un
/// seul ne conclut rien : on ne laisse personne derriere, et un survivant qui rentre le premier
/// attend les autres au van. C'est ce qui donne au temps imparti son interet — sans cette regle, la
/// mission se terminait a la seconde ou le plus rapide franchissait le seuil, et le sort des deux
/// autres n'avait aucune consequence.
///
/// <b>Defaite</b> : plus aucun survivant capable d'agir, plus aucun medicament nulle part — derniere
/// boite consommee pour soigner un blesse, par exemple —, ou le groupe rentre les mains vides apres
/// le rappel. Ce sont des fins que le jeu produit reellement, et non des clauses de style :
/// <c>HealSelf</c> et <c>UseItem</c> consomment la caisse que la mission demandait de rapporter.
///
/// <b>Le temps imparti</b> (<see cref="MissionSettings.TimeLimit"/>) : une expedition ne dure pas
/// une soiree. Le delai ecoule, le van annonce le depart et tout le monde rentre — c'est le
/// <see cref="SurvivorBand.Recall"/>, un ordre de plus dans les scores d'utilite, pas un pilotage.
///
/// C'est le seul geste que cette classe pose sur le monde ; pour le reste, elle observe. Le rappel
/// lui revient parce que c'est elle qui tient l'horloge : le confier a l'appelant en ferait deux
/// copies — une dans <c>GameRoot</c>, une dans le harnais de test — qui divergeraient le jour ou
/// l'une des deux changerait.
/// </summary>
public sealed class MissionTracker
{
    /// <summary>Ce que le rappel dit aux survivants. Le HUD et le journal l'affichent tel quel.</summary>
    public const string RecallOrder = "rentrer au van — temps ecoule";

    private readonly WorldState _world;
    private readonly SurvivorBand _band;

    /// <summary>Ceux qui ont quitte le van, puis ceux qui y sont revenus, par identifiant.</summary>
    private readonly HashSet<string> _departed = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _returned = new(StringComparer.OrdinalIgnoreCase);

    public MissionTracker(
        WorldState world,
        SurvivorBand band,
        MissionDefinition? mission = null,
        MissionSettings? settings = null,
        MissionObjectiveTracker? objectives = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _band = band ?? throw new ArgumentNullException(nameof(band));
        Mission = mission ?? new MissionDefinition { Id = "mission", Name = "Mission" };
        Settings = settings ?? new MissionSettings();
        Objectives = objectives;

        // La mission a le dernier mot sur son propre temps : une mission longue ou sans limite se
        // declare dans config/missions.json, comme sa horde.
        TimeLimit = Mission.TimeLimit ?? Settings.TimeLimit;
    }

    /// <summary>Ce qui a ete demande : quoi rapporter, et ou le rapporter.</summary>
    public MissionDefinition Mission { get; }

    /// <summary>Reglages de mission, section <c>mission</c> de <c>config/game_config.json</c>.</summary>
    public MissionSettings Settings { get; }

    /// <summary>
    /// Les compteurs d'une mission generee (livrable 16), ou <c>null</c> pour une mission ecrite a
    /// la main. Leur absence est ce qui laisse le MVP intact : sans eux, la condition de victoire
    /// reste mot pour mot celle du livrable 8 — rapporter <see cref="MissionDefinition.Item"/> au
    /// van, et y rentrer au complet.
    /// </summary>
    public MissionObjectiveTracker? Objectives { get; }

    /// <summary>Temps imparti, en secondes. Zero ou moins : la mission n'est pas chronometree.</summary>
    public double TimeLimit { get; }

    /// <summary>Vrai si la mission est chronometree.</summary>
    public bool Timed => TimeLimit > 0d;

    /// <summary>
    /// Ce qu'il reste avant le rappel, en secondes ; <c>null</c> si la mission n'est pas
    /// chronometree. C'est ce que le HUD affiche, et la seule facon pour le joueur de savoir s'il a
    /// encore le temps d'aller voir un batiment de plus.
    /// </summary>
    public double? TimeLeft(double now) => Timed ? Math.Max(0d, TimeLimit - now) : null;

    /// <summary>Vrai une fois le temps ecoule et le groupe rappele. Ne redescend jamais.</summary>
    public bool Recalled { get; private set; }

    /// <summary>Verdict courant. Une fois la partie finie, il ne change plus.</summary>
    public MissionVerdict Verdict { get; private set; }
        = new(MissionOutcome.Running, "mission en cours", 0d);

    public MissionOutcome Outcome => Verdict.Outcome;

    public bool IsOver => Verdict.IsOver;

    /// <summary>Celui qui a rapporte la caisse, une fois la mission remplie.</summary>
    public Survivor? Courier { get; private set; }

    /// <summary>Emis une seule fois, au moment ou la partie bascule.</summary>
    public event Action<MissionVerdict>? Finished;

    /// <summary>
    /// Emis une seule fois, quand le temps est ecoule : le van annonce le depart. La liste est celle
    /// des survivants qui ont recu le rappel.
    /// </summary>
    public event Action<IReadOnlyList<string>>? Recalling;

    /// <summary>Ceux qui sont au van. Alimente le HUD : « rentres : 2 ».</summary>
    public IEnumerable<Survivor> Home => _band.Survivors.Where(IsHome);

    /// <summary>
    /// Ce survivant-la est-il au van ?
    ///
    /// Deux facons de l'etre. Y etre en ce moment, d'abord : celui qui n'a jamais quitte le vehicule
    /// n'a rien a rejoindre, et exiger un retour de lui empecherait la mission de se conclure. S'en
    /// etre ecarte de moins de <see cref="MissionSettings.HomeRadius"/> cases ensuite : un survivant
    /// arrive au van a rempli son ordre, la Utility AI lui redonne la main, et il peut tourner autour
    /// du vehicule en attendant les autres — le declarer « ressorti » a chaque pas de cote
    /// suffirait a repousser la fin de la mission indefiniment.
    ///
    /// Ce qui est retenu est donc un retour <b>revocable</b>, et c'est la tout l'enjeu : tant qu'il
    /// ne l'etait pas, un survivant qui traversait le camp dans les premieres secondes de
    /// l'expedition comptait comme rentre jusqu'a la fin de la partie. Sur la carte livree, ou le
    /// camp jouxte la ruelle, les trois y passaient — et la victoire se declarait a la seconde ou la
    /// caisse etait ramassee, tout le groupe etant encore dans la pharmacie.
    /// </summary>
    public bool IsHome(Survivor survivor)
        => survivor is not null
        && (_returned.Contains(survivor.Id) || AtCamp(survivor));

    /// <summary>
    /// Une passe d'observation. Trois temps, toujours dans cet ordre : qui est parti et qui est
    /// rentre, puis l'horloge — c'est la seule chose que cette classe impose au monde —, puis le
    /// verdict, sur la situation telle que la passe vient de la laisser.
    ///
    /// La victoire est cherchee avant la defaite : un groupe qui rentre au complet, caisse en main,
    /// et dont le dernier tombe sur le seuil a rempli sa mission.
    /// </summary>
    public MissionVerdict Evaluate(double now)
    {
        if (IsOver) return Verdict;

        Track();
        Objectives?.Observe();
        Recall(now);

        var verdict = Judge(now);
        if (!verdict.IsOver) return Verdict;

        Verdict = verdict;
        Finished?.Invoke(verdict);
        return verdict;
    }

    /// <summary>
    /// Qui est sorti, et qui est revenu. Le depart est enregistre avant le retour : au van sans en
    /// etre jamais parti, on n'est pas « rentre », on n'a pas bouge — et sans cette distinction,
    /// tout le groupe serait declare rentre a la premiere seconde, ou la mission n'a pas commence.
    ///
    /// Le retour, lui, s'annule : celui qui s'eloigne de plus de
    /// <see cref="MissionSettings.HomeRadius"/> cases du camp est reparti, quoi qu'il ait fait avant.
    /// </summary>
    private void Track()
    {
        if (Mission.CampIn(_world.Grid) is not { } camp) return;

        foreach (var survivor in _band.Survivors)
        {
            if (!survivor.Alive) continue;

            if (camp.Contains(survivor.Position))
            {
                if (_departed.Contains(survivor.Id)) _returned.Add(survivor.Id);
                continue;
            }

            _departed.Add(survivor.Id);

            if (DistanceTo(camp, survivor.Position) > Settings.HomeRadius) _returned.Remove(survivor.Id);
        }
    }

    /// <summary>
    /// Distance de Chebyshev d'une case au bord d'une zone, nulle a l'interieur. C'est la meme
    /// metrique que les deplacements : le van est a « deux pas », pas a « deux cases en ligne droite ».
    /// </summary>
    private static int DistanceTo(MapZone zone, GridPosition cell)
    {
        int dx = Math.Max(zone.Min.X - cell.X, cell.X - zone.Max.X);
        int dy = Math.Max(zone.Min.Y - cell.Y, cell.Y - zone.Max.Y);

        return Math.Max(0, Math.Max(dx, dy));
    }

    /// <summary>Ce survivant-la est-il dans la zone du van, a cet instant ?</summary>
    private bool AtCamp(Survivor survivor)
        => Mission.CampIn(_world.Grid) is { } camp && camp.Contains(survivor.Position);

    /// <summary>
    /// Le temps imparti. Ecoule, le van annonce le depart et tout le monde rentre — une fois, pas a
    /// chaque passe : l'ordre pose reste en place, et le repeter chaque frame noierait la decision
    /// sous les replanifications.
    /// </summary>
    private void Recall(double now)
    {
        if (Recalled || !Timed || now < TimeLimit) return;

        Recalled = true;

        var called = _band.Recall(Mission.Camp, RecallOrder, Settings.RecallPriority);
        Recalling?.Invoke([.. called.Select(agent => agent.Survivor.Name)]);
    }

    private MissionVerdict Judge(double now)
    {
        bool home = GroupIsHome();

        if (Objectives is not null) return JudgeObjectives(now, home);

        if (home && Courier is null) Courier = Homecoming().FirstOrDefault();

        if (home && Courier is not null)
        {
            return new MissionVerdict(
                MissionOutcome.Victory,
                $"le groupe est rentre au van, {Courier.Name} rapporte {Mission.Item}",
                now);
        }

        // Rentrer bredouille n'est une defaite qu'une fois le temps ecoule : avant le rappel, un
        // groupe au complet au van peut encore repartir, et rien ne dit qu'il a renonce.
        if (home && Recalled)
        {
            return new MissionVerdict(
                MissionOutcome.Defeat, $"le temps est ecoule, le groupe rentre sans {Mission.Item}", now);
        }

        if (!_band.Standing.Any())
        {
            return new MissionVerdict(MissionOutcome.Defeat, "plus aucun survivant capable d'agir", now);
        }

        // L'objet de la mission a disparu du jeu : plus rien ne peut la remplir. La question est
        // posee sur le monde reel et non sur une carte mentale — c'est la seule question du jeu a
        // laquelle la verite repond, parce qu'elle ne decide du comportement de personne.
        if (!SuppliesExist())
        {
            return new MissionVerdict(
                MissionOutcome.Defeat, "les medicaments sont definitivement hors d'atteinte", now);
        }

        return new MissionVerdict(MissionOutcome.Running, "mission en cours", now);
    }

    /// <summary>
    /// Le verdict d'une mission generee (livrable 16). C'est la meme phrase que ci-dessus, avec un
    /// autre sujet : le groupe est rentre <b>et</b> ses exigences sont remplies.
    ///
    /// Ce qui change vraiment tient en un mot : « rempli » n'est plus « quelqu'un porte la caisse »
    /// mais « les compteurs y sont ». Une elimination n'a donc rien a rapporter — le compteur de
    /// morts suffit —, et une recuperation exige que les objets soient encore dans les sacs au
    /// moment du retour, pas seulement qu'ils y aient ete.
    ///
    /// L'ordre est celui de la version manuelle, et pour la meme raison : la victoire est cherchee
    /// avant la defaite, et un groupe qui rentre au complet avec son compte a rempli sa mission
    /// meme si le dernier tombe sur le seuil.
    /// </summary>
    private MissionVerdict JudgeObjectives(double now, bool home)
    {
        var objectives = Objectives!;

        if (home && objectives.Fulfilled)
        {
            // Le porteur n'a de sens que pour une recuperation. Une elimination n'en a pas, et le
            // HUD n'en affiche donc aucun : personne ne « rapporte » huit morts.
            Courier ??= Homecoming().FirstOrDefault();

            return new MissionVerdict(
                MissionOutcome.Victory, $"le groupe est rentre au van — {objectives}", now);
        }

        if (home && Recalled)
        {
            return new MissionVerdict(
                MissionOutcome.Defeat, $"le temps est ecoule, objectif inacheve — {objectives}", now);
        }

        if (!_band.Standing.Any())
        {
            return new MissionVerdict(MissionOutcome.Defeat, "plus aucun survivant capable d'agir", now);
        }

        if (objectives.Impossible)
        {
            return new MissionVerdict(
                MissionOutcome.Defeat, $"l'objectif est devenu hors d'atteinte — {objectives}", now);
        }

        return new MissionVerdict(MissionOutcome.Running, "mission en cours", now);
    }

    // --- Conditions --------------------------------------------------------------------------

    /// <summary>
    /// Tout le monde est-il rentre ? « Tout le monde », ce sont les survivants encore capables
    /// d'agir : on n'attend pas indefiniment celui qui est reste sur le pave, sans quoi une seule
    /// perte empecherait la mission de se conclure.
    ///
    /// Il faut au moins un vivant, et il faut que le groupe soit reellement parti : au premier
    /// instant, tout le monde est au van et personne n'en est encore sorti.
    /// </summary>
    private bool GroupIsHome()
    {
        var standing = _band.Standing.Select(agent => agent.Survivor).ToList();

        return standing.Count > 0 && standing.All(IsHome);
    }

    /// <summary>
    /// Les survivants au van avec ce que la mission demandait. Il en faut un au van <b>et</b> la
    /// caisse avec lui : c'est elle qui doit franchir la ligne, pas seulement quelqu'un.
    /// </summary>
    private IEnumerable<Survivor> Homecoming()
        => _band.Survivors.Where(survivor => survivor.Alive && IsHome(survivor) && survivor.Carries(Mission.Item));

    /// <summary>Reste-t-il de quoi remplir la mission quelque part ?</summary>
    private bool SuppliesExist()
        => _world.Items.Values.Any(item => item.ItemId.Equals(Mission.Item, StringComparison.OrdinalIgnoreCase))
        || _band.Survivors.Any(survivor => survivor.Carries(Mission.Item));

    /// <summary>Qui porte la caisse, pour le HUD. Vide tant que personne ne l'a ramassee.</summary>
    public IEnumerable<Survivor> Carriers
        => _band.Survivors.Where(survivor => survivor.Carries(Mission.Item));
}
