using System.Diagnostics;
using System.Text;
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

        string problem = Snapshot("10-completeplayermission.pddl");

        Assert.True(File.Exists(problem), $"{problem} est absent : regenerer avec HORDINARY_PDDL_REGEN=1.");

        if (FastDownward.Locate() is not { } planner)
        {
            // On verifie que l'absence est un fait constate et non une exception, puis que le domaine
            // contraint est bien la, prêt a lui etre donne.
            Assert.False(FastDownward.Available);
            Assert.NotEmpty(PddlWriter.Render(instructions.Compiled));
            return;
        }

        var result = FastDownward.Solve(planner, PddlWriter.Render(instructions.Compiled), problem);

        Assert.False(result.Rejected, $"Fast Downward refuse l'entree :\n{result.Output}");

        Assert.True(
            result.Solved || result.Unsolvable,
            $"Fast Downward n'a ni resolu ni conclu a l'impossibilite :\n{result.Output}");
    }

    /// <summary>
    /// Les dix instantanes versionnes, recompiles avec une contrainte active, restent du PDDL que Fast
    /// Downward <b>accepte</b>. C'est le test que la specification demande, et il est reste sans sujet
    /// tant qu'aucun planificateur n'etait la.
    ///
    /// <para>
    /// Ce qu'il surveille est l'acceptation, pas le plan : une contrainte a parfaitement le droit de
    /// rendre une mission impossible — c'est meme la raison d'etre du portillon 3 — mais elle n'a
    /// jamais le droit de produire un domaine illisible. Les deux se distinguent au code de retour et
    /// non au texte, sans quoi la nuance se perdrait.
    /// </para>
    ///
    /// <para>
    /// Le premier passage a trouve quatre defauts d'export d'un coup : des lieux nommes par une action
    /// sans etre declares, un cout fractionnaire, la redeclaration de ces lieux par le probleme, et
    /// l'enchainement des trois. Aucun n'etait visible autrement.
    /// </para>
    /// </summary>
    [Fact]
    public void Les_instantanes_contraints_restent_du_pddl_accepte()
    {
        var instructions = new TacticalInstructions(Domain, Settings);
        Assert.True(instructions.Add(Grouped.Text).Accepted);

        string domain = PddlWriter.Render(instructions.Compiled);
        var snapshots = Directory
            .GetFiles(Path.Combine(DemoMap.RepositoryRoot, "tools", "pddl", "snapshots"), "*.pddl")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(snapshots);

        if (FastDownward.Locate() is not { } planner)
        {
            Assert.All(snapshots, snapshot => Assert.NotEmpty(File.ReadAllText(snapshot)));
            return;
        }

        foreach (string snapshot in snapshots)
        {
            var result = FastDownward.Solve(planner, domain, snapshot);

            Assert.False(
                result.Rejected,
                $"{Path.GetFileName(snapshot)} : Fast Downward refuse le domaine contraint.\n{result.Output}");
        }
    }

    /// <summary>
    /// Le portillon 3 en entier, avec un vrai planificateur : trois consignes actives, une seule qui
    /// rend la mission impossible, Fast Downward qui le <b>prouve</b>, et la reduction gloutonne qui
    /// nomme la coupable — elle seule.
    ///
    /// <para>
    /// Les autres tests de ce fichier eprouvent la strategie d'interrogation contre un oracle simule,
    /// ce qui est la bonne facon de la tester. Celui-ci repond a l'autre question, celle qu'aucun
    /// oracle simule ne peut poser : est-ce que le domaine qu'on lui donne dit bien ce qu'on croit ?
    /// L'instantane 10 a pour but <c>(at agent mission)</c> et pour unique plan
    /// <c>(movetolocation mission)</c> ; interdire le deplacement le rend donc impossible, et rien
    /// d'autre dans le jeu de consignes ne le rend impossible.
    /// </para>
    /// </summary>
    [Fact]
    public void Une_consigne_qui_rend_la_mission_impossible_est_prouvee_telle()
    {
        var blocking = Parse("forbid(MoveToLocation)");
        IReadOnlyList<TacticalConstraint> active = [NoWindows, blocking, NoForcing];
        string problem = Snapshot("10-completeplayermission.pddl");

        if (FastDownward.Locate() is not { } planner)
        {
            // Sans planificateur, on verifie au moins que ce qu'on lui donnerait est bien forme.
            Assert.NotEmpty(Compile(active));
            return;
        }

        int calls = 0;

        bool Oracle(IReadOnlyList<TacticalConstraint> set)
        {
            calls++;

            var result = FastDownward.Solve(planner, Compile(set), problem);

            Assert.False(result.Rejected, $"Fast Downward refuse le domaine contraint :\n{result.Output}");

            // Un « 12 » — recherche interrompue sans preuve — n'autorise a accuser personne. A* avec
            // lmcut est complet, donc il ne doit pas survenir ; s'il survient, il faut le voir.
            Assert.True(
                result.Solved || result.Unsolvable,
                $"Ni plan ni preuve d'impossibilite (code {result.ExitCode}) :\n{result.Output}");

            return result.Solved;
        }

        var verdict = SatisfiabilityGate.Check(active, Oracle);

        Assert.False(verdict.Satisfiable);
        Assert.False(verdict.Blameless);
        Assert.Equal([blocking], verdict.Culprits);
        Assert.Contains("forbid(MoveToLocation)", verdict.Reason);
        Assert.Equal(calls, verdict.Probes);
    }

    /// <summary>
    /// VAL, témoin extérieur du portillon 4 : le plan que Fast Downward produit contre le domaine
    /// <b>compile</b> satisfait la forme <b>declarative</b> de la consigne, et le plan produit sans la
    /// consigne ne la satisfait pas.
    ///
    /// <para>
    /// C'est le test que la specification reclame, et le seul du projet ou deux outils tiers
    /// independants repondent a la question que le jeu se pose a lui-meme. Le portillon 4 demande
    /// « ces deux rendus disent-ils la meme chose ? » ; <c>TrajectoryChecker</c> y repond en C#, ecrit
    /// par la meme main que le compilateur. Si les deux se trompaient de la meme facon, rien ne le
    /// verrait. VAL ne sait rien de notre compilation : il ne lit que le <c>(:constraints …)</c>.
    /// </para>
    ///
    /// <para>
    /// La consigne vise <c>enter:pharmacy</c> et non <c>enter:building</c>, parce que l'instantane 09
    /// declare <c>(pharmacy objective_zone)</c> et reserve <c>building</c> a deux lieux que le plan ne
    /// traverse pas. Une consigne qui ne mord sur aucun pas du plan rendrait ce test vert pour la
    /// mauvaise raison.
    /// </para>
    /// </summary>
    [Fact]
    public void Val_confirme_que_la_compilation_dit_ce_que_le_joueur_a_dit()
    {
        var instructions = new TacticalInstructions(Domain, Settings);
        Assert.True(instructions.Add("require_before(at(agent, group), enter:pharmacy)").Accepted);

        string declared = PddlWriter.Render(
            instructions.Compiled.With(
                constraints: [.. instructions.Active.Select(active => active.Declarative(instructions.Compiled))]),
            includeConstraints: true);

        Assert.Contains("(:constraints", declared);

        string problem = Snapshot("09-searchforobjective.pddl");

        if (FastDownward.Locate() is not { } planner || Val.Locate() is not { } val)
        {
            Assert.NotEmpty(declared);
            return;
        }

        var constrained = FastDownward.Solve(planner, PddlWriter.Render(instructions.Compiled), problem);
        var free = FastDownward.Solve(planner, PddlWriter.Render(Domain), problem);

        Assert.True(constrained.Solved, constrained.Output);
        Assert.True(free.Solved, free.Output);

        // Si les deux plans coincidaient, la consigne ne mordrait sur aucun pas et le reste du test ne
        // prouverait rien.
        Assert.NotEqual(free.Plan, constrained.Plan);

        // Ce que le domaine compile autorise satisfait ce que le joueur a dit. C'est la question du
        // portillon 4, posee a quelqu'un d'autre que nous.
        var accepted = Val.Check(val, declared, problem, constrained.Plan);

        Assert.True(accepted.Valid, $"VAL refuse le plan du domaine compile :\n{accepted.Output}");

        // Le temoin negatif ne peut pas se prendre sur le domaine compile : le plan libre n'y est meme
        // pas **executable**, puisque la compilation a scinde MoveToLocation et qu'entrer dans une
        // pharmacie passe desormais par un autre schema. VAL le refuserait pour inapplicabilite, ce
        // qui ne dirait rien de la contrainte. On adosse donc le bloc PDDL 3 au domaine **d'origine**,
        // ou le plan libre s'execute — et ou il viole la consigne.
        string undisturbed = PddlWriter.Render(
            Domain.With(constraints: [.. instructions.Active.Select(active => active.Declarative(Domain))]),
            includeConstraints: true);

        var rejected = Val.Check(val, undisturbed, problem, free.Plan);

        Assert.False(rejected.Valid, $"VAL accepte un plan qui ignore la consigne :\n{rejected.Output}");

        Assert.True(
            rejected.Output.Contains("sometime before", StringComparison.OrdinalIgnoreCase),
            $"VAL refuse, mais pas sur la contrainte :\n{rejected.Output}");
    }

    /// <summary>Le texte du domaine compile pour un jeu de consignes donne.</summary>
    private static string Compile(IEnumerable<TacticalConstraint> constraints)
    {
        var instructions = new TacticalInstructions(Domain, Settings);

        foreach (var constraint in constraints)
        {
            Assert.True(instructions.Add(constraint.Text).Accepted, constraint.Text);
        }

        return PddlWriter.Render(instructions.Compiled);
    }

    private static string Snapshot(string name)
        => Path.Combine(DemoMap.RepositoryRoot, "tools", "pddl", "snapshots", name);
}

