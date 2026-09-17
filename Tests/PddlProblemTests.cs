using Hordinary.AI.Planning;
using Hordinary.AI.Planning.Pddl;

namespace Hordinary.Tests;

/// <summary>
/// L'export du <b>probleme</b> (spec G). Le domaine ne change pas d'une partie a l'autre ; un
/// probleme est un instantane de ce qu'un survivant croyait a un instant donne.
///
/// <para>
/// Le test qui compte est <see cref="Un_objet_jamais_percu_n_apparait_pas_dans_le_probleme"/> :
/// l'observabilite partielle est la these du projet, et elle doit survivre a l'export. Un
/// planificateur tiers qui verrait plus que le survivant ne corroborerait plus rien.
/// </para>
/// </summary>
public class PddlProblemTests
{
    private static readonly PlanningDomain DeliveredDomain =
        PlanningDomain.FromJson(DemoMap.ReadConfig("planner_domain.json"));

    private static PddlDomain Exported { get; } = PddlDomainBuilder.From(DeliveredDomain);

    /// <summary>Un probleme reel, pris d'une mission qui tourne : c'est celui que GBFS a resolu.</summary>
    private static PlanningProblem FirstProblem(MissionRun run)
    {
        Assert.True(run.RunUntil(() => run.Plans.Count > 0, maxTicks: 600), "Aucun plan produit.");
        return run.Plans[0].Problem;
    }

    // --- L'observabilite partielle survit a l'export -----------------------------------------------

