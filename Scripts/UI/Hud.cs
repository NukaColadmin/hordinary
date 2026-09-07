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
        IReadOnlyList<Zombie> horde,
        bool running)
    {
        Write($"""
            Hordinary — {mission.Mission.Name}
            Carte {map.Width} x {map.Height}, origine {map.Origin} — t = {world.Time:0.0} s
            {OutcomeLine(mission)}
            {BandLine(band, commands)}
            {ObjectiveLine(mission)}
            {HordeLine(horde)}
            {ExplorationLine(commands.Selected)}
            {CommandLine(commands)}
            {Controls()}
            """);
    }

    /// <summary>
    /// L'issue de la partie. Tant qu'elle court, la ligne rappelle ce qu'on attend du joueur : le
    /// joueur ne devrait jamais avoir a ouvrir la specification pour savoir ce qu'il fait la.
    /// </summary>
    private static string OutcomeLine(MissionTracker mission)
        => mission.IsOver
            ? $"*** {mission.Verdict} ***"
            : $"Mission : rapporter {mission.Mission.Item} au camp — depart et retour au meme endroit";

    private static string BandLine(SurvivorBand band, CommandManager commands)
    {
        var selected = commands.Selected;

        var parts = band.Agents.Select(agent =>
        {
            var survivor = agent.Survivor;
            string mark = ReferenceEquals(agent, selected) ? ">" : " ";

            string state = survivor switch
            {
                { Alive: false } => "hors de combat",
                _ => $"{survivor.HealthRatio:P0}, {survivor.CurrentGoal ?? "sans objectif"}",
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

    /// <summary>
    /// Ce que la mission envoie chercher, et qui le porte. Le HUD ne nomme pas plus les medicaments
    /// que le reste du jeu : il affiche l'identifiant que la mission a choisi.
    /// </summary>
    private static string ObjectiveLine(MissionTracker mission)
    {
        var carriers = mission.Carriers.ToList();
        string what = mission.Mission.Item;

        return carriers.Count == 0
            ? $"{what} : pas encore ramasse"
            : $"{what} : porte par {string.Join(", ", carriers.Select(survivor => survivor.Name))}";
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
        1 pharmacie  2 retour au camp  4 explorer  5 regrouper
        B : interdire / autoriser le bruit              Z : interdire / autoriser la zone visee
        R : marcher / courir     Espace : distraction     C : effacer le danger
        F1 chemin A*  F2 carte mentale  F3 bruits  F4 scores  F5 plan  F6 journal  F7 groupe
        """;
}