/// <summary>
/// Le lanceur de Fast Downward. Il vit dans les tests et non dans <c>Hordinary.Core</c> : lancer un
/// processus externe n'est pas une decision de simulation, et le coeur n'en a aucun besoin.
///
/// <para>
/// Trois facons de le trouver, dans cet ordre : <see cref="PathVariable"/>, le PATH, puis une image
/// <b>Docker</b>. C'est le troisieme chemin qui a change ce fichier de statut. Le binaire n'etait
/// installe sur aucune machine du projet, donc le portillon 3 n'avait jamais ete execute, donc quatre
/// defauts d'export ont traverse toute la spec G sans etre vus. Une image officielle a suffi a les
/// rendre en quelques minutes — et c'est l'argument general : un outil qu'on n'installe pas est un
/// test qu'on n'ecrit pas.
/// </para>
/// </summary>
internal static class FastDownward
{
    /// <summary>La variable d'environnement qui donne le chemin, quand il n'est pas dans le PATH.</summary>
    public const string PathVariable = "HORDINARY_FAST_DOWNWARD";

    /// <summary>L'image a employer, si l'on en veut une autre que celle publiee par l'equipe.</summary>
    public const string ImageVariable = "HORDINARY_FAST_DOWNWARD_IMAGE";

    private const string DefaultImage = "aibasel/downward";