    /// <summary>
    /// Ce que le survivant n'a jamais percu n'existe pas dans le probleme : ni objet, ni fait. Un
    /// plan ne peut donc pas s'appuyer dessus — ni sous GBFS, ni sous Fast Downward.
    ///
    /// La carte livree compte deux batiments ; celui qu'Alice n'a pas encore vu ne doit figurer nulle
    /// part, alors qu'il est parfaitement present dans le monde reel.
    /// </summary>
    [Fact]
    public void Un_objet_jamais_percu_n_apparait_pas_dans_le_probleme()
    {
        var run = new MissionRun();
        var problem = FirstProblem(run);
        var exported = PddlProblemBuilder.From(problem, Exported);
        string text = PddlWriter.Render(exported);

        var known = new HashSet<string>(
            problem.Objects.Select(candidate => candidate.Name), StringComparer.OrdinalIgnoreCase);

        var unseen = run.Map.Zones
            .Select(zone => SymbolicStateBuilder.ObjectNameOf(zone.Name))
            .Where(name => !known.Contains(name))
            .ToList();

        Assert.NotEmpty(unseen);

        foreach (string name in unseen)
        {
            Assert.DoesNotContain(exported.Objects, declared => declared.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain($" {name} ", text, StringComparison.Ordinal);
            Assert.DoesNotContain($" {name})", text, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Trois croyances divergentes, trois <c>:init</c> distincts, un seul domaine. C'est la
    /// contrepartie de la precedente : la difference entre deux survivants n'est pas gommee par
    /// l'export non plus.
    /// </summary>
    [Fact]
    public void Deux_agents_produisent_deux_problemes_differents()
    {
        var run = new MissionRun(team: true);
        run.RunUntil(() => false, maxTicks: 400);

        var goal = DeliveredDomain.Goals.First(candidate => candidate.Enabled);

        var problems = run.Band.Agents
            .Select(agent => SymbolicStateBuilder.Build(agent.Context!, agent.Domain, goal, MissionRun.Config.Planner))
            .Select(problem => PddlWriter.Render(PddlProblemBuilder.From(problem, Exported)))
            .ToList();

        Assert.Equal(3, problems.Count);
        Assert.Equal(problems.Count, problems.Distinct(StringComparer.Ordinal).Count());

        // Un seul domaine, pourtant : ce qui differe est ce qu'ils savent, pas ce qu'ils savent faire.
        Assert.All(problems, text => Assert.Contains($"(:domain {Exported.Name})", text, StringComparison.Ordinal));
    }

    // --- Bonne formation ---------------------------------------------------------------------------

    /// <summary>
    /// Tout symbole cite dans <c>:init</c> ou dans <c>:goal</c> est declare — dans <c>:objects</c>
    /// ici, ou dans les <c>:constants</c> du domaine. C'est la premiere chose qu'un planificateur
    /// tiers refuse, et elle ne se voit pas a l'oeil sur cent lignes de faits.
    /// </summary>
    [Fact]
    public void Chaque_symbole_du_probleme_est_declare()
    {
        var problem = PddlProblemBuilder.From(FirstProblem(new MissionRun(team: true)), Exported);

        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var constant in Exported.Constants) declared.Add(constant.Name);
        foreach (var candidate in problem.Objects) declared.Add(candidate.Name);

        foreach (var formula in problem.Init.Concat(problem.Goal.Literals))
        {
            foreach (string symbol in Symbols(formula))
            {
                Assert.True(declared.Contains(symbol), $"« {symbol} » est cite sans etre declare.");
            }
        }
    }

    /// <summary>
    /// Un objet du probleme n'est jamais une constante du domaine. Les redeclarer serait une erreur
    /// de syntaxe pour un planificateur strict — et le signe qu'on ne sait plus ce qui appartient au
    /// langage et ce qui appartient a la partie.
    /// </summary>
    [Fact]
    public void Un_objet_du_probleme_n_est_jamais_une_constante_du_domaine()
    {
        var problem = PddlProblemBuilder.From(FirstProblem(new MissionRun()), Exported);

        foreach (var candidate in problem.Objects)
        {
            Assert.DoesNotContain(
                Exported.Constants,
                constant => constant.Name.Equals(candidate.Name, StringComparison.OrdinalIgnoreCase));
        }

        // Et l'agent, qui est une constante, ne doit pas y etre non plus.
        Assert.DoesNotContain(problem.Objects, candidate => candidate.Name.Equals(PlanningProblem.Agent, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Le probleme fixe le cout de depart et la metrique. Sans <c>(:metric minimize (total-cost))</c>,
    /// Fast Downward ignore les couts — et <c>penalize</c>, qui ne vit que dans eux, ne voudrait plus
    /// rien dire meme la ou il agit.
    /// </summary>
    [Fact]
    public void Le_probleme_exporte_porte_la_metrique_de_cout()
    {
        string text = PddlWriter.Render(PddlProblemBuilder.From(FirstProblem(new MissionRun()), Exported));

        Assert.Contains($"(= ({PddlIncrease.TotalCost}) 0)", text, StringComparison.Ordinal);
        Assert.Contains($"(:metric minimize ({PddlIncrease.TotalCost}))", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Un probleme ne fixe aucune valeur qu'aucune action ne lit.
    ///
    /// <para>
    /// Le probleme exportait une table des distances entre lieux — une ligne par paire, les deux
    /// tiers du fichier — que rien dans le domaine ne consultait : ecrire
    /// <c>(increase (total-cost) (distance ?from ?to))</c> demanderait un parametre d'origine que
    /// <c>MoveToLocation</c> n'a pas. Des centaines de lignes qui donnaient a croire que le cout
    /// exporte tenait compte du trajet, alors qu'il n'en tient pas compte.
    /// </para>
    ///
    /// <para>
    /// Le test est ecrit en general plutot que contre <c>distance</c> nommement : toute fonction
    /// declaree doit etre lue quelque part, sans quoi elle reviendra sous un autre nom.
    /// </para>
    /// </summary>
    [Fact]
    public void Le_probleme_ne_fixe_aucune_valeur_que_personne_ne_lit()
    {
        var problem = PddlProblemBuilder.From(FirstProblem(new MissionRun(team: true)), Exported);

        // La question se pose sur le texte rendu, et non sur la representation : le cout ne vit pas
        // dans les effets d'une PddlAction — il a son champ — et ne redevient un (increase …) qu'au
        // rendu. C'est de toute facon le texte que lit un planificateur.
        string domain = PddlWriter.Render(Exported);

        bool Read(string function) => domain.Contains($"(increase ({function})", StringComparison.Ordinal);

        // Le cout total est incremente par toutes les actions : c'est la seule fonction qui merite sa place.
        Assert.True(Read(PddlIncrease.TotalCost), "Aucune action n'incremente le cout total.");

        foreach (var assign in problem.Init.OfType<PddlAssign>())
        {
            Assert.True(Read(assign.Function), $"Le probleme fixe « {assign.Function} », qu'aucune action ne lit.");
        }

        foreach (var function in Exported.Functions)
        {
            Assert.True(Read(function.Name), $"Le domaine declare la fonction « {function.Name} », qu'aucune action ne lit.");
        }
    }

    /// <summary>
    /// Le texte rendu ne perd ni n'invente rien par rapport a la representation — la meme propriete
    /// que pour le domaine, et la meme raison : c'est elle qui rend la structure intermediaire utile
    /// plutot que decorative.
    /// </summary>
    [Fact]
    public void Le_probleme_rendu_correspond_a_la_representation()
    {
        var exported = PddlProblemBuilder.From(FirstProblem(new MissionRun()), Exported);
        string text = PddlWriter.Render(exported);

        Assert.Contains($"(define (problem {exported.Name})", text, StringComparison.Ordinal);
        Assert.Contains($"(:domain {exported.Domain})", text, StringComparison.Ordinal);

        foreach (var fact in exported.Init) Assert.Contains(fact.Canonical, text, StringComparison.Ordinal);
        foreach (var candidate in exported.Objects) Assert.Contains(candidate.Name, text, StringComparison.Ordinal);

        Assert.Contains(exported.Goal.Canonical, text, StringComparison.Ordinal);

        // Les parentheses s'equilibrent : un fichier qui ne se ferme pas n'atteint meme pas l'analyseur.
        Assert.Equal(text.Count(letter => letter == '('), text.Count(letter => letter == ')'));
    }

    // --- Outils ------------------------------------------------------------------------------------

    /// <summary>Les symboles cites par une formule : ses arguments, variables exclues.</summary>
    private static IEnumerable<string> Symbols(PddlFormula formula) => formula switch
    {
        PddlAtom atom => atom.Arguments.Where(argument => !argument.StartsWith('?')),
        PddlNegation negation => Symbols(negation.Inner),
        PddlAssign assign => assign.Arguments,
        _ => [],
    };
}
