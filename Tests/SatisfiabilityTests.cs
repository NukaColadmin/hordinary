using System.Diagnostics;
using Hordinary.AI.Instructions;
using Hordinary.AI.Planning;
using Hordinary.AI.Planning.Pddl;
using Hordinary.Config;

namespace Hordinary.Tests;

/// <summary>
/// Portillon 3 : la satisfiabilite, et la recherche du coupable minimal (livrable 15).
///
/// <para>
/// Le portillon lui-meme est Fast Downward, <b>hors ligne par construction</b> — aucun planificateur
/// tiers ne tourne pendant une partie, et le binaire n'est installe sur aucune machine du projet. Ce
/// qui est eprouve ici est donc ce qui reste quand on le retire, et c'est la partie qui peut etre
/// fausse en silence : la strategie d'interrogation.
/// </para>
///
/// <para>
/// Les tests d'integration ne sont pas marques <c>Skip</c> : ils <b>changent de question</b> selon
/// que le binaire est la. Present, ils lui demandent un plan ; absent, ils verifient que ce qu'on lui
/// donnerait est bien forme et que le lanceur signale proprement son absence. Un test toujours vert
/// pour cause d'ignorance ne surveille rien — celui-ci surveille au moins l'entree du portillon.
/// </para>
/// </summary>
public class SatisfiabilityTests
{
    private static readonly PlanningDomain DeliveredDomain =
        PlanningDomain.FromJson(DemoMap.ReadConfig("planner_domain.json"));

    private static readonly GameConfig DeliveredConfig =
        GameConfig.FromJson(DemoMap.ReadConfig("game_config.json"));

    private static readonly PddlDomain Domain = PddlDomainBuilder.From(DeliveredDomain);

    private static InstructionSettings Settings => DeliveredConfig.Instructions;

    private static TacticalConstraint Parse(string text)
    {
        var result = ConstraintGrammar.Parse(text, Domain, Settings);
        Assert.True(result.Accepted, $"« {text} » : {result.Reason}");
        return result.Constraint!;
    }

    private static readonly TacticalConstraint NoWindows = Parse("forbid(BreakWindow)");
    private static readonly TacticalConstraint NoForcing = Parse("forbid(ForceDoor)");
    private static readonly TacticalConstraint NoOpening = Parse("forbid(OpenDoor)");
    private static readonly TacticalConstraint Grouped = Parse("require_before(at(agent, group), enter:building)");

    // --- La strategie d'interrogation --------------------------------------------------------------

    /// <summary>Rien ne bloque : un seul appel au planificateur, et aucun accuse.</summary>
    [Fact]
    public void Un_domaine_qui_laisse_un_plan_n_accuse_personne()
    {
        var verdict = SatisfiabilityGate.Check([NoWindows, NoForcing], _ => true);

        Assert.True(verdict.Satisfiable);
        Assert.Empty(verdict.Culprits);
        Assert.Equal(1, verdict.Probes);
    }

    /// <summary>
    /// Trois contraintes actives, une seule coupable : c'est elle qu'on nomme, et elle seule.
    ///
    /// <para>
    /// C'est tout l'interet du portillon. « Votre consigne rend la mission impossible » avec trois
    /// consignes actives ne dit rien — le joueur les retire au hasard.
    /// </para>
    /// </summary>
    [Fact]
    public void Une_seule_coupable_parmi_trois_est_isolee()
    {
        // L'oracle bloque des que « forbid(OpenDoor) » est presente, et pas autrement.
        bool Oracle(IReadOnlyList<TacticalConstraint> set) => !set.Contains(NoOpening);

        var verdict = SatisfiabilityGate.Check([NoWindows, NoOpening, NoForcing], Oracle);

        Assert.False(verdict.Satisfiable);
        Assert.False(verdict.Blameless);
        Assert.Equal([NoOpening], verdict.Culprits);
        Assert.Contains("forbid(OpenDoor)", verdict.Reason);
    }

    /// <summary>
    /// Deux contraintes qui ne bloquent qu'ensemble sont nommees ensemble. C'est le cas que la
    /// specification appelle « deux contraintes contradictoires refusees en nommant les deux ».
    /// </summary>
    [Fact]
    public void Deux_contraintes_qui_ne_bloquent_qu_ensemble_sont_nommees_ensemble()
    {
        bool Oracle(IReadOnlyList<TacticalConstraint> set)
            => !(set.Contains(NoOpening) && set.Contains(NoForcing));

        var verdict = SatisfiabilityGate.Check([NoWindows, NoOpening, NoForcing, Grouped], Oracle);

        Assert.False(verdict.Satisfiable);
        Assert.Equal(2, verdict.Culprits.Count);
        Assert.Contains(NoOpening, verdict.Culprits);
        Assert.Contains(NoForcing, verdict.Culprits);
    }