    private static readonly Lazy<Planner?> Search = new(Find, isThreadSafe: true);

    public static bool Available => Locate() is not null;

    /// <summary>De quoi lancer, ou <c>null</c>. Aucune exception : l'absence est un cas normal.</summary>
    public static Planner? Locate() => Search.Value;

    /// <summary>Un binaire local, ou une image Docker. Jamais les deux.</summary>
    internal sealed record Planner(string? Binary, string? Image)
    {
        public override string ToString() => Binary ?? $"docker {Image}";
    }

    private static Planner? Find()
    {
        if (Environment.GetEnvironmentVariable(PathVariable) is { Length: > 0 } declared && File.Exists(declared))
            return new Planner(declared, null);

        string[] names = ["fast-downward.py", "fast-downward", "downward"];
        string paths = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        foreach (string directory in paths.Split(Path.PathSeparator))
        {
            if (directory.Length == 0) continue;

            foreach (string name in names)
            {
                string candidate = Path.Combine(directory, name);

                if (File.Exists(candidate)) return new Planner(candidate, null);
            }
        }

        string image = Environment.GetEnvironmentVariable(ImageVariable) is { Length: > 0 } named
            ? named
            : DefaultImage;

        return HasImage(image) ? new Planner(null, image) : null;
    }

    /// <summary>
    /// L'image est-elle deja presente ? On ne la tire pas : un test ne telecharge rien, sans quoi la
    /// suite dependrait du reseau et son premier passage durerait des minutes. C'est
    /// <c>docker pull aibasel/downward</c>, une fois, a la main.
    /// </summary>
    internal static bool HasImage(string image)
    {
        try
        {
            var start = Start("docker");
            start.ArgumentList.Add("image");
            start.ArgumentList.Add("inspect");
            start.ArgumentList.Add(image);
            start.ArgumentList.Add("--format");
            start.ArgumentList.Add("{{.Id}}");

            return Run(start).ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Docker n'est pas installe. C'est un fait constate, pas une panne.
            return false;
        }
    }

