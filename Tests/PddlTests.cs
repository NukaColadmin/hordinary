using Hordinary.AI.Planning;
using Hordinary.AI.Planning.Pddl;

namespace Hordinary.Tests;

/// <summary>
/// Export PDDL (spec G). Les tests portent sur le <b>vrai</b> domaine livre
/// (<c>config/planner_domain.json</c>) : retirer une action, renommer un predicat ou changer un cout
/// doit se voir ici.
///
/// Aucun nombre n'y est ecrit en dur — ni le nombre d'actions, ni celui des predicats. Ce qui est
/// verifie est une correspondance : « autant d'actions que le fichier en active », « chaque predicat
/// employe est declare », « les deux rendus decrivent le meme domaine ». Un test qui compterait 25
/// actions deviendrait faux a la premiere action ajoutee, et n'apprendrait rien sur l'export.
/// </summary>
public class PddlTests
{
    private static readonly PlanningDomain DeliveredDomain =
        PlanningDomain.FromJson(DemoMap.ReadConfig("planner_domain.json"));

    private static PddlDomain Exported => PddlDomainBuilder.From(DeliveredDomain);

    // --- Structure -------------------------------------------------------------------------------

    /// <summary>
    /// Le domaine exporte declare exactement les actions activees du fichier, dans le meme ordre.
    /// Les actions a <c>enabled: false</c> n'en sont pas : un planificateur tiers doit voir le meme
    /// domaine que GBFS, sans quoi sa validation ne dirait rien du jeu.
    /// </summary>
    [Fact]
    public void Le_domaine_exporte_declare_toutes_les_actions_activees()
    {
        var exported = Exported;

        Assert.Equal(
            DeliveredDomain.Active.Select(schema => schema.Name),
            exported.Actions.Select(action => action.Name));

        Assert.All(exported.Actions, action => Assert.NotNull(action.Origin));
    }

    /// <summary>
    /// Tout predicat cite par une precondition ou un effet figure dans <c>(:predicates …)</c> avec la
    /// bonne arite. C'est la premiere chose qu'un planificateur tiers refuse, et c'est aussi ce sur
    /// quoi la grammaire du livrable 15 s'appuie pour refuser un symbole invente.
    /// </summary>
    [Fact]
    public void Chaque_predicat_utilise_est_declare()
    {
        var exported = Exported;

        foreach (var action in exported.Actions)
        {
            foreach (var atom in Atoms(action.Precondition).Concat(Atoms(action.Effect)))
            {
                var declared = exported.FindPredicate(atom.Predicate);

                Assert.True(declared is not null, $"Le predicat « {atom.Predicate} » de « {action.Name} » n'est pas declare.");
                Assert.Equal(declared!.Arity, atom.Arity);
            }
        }
    }

    /// <summary>
    /// Les categories de lieux de <c>places.kinds</c> sont declarees comme predicats unaires alors
    /// qu'aucune action ne les pose : c'est le moteur qui les emet. Sans cette declaration, le schema
    /// scinde par « avant d'entrer dans un batiment » citerait un predicat inconnu.
    /// </summary>
    [Fact]
    public void Les_categories_de_lieux_sont_declarees_comme_predicats()
    {
        var exported = Exported;

        Assert.NotEmpty(exported.LocationKinds);

        foreach (string kind in exported.LocationKinds)
        {
            var declared = exported.FindPredicate(kind);

            Assert.True(declared is not null, $"La categorie « {kind} » n'est pas declaree comme predicat.");
            Assert.Equal(1, declared!.Arity);
        }
    }

    /// <summary>
    /// Les reperes recalcules a chaque decision — <c>group</c>, <c>cover</c>… — sont des objets du
    /// <b>probleme</b>, pas des constantes du domaine. Ils ne doivent donc pas se retrouver dans
    /// <c>(:constants …)</c>, et doivent rester nommables par une instruction du joueur.
    /// </summary>
    [Fact]
    public void Les_reperes_sont_des_objets_du_probleme_et_non_des_constantes()
    {
        var exported = Exported;

        Assert.NotEmpty(exported.Landmarks);

        foreach (string landmark in exported.Landmarks)
        {
            Assert.DoesNotContain(
                exported.Constants,
                constant => constant.Name.Equals(landmark, StringComparison.OrdinalIgnoreCase));
        }

        Assert.All(
            exported.Constants,
            constant => Assert.NotEqual(PlanningObjectTypes.Location, constant.Type));
    }

    /// <summary>
    /// Le joker <c>*</c> du domaine du jeu n'existe pas en PDDL : <c>"!at(agent, *)"</c> se rend en
    /// effet universellement quantifie. C'est la reponse retenue par la spec G, et c'est aussi elle
    /// qui permet au livrable 15 de reconnaitre un schema de deplacement sans nommer le predicat
    /// <c>at</c>.
    /// </summary>
    [Fact]
    public void Le_deplacement_exporte_un_effet_universel()
    {
        var move = Exported.FindAction("MoveToLocation");
        Assert.NotNull(move);

        var universal = move!.Effect.Literals.OfType<PddlUniversal>().SingleOrDefault();

        Assert.NotNull(universal);
        Assert.Equal(PlanningObjectTypes.Location, universal!.Variable.Type);
        Assert.IsType<PddlNegation>(universal.Body);
        Assert.Contains("(forall (", universal.Canonical, StringComparison.Ordinal);
    }

