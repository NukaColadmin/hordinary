using Hordinary.AI.Planning;
using Hordinary.AI.Planning.Pddl;
using Hordinary.Config;

namespace Hordinary.Tests;

/// <summary>
/// Les sorties versionnees de la spec G : <c>tools/pddl/domain.pddl</c> et les instantanes de
/// <c>tools/pddl/snapshots/</c>.
///
/// <para>
/// Ces fichiers sont <b>generes</b> et pourtant versionnes, ce qui est normalement une mauvaise idee.
/// Ils le sont pour une raison precise : ce sont eux qu'on donne a Fast Downward et a VAL, hors
/// ligne, et un depot qui ne les contient pas oblige a lancer le jeu avant de pouvoir verifier quoi
/// que ce soit. Le risque habituel d'un fichier genere versionne — qu'il derive de sa source sans
/// qu'on le voie — est couvert ici : ces tests <b>rendent a nouveau</b> et comparent. Changer une
/// action du domaine sans regenerer fait echouer la suite.
/// </para>
///
/// <para>
/// Pour regenerer : <c>HORDINARY_PDDL_REGEN=1 dotnet test --filter PddlArtifactsTests</c>.
/// </para>
/// </summary>
public class PddlArtifactsTests
{
    /// <summary>Le nombre d'instantanes que la spec G demande.</summary>
    private const int SnapshotCount = 10;

    /// <summary>Graine de l'erreur de localisation des bruits, pour que la capture soit rejouable.</summary>
    private const int NoiseSeed = 20260728;

    /// <summary>
    /// Le budget de planification de la capture. Celui du fichier livre est une <b>horloge</b>
    /// (<c>planner.max_time_ms: 50</c>) : une recherche qui aboutit seule peut expirer quand la
    /// machine est chargee, et la suite des plans d'une partie en depend. Le desserrer ici est ce qui
    /// rend la capture rejouable — le plafond de noeuds, lui, ne bouge pas, parce que c'est celui qui
    /// dit quelque chose du domaine.
    /// </summary>
    private static PlannerSettings Budget => MissionRun.Config.Planner with { MaxTimeMs = 60_000 };

    private static string ToolsDirectory => Path.Combine(DemoMap.RepositoryRoot, "tools", "pddl");

    private static string SnapshotDirectory => Path.Combine(ToolsDirectory, "snapshots");

    private static bool Regenerating
        => Environment.GetEnvironmentVariable("HORDINARY_PDDL_REGEN") is "1" or "true";

    private static PddlDomain Exported { get; } =
        PddlDomainBuilder.From(PlanningDomain.FromJson(DemoMap.ReadConfig("planner_domain.json")));

    /// <summary>
    /// Le domaine versionne est celui que le code produit aujourd'hui. C'est un test de non-derive :
    /// il ne verifie pas que le PDDL est bon — les autres s'en chargent — mais que le fichier qu'on
    /// donne aux outils hors ligne est bien celui du jeu qu'on joue.
    /// </summary>
    [Fact]
    public void Le_domaine_versionne_est_a_jour()
        => Check(Path.Combine(ToolsDirectory, "domain.pddl"), PddlWriter.Render(Exported));

    /// <summary>
    /// Les dix instantanes sont preleves dans une partie reelle, jouee du depart au verdict avec trois
    /// survivants aux croyances divergentes. Ce ne sont pas des situations ecrites a la main : un
    /// probleme fabrique prouverait que l'export fonctionne sur ce qu'on a bien voulu lui donner.
    /// </summary>
    [Fact]
    public void Les_instantanes_versionnes_sont_a_jour()
    {
        var snapshots = Capture();

        Assert.Equal(SnapshotCount, snapshots.Count);

        foreach (var (name, text) in snapshots) Check(Path.Combine(SnapshotDirectory, name), text);
    }

    /// <summary>
    /// La capture est reproductible : deux parcours de la meme partie donnent les memes dix
    /// problemes. Sans cela, les fichiers versionnes changeraient a chaque regeneration et le test de
    /// non-derive ne vaudrait rien — il signalerait du bruit au lieu d'un changement.
    /// </summary>
    [Fact]
    public void La_capture_des_instantanes_est_reproductible()
    {
        var first = Capture();
        var second = Capture();

        Assert.Equal(first.Select(snapshot => snapshot.Name), second.Select(snapshot => snapshot.Name));
        Assert.Equal(first.Select(snapshot => snapshot.Text), second.Select(snapshot => snapshot.Text));
    }

