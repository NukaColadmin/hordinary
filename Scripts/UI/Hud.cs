using Godot;
using Hordinary.AI.Zombies;
using Hordinary.Agents;
using Hordinary.Commands;
using Hordinary.Game;
using Hordinary.World;

// Godot expose un noeud 3D nomme GridMap : on desambigue en faveur de notre grille de jeu.
using GridMap = Hordinary.World.GridMap;

namespace Hordinary.UI;

/// <summary>
/// Le HUD (specification section A : <c>UI/HUD</c>) : l'etat de la partie, pas celui d'un survivant.
///
/// La repartition avec <see cref="AgentPanel"/> tient en une phrase : ce panneau-ci montre ce que le
/// <b>joueur</b> sait — l'heure, le groupe, la horde, l'issue de la partie, les ordres qu'il a
/// donnes —, l'autre montre ce qu'un <b>survivant</b> sait. Le joueur voit la horde entiere ; Alice
/// ne voit que ce qu'elle a croise. Melanger les deux ferait mentir tout le livrable 2.
/// </summary>
public partial class Hud : InfoPanel
{
    public Hud() : base("Hud") { }

    /// <summary>Deplie les intentions connues et les frontieres candidates (touche F7).</summary>
    public bool ShowBandDetail { get; set; }

    public void Refresh(
        GridMap map,
        WorldState world,
        SurvivorBand band,
        CommandManager commands,
        MissionTracker mission,
        MissionRecall recall,
        IReadOnlyList<Zombie> horde,
        bool running)
    {
        Write($"""
            Hordinary — {mission.Mission.Name}
            Carte {map.Width} x {map.Height}, origine {map.Origin} — t = {world.Time:0.0} s
            {OutcomeLine(mission)}
            {RecallLine(recall)}
            {BandLine(band, commands)}
            {MedicineLine(mission)}
            {HordeLine(horde)}
            {ExplorationLine(commands.Selected)}
            {CommandLine(commands)}
            {Controls()}
            """);
    }

    /// <summary>
    /// L'issue de la partie. Tant qu'elle court, la ligne rappelle ce qu'on attend du joueur : le
    /// joueur ne devrait jamais avoir a ouvrir la specification pour savoir ce qu'il fait la.
    ///
    /// Les trois etats sont distincts, et ce n'est pas cosmetique : entre « mission en cours » et
    /// « partie finie » il y a « mission remplie », pendant laquelle le jeu tourne toujours et ou le
    /// groupe rentre. Les confondre etait le defaut que ce livrable corrige.
    /// </summary>
    private static string OutcomeLine(MissionTracker mission)
    {
        if (mission.IsOver) return $"*** {mission.Verdict} ***";

        if (mission.Done)
        {
            return $"Mission remplie par {mission.Courier?.Name ?? "le groupe"} "
                 + $"(t = {mission.DoneAt:0.0} s) — rappel en cours, la partie continue";
        }

        return $"Mission : rapporter {mission.Mission.Item} au camp — depart et retour au meme endroit";
    }

    /// <summary>Ou en est le rappel : qui porte la nouvelle, et combien l'ont recue.</summary>
    private static string RecallLine(MissionRecall recall)
    {
        if (!recall.Started) return $"Camp : {recall.CampZone?.Name ?? recall.Camp.ToString()}";

        string tour = recall.Everyone ? "tout le monde est prevenu" : "tournee en cours";

        return $"Rappel : {recall.Messenger?.Survivor.Name} annonce « {MissionRecall.Message} » "
             + $"— {recall.Told.Count} prevenu(s), {tour}";
    }

    private static string BandLine(SurvivorBand band, CommandManager commands)
    {
        var selected = commands.Selected;

        var parts = band.Agents.Select(agent =>
        {
            var survivor = agent.Survivor;
            string mark = ReferenceEquals(agent, selected) ? ">" : " ";

            string state = survivor switch
            {
                { Returned: true } => "rentre au camp",
                { Alive: false } => "hors de combat",
                _ => $"{survivor.HealthRatio:P0}, {survivor.SanityRatio:P0}, "
                   + $"{survivor.CurrentGoal ?? "sans objectif"}",
            };

            // « a portee » : ce que le survivant affiche sait, celui-ci peut l'apprendre a la
            // prochaine seconde. Hors portee, ils sont aussi etrangers que deux inconnus.
            string reach = selected is null
                || ReferenceEquals(agent, selected)
                || !band.Sharing.CanTalk(selected.Survivor, survivor)
                    ? string.Empty
                    : " (a portee)";

            return $"{mark}{survivor.Name} [{state}]{reach}";
        });

        string addressee = commands.GroupSelected ? "groupe" : selected?.Survivor.Name ?? "personne";

        return $"Groupe (ordres au {addressee}) : {string.Join("  ", parts)}";
    }

    private static string MedicineLine(MissionTracker mission)
    {
        var carriers = mission.Carriers.ToList();

        return carriers.Count == 0
            ? "Medicaments : pas encore ramasses"
            : $"Medicaments : portes par {string.Join(", ", carriers.Select(survivor => survivor.Name))}";
    }

    private static string HordeLine(IReadOnlyList<Zombie> horde)
    {
        int hunting = 0, curious = 0, searching = 0;

        foreach (var zombie in horde)
        {
            switch (zombie.State)
            {
                case ZombieState.Chase or ZombieState.Attack: hunting++; break;
                case ZombieState.InvestigateNoise: curious++; break;
                case ZombieState.SearchLastKnownPosition: searching++; break;
            }
        }

        return $"Horde : {horde.Count} zombies — {hunting} en chasse, {curious} sur un bruit, {searching} en fouille";
    }

    /// <summary>
    /// La frontiere retenue par le survivant affiche, et sous F7 le detail de sa note. C'est ce qui
    /// explique pourquoi celui-ci part au nord pendant qu'un autre part au sud (livrable 7).
    /// </summary>
    private string ExplorationLine(SurvivorAgent? agent)
    {
        if (agent?.Context is not { } context) return "Exploration : en attente d'une perception";
        if (context.BestFrontier is not { } frontier) return "Exploration : plus rien a decouvrir d'accessible";

        if (!ShowBandDetail) return $"Exploration : {frontier}";

        var ranked = context.RankFrontiers().Take(5).Select(candidate => $"  {candidate}");
        return $"Exploration : {frontier}\n{string.Join("\n", ranked)}";
    }

    private static string CommandLine(CommandManager commands)
    {
        if (commands.History.Count == 0) return "Dernier ordre : aucun";

        var recent = commands.History.TakeLast(3).Select(result => $"  {result}");
        return $"Derniers ordres :\n{string.Join("\n", recent)}";
    }

    private static string Controls() => """

        Tab / clic / fiche : changer de point de vue    Maj + ordre : au groupe entier
        Clic gauche : donner un objectif                Clic droit : cycler le danger
        1 pharmacie  2 retour au camp  3 rappeler au camp  4 explorer  5 regrouper
        B : interdire / autoriser le bruit              Z : interdire / autoriser la zone visee
        R : marcher / courir     Espace : distraction     C : effacer le danger
        F1 chemin A*  F2 carte mentale  F3 bruits  F4 scores  F5 plan  F6 journal  F7 groupe
        """;
}