    /// <summary>
    /// Le resultat est minimal au sens de l'inclusion : retirer n'importe laquelle des accusees fait
    /// disparaitre le blocage. C'est la propriete qu'on promet au joueur, donc celle a verifier —
    /// pas la taille du resultat, qui n'en est qu'une consequence.
    /// </summary>
    [Fact]
    public void Le_coupable_designe_est_minimal_par_inclusion()
    {
        bool Oracle(IReadOnlyList<TacticalConstraint> set)
            => !(set.Contains(NoOpening) && set.Contains(NoForcing));

        var verdict = SatisfiabilityGate.Check([NoWindows, NoOpening, NoForcing, Grouped], Oracle);

        Assert.False(Oracle(verdict.Culprits));

        foreach (var accused in verdict.Culprits)
        {
            var without = verdict.Culprits.Where(c => !ReferenceEquals(c, accused)).ToList();

            Assert.True(Oracle(without), $"« {accused.Text} » est accusee sans que son retrait debloque quoi que ce soit.");
        }
    }

    /// <summary>
    /// Une mission sans solution n'accuse aucune consigne.
    ///
    /// <para>
    /// C'est l'erreur judiciaire que le portillon doit eviter : sans cette question, la reduction
    /// gloutonne aurait designe la derniere contrainte restante — le joueur l'aurait retiree, et la
    /// mission serait restee impossible.
    /// </para>
    /// </summary>
    [Fact]
    public void Une_mission_deja_impossible_n_accuse_aucune_consigne()
    {
        var verdict = SatisfiabilityGate.Check([NoWindows, NoOpening], _ => false);

        Assert.False(verdict.Satisfiable);
        Assert.True(verdict.Blameless);
        Assert.Empty(verdict.Culprits);
        Assert.Contains("aucune consigne n'est en cause", verdict.Reason);
    }

    /// <summary>
    /// Le nombre d'appels reste lineaire. La difference entre « n + 2 » et « 2^n » ne se voit pas
    /// sur cinq contraintes en memoire ; elle se voit quand chaque appel est un processus externe.
    /// </summary>
    [Fact]
    public void Le_nombre_d_appels_au_planificateur_reste_lineaire()
    {
        var constraints = new List<TacticalConstraint> { NoWindows, NoOpening, NoForcing, Grouped };

        var verdict = SatisfiabilityGate.Check(constraints, set => !set.Contains(NoOpening));

        Assert.True(verdict.Probes <= constraints.Count + 2, $"{verdict.Probes} appels pour {constraints.Count} contraintes.");
    }

    /// <summary>
    /// A blocage egal, c'est la derniere consigne posee qui est accusee.
    ///
    /// <para>
    /// Ce n'est pas une finesse d'algorithme mais une decision d'interface : le joueur comprend qu'on
    /// accuse ce qu'il vient d'ajouter, beaucoup moins qu'on accuse la premiere de sa liste.
    /// </para>
    /// </summary>
    [Fact]
    public void A_blocage_egal_la_derniere_posee_est_accusee()
    {
        // Les deux forbid bloquent, chacune seule. Deux reponses minimales existent ; on veut la seconde.
        bool Oracle(IReadOnlyList<TacticalConstraint> set) => set.Count == 0;

        var verdict = SatisfiabilityGate.Check([NoWindows, NoOpening], Oracle);

        Assert.False(verdict.Blameless);
        Assert.Single(verdict.Culprits);
        Assert.Equal(NoOpening, verdict.Culprits[0]);
    }

    // --- L'entree du portillon ----------------------------------------------------------------------

    /// <summary>
    /// Ce qu'on donnerait a Fast Downward est du PDDL bien forme, dans le meme fragment que la spec G
    /// et <b>sans</b> bloc <c>(:constraints …)</c> — il ne prend pas les contraintes de trajectoire
    /// PDDL 3 en entree.
    ///
    /// <para>
    /// Le test tourne sans le binaire, et c'est le seul moyen qu'on ait aujourd'hui de surveiller
    /// l'entree du portillon 3. Il ne remplace pas Fast Downward : il garantit qu'il aura quelque
    /// chose de recevable a lire le jour ou on l'installera.
    /// </para>
    /// </summary>
    [Fact]
    public void Le_domaine_soumis_au_portillon_3_est_bien_forme()
    {
        var instructions = new TacticalInstructions(Domain, Settings);

        Assert.True(instructions.Add(Grouped.Text).Accepted);

        string text = PddlWriter.Render(instructions.Compiled);

        Assert.DoesNotContain("(:constraints", text);
        Assert.Equal(Domain.Requirements, instructions.Compiled.Requirements);
        Assert.Equal(0, Balance(PddlWriter.Render(Domain)));
        Assert.Equal(0, Balance(text));

        // La version declarative, elle, porte le bloc PDDL 3 : c'est celle de VAL, pas celle du
        // planificateur. Le domaine compile ne la porte pas de lui-meme, et c'est voulu — le bloc
        // n'est attache qu'a l'ecriture, exactement comme le fait MissionDomainFile. Un domaine qui
        // trainerait ses contraintes PDDL 3 finirait un jour soumis a Fast Downward avec.
        string declared = PddlWriter.Render(
            instructions.Compiled.With(
                constraints: [.. instructions.Active.Select(c => c.Declarative(instructions.Compiled))]),
            includeConstraints: true);

        Assert.Contains("(:constraints", declared);
        Assert.Equal(0, Balance(declared));
    }