    /// <summary>
    /// Les dix instantanes ne decrivent pas dix fois la meme chose. Un jeu d'echantillons identiques
    /// passerait tous les tests de forme et ne corroborerait rien.
    /// </summary>
    [Fact]
    public void Les_instantanes_ne_sont_pas_dix_fois_le_meme()
    {
        var snapshots = Capture();

        Assert.Equal(SnapshotCount, snapshots.Select(snapshot => snapshot.Text).Distinct(StringComparer.Ordinal).Count());
    }

    // --- Capture ------------------------------------------------------------------------------------

    private sealed record Snapshot(string Name, string Text);

    /// <summary>
    /// Dix problemes distincts, egalement repartis sur une partie a trois jouee jusqu'au verdict. La
    /// mission est celle de <c>config/missions.json</c> : la capture suit le jeu et ne s'en invente
    /// pas un.
    /// </summary>
    private static List<Snapshot> Capture()
    {
        // La graine de bruit est explicite : sans elle, l'erreur de localisation tire dans
        // Random.Shared et deux captures de la meme partie divergent des qu'un bruit est entendu de
        // loin. Les fichiers versionnes changeraient alors a chaque regeneration, et le test de
        // non-derive signalerait du bruit plutot qu'un changement.
        var run = new MissionRun(team: true, noiseSeed: NoiseSeed, planner: Budget);

        // Toute la mission, pas ses premieres secondes. Prendre les dix premiers problemes donnerait
        // dix fois « ExploreUnknownArea » : au depart personne ne sait rien, et tout le monde
        // explore. Ce qui interesse un planificateur tiers est ailleurs — une porte close, une caisse
        // reperee, un retour au van.
        run.RunUntil(() => run.Tracker.IsOver, maxTicks: MissionRun.DefaultMaxTicks);

        var distinct = new List<(string Goal, string Text)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var plan in run.Plans)
        {
            var problem = PddlProblemBuilder.From(plan.Problem, Exported);
            string text = PddlWriter.Render(problem);

            if (seen.Add(text)) distinct.Add((problem.Name, text));
        }

        Assert.True(
            distinct.Count >= SnapshotCount,
            $"La partie n'a produit que {distinct.Count} problemes distincts : impossible d'en capturer {SnapshotCount}.");

        // Dix prelevements egalement repartis sur la partie : le premier et le dernier compris, pour
        // que les deux bouts de la mission soient representes.
        var snapshots = new List<Snapshot>();

        for (int index = 0; index < SnapshotCount; index++)
        {
            int position = SnapshotCount == 1 ? 0 : index * (distinct.Count - 1) / (SnapshotCount - 1);
            var (goal, text) = distinct[position];

            snapshots.Add(new Snapshot($"{index + 1:00}-{goal}.pddl", text));
        }

        return snapshots;
    }

    // --- Comparaison --------------------------------------------------------------------------------

    /// <summary>
    /// Compare un fichier versionne a ce que le code rend — ou le reecrit, quand on regenere
    /// volontairement. Les fins de ligne sont normalisees : un depot clone sous Windows et un autre
    /// sous Linux ne doivent pas se contredire sur un CR.
    /// </summary>
    private static void Check(string path, string rendered)
    {
        if (Regenerating)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, rendered);
            return;
        }

        Assert.True(
            File.Exists(path),
            $"{Relative(path)} est absent. Regenerer : HORDINARY_PDDL_REGEN=1 dotnet test --filter PddlArtifactsTests");

        Assert.True(
            Normalize(File.ReadAllText(path)) == Normalize(rendered),
            $"{Relative(path)} ne correspond plus a ce que le code produit. "
          + "Regenerer : HORDINARY_PDDL_REGEN=1 dotnet test --filter PddlArtifactsTests");
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();

    private static string Relative(string path) => Path.GetRelativePath(DemoMap.RepositoryRoot, path);
}