    /// <summary>
    /// Le verdict, lu dans le <b>code de retour</b> et non dans le texte de sortie.
    ///
    /// <para>
    /// La difference decide de tout pour le portillon 3 : <c>11</c> est une preuve d'impossibilite,
    /// <c>12</c> est une recherche qui a renonce. Accuser la consigne d'un joueur sur un <c>12</c>
    /// serait l'erreur judiciaire que <see cref="SatisfiabilityGate"/> existe pour eviter. Le texte ne
    /// permet pas cette distinction : chercher « unsolvable » dans la sortie confondait les deux, et
    /// attrapait en prime le mot dans une ligne de statistiques.
    /// </para>
    /// </summary>
    internal sealed record Result(int ExitCode, string Output, IReadOnlyList<string> Plan)
    {
        /// <summary>0 a 3 : un plan existe, avec ou sans epuisement d'un portfolio.</summary>
        public bool Solved => ExitCode is >= 0 and <= 3;

        /// <summary>11 et 13 : la tache est prouvee sans solution.</summary>
        public bool Unsolvable => ExitCode is 11 or 13;

        /// <summary>30 et au-dela : c'est l'entree qui est fautive, pas la tache.</summary>
        public bool Rejected => ExitCode >= 30;
    }

    public static Result Solve(
        Planner planner,
        string domainText,
        string problemPath,
        string search = "astar(lmcut())")
    {
        string directory = Path.Combine(Path.GetTempPath(), $"hordinary-fd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            // Les deux fichiers dans un seul dossier : c'est ce qu'exige le montage Docker, et ca ne
            // coute rien au lancement local. Sans BOM, explicitement : le parseur refuse le domaine
            // des le premier caractere si on lui en laisse un, avec un message qui ne dit pas pourquoi.
            File.WriteAllText(Path.Combine(directory, "domain.pddl"), domainText, new UTF8Encoding(false));
            File.Copy(problemPath, Path.Combine(directory, "problem.pddl"));

            var result = Run(planner.Image is { } image
                ? Docker(image, directory, search)
                : Local(planner.Binary!, directory, search));

            return result with { Plan = Plan(Path.Combine(directory, "plan.txt")) };
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Le plan, une action par ligne, commentaires retires. C'est le format que VAL attend — et la
    /// raison pour laquelle on le recupere : un plan que Fast Downward produit contre le domaine
    /// <b>compile</b> est ce qu'on soumet ensuite a VAL contre le domaine <b>declaratif</b>.
    /// </summary>
    private static IReadOnlyList<string> Plan(string path)
        => File.Exists(path)
            ? [.. File.ReadAllLines(path)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith(';'))]
            : [];

    private static ProcessStartInfo Docker(string image, string directory, string search)
    {
        var start = Start("docker");

        start.ArgumentList.Add("run");
        start.ArgumentList.Add("--rm");
        start.ArgumentList.Add("-v");
        start.ArgumentList.Add($"{directory}:/benchmarks");
        start.ArgumentList.Add("-w");
        start.ArgumentList.Add("/benchmarks");
        start.ArgumentList.Add(image);

        // Option du pilote : elle doit preceder les fichiers, sinon elle est lue comme une option de
        // recherche et le plan n'est ecrit nulle part.
        start.ArgumentList.Add("--plan-file");
        start.ArgumentList.Add("/benchmarks/plan.txt");

        start.ArgumentList.Add("/benchmarks/domain.pddl");
        start.ArgumentList.Add("/benchmarks/problem.pddl");
        start.ArgumentList.Add("--search");
        start.ArgumentList.Add(search);

        return start;
    }

    private static ProcessStartInfo Local(string binary, string directory, string search)
    {
        bool script = binary.EndsWith(".py", StringComparison.OrdinalIgnoreCase);
        var start = Start(script ? "python" : binary);

        if (script) start.ArgumentList.Add(binary);

        start.ArgumentList.Add("--plan-file");
        start.ArgumentList.Add(Path.Combine(directory, "plan.txt"));

        start.ArgumentList.Add(Path.Combine(directory, "domain.pddl"));
        start.ArgumentList.Add(Path.Combine(directory, "problem.pddl"));
        start.ArgumentList.Add("--search");
        start.ArgumentList.Add(search);

        // Fast Downward ecrit output.sas et son plan dans le dossier courant : il travaille donc dans
        // le dossier temporaire, et non la ou la suite a ete lancee.
        start.WorkingDirectory = directory;

        return start;
    }

    private static ProcessStartInfo Start(string file) => new(file)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };

    private static Result Run(ProcessStartInfo start)
    {
        using var process = Process.Start(start)!;

        // Lire avant d'attendre : un tampon de sortie plein bloque le processus, et Fast Downward est
        // bavard.
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit(120_000);

        return new Result(process.ExitCode, output, []);
    }
}

/// <summary>
/// VAL, le validateur de plans du groupe Planning de King's College London.
///
/// <para>
/// Il ne remplace pas le portillon 4 : <c>TrajectoryChecker</c> fait le meme travail, en C#, sans
/// binaire, et tourne a chaque plan produit en jeu. VAL lui apporte ce qu'un verificateur ecrit par la
/// meme main ne peut pas s'apporter — un <b>temoin exterieur</b>. Un plan valide au regard du domaine
/// reecrit et faux au regard de la phrase du joueur passerait les deux si les deux se trompaient de la
/// meme facon ; il ne passe pas VAL, qui ne sait rien de notre compilation et ne lit que le
/// <c>(:constraints …)</c> de PDDL 3.
/// </para>
///
/// <para>
/// Contrairement a Fast Downward, VAL n'a pas d'image publiee : celle-ci se construit une fois, a la
/// main, depuis <c>tools/pddl/val/Dockerfile</c>. Absente, les tests changent de question.
/// </para>
/// </summary>
internal static class Val
{
    public const string PathVariable = "HORDINARY_VAL";

    public const string ImageVariable = "HORDINARY_VAL_IMAGE";

    private const string DefaultImage = "hordinary-val";

    private static readonly Lazy<FastDownward.Planner?> Search = new(Find, isThreadSafe: true);

    public static FastDownward.Planner? Locate() => Search.Value;

    private static FastDownward.Planner? Find()
    {
        if (Environment.GetEnvironmentVariable(PathVariable) is { Length: > 0 } declared && File.Exists(declared))
            return new FastDownward.Planner(declared, null);

        string image = Environment.GetEnvironmentVariable(ImageVariable) is { Length: > 0 } named
            ? named
            : DefaultImage;

        return FastDownward.HasImage(image) ? new FastDownward.Planner(null, image) : null;
    }

    /// <summary>
    /// Le verdict de VAL. Il se lit au code de retour — 0 quand tous les plans soumis sont valides —
    /// et la raison, quand il y en a une, est dans la sortie.
    /// </summary>
    internal sealed record Verdict(bool Valid, string Output);

    public static Verdict Check(
        FastDownward.Planner val,
        string declaredDomain,
        string problemPath,
        IEnumerable<string> plan)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"hordinary-val-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var utf8 = new UTF8Encoding(false);

            File.WriteAllText(Path.Combine(directory, "domain.pddl"), declaredDomain, utf8);
            File.Copy(problemPath, Path.Combine(directory, "problem.pddl"));
            File.WriteAllLines(Path.Combine(directory, "plan.txt"), plan, utf8);

            var start = new ProcessStartInfo(val.Image is null ? val.Binary! : "docker")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = directory,
            };

            if (val.Image is { } image)
            {
                start.ArgumentList.Add("run");
                start.ArgumentList.Add("--rm");
                start.ArgumentList.Add("-v");
                start.ArgumentList.Add($"{directory}:/data");
                start.ArgumentList.Add(image);

                // `-v` cote VAL : sans le mode verbeux, un plan refuse pour violation de contrainte
                // rend « Plan failed to execute » et rien de plus. La raison est ce qu'on vient
                // chercher.
                start.ArgumentList.Add("-v");
                start.ArgumentList.Add("/data/domain.pddl");
                start.ArgumentList.Add("/data/problem.pddl");
                start.ArgumentList.Add("/data/plan.txt");
            }
            else
            {
                start.ArgumentList.Add("-v");
                start.ArgumentList.Add(Path.Combine(directory, "domain.pddl"));
                start.ArgumentList.Add(Path.Combine(directory, "problem.pddl"));
                start.ArgumentList.Add(Path.Combine(directory, "plan.txt"));
            }

            using var process = Process.Start(start)!;

            string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit(120_000);

            return new Verdict(process.ExitCode == 0, output);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