    /// <summary>Parentheses appariees : le minimum qu'un analyseur exige avant de lire quoi que ce soit.</summary>
    private static int Balance(string text)
    {
        int depth = 0;

        foreach (char character in text)
        {
            if (character == '(') depth++;
            else if (character == ')') depth--;

            Assert.True(depth >= 0, "Une parenthese fermante sans ouvrante.");
        }

        return depth;
    }

    /// <summary>
    /// Le lanceur de Fast Downward : il tourne s'il trouve le binaire, et dit clairement qu'il ne l'a
    /// pas trouve sinon.
    ///
    /// <para>
    /// Ce test n'est pas marque <c>Skip</c> : il <b>change de question</b> selon ce qui est installe.
    /// Binaire present, il lui soumet le domaine contraint et le dernier instantane versionne, et
    /// exige une reponse. Absent, il verifie que le lanceur le signale proprement plutot que de lever
    /// une exception opaque au fond d'une pile — ce qui est la seule chose qui puisse encore etre
    /// cassee ici, et qui le serait en silence.
    /// </para>
    /// </summary>
    [Fact]
    public void Fast_downward_est_interroge_s_il_est_installe()
    {
        var instructions = new TacticalInstructions(Domain, Settings);
        Assert.True(instructions.Add(Grouped.Text).Accepted);

        string problem = Path.Combine(
            DemoMap.RepositoryRoot, "tools", "pddl", "snapshots", "10-completeplayermission.pddl");

        Assert.True(File.Exists(problem), $"{problem} est absent : regenerer avec HORDINARY_PDDL_REGEN=1.");

        if (FastDownward.Locate() is not { } binary)
        {
            // Le chemin normal sur ce projet. On verifie que l'absence est un fait constate et non une
            // exception, puis que le domaine contraint est bien la, prêt a lui etre donne.
            Assert.False(FastDownward.Available);
            Assert.NotEmpty(PddlWriter.Render(instructions.Compiled));
            return;
        }

        string domain = Path.Combine(Path.GetTempPath(), $"hordinary-{Guid.NewGuid():N}.pddl");
        File.WriteAllText(domain, PddlWriter.Render(instructions.Compiled));

        try
        {
            var result = FastDownward.Solve(binary, domain, problem);

            Assert.True(
                result.Solved || result.Unsolvable,
                $"Fast Downward n'a ni resolu ni conclu a l'impossibilite :\n{result.Output}");
        }
        finally
        {
            File.Delete(domain);
        }
    }
}

/// <summary>
/// Le lanceur de Fast Downward. Il vit dans les tests et non dans <c>Hordinary.Core</c> : lancer un
/// processus externe n'est pas une decision de simulation, et le coeur n'en a aucun besoin.
/// </summary>
internal static class FastDownward
{
    /// <summary>La variable d'environnement qui donne le chemin, quand il n'est pas dans le PATH.</summary>
    public const string PathVariable = "HORDINARY_FAST_DOWNWARD";

    public static bool Available => Locate() is not null;

    /// <summary>Le binaire, ou <c>null</c>. Aucune exception : son absence est le cas normal ici.</summary>
    public static string? Locate()
    {
        if (Environment.GetEnvironmentVariable(PathVariable) is { Length: > 0 } declared && File.Exists(declared))
            return declared;

        string[] names = ["fast-downward.py", "fast-downward", "downward"];
        string paths = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        foreach (string directory in paths.Split(Path.PathSeparator))
        {
            if (directory.Length == 0) continue;

            foreach (string name in names)
            {
                string candidate = Path.Combine(directory, name);

                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }

    internal sealed record Result(bool Solved, bool Unsolvable, string Output);

    public static Result Solve(string binary, string domain, string problem, string search = "astar(lmcut())")
    {
        var start = new ProcessStartInfo
        {
            FileName = binary.EndsWith(".py", StringComparison.OrdinalIgnoreCase) ? "python" : binary,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        if (binary.EndsWith(".py", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(binary);

        start.ArgumentList.Add(domain);
        start.ArgumentList.Add(problem);
        start.ArgumentList.Add("--search");
        start.ArgumentList.Add(search);

        using var process = Process.Start(start)!;

        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit(120_000);

        return new Result(
            Solved: output.Contains("Solution found", StringComparison.OrdinalIgnoreCase),
            Unsolvable: output.Contains("unsolvable", StringComparison.OrdinalIgnoreCase),
            Output: output);
    }
}
