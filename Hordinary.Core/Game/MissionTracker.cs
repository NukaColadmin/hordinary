using Hordinary.Agents;
using Hordinary.World;

namespace Hordinary.Game;

/// <summary>Ou en est la partie (specification section 6).</summary>
public enum MissionOutcome
{
    /// <summary>Ni gagnee ni perdue : la partie continue.</summary>
    Running = 0,

    /// <summary>Les medicaments sont revenus au camp, et le groupe avec.</summary>
    Victory,

    /// <summary>Plus personne pour agir, ou plus de medicaments a rapporter.</summary>
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
/// Ferme la boucle de jeu (specification section 6, livrable 8) : elle regarde le monde et dit ou en
/// est la mission.
///
/// Elle distingue deux moments que le prototype confondait, et c'est tout l'objet de cette classe :
///
/// <list type="number">
/// <item><b>La mission est remplie</b> — la caisse est revenue au camp avec quelqu'un. C'est un
/// evenement (<see cref="Accomplished"/>), pas une fin : le jeu continue. Ce qui se declenche alors,
/// c'est le rappel (<see cref="MissionRecall"/>) — celui qui a rapporte la caisse repart chercher
/// les autres pour leur dire de rentrer.</item>
/// <item><b>La partie est finie</b> — il ne reste plus personne sur le terrain. Soit tout le monde
/// est rentre, et c'est une victoire ; soit tout le monde est tombe, et c'en est une autre.</item>
/// </list>
///
/// Une mission commence et finit au camp : la zone de retour est celle que declare la
/// <see cref="MissionDefinition"/>, et c'est aussi celle d'ou le groupe est parti. « Sortir » et
/// « rentrer » designent donc le meme endroit, ce qui est la lecture qui donne un sens au mot
/// « mission ».
///
/// <b>Defaite</b> : plus aucun survivant capable d'agir alors que la mission n'etait pas remplie, ou
/// plus aucun medicament nulle part — derniere boite consommee pour soigner un blesse, par exemple.
/// C'est une fin de partie que le jeu peut reellement produire, et non une clause de style :
/// <c>HealSelf</c> et <c>HealAlly</c> consomment la caisse que la mission demandait de rapporter.
///
/// La classe ne decide de rien et ne modifie rien : elle observe. C'est ce qui permet de la derouler
/// en test sans moteur, et de l'appeler a la cadence qu'on veut sans changer le resultat.
/// </summary>
public sealed class MissionTracker
{
    private readonly WorldState _world;
    private readonly SurvivorBand _band;

    public MissionTracker(WorldState world, SurvivorBand band, MissionDefinition? mission = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _band = band ?? throw new ArgumentNullException(nameof(band));
        Mission = mission ?? new MissionDefinition { Id = "mission", Name = "Mission" };
    }

    /// <summary>Ce qui a ete demande : quoi rapporter, et ou le rapporter.</summary>
    public MissionDefinition Mission { get; }

    /// <summary>Verdict courant. Une fois la partie finie, il ne change plus.</summary>
    public MissionVerdict Verdict { get; private set; }
        = new(MissionOutcome.Running, "mission en cours", 0d);

    public MissionOutcome Outcome => Verdict.Outcome;

    public bool IsOver => Verdict.IsOver;

    /// <summary>
    /// Vrai des que la caisse est revenue au camp avec quelqu'un. La partie, elle, continue : c'est
    /// le rappel qui commence, pas le generique.
    /// </summary>
    public bool Done { get; private set; }

    /// <summary>Date de jeu a laquelle la mission a ete remplie.</summary>
    public double DoneAt { get; private set; }

    /// <summary>Celui qui a rapporte la caisse. C'est lui qui ira prevenir les autres.</summary>
    public Survivor? Courier { get; private set; }

    /// <summary>Emis une seule fois, quand la mission est remplie. La partie n'est pas finie.</summary>
    public event Action<Survivor>? Accomplished;

    /// <summary>Emis une seule fois, au moment ou la partie bascule.</summary>
    public event Action<MissionVerdict>? Finished;