    // --- Rendu texte -----------------------------------------------------------------------------

    /// <summary>
    /// Le texte rendu ne decide de rien : tout ce qu'il ecrit existe deja dans la representation. Le
    /// verifier, c'est verifier qu'aucune troisieme source de verite ne s'est glissee dans l'ecriture.
    /// </summary>
    [Fact]
    public void Le_texte_rendu_correspond_a_la_representation()
    {
        var exported = Exported;
        string text = PddlWriter.Render(exported);

        Assert.StartsWith($"(define (domain {exported.Name})", text, StringComparison.Ordinal);
        Assert.Contains($"(:requirements {string.Join(' ', exported.Requirements)})", text, StringComparison.Ordinal);

        foreach (var predicate in exported.Predicates) Assert.Contains(predicate.Canonical, text, StringComparison.Ordinal);
        foreach (var function in exported.Functions) Assert.Contains(function.Canonical, text, StringComparison.Ordinal);
        foreach (var action in exported.Actions) Assert.Contains($"(:action {action.Name}", text, StringComparison.Ordinal);

        Assert.Equal(
            exported.Actions.Count,
            text.Split("(:action ", StringSplitOptions.None).Length - 1);
    }

    /// <summary>
    /// Le cout ne vit pas dans les effets de la representation — il a son champ — mais il en est un en
    /// PDDL. Le rendu est le seul endroit ou il le redevient, et il le fait pour toutes les actions.
    /// </summary>
    [Fact]
    public void Le_texte_rendu_reinjecte_le_cout_dans_les_effets()
    {
        var exported = Exported;
        string text = PddlWriter.Render(exported);

        Assert.Equal(
            exported.Actions.Count,
            text.Split($"(increase ({PddlIncrease.TotalCost}) ", StringSplitOptions.None).Length - 1);
    }

    /// <summary>
    /// Un domaine soumis a un planificateur ne porte jamais de bloc <c>(:constraints …)</c> : ni GBFS
    /// ni Fast Downward ne savent le resoudre. Le bloc n'existe que sur demande, pour la corroboration
    /// par VAL.
    /// </summary>
    [Fact]
    public void Le_domaine_exporte_ne_porte_aucun_bloc_de_contraintes()
    {
        Assert.Empty(Exported.Constraints);
        Assert.DoesNotContain("(:constraints", PddlWriter.Render(Exported), StringComparison.Ordinal);
    }

    // --- Les deux rendus -------------------------------------------------------------------------

    /// <summary>
    /// La propriete centrale de la spec G : <c>PddlDomain</c> a deux rendus, et ils decrivent le meme
    /// domaine. Le chemin <c>planner_domain.json → PddlDomain → PlanningDomain</c> rend a GBFS
    /// exactement ce qu'il aurait lu directement — memes preconditions, memes effets, memes couts,
    /// meme couche d'execution.
    ///
    /// Sans ce test, rien n'empecherait l'export de deriver du domaine joue, et la validation par un
    /// planificateur tiers ne dirait plus rien du jeu.
    /// </summary>
    [Fact]
    public void Les_deux_rendus_decrivent_le_meme_domaine()
    {
        var rendered = Exported.ToPlanningDomain();
        var source = DeliveredDomain.Active.ToList();

        Assert.Equal(source.Count, rendered.Actions.Count);

        foreach (var (original, copy) in source.Zip(rendered.Actions))
        {
            Assert.Equal(original.Name, copy.Name);
            Assert.Equal(original.Verb, copy.Verb);
            Assert.Equal(original.Cost, copy.Cost);
            Assert.Equal(original.DistanceCost, copy.DistanceCost);
            Assert.Equal(original.NoiseLevel, copy.NoiseLevel);
            Assert.Equal(original.NoiseAction, copy.NoiseAction);
            Assert.Equal(original.Duration, copy.Duration);

            Assert.Equal(original.ParsedPreconditions, copy.ParsedPreconditions);
            Assert.Equal(original.ParsedEffects, copy.ParsedEffects);

            Assert.Equal(
                original.Parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal),
                copy.Parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        }

        Assert.Equal(DeliveredDomain.Goals.Where(goal => goal.Enabled).Select(goal => goal.Name), rendered.Goals.Select(goal => goal.Name));
        Assert.Equal(DeliveredDomain.Places.Kinds, rendered.Places.Kinds);
    }

    /// <summary>
    /// Le domaine rendu vers GBFS est un domaine que GBFS accepte : <c>Validate</c> est le controle
    /// que le jeu applique au fichier livre, et il doit passer sur le produit de l'aller-retour.
    /// </summary>
    [Fact]
    public void Le_domaine_rendu_pour_gbfs_reste_valide()
    {
        Exported.ToPlanningDomain().Validate();
    }

    // --- Outils ----------------------------------------------------------------------------------

    /// <summary>Les atomes d'une formule, quelle que soit la facon dont ils y sont enveloppes.</summary>
    private static IEnumerable<PddlAtom> Atoms(PddlFormula formula)
        => formula.Literals
            .Select(literal => literal switch
            {
                PddlAtom atom => atom,
                PddlNegation { Inner: PddlAtom atom } => atom,
                PddlUniversal { Body: PddlNegation { Inner: PddlAtom atom } } => atom,
                PddlUniversal { Body: PddlAtom atom } => atom,
                _ => null,
            })
            .OfType<PddlAtom>();
}
