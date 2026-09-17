using Hordinary.Agents;
using Hordinary.World;

namespace Hordinary.Game;

/// <summary>Une ligne de progression, telle que le HUD et l'ecran de camp l'affichent.</summary>
/// <param name="Label">Ce qui est compte : un identifiant d'objet, une ressource, « zombies ».</param>
/// <param name="Done">Ce qui est acquis.</param>
/// <param name="Needed">Ce qu'il faut atteindre.</param>
public readonly record struct MissionProgress(string Label, int Done, int Needed)
{
    public bool Met => Done >= Needed;

    public override string ToString() => $"{Label} : {Done} / {Needed}";
}

/// <summary>
/// Les compteurs propres aux trois types d'objectif du livrable 16.
///
/// Il ne decide de rien : <see cref="MissionTracker"/> reste le seul a rendre un verdict, et cette
/// classe ne fait que repondre « ou en est-on ». La separation vaut la peine parce que les deux
/// questions n'ont pas la meme duree de vie — le verdict est definitif, la progression fait des
/// aller-retours : un objet ramasse puis perdu redescend le compteur, et c'est voulu.
///
/// <b>Pourquoi observer plutot que s'abonner.</b> Un zombie qui tombe est retire par
/// <c>EntityManager.Remove</c>, qui emet un evenement — mais <see cref="IHorde"/>, l'interface que le
/// groupe connait, ne l'expose pas, et l'y ajouter obligerait chaque horde de test a le porter. On
/// compare donc la horde d'une passe a l'autre : un identifiant qui disparait est un zombie qui est
/// tombe, et sa derniere position connue dit ou. Le resultat est le meme, et rien de nouveau n'a
/// besoin d'etre cable pour qu'une mission se deroule en test.
/// </summary>
public sealed class MissionObjectiveTracker
{
    private readonly MissionDefinition _mission;
    private readonly WorldState _world;
    private readonly SurvivorBand _band;
    private readonly ItemCatalogue _catalogue;

    /// <summary>Derniere position connue de chaque zombie encore debout a la passe precedente.</summary>
    private readonly Dictionary<string, GridPosition> _standing = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Les zombies deja comptes. Un mort ne compte qu'une fois, meme reevalue.</summary>
    private readonly HashSet<string> _counted = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ce que chaque survivant portait a la passe precedente, par objet.</summary>
    private readonly Dictionary<string, Dictionary<string, int>> _carried
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ce qui a ete ramasse <b>dans la zone cible</b>, par objet. Ne redescend jamais.</summary>
    private readonly Dictionary<string, int> _credited = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Ce dont on a constate l'existence au moins une fois. Sans ce garde-fou, une mission serait
    /// declaree impossible a la premiere passe — avant que la horde ne soit posee, ou avant que les
    /// caisses ne le soient — et la partie se conclurait par une defaite au premier tick.
    /// </summary>
    private readonly HashSet<string> _witnessed = new(StringComparer.OrdinalIgnoreCase);

    private bool _hordeSeen;

