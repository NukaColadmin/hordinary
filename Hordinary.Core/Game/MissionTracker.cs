using Hordinary.Agents;
using Hordinary.World;

namespace Hordinary.Game;

/// <summary>Ou en est la partie (specification section 6).</summary>
public enum MissionOutcome
{
    /// <summary>Ni gagnee ni perdue : la partie continue.</summary>
    Running = 0,

    /// <summary>Les medicaments sont revenus au camp.</summary>
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
/// <b>Victoire</b> : la caisse est revenue au camp avec quelqu'un. C'est la fin de la partie, et non
/// un evenement de plus — une mission commence et finit au camp, et la ligne d'arrivee est celle que
/// la <see cref="MissionDefinition"/> declare, celle-la meme d'ou le groupe est parti.
///
/// <b>Defaite</b> : plus aucun survivant capable d'agir, ou plus aucun medicament nulle part —
/// derniere boite consommee pour soigner un blesse, par exemple. C'est une fin de partie que le jeu
/// peut reellement produire, et non une clause de style : <c>HealSelf</c> et <c>HealAlly</c>
/// consomment la caisse que la mission demandait de rapporter.
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

    /// <summary>Celui qui a rapporte la caisse, une fois la mission remplie.</summary>
    public Survivor? Courier { get; private set; }

    /// <summary>Emis une seule fois, au moment ou la partie bascule.</summary>
    public event Action<MissionVerdict>? Finished;

    /// <summary>
    /// Une passe d'observation. La victoire est cherchee avant la defaite : un dernier survivant qui
    /// rentre la caisse a la main et tombe sur le seuil a rempli sa mission.
    /// </summary>
    public MissionVerdict Evaluate(double now)
    {
        if (IsOver) return Verdict;

        var verdict = Judge(now);
        if (!verdict.IsOver) return Verdict;

        Verdict = verdict;
        Finished?.Invoke(verdict);
        return verdict;
    }

    private MissionVerdict Judge(double now)
    {
        if (Homecoming().FirstOrDefault() is { } courier)
        {
            Courier = courier;

            return new MissionVerdict(
                MissionOutcome.Victory, $"{courier.Name} a rapporte les medicaments au camp", now);
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

    /// <summary>Reste-t-il de quoi remplir la mission quelque part ?</summary>
    private bool SuppliesExist()
        => _world.Items.Values.Any(item => item.ItemId.Equals(Mission.Item, StringComparison.OrdinalIgnoreCase))
        || _band.Survivors.Any(survivor => survivor.Carries(Mission.Item));

    /// <summary>Qui porte la caisse, pour le HUD. Vide tant que personne ne l'a ramassee.</summary>
    public IEnumerable<Survivor> Carriers
        => _band.Survivors.Where(survivor => survivor.Carries(Mission.Item));
}
