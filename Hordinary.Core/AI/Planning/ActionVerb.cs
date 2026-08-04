namespace Hordinary.AI.Planning;

/// <summary>
/// Le socle de verbes primitifs que connait le moteur (specification, livrable 5 : « Data-driven :
/// verbes primitifs, pas une classe par action »).
///
/// Il n'existe volontairement aucun <c>MoveToAction</c>, <c>OpenDoorAction</c>, <c>PickUpMedicineAction</c> :
/// les dix-sept actions executables de la specification sont des <b>instances</b> de ces six verbes,
/// decrites dans <c>config/planner_domain.json</c> avec des parametres differents. Ajouter une action
/// au jeu, ou en retirer une pour isoler un comportement en test, se fait dans ce fichier — jamais en
/// recompilant.
/// </summary>
public enum ActionVerb
{
    /// <summary>Se rendre a la position d'un objet du probleme. Delegue le trajet a A*.</summary>
    ToPos = 0,

    /// <summary>Ramasser un objet la ou l'agent se trouve.</summary>
    Take,

    /// <summary>Deposer un objet transporte a un endroit.</summary>
    Put,

    /// <summary>Changer l'etat du monde ou de l'agent : ouvrir une porte, fouiller, soigner, partager.</summary>
    SetFlag,

    /// <summary>Laisser passer du temps. Combine a un <c>noise_level</c> eleve, c'est une distraction.</summary>
    Wait,

    /// <summary>
    /// Infliger des degats a une autre entite. C'est « effectuer une attaque simple » du livrable 8 :
    /// <c>AttackZombie</c> l'instancie sur une menace memorisee, sans qu'aucune classe de combat ait
    /// eu a exister. Repousser un zombie, en revanche, ne l'entame pas — c'est un <see cref="SetFlag"/>.
    /// </summary>
    Damage,
}

/// <summary>Vocabulaire des verbes : leur nom en configuration et les parametres qu'ils exigent.</summary>
public static class ActionVerbs
{
    private sealed record VerbSpec(ActionVerb Verb, string[] Required, string? PositionParameter);

    private static readonly Dictionary<string, VerbSpec> Specs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["toPos"] = new(ActionVerb.ToPos, ["location"], "location"),
        ["take"] = new(ActionVerb.Take, ["item"], null),
        ["put"] = new(ActionVerb.Put, ["item", "location"], "location"),
        ["setFlag"] = new(ActionVerb.SetFlag, ["condition"], "target"),
        ["wait"] = new(ActionVerb.Wait, [], null),
        ["damage"] = new(ActionVerb.Damage, ["target", "amount"], "target"),
    };

    public static IReadOnlyCollection<string> Names => Specs.Keys;

    public static bool TryParse(string? name, out ActionVerb verb)
    {
        if (name is not null && Specs.TryGetValue(name, out var spec))
        {
            verb = spec.Verb;
            return true;
        }

        verb = default;
        return false;
    }

    /// <summary>Parametres sans lesquels le verbe ne veut rien dire, verifies au chargement du domaine.</summary>
    public static IReadOnlyList<string> RequiredParameters(ActionVerb verb) => Find(verb).Required;

    /// <summary>
    /// Parametre designant l'objet dont la position pilote l'execution — la ou il faut aller, poser,
    /// ou frapper. <c>null</c> quand le verbe agit sur place. Facultatif pour <c>setFlag</c> :
    /// fouiller une piece se fait sur place, ouvrir une porte a la porte.
    /// </summary>
    public static string? PositionParameter(ActionVerb verb) => Find(verb).PositionParameter;

    public static string NameOf(ActionVerb verb) => Specs.First(pair => pair.Value.Verb == verb).Key;

    public static string Vocabulary() => $"Verbes connus : {string.Join(", ", Names.Order())}.";

    private static VerbSpec Find(ActionVerb verb) => Specs.Values.First(spec => spec.Verb == verb);
}