    public MissionObjectiveTracker(
        MissionDefinition mission,
        WorldState world,
        SurvivorBand band,
        ItemCatalogue catalogue)
    {
        _mission = mission ?? throw new ArgumentNullException(nameof(mission));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _band = band ?? throw new ArgumentNullException(nameof(band));
        _catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));

        Kind = mission.Type ?? MissionObjectiveKind.CollectObjects;
        Requirements = mission.Requirements ?? MissionRequirements.None;
        Payload = Requirements.Payload(Kind, catalogue);
    }

    public MissionObjectiveKind Kind { get; }

    public MissionRequirements Requirements { get; }

    /// <summary>Les objets a rapporter, ressources deja traduites en objets du catalogue.</summary>
    public IReadOnlyList<ItemRequirement> Payload { get; }

    /// <summary>La zone ou l'objectif doit se realiser, sur la carte reellement jouee.</summary>
    public MapZone? TargetZone => _mission.ObjectiveIn(_world.Grid);

    /// <summary>Zombies abattus dans la zone cible. Ceux tombes ailleurs ne comptent pas.</summary>
    public int Killed { get; private set; }

    /// <summary>
    /// Une passe d'observation. Appelee par <see cref="MissionTracker.Evaluate"/>, donc a chaque
    /// tick de la boucle : c'est ce qui permet de dater un ramassage — savoir <i>ou</i> se tenait
    /// celui qui a ramasse demande de l'avoir vu au moment ou il l'a fait.
    /// </summary>
    public void Observe()
    {
        if (Kind == MissionObjectiveKind.KillZombies) ObserveKills();
        else ObserveCollection();
    }

    // --- Elimination ------------------------------------------------------------------------------

    private void ObserveKills()
    {
        var zone = TargetZone;
        var alive = _band.Horde?.Zombies ?? [];

        if (alive.Count > 0) _hordeSeen = true;

        var living = new HashSet<string>(alive.Select(zombie => zombie.Id), StringComparer.OrdinalIgnoreCase);

        foreach (var (id, last) in _standing)
        {
            if (living.Contains(id) || !_counted.Add(id)) continue;

            // Tombe hors de la zone : la mort est enregistree pour ne pas la recompter, mais elle ne
            // fait pas progresser l'objectif. « Dans la zone » se juge sur la derniere position
            // connue, qui est celle qu'il occupait au coup fatal.
            if (zone is not null && zone.Contains(last)) Killed++;
        }

        _standing.Clear();
        foreach (var zombie in alive) _standing[zombie.Id] = zombie.Position;
    }

    // --- Recuperation -----------------------------------------------------------------------------

    /// <summary>
    /// Ce que chacun porte, compare a la passe precedente. Une hausse constatee sur quelqu'un qui se
    /// tient dans la zone cible est un ramassage sur place, et c'est ce qui est credite ; une hausse
    /// constatee ailleurs ne credite rien — un objet trouve dans la rue ne remplit pas une mission
    /// qui envoyait fouiller le poste de police.
    /// </summary>
    private void ObserveCollection()
    {
        if (Payload.Count == 0) return;

        var zone = TargetZone;

        foreach (var survivor in _band.Survivors)
        {
            if (!_carried.TryGetValue(survivor.Id, out var before))
            {
                before = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                _carried[survivor.Id] = before;
            }

            bool onSite = survivor.Alive && zone is not null && zone.Contains(survivor.Position);

            foreach (var line in Payload)
            {
                int now = survivor.Alive ? survivor.Inventory.CountOf(line.Item) : 0;
                before.TryGetValue(line.Item, out int was);

                if (onSite && now > was)
                {
                    _credited.TryGetValue(line.Item, out int credited);
                    _credited[line.Item] = credited + (now - was);
                }

                before[line.Item] = now;
            }
        }

        foreach (var line in Payload)
        {
            if (Available(line.Item) > 0) _witnessed.Add(line.Item);
        }
    }

    /// <summary>Ce que le groupe porte reellement. Un mort ne porte plus rien : il a laisse tomber.</summary>
    private int CarriedNow(string itemId)
        => _band.Survivors.Where(survivor => survivor.Alive).Sum(survivor => survivor.Inventory.CountOf(itemId));

    /// <summary>Tout ce qui existe encore de cet objet : dans les sacs, et au sol.</summary>
    private int Available(string itemId)
        => CarriedNow(itemId)
         + _world.Items.Values.Count(item => item.ItemId.Equals(itemId, StringComparison.OrdinalIgnoreCase));

    // --- Ou en est-on -----------------------------------------------------------------------------

    /// <summary>
    /// La progression, ligne par ligne. Pour une recuperation, elle est le minimum de deux choses :
    /// ce qui a ete ramasse dans la zone, et ce qui est encore porte. Perdre un objet apres l'avoir
    /// ramasse rend donc l'objectif incomplet, sans effacer le fait qu'on l'avait bien pris la-bas.
    /// </summary>
    public IReadOnlyList<MissionProgress> Progress()
    {
        if (Kind == MissionObjectiveKind.KillZombies)
        {
            return [new MissionProgress("zombies elimines", Killed, Requirements.Zombies)];
        }

        return
        [
            .. Payload.Select(line =>
            {
                _credited.TryGetValue(line.Item, out int credited);
                return new MissionProgress(line.Item, Math.Min(credited, CarriedNow(line.Item)), line.Quantity);
            }),
        ];
    }

    /// <summary>Toutes les exigences sont-elles atteintes, a cet instant ?</summary>
    public bool Fulfilled
    {
        get
        {
            var lines = Progress();
            return lines.Count > 0 && lines.All(line => line.Met);
        }
    }

    /// <summary>
    /// Plus rien ne peut remplir la mission. C'est le pendant du « les medicaments sont hors
    /// d'atteinte » des missions manuelles, et il repond a la meme question : faut-il laisser le
    /// groupe s'obstiner ?
    ///
    /// La reponse n'est jamais donnee avant d'avoir constate l'existence de ce qu'on cherche — voir
    /// <see cref="_witnessed"/>. Un compteur a zero parce que rien n'a encore ete pose n'est pas une
    /// mission perdue, c'est une mission qui n'a pas commence.
    /// </summary>
    public bool Impossible
    {
        get
        {
            if (Kind == MissionObjectiveKind.KillZombies)
            {
                if (!_hordeSeen) return false;
                return Killed + (_band.Horde?.Zombies.Count ?? 0) < Requirements.Zombies;
            }

            return Payload.Any(line => _witnessed.Contains(line.Item) && Available(line.Item) < line.Quantity);
        }
    }

    /// <summary>Ce que le HUD et le verdict affichent : la progression, en une ligne.</summary>
    public override string ToString() => string.Join(", ", Progress());
}