    /// <summary>
    /// Une passe d'observation. L'accomplissement est cherche avant la fin de partie : un dernier
    /// survivant qui rentre la caisse a la main et tombe sur le seuil a rempli sa mission.
    /// </summary>
    public MissionVerdict Evaluate(double now)
    {
        Deliver(now);

        if (IsOver) return Verdict;

        var verdict = Judge(now);
        if (!verdict.IsOver) return Verdict;

        Verdict = verdict;
        Finished?.Invoke(verdict);
        return verdict;
    }

    /// <summary>
    /// La mission est-elle remplie ? Une fois pour toutes : la caisse peut repartir dans un sac, le
    /// travail a ete fait.
    /// </summary>
    private void Deliver(double now)
    {
        if (Done) return;
        if (Homecoming().FirstOrDefault() is not { } courier) return;

        Done = true;
        DoneAt = now;
        Courier = courier;
        Accomplished?.Invoke(courier);
    }

    private MissionVerdict Judge(double now)
    {
        // Tant qu'il reste quelqu'un dehors, rien n'est joue : la mission peut etre remplie et un
        // survivant toujours en train de courir vers le camp a l'autre bout de la carte.
        if (_band.Standing.Any()) return Running(now);

        var home = _band.Home.ToList();

        if (Done && home.Count > 0)
        {
            string who = string.Join(", ", home.Select(agent => agent.Survivor.Name));
            return new MissionVerdict(
                MissionOutcome.Victory,
                $"les medicaments sont au camp, {who} {(home.Count > 1 ? "sont rentres" : "est rentre")}",
                now);
        }

        if (home.Count > 0)
        {
            return new MissionVerdict(
                MissionOutcome.Defeat, "le groupe est rentre au camp les mains vides", now);
        }

        return new MissionVerdict(MissionOutcome.Defeat, "plus aucun survivant capable d'agir", now);
    }

    /// <summary>
    /// La partie continue — sauf si l'objet de la mission a disparu du jeu, auquel cas plus rien ne
    /// peut la remplir. La question ne se pose plus une fois la caisse rentree : ce qui compte alors
    /// est deja au camp.
    /// </summary>
    private MissionVerdict Running(double now)
    {
        if (!Done && !SuppliesExist())
        {
            return new MissionVerdict(
                MissionOutcome.Defeat, "les medicaments sont definitivement hors d'atteinte", now);
        }

        return new MissionVerdict(MissionOutcome.Running, "mission en cours", now);
    }

    // --- Conditions --------------------------------------------------------------------------

    /// <summary>
    /// Les survivants rentres au camp avec ce que la mission demandait. Il en faut au moins un dans
    /// la zone <b>et</b> la caisse avec lui : c'est elle qui doit franchir la ligne, pas seulement
    /// quelqu'un.
    /// </summary>
    private IEnumerable<Survivor> Homecoming()
    {
        if (Mission.CampIn(_world.Grid) is not { } camp) yield break;

        foreach (var survivor in _band.Survivors)
        {
            if (!survivor.Alive) continue;
            if (!camp.Contains(survivor.Position)) continue;
            if (!survivor.Carries(Mission.Item)) continue;

            yield return survivor;
        }
    }

    /// <summary>
    /// Reste-t-il de quoi remplir la mission quelque part ? Posee sur le monde reel et non sur une
    /// carte mentale : c'est la seule question du jeu a laquelle la verite repond, parce qu'elle ne
    /// decide du comportement de personne — elle ne fait qu'arreter la partie.
    /// </summary>
    private bool SuppliesExist()
        => _world.Items.Values.Any(item => item.Kind == Mission.Item)
        || _band.Survivors.Any(survivor => survivor.Carries(Mission.Item));

    /// <summary>Qui porte la caisse, pour le HUD. Vide tant que personne ne l'a ramassee.</summary>
    public IEnumerable<Survivor> Carriers
        => _band.Survivors.Where(survivor => survivor.Carries(Mission.Item));
}
