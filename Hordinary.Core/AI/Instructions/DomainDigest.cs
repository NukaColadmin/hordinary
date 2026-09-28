using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hordinary.AI.Planning.Pddl;

namespace Hordinary.AI.Instructions;

/// <summary>
/// Les options de serialisation du contrat d'API du livrable 15. Elles sont ici plutot que dans
/// <c>GameConfig</c> parce que le contrat n'est pas un fichier de configuration : c'est ce que deux
/// programmes ecrits dans deux langages doivent lire pareil. Snake case des deux cotes, et rien
/// d'autre a se dire.
/// </summary>
public static class InstructionJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    /// <summary>Les memes options, mises en forme : pour le fichier que les tests du service relisent.</summary>
    public static JsonSerializerOptions Readable { get; } = new(Options) { WriteIndented = true };
}

/// <summary>Un predicat, tel que le digest le transmet : son nom et le type de ses arguments.</summary>
public sealed record DigestPredicate(string Name, IReadOnlyList<string> Params);

/// <summary>
/// Un symbole nommable, tel que le digest le transmet : son nom et son type.
///
/// <para>
/// Le type n'y figurait pas, et c'etait un trou reel et non un detail de presentation. Le service
/// voyait <c>agent</c> et <c>toolbox</c> dans la meme liste plate, sans rien pour distinguer celui
/// qu'on equipe de celui qui equipe : <c>equipped(agent)</c> etait indiscernable de
/// <c>equipped(toolbox)</c>, et le noeud <c>self_check</c> n'avait pas de quoi le rattraper. Un
/// modele a fini par produire le premier.
/// </para>
/// </summary>
public sealed record DigestObject(string Name, string Type);

/// <summary>
/// La projection du domaine que le service de langage recoit (livrable 15, noeud <c>context</c>).
///
/// <para>
/// Elle est <b>derivee du <see cref="PddlDomain"/></b>, jamais redigee a la main. C'est la propriete
/// qui compte : le service et le portillon 1 lisent la meme source de verite sur ce qui est nommable,
/// donc un symbole que le modele a vu dans son contexte ne peut pas etre un symbole que la grammaire
/// refuse ensuite. Une liste tenue a part aurait pris du retard sur le domaine des le premier
/// livrable suivant, et le symptome aurait ete un refus incomprehensible pour le joueur.
/// </para>
///
/// <para>
/// C'est une <b>projection en JSON, pas le texte PDDL brut</b>. Les deux tiennent dans la fenetre de
/// contexte ; la projection coute moins de jetons et evite d'apprendre au modele une syntaxe qu'il
/// n'a pas a produire — il rend une contrainte de la grammaire fermee, jamais du Lisp.
/// </para>
///
/// <para>
/// <see cref="Objects"/> ne figurait pas au contrat initial de la specification, et il a fallu
/// l'ajouter : le portillon 1 refuse tout objet qu'il ne connait pas, et l'exemple canonique du
/// livrable — <c>require_before(at(agent, group), enter:building)</c> — cite <c>group</c>, qui est un
/// repere declare dans <c>config/planner_domain.json</c> et non un type ni une categorie de lieu. Un
/// digest qui ne le montre pas demande au modele de deviner un vocabulaire dont la grammaire exige
/// pourtant l'exactitude ; il aurait produit des refus a repetition, imputes au modele alors qu'ils
/// venaient du contexte qu'on lui donne.
/// </para>
///
/// <para>
/// <see cref="Glossary"/> est le second ajout, et il vient de la meme lecon, un cran plus loin : le
/// type dit a quelle position un symbole se met, pas ce qu'il designe. Mesure avec le modele livre,
/// « Soignez les blesses avant d'entrer » — le libelle d'une consigne pre-ecrite — etait refusee,
/// faute de rien qui relie « soigner les blesses » a <c>band_tended</c>. Il n'est rendu qu'en
/// <b>ajout</b> : un service qui l'ignore lit le reste du digest comme avant.
/// </para>
/// </summary>
public sealed record DomainDigest(
    string DomainHash,
    IReadOnlyList<string> Types,
    IReadOnlyList<DigestPredicate> Predicates,
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> LocationKinds,
    IReadOnlyList<DigestObject> Objects,
    IReadOnlyList<string> ActiveConstraints,
    IReadOnlyDictionary<string, string> Glossary)
{
    /// <summary>
    /// Projette le domaine, avec les contraintes deja actives : le modele doit les voir pour ne pas
    /// proposer la contraire de ce que le joueur vient d'obtenir.
    /// </summary>
    public static DomainDigest From(PddlDomain domain, IEnumerable<TacticalConstraint>? active = null)
    {
        ArgumentNullException.ThrowIfNull(domain);

        var predicates = domain.Predicates.Select(predicate => new DigestPredicate(predicate.Name, [.. predicate.ParameterTypes])).ToList();
        var actions = domain.Actions.Select(action => action.Name).ToList();
        var objects = Vocabulary(domain).ToList();

        return new DomainDigest(
            DomainHash: HashOf(domain),
            Types: [.. domain.Types],
            Predicates: predicates,
            Actions: actions,
            LocationKinds: [.. domain.LocationKinds],
            Objects: objects,
            ActiveConstraints: [.. active?.Select(constraint => constraint.Text) ?? []],
            Glossary: Define(domain, NamesOf(predicates, actions, domain.LocationKinds, objects)));
    }

    /// <summary>
    /// Les noms que le digest montre, donc ceux qu'une definition doit couvrir. Un nom sans definition
    /// est montre quand meme, sans sens a cote : c'est le test sur le domaine livre, et non ce code,
    /// qui exige qu'il n'en manque aucune — un domaine de test n'a pas a ecrire de glossaire.
    /// </summary>
    public static IEnumerable<string> NamesOf(
        IEnumerable<DigestPredicate> predicates,
        IEnumerable<string> actions,
        IEnumerable<string> locationKinds,
        IEnumerable<DigestObject> objects)
        => predicates.Select(predicate => predicate.Name)
            .Concat(actions)
            .Concat(locationKinds)
            .Concat(objects.Select(entry => entry.Name))
            .Distinct(StringComparer.Ordinal);

    /// <summary>
    /// Les definitions des seuls noms montres, triees : le fichier versionne sous <c>service/tests/</c>
    /// se compare a la ligne pres, et l'ordre d'un dictionnaire n'est pas une propriete sur laquelle
    /// compter.
    /// </summary>
    private static SortedDictionary<string, string> Define(PddlDomain domain, IEnumerable<string> names)
    {
        var glossary = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (string name in names)
        {
            if (domain.Glossary.TryGetValue(name, out string? meaning)) glossary[name] = meaning;
        }

        return glossary;
    }

    /// <summary>
    /// L'empreinte du domaine. Elle porte sur le <b>texte rendu</b> et non sur la projection : c'est
    /// le domaine qui fait foi, et deux projections identiques d'un domaine qui a change seraient un
    /// piege — le cache du client rendrait une contrainte compilee contre un vocabulaire disparu.
    /// </summary>
    public static string HashOf(PddlDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);

        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(PddlWriter.Render(domain)));

        return "sha256:" + Convert.ToHexString(digest).ToLowerInvariant();
    }

    /// <summary>
    /// Les objets nommables dans un fait, avec leur type. C'est exactement ce que
    /// <see cref="ConstraintGrammar"/> accepte — la liste n'est pas recopiee, elle est lue au meme
    /// endroit, <see cref="PddlDomain.Vocabulary"/>.
    /// </summary>
    private static IEnumerable<DigestObject> Vocabulary(PddlDomain domain)
        => domain.Vocabulary.Select(entry => new DigestObject(entry.Name, entry.Type));

    /// <param name="indented">
    /// Pour la version versionnee sous <c>service/tests/</c>, qui se relit a l'oeil. Le service, lui,
    /// recoit la forme compacte : personne ne lit une requete HTTP.
    /// </param>
    public string ToJson(bool indented = false)
        => JsonSerializer.Serialize(this, indented ? InstructionJson.Readable : InstructionJson.Options);

    public override string ToString()
        => $"{Predicates.Count} predicats, {Actions.Count} schemas, {Objects.Count} objets, {ActiveConstraints.Count} contraintes actives";
}
